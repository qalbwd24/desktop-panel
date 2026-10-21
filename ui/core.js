export const weekdays = ['周一', '周二', '周三', '周四', '周五', '周六', '周日'];
export function localDate(now = new Date()) { return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2,'0')}-${String(now.getDate()).padStart(2,'0')}`; }
export function validDate(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value ?? '')) return false;
  const parsed = new Date(`${value}T00:00:00Z`);
  return Number.isFinite(parsed.getTime()) && parsed.toISOString().slice(0,10) === value;
}
export function addDays(value, count) { if (!validDate(value)) throw Error('日期无效'); const d = new Date(`${value}T00:00:00Z`); d.setUTCDate(d.getUTCDate() + count); return d.toISOString().slice(0,10); }
export function weekDates(value = localDate()) { const day = new Date(`${value}T00:00:00Z`).getUTCDay(); const monday = addDays(value, -((day + 6) % 7)); return Array.from({length:7}, (_,i)=>addDays(monday,i)); }
export function daysUntil(date, today = localDate()) { if (!validDate(date) || !validDate(today)) throw Error('日期无效'); return Math.round((Date.parse(`${date}T00:00:00Z`) - Date.parse(`${today}T00:00:00Z`))/86400000); }
export const countdownUnits=[{value:'days',label:'天'},{value:'weeks',label:'周'},{value:'months',label:'月'}];
export function nextUnit(unit='days'){return countdownUnits[(Math.max(0,countdownUnits.findIndex(item=>item.value===unit))+1)%countdownUnits.length].value;}
function addMonthsClamped(value,count){const date=new Date(`${value}T00:00:00Z`),day=date.getUTCDate();date.setUTCDate(1);date.setUTCMonth(date.getUTCMonth()+count);const last=new Date(date);last.setUTCMonth(last.getUTCMonth()+1,0);date.setUTCDate(Math.min(day,last.getUTCDate()));return date.toISOString().slice(0,10);}
export function countdownDistance(date,unit='days',today=localDate()){
  const days=daysUntil(date,today),absolute=Math.abs(days);if(!countdownUnits.some(item=>item.value===unit))unit='days';
  let whole=0,remainder=absolute;
  if(unit==='weeks'){whole=Math.floor(absolute/7);remainder=absolute%7;}
  if(unit==='months'&&absolute){const start=days>0?today:date,end=days>0?date:today;whole=(Number(end.slice(0,4))-Number(start.slice(0,4)))*12+Number(end.slice(5,7))-Number(start.slice(5,7));let anchor=addMonthsClamped(start,whole);if(anchor>end){whole--;anchor=addMonthsClamped(start,whole);}remainder=daysUntil(end,anchor);}
  const unitName=countdownUnits.find(item=>item.value===unit).label;
  const value=days===0?'今天':whole?`${whole}${unit==='months'?'个月':'周'}${remainder?`${remainder}天`:''}`:String(absolute);
  return {days,value,unit,unitName,whole,remainder,label:days===0?unitName:`${whole?'':'天'}${days>0?'后':'前'}`,hint:`当前以${unitName}显示；不足一${unit==='months'?'个月':unitName}时显示剩余天数，点击切换天、周、月`};
}
export function resolveCountdown(row,events){const event=row.eventId?events.find(event=>event.id===row.eventId):null;return {...row,...(event?{title:event.title,date:event.date}:{}),linked:!!event,missingSource:!!row.eventId&&!event};}
export function sortedEvents(events, date) { return events.filter(row=>row.date === date).slice().sort((a,b)=>a.time.localeCompare(b.time) || a.title.localeCompare(b.title,'zh-CN')); }
export function reorder(items, source, target) { const rows = items.slice(); const from = rows.findIndex(r=>r.id===source), to = rows.findIndex(r=>r.id===target); if (from < 0 || to < 0 || from === to) return rows; const [row] = rows.splice(from,1); rows.splice(to,0,row); return rows.map((r,i)=>({...r,order:i})); }
export function minCardHeight(width,height){return width<600?Math.min(160,Math.max(100,(height-96)/3)):160;}
export function fitRect(rect, width, height) {
  const w = Math.min(Math.max(80, Number(rect.width) || 320), Math.max(80,width-32));
  const h = Math.min(Math.max(minCardHeight(width,height), Number(rect.height) || 300), Math.max(100,height-32));
  const x = Math.min(1,Math.max(0,Number(rect.x) || 0)), y = Math.min(1,Math.max(0,Number(rect.y) || 0));
  return {left:16+x*Math.max(0,width-w-32),top:16+y*Math.max(0,height-h-32),width:w,height:h};
}
export function fitScreenRect(rect,width,height,viewport) {
  if(!viewport || !(viewport.width>0) || !(viewport.height>0))return fitRect(rect,width,height);
  // Keep the saved profile intact so reconnecting the original screen restores it.
  return fitRect({...rect,width:rect.width*width/viewport.width,height:rect.height*height/viewport.height},width,height);
}
export function defaultRects(width,height) {
  const todoWidth = Math.min(320,width-32), weekWidth = Math.min(600, Math.max(310,width-todoWidth-80));
  const make=(left,top,w,h)=>({x:Math.max(0,left-16)/Math.max(1,width-w-32),y:Math.max(0,top-16)/Math.max(1,height-h-32),width:w,height:h});
  if (width < 1100 || height < 760) {
    const availableHeight=height-64;
    if(width>=600){
      const sideWidth=Math.min(320,Math.max(260,(width-72)*.42)),mainWidth=width-sideWidth-72;
      const sideHeight=(availableHeight-24)/2;
      return {schedule:make(24,32,mainWidth,availableHeight),todo:make(width-sideWidth-24,32,sideWidth,sideHeight),countdown:make(width-sideWidth-24,32+sideHeight+24,sideWidth,sideHeight)};
    }
    const available=height-96,scheduleHeight=available>=480?Math.min(300,available-320):available/3,sideHeight=(available-scheduleHeight)/2;
    return {todo:make(24,24,width-48,sideHeight),schedule:make(24,24+sideHeight+16,width-48,scheduleHeight),countdown:make(24,24+sideHeight+16+scheduleHeight+16,width-48,sideHeight)};
  }
  const right = width-todoWidth-42;
  return {todo:make(right,66,todoWidth,330),schedule:make(Math.max(40,right-weekWidth-24),66,weekWidth,490),countdown:make(right,420,todoWidth,290)};
}
export const themePresets = {'深色':{tint:'#202632',color:'#f4f5f7',accent:'#b8e8d6'},'浅色':{tint:'#edf3ef',color:'#253a32',accent:'#34785c'}};
export const defaultStyle = {theme:'深色',mode:'毛玻璃',background:'颜色',effect:'毛玻璃',presentation:'列表',transparency:55,blur:18,...themePresets['深色'],font:'Microsoft YaHei UI',fontSize:15,fontWeight:500,numberWeight:600,radius:22,image:''};
export function normalizeStyle(style = {}) {
  style=style&&typeof style==='object'?style:{};
  const number=(value,fallback)=>Number.isFinite(Number(value))?Number(value):fallback;
  const hex = value=>/^#[a-f\d]{6}$/i.test(value ?? '');
  const preset=themePresets[style.theme]??themePresets['深色'];
  const background=['颜色','图片','无'].includes(style.background)?style.background:style.mode==='图片'?'图片':style.mode==='纯透明'?'无':'颜色';
  const effect=['毛玻璃','无模糊'].includes(style.effect)?style.effect:['图片','纯透明','半透明'].includes(style.mode)?'无模糊':'毛玻璃';
  const mode=background==='图片'?'图片':background==='无'?'纯透明':effect==='毛玻璃'?'毛玻璃':'半透明';
  return {...defaultStyle,...style, theme:style.theme==='浅色'?'浅色':'深色',background,effect,mode,presentation:style.presentation==='简约'?'简约':'列表',
    transparency:Math.min(100,Math.max(0,number(style.transparency ?? defaultStyle.transparency,defaultStyle.transparency))),
    blur:Math.min(40,Math.max(0,number(style.blur ?? defaultStyle.blur,defaultStyle.blur))),
    fontSize:Math.min(32,Math.max(11,number(style.fontSize ?? defaultStyle.fontSize,defaultStyle.fontSize))),
    fontWeight:Math.min(900,Math.max(100,Math.round(number(style.fontWeight ?? defaultStyle.fontWeight,defaultStyle.fontWeight)))),
    numberWeight:Math.min(900,Math.max(100,Math.round(number(style.numberWeight ?? defaultStyle.numberWeight,defaultStyle.numberWeight)))),
    radius:Math.min(40,Math.max(0,number(style.radius ?? defaultStyle.radius,defaultStyle.radius))),
    tint:hex(style.tint)?style.tint:preset.tint,color:hex(style.color)?style.color:preset.color,accent:hex(style.accent)?style.accent:preset.accent,
    image:/^https:\/\/assets\.desk\.local\/backgrounds\/[a-f\d]+\.(png|jpe?g|webp|bmp)$/i.test(style.image ?? '')?style.image:''};
}
export function id() { return globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`; }
export function el(tag, className='', text) { const node=document.createElement(tag); if(className) node.className=className; if(text!==undefined) node.textContent=text; return node; }
export function button(label, action, className='') { const node=el('button',className,label); node.type='button'; node.addEventListener('click',action); return node; }
