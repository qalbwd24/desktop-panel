import {browserEditor} from './bridge.js';
import {el,button,localDate,countdownUnits,countdownDistance,nextUnit,resolveCountdown} from './core.js';
const $=selector=>document.querySelector(selector);
let token=sessionStorage.getItem('desktop-panel-api-token')??'',key='todos',rows=[],eventRows=[],revision='';
function status(text,error=false){$('#status').textContent=text;$('#status').classList.toggle('error',error);}
function run(action){Promise.resolve().then(action).catch(error=>{status(error.message,true);});}
async function request(path,options={}){
  const response=await fetch(`/api/v1/${path}`,{...options,credentials:'same-origin',headers:{...(token?{Authorization:`Bearer ${token}`} : {}),...(options.body?{'Content-Type':'application/json'}:{}),...options.headers}});
  const result=await response.json();if(!response.ok){if(response.status===401){$('#connect').hidden=false;$('#management').hidden=true;}throw Error(result.error??'请求失败');}return result;
}
async function load(){
  const current=key,result=await request(current);if(current!==key)return;
  if(current==='countdowns'){const events=await request('events');if(current!==key)return;eventRows=events.items;}
  $('#management').hidden=false;$('#connect').hidden=true;status('');
  $('#records').hidden=key==='settings';$('#settings').hidden=key!=='settings';
  for(const node of document.querySelectorAll('nav button'))node.classList.toggle('active',node.dataset.key===key);
  if(key==='settings'){$('#address').value=result.baseUrl;$('#api-key').value=result.token;return;}
  rows=result.items;revision=result.revision;render();
}
function render(){
  const list=$('#list');list.replaceChildren();$('#count').textContent=`${rows.length} 项`;
  const sorted=(key==='countdowns'?rows.map(row=>resolveCountdown(row,eventRows)):[...rows]).sort(key==='todos'?(a,b)=>(a.order??0)-(b.order??0):(a,b)=>a.date.localeCompare(b.date)||(a.time??'').localeCompare(b.time??''));
  if(!sorted.length){list.append(el('div','empty','暂无记录'));return;}
  for(const item of sorted){
    const node=el('div',`row${item.done?' completed':''}`),content=el('div','content');
    if(key==='todos'){const toggle=button(item.done?'✓':'○',()=>run(()=>write('PATCH',item.id,{done:!item.done})),'icon');toggle.setAttribute('aria-label',`${item.done?'取消完成':'完成'}：${item.title}`);node.append(toggle);}
    content.append(el('span','title',item.title));
    if(key==='events')content.append(el('small','',`${item.date}  ${item.time}`));
    if(key==='countdowns'){const distance=countdownDistance(item.date,item.unit);content.append(el('small','',`${item.date}${item.linked?' · 关联日程':item.missingSource?' · 日程已删除':''}`));const counter=button(`${distance.value}${distance.label}`,()=>run(()=>write('PATCH',item.id,{unit:nextUnit(item.unit)})),'icon');counter.title=distance.hint;counter.setAttribute('aria-label',`${item.title}倒计时，切换单位`);node.append(counter);}
    const edit=button('编辑',()=>run(()=>editItem(item)),'icon');edit.setAttribute('aria-label',`编辑：${item.title}`);
    const remove=button('×',()=>run(async()=>{if(confirm(`删除“${item.title}”？`))await write('DELETE',item.id);}),'icon');remove.setAttribute('aria-label',`删除：${item.title}`);
    node.append(content,edit,remove);list.append(node);
  }
}
async function write(method,id,value){
  const resource=key;
  try{await request(resource+(id?`/${encodeURIComponent(id)}`:''),{method,headers:{'If-Match':`"${revision}"`},...(value?{body:JSON.stringify(value)}:{})});await load();status('已保存');}
  catch(error){await load();throw error;}
}
async function editItem(item){
  const resource=key;
  const fields=[{name:'title',label:'内容',type:'text',value:item?.title??''}];
  if(key==='countdowns')fields.unshift({name:'eventId',label:'关联日程',type:'select',value:item?.eventId??'',options:[{value:'',label:'独立日期'},...eventRows.map(row=>({value:row.id,label:`${row.date} · ${row.title}`}))],presets:Object.fromEntries(eventRows.map(row=>[row.id,{title:row.title,date:row.date}]))});
  if(key!=='todos')fields.push({name:'date',label:'日期',type:'date',value:item?.date??localDate()});
  if(key==='events')fields.push({name:'time',label:'时间',type:'time',value:item?.time??'09:00'});
  if(key==='countdowns')fields.push({name:'unit',label:'显示单位',type:'select',value:item?.unit??'days',options:countdownUnits});
  const result=await browserEditor(item?'编辑':'添加',fields);if(!result)return;
  if(resource!==key)return;
  result.title=result.title.trim();if(!result.title)throw Error('请输入内容');
  if(key==='countdowns'){const source=eventRows.find(row=>row.id===result.eventId);if(source){result.title=source.title;result.date=source.date;}result.eventId||=null;}
  await write(item?'PATCH':'POST',item?.id,result);
}
$('#connect').onsubmit=event=>{event.preventDefault();token=$('#token').value.trim();sessionStorage.setItem('desktop-panel-api-token',token);run(load);};
for(const node of document.querySelectorAll('nav button'))node.onclick=()=>{key=node.dataset.key;run(load);};
$('#add').onclick=()=>run(()=>editItem());$('#refresh').onclick=()=>run(load);
$('#reveal').onclick=()=>{const input=$('#api-key');input.type=input.type==='password'?'text':'password';$('#reveal').textContent=input.type==='password'?'显示密钥':'隐藏密钥';};
run(load);
