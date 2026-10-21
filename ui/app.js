import {bridge} from './bridge.js';
import {el,button,fitRect,fitScreenRect,minCardHeight,defaultRects,defaultStyle,themePresets,normalizeStyle,localDate,weekdays,weekDates,addDays} from './core.js';
import {createTodo} from './widgets/todo.js';
import {createSchedule} from './widgets/schedule.js';
import {createCountdown} from './widgets/countdown.js';
const desktop=document.querySelector('#desktop');
const cards=new Map(), widgets=new Map();
let data,environment,profile,revisions={},editing=false,queue=Promise.resolve(),regionFrame,toastTimer,midnightTimer,menuTrigger;
const titles={todo:'待办',schedule:'周日程',countdown:'倒计时'};
function toast(message,error=false) { const node=document.querySelector('#toast');node.textContent=message;node.style.borderColor=error?'#e9a09077':'';node.hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>{node.hidden=true;reportRegions();},4000);reportRegions(); }
const api={
  get data(){return data;},
  run(action){Promise.resolve().then(action).catch(error=>{console.error(error);toast(error.message,true);});},
  async edit(title,fields){return bridge.request({type:'edit',title,fields});},
  layoutChange(transform){
    const operation=queue.catch(()=>{}).then(async()=>{const before=structuredClone(data.layout);transform(data.layout);profileForScreen();arrange();try{await saveLayout();}catch(error){if(!bridge.native){data.layout=before;profileForScreen();arrange();}throw error;}});queue=operation;return operation;
  },
  change(key,transform){
    const operation=queue.catch(()=>{}).then(async()=>{
      const before=structuredClone(data[key]);data[key]=transform(structuredClone(before));
      const previous=new Map(before.map(row=>[row.id,row]));const now=new Date().toISOString();
      for(const row of data[key])if(JSON.stringify(previous.get(row.id))!==JSON.stringify(row)){row.createdAt??=previous.get(row.id)?.createdAt??now;row.updatedAt=now;}
      renderKey(key);
      try {const revision=await bridge.request({type:'save',key,value:data[key],revision:revisions[key]});if(bridge.native)revisions[key]=revision;}
      catch(error){if(bridge.native){const latest=await bridge.request({type:'read',key});data[key]=latest.value;revisions[key]=latest.revision;}else data[key]=before;renderKey(key);throw error;}
    });queue=operation;return operation;
  },
  empty(title,subtitle){const node=el('div','empty');node.append(el('strong','',title));if(subtitle)node.append(el('span','',subtitle));return node;},
  card(key,title,subtitle){
    const node=el('section','widget');node.id=`widget-${key}`;node.setAttribute('aria-label',title);node.dataset.widget=key;
    const header=el('header','widget-header'),heading=el('div','heading-block');
    const eyebrow=el('span','eyebrow','');eyebrow.hidden=key!=='todo';
    const headingTitle=el('h1','',title),sub=el('span','subtitle',subtitle);heading.append(eyebrow,headingTitle,sub);
    const settings=button('⚙',()=>openWidgetMenu(key,settings),'widget-menu');settings.title='设置';settings.setAttribute('aria-label',`${title}设置`);settings.setAttribute('aria-expanded','false');
    header.append(heading,settings);const body=el('div','widget-body'),actions=el('footer','widget-actions');
    const resize=el('div','resize-handle');resize.title='调整尺寸';
    node.append(header,body,actions,resize);desktop.append(node);cards.set(key,node);
    if(bridge.native){node.addEventListener('pointerenter',()=>bridge.send({type:'activity',active:true}));node.addEventListener('pointerleave',()=>bridge.send({type:'activity',active:false}));}
    attachDrag(key,node,header,resize);return {node,header,body,actions,subtitle:sub};
  }
};
function renderKey(key){if(key==='todos')widgets.get('todo').render();else if(key==='events'||key==='countdowns'){widgets.get('schedule').render();widgets.get('countdown').render();}reportRegions();}
function profileForScreen(){
  data.layout.screens??={};data.layout.styles??={};
  const key=environment.monitor??'default';
  if(!data.layout.screens[key] || typeof data.layout.screens[key]!=='object') data.layout.screens[key]={_viewport:{width:innerWidth,height:innerHeight}};
  profile=data.layout.screens[key];const defaults=defaultRects(innerWidth,innerHeight);
  for(const key of Object.keys(titles)) if(!profile[key] || typeof profile[key]!=='object') profile[key]=defaults[key];
}
function arrange(){
  if(!profile)return;
  const calculated=Object.fromEntries([...cards.keys()].map(key=>[key,fitScreenRect(profile[key],innerWidth,innerHeight,profile._viewport)]));
  const values=Object.values(calculated);
  const overlap=values.some((a,i)=>values.slice(i+1).some(b=>Math.min(a.left+a.width,b.left+b.width)>Math.max(a.left,b.left)&&Math.min(a.top+a.height,b.top+b.height)>Math.max(a.top,b.top)));
  const resized=profile._viewport?Math.abs(innerWidth-profile._viewport.width)>1||Math.abs(innerHeight-profile._viewport.height)>1:[...cards.keys()].some(key=>profile[key].width>innerWidth-32||profile[key].height>innerHeight-32);
  const temporary=overlap&&!editing&&resized?defaultRects(innerWidth,innerHeight):null;
  for(const [key,node] of cards){const rect=temporary?fitRect(temporary[key],innerWidth,innerHeight):calculated[key];node.classList.toggle('compact',rect.height<(key==='schedule'?320:270));Object.assign(node.style,{left:`${rect.left}px`,top:`${rect.top}px`,width:`${rect.width}px`,height:`${rect.height}px`});applyStyle(key);}
  reportRegions();
}
function applyStyle(key,preview){
  const node=cards.get(key),style=normalizeStyle(preview??data.layout.styles[key]);
  node.dataset.mode=style.mode;node.dataset.background=style.background;node.dataset.theme=style.theme;node.dataset.clear=String(style.transparency===100||style.background==='无');
  node.classList.toggle('countdown-simple',key==='countdown'&&style.presentation==='简约');
  const alpha=1-style.transparency/100;const rgb=[1,3,5].map(i=>parseInt(style.tint.slice(i,i+2),16));
  node.style.setProperty('--tint',`rgba(${rgb.join(',')},${alpha})`);
  node.style.setProperty('--blur',`${style.effect==='毛玻璃'&&style.background!=='无'&&alpha>0?style.blur:0}px`);
  node.style.setProperty('--text',style.color);node.style.setProperty('--font-size',`${style.fontSize}px`);
  node.style.setProperty('--font-weight',String(style.fontWeight));node.style.setProperty('--number-weight',String(style.numberWeight));
  node.style.setProperty('--accent',style.accent);
  node.style.setProperty('--font',`${JSON.stringify(String(style.font))},'Microsoft YaHei UI',sans-serif`);
  node.style.setProperty('--radius',`${style.radius}px`);
  node.style.setProperty('--image',style.background==='图片'&&style.image&&alpha>0?`url(${JSON.stringify(style.image)})`:'none');
  node.style.setProperty('--image-alpha',String(alpha));
}
async function saveLayout(){
  try{const revision=await bridge.request({type:'save',key:'layout',value:data.layout,revision:revisions.layout});if(bridge.native)revisions.layout=revision;}
  catch(error){if(bridge.native){const latest=await bridge.request({type:'read',key:'layout'});data.layout=latest.value;revisions.layout=latest.revision;profileForScreen();arrange();}throw error;}
}
async function editStyle(key){
  closeWidgetMenu();
  const style=normalizeStyle(data.layout.styles[key]);
  const fields=[
    ...(key==='countdown'?[{name:'presentation',label:'倒计时样式',type:'select',options:['列表','简约'],value:style.presentation}]:[]),
    {name:'theme',label:'主题',type:'select',options:['深色','浅色'],presets:themePresets,value:style.theme},
    {name:'background',label:'背景',type:'select',options:['颜色','图片','无'],value:style.background},
    {name:'transparency',label:'透明度 %',type:'range',min:0,max:100,value:style.transparency},
    {name:'effect',label:'背景效果',type:'select',options:['毛玻璃','无模糊'],value:style.effect},
    {name:'blur',label:'模糊强度',type:'range',min:0,max:40,value:style.blur},
    {name:'tint',label:'背景颜色',type:'color',value:style.tint},
    {name:'color',label:'文字颜色',type:'color',value:style.color},
    {name:'accent',label:'强调颜色',type:'color',value:style.accent},
    {name:'font',label:'字体名称',type:'text',value:style.font},
    {name:'fontSize',label:'字号',type:'range',min:11,max:32,value:style.fontSize},
    {name:'fontWeight',label:'文字粗细',type:'range',min:100,max:900,step:100,value:style.fontWeight},
    ...(key==='countdown'?[{name:'numberWeight',label:'数字粗细',type:'range',min:100,max:900,step:100,value:style.numberWeight}]:[]),
    {name:'radius',label:'圆角',type:'range',min:0,max:40,value:style.radius}
  ];
  let value;
  try{value=await bridge.request({type:'edit',title:`${titles[key]} · 外观`,fields,previewWidget:key},values=>applyStyle(key,{...style,...values}));}
  finally{applyStyle(key);}
  if(!value)return;
  if(![value.tint,value.color,value.accent].every(value=>/^#[a-f\d]{6}$/i.test(value)))throw Error('颜色请填写六位十六进制，例如 #202632');
  const next=normalizeStyle({...style,...value});
  if(next.background==='图片'&&!next.image){
    if(!bridge.native)throw Error('选择本地图片请使用 Windows 程序');
    const image=await bridge.request({type:'pick-image'});if(image)next.image=image;if(!next.image)return;
  }
  await api.layoutChange(layout=>{layout.styles[key]=next;});toast('外观已保存');
}
async function chooseBackgroundImage(key){
  if(!bridge.native)throw Error('选择本地图片请使用 Windows 程序');
  const image=await bridge.request({type:'pick-image'});if(!image)return;
  await api.layoutChange(layout=>{layout.styles[key]=normalizeStyle({...normalizeStyle(layout.styles[key]),background:'图片',image});});toast('背景图片已保存');
}
function closeWidgetMenu(){const menu=document.querySelector('#widget-menu-panel');if(menu)menu.remove();if(menuTrigger)menuTrigger.setAttribute('aria-expanded','false');menuTrigger=null;reportRegions();}
function openWidgetMenu(key,trigger){
  if(menuTrigger===trigger){closeWidgetMenu();return;}closeWidgetMenu();menuTrigger=trigger;trigger.setAttribute('aria-expanded','true');
  const menu=el('div','settings-menu');menu.id='widget-menu-panel';menu.setAttribute('role','dialog');menu.setAttribute('aria-label',`${titles[key]}设置`);
  const action=(label,run)=>button(label,()=>{closeWidgetMenu();api.run(run);},'settings-action');
  menu.append(el('strong','',titles[key]),action('外观设置',()=>editStyle(key)),action('位置与尺寸',()=>editGeometry(key)),action('拖动布局',()=>setEditing(true)));
  if(key==='countdown'){const style=normalizeStyle(data.layout.styles[key]);menu.append(action(style.presentation==='简约'?'切换为列表样式':'切换为简约样式',()=>api.layoutChange(layout=>{layout.styles[key]={...normalizeStyle(layout.styles[key]),presentation:style.presentation==='简约'?'列表':'简约'};})));}
  menu.append(action(normalizeStyle(data.layout.styles[key]).image?'更换背景图片':'选择背景图片',()=>chooseBackgroundImage(key)));
  const themes=el('div','theme-choices');for(const theme of ['浅色','深色']){const choice=action(theme,()=>api.layoutChange(layout=>{layout.styles[key]={...normalizeStyle(layout.styles[key]),theme,...themePresets[theme]};}));choice.setAttribute('aria-pressed',String(normalizeStyle(data.layout.styles[key]).theme===theme));themes.append(choice);}menu.append(themes);document.body.append(menu);
  const origin=trigger.getBoundingClientRect(),rect=menu.getBoundingClientRect();menu.style.left=`${Math.max(12,Math.min(innerWidth-rect.width-12,origin.right-rect.width))}px`;menu.style.top=`${Math.max(12,Math.min(innerHeight-rect.height-12,origin.bottom+8))}px`;reportRegions();
  if(!bridge.native)menu.querySelector('button').focus();
}
async function editGeometry(key){
  const current=cards.get(key).getBoundingClientRect();
  const values=await api.edit(`${titles[key]} · 位置与尺寸`,[{name:'left',label:'水平位置',type:'number',min:16,max:innerWidth-16,value:Math.round(current.left)},{name:'top',label:'垂直位置',type:'number',min:16,max:innerHeight-16,value:Math.round(current.top)},{name:'width',label:'宽度',type:'number',min:Math.min(80,innerWidth-32),max:innerWidth-32,value:Math.round(current.width)},{name:'height',label:'高度',type:'number',min:minCardHeight(innerWidth,innerHeight),max:innerHeight-32,value:Math.round(current.height)}]);
  if(!values)return;const width=Number(values.width),height=Number(values.height);const left=Math.min(innerWidth-width-16,Math.max(16,Number(values.left))),top=Math.min(innerHeight-height-16,Math.max(16,Number(values.top)));
  await api.layoutChange(layout=>{captureGeometry();layout.screens[environment.monitor][key]={x:(left-16)/Math.max(1,innerWidth-width-32),y:(top-16)/Math.max(1,innerHeight-height-32),width,height};});toast('位置已保存');
}
function captureGeometry(){
  for(const [key,node] of cards){const rect=node.getBoundingClientRect();profile[key]={x:Math.max(0,rect.left-16)/Math.max(1,innerWidth-rect.width-32),y:Math.max(0,rect.top-16)/Math.max(1,innerHeight-rect.height-32),width:rect.width,height:rect.height};}
  profile._viewport={width:innerWidth,height:innerHeight};
}
function setEditing(value){
  closeWidgetMenu();
  if(value&&!editing)captureGeometry();
  editing=value;document.body.classList.toggle('editing',editing);document.querySelector('#layout-toolbar').hidden=!editing;reportRegions();
  if(value)profile._viewport={width:innerWidth,height:innerHeight};
}
function attachDrag(key,node,header,resize){
  const start=(event,resizing)=>{
    if(!editing || event.button!==0 || event.target.closest('button'))return;
    event.preventDefault();const origin=node.getBoundingClientRect(),startX=event.clientX,startY=event.clientY;
    const handle=resizing?resize:header;handle.setPointerCapture(event.pointerId);
    const move=event=>{
      let width=origin.width,height=origin.height,left=origin.left,top=origin.top;
      if(resizing){width=Math.max(80,Math.min(innerWidth-left-16,origin.width+event.clientX-startX));height=Math.max(minCardHeight(innerWidth,innerHeight),Math.min(innerHeight-top-16,origin.height+event.clientY-startY));}
      else{left=Math.min(innerWidth-width-16,Math.max(16,origin.left+event.clientX-startX));top=Math.min(innerHeight-height-16,Math.max(16,origin.top+event.clientY-startY));}
      profile[key]={x:Math.max(0,left-16)/Math.max(1,innerWidth-width-32),y:Math.max(0,top-16)/Math.max(1,innerHeight-height-32),width,height};arrange();
    };
    const stop=()=>{handle.removeEventListener('pointermove',move);handle.removeEventListener('pointerup',stop);handle.removeEventListener('pointercancel',stop);};
    handle.addEventListener('pointermove',move);handle.addEventListener('pointerup',stop);handle.addEventListener('pointercancel',stop);
  };
  header.addEventListener('pointerdown',event=>start(event,false));resize.addEventListener('pointerdown',event=>start(event,true));
}
function reportRegions(){
  if(!bridge.native || !environment?.desktop)return;
  cancelAnimationFrame(regionFrame);regionFrame=requestAnimationFrame(()=>{
    const nodes=[...cards.values(),...document.querySelectorAll('#layout-toolbar:not([hidden]),#toast:not([hidden]),#widget-menu-panel')];
    const rects=nodes.map(node=>{const rect=node.getBoundingClientRect();return {x:rect.x,y:rect.y,width:rect.width,height:rect.height};});
    bridge.send({type:'regions',width:innerWidth,height:innerHeight,rects});
  });
}
function refreshDate(){
  const date=localDate();const weekday=weekdays[weekDates(date).indexOf(date)];
  const node=document.querySelector('#widget-todo .eyebrow');if(node)node.textContent=`${date.replaceAll('-',' / ')} · ${weekday}`;
  for(const widget of widgets.values())widget.render();
  clearTimeout(midnightTimer);const tomorrow=new Date();tomorrow.setHours(24,0,0,60);midnightTimer=setTimeout(refreshDate,Math.max(1000,tomorrow-new Date()));
}
function setWallpaper(){if(environment.wallpaper)document.body.style.backgroundImage=`url(${JSON.stringify(environment.wallpaper)})`;}
bridge.listen(message=>{
  if(message.type==='init'){
    data={todos:[],events:[],countdowns:[],layout:{},...message.data};environment=message.environment;
    revisions=message.revisions??{};
    profileForScreen();widgets.set('todo',createTodo(api));widgets.set('schedule',createSchedule(api));widgets.set('countdown',createCountdown(api));
    setWallpaper();arrange();refreshDate();document.querySelector('#preview-toolbar').hidden=environment.desktop;
    if(bridge.native&&!environment.desktop)document.querySelector('#preview-toolbar span').textContent='窗口预览 · 数据仍保存在本地 JSON';
    if(environment.smoke)api.run(smoke);
  }else if(message.type==='data'){
    data[message.key]=message.value;revisions[message.key]=message.revision;
    if(message.key==='layout'){profileForScreen();setEditing(false);arrange();for(const widget of widgets.values())widget.render();}else renderKey(message.key);
  }else if(message.type==='environment'){environment=message.environment;setWallpaper();}
  else if(message.type==='command'&&message.command==='layout')setEditing(!editing);
});
document.querySelector('#edit-layout').onclick=()=>setEditing(!editing);
document.querySelector('#finish-layout').onclick=()=>api.run(async()=>{await saveLayout();setEditing(false);toast('布局已保存');});
document.querySelector('#reset-layout').onclick=()=>{Object.assign(profile,defaultRects(innerWidth,innerHeight),{_viewport:{width:innerWidth,height:innerHeight}});arrange();};
window.addEventListener('resize',arrange);document.addEventListener('visibilitychange',()=>{if(!document.hidden)refreshDate();});
document.addEventListener('pointerdown',event=>{const menu=document.querySelector('#widget-menu-panel');if(menu&&!menu.contains(event.target)&&!event.target.closest('.widget-menu'))closeWidgetMenu();});
document.addEventListener('keydown',event=>{if(event.key==='Escape'&&menuTrigger)closeWidgetMenu();else if(event.key==='Escape'&&editing)api.run(async()=>{await saveLayout();setEditing(false);});});
window.addEventListener('error',event=>toast(event.message,true));
bridge.ready();
async function smoke(){
  let apiProbe;
  const existed=data.todos.some(row=>row.id==='smoke-todo');
  if(!existed){
    await api.change('todos',rows=>[...rows,{id:'smoke-todo',title:'验证：中文待办 <b>原样显示</b>',done:false,order:rows.length}]);
    const checkbox=document.querySelector('[data-id="smoke-todo"] .check');checkbox.click();await Promise.resolve();await Promise.resolve();await queue;
    await api.change('events',rows=>[...rows,{id:'smoke-event',title:'验证周日程',date:localDate(),time:'10:00'}]);
    await api.change('countdowns',rows=>[...rows,{id:'smoke-countdown',title:'验证倒计时',date:addDays(localDate(),12),unit:'days'}]);
    document.querySelector('[data-id="smoke-countdown"] .remaining').click();await Promise.resolve();await Promise.resolve();await queue;
    await api.change('countdowns',rows=>[...rows,{id:'smoke-linked',title:'原名称',date:localDate(),eventId:'smoke-event',unit:'weeks'}]);
    await api.change('events',rows=>rows.map(row=>row.id==='smoke-event'?{...row,title:'日程联动验证',date:addDays(localDate(),19)}:row));
    data.layout.styles.todo={...defaultStyle,...themePresets['浅色'],theme:'浅色',transparency:62,fontSize:16};profile.todo={...profile.todo,x:.94};await saveLayout();arrange();
  }
  const row=data.todos.find(row=>row.id==='smoke-todo');
  if(environment.apiSmoke){apiProbe=await bridge.request({type:'api-smoke'});await new Promise(resolve=>setTimeout(resolve,300));}
  const apiPassed=!environment.apiSmoke||(apiProbe.sessionAuthenticated&&apiProbe.singleUse&&data.todos.some(row=>row.id===apiProbe.id)&&document.querySelector(`[data-id="${apiProbe.id}"] .item-title`)?.textContent==='后台同步验证');
  const linkedTitle=document.querySelector('[data-id="smoke-linked"] .item-title')?.textContent;
  const linked=data.countdowns.find(row=>row.id==='smoke-linked')?.eventId==='smoke-event'&&linkedTitle===data.events.find(row=>row.id==='smoke-event')?.title;
  const appearance=data.layout.styles.todo.theme==='浅色'&&data.layout.styles.todo.transparency===62&&Math.abs(profile.todo.x-.94)<.001;
  const integerFormat=document.querySelector('[data-id="smoke-countdown"] .remaining strong')?.textContent==='1周5天'&&document.querySelector('[data-id="smoke-linked"] .remaining strong')?.textContent==='2周5天';
  const passed=apiPassed&&linked&&appearance&&integerFormat&&row?.done===true&&data.events.some(row=>row.id==='smoke-event')&&data.countdowns.some(row=>row.id==='smoke-countdown'&&row.unit==='weeks')&&!document.querySelector('.item-title b');
  await new Promise(resolve=>setTimeout(resolve,700));
  bridge.send({type:'smoke-result',value:{passed,restart:existed,todoCompleted:row?.done===true,widgets:cards.size,plainText:!document.querySelector('.item-title b'),styleSaved:appearance,linkedCountdown:linked,integerFormat,unitSaved:data.countdowns.find(row=>row.id==='smoke-countdown')?.unit==='weeks',apiSync:environment.apiSmoke?apiPassed:null}});
}
