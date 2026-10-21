import {el,button,id,localDate,weekDates,weekdays,sortedEvents,addDays,validDate,countdownDistance,nextUnit} from '../core.js';
export function createSchedule(api) {
  const widget=api.card('schedule','日程','');
  let selected=localDate(), anchor=selected;
  async function edit(row) {
    const value=await api.edit(row?'编辑日程':'新增日程',[{name:'title',label:'日程内容',type:'text',value:row?.title??''},{name:'date',label:'日期',type:'date',value:row?.date??selected},{name:'time',label:'时间',type:'time',value:row?.time??'09:00'}]);
    if(!value) return; const title=value.title.trim(); if(!title || !validDate(value.date) || !/^([01]\d|2[0-3]):[0-5]\d$/.test(value.time)) throw Error('请检查日程内容、日期和时间');
    const entry={...row,id:row?.id??id(),title,date:value.date,time:value.time};
    await api.change('events',items=>row?items.map(item=>item.id===row.id?entry:item):[...items,entry]);
    selected=value.date;anchor=selected;render();
  }
  function render() {
    const dates=weekDates(anchor),today=localDate();
    widget.subtitle.textContent=`${dates[0].slice(5).replace('-',' / ')} — ${dates[6].slice(5).replace('-',' / ')}`;
    widget.body.replaceChildren();
    const nav=el('div','week-nav');
    nav.append(button('‹',()=>{anchor=addDays(anchor,-7);selected=weekDates(anchor)[0];render();},'nav-button'),button('本周',()=>{selected=localDate();anchor=selected;render();},'text-button'),button('›',()=>{anchor=addDays(anchor,7);selected=weekDates(anchor)[0];render();},'nav-button'));
    const strip=el('div','week-strip');
    dates.forEach((date,i)=>{
      const day=button('',()=>{selected=date;render();},`day ${date===selected?'selected':''} ${date===today?'today':''}`);day.setAttribute('aria-label',`${date} ${weekdays[i]}`);
      day.append(el('span','weekday',weekdays[i]),el('strong','day-number',String(Number(date.slice(-2)))));
      if(api.data.events.some(row=>row.date===date)) day.append(el('span','event-dot'));strip.append(day);
    });
    const caption=el('div','day-caption',`${selected.slice(5).replace('-',' / ')} · ${weekdays[weekDates(selected).indexOf(selected)]}`);
    const distance=countdownDistance(selected,api.data.layout.units?.schedule??'weeks');
    const counter=button(distance.days===0?`今天 · ${distance.unitName}`:`${distance.value}${distance.label}`,()=>api.run(async()=>{const unit=nextUnit(api.data.layout.units?.schedule??'weeks');await api.layoutChange(layout=>{layout.units??={};layout.units.schedule=unit;});render();}),'date-distance');counter.title=distance.hint;counter.setAttribute('aria-label',`日程倒计时，当前${distance.unitName}，切换单位`);caption.append(counter);
    const list=el('div','event-list'),events=sortedEvents(api.data.events,selected);
    if(!events.length) list.append(api.empty('暂无日程',''));
    for(const row of events) {
      const line=el('div','event-row');line.append(el('span','event-time',row.time),button(row.title,()=>api.run(()=>edit(row)),'item-title'));
      const exists=api.data.countdowns.some(item=>item.eventId===row.id);
      const linked=button(exists?'✓':'↗',()=>api.run(()=>api.change('countdowns',items=>items.some(item=>item.eventId===row.id)?items:[...items,{id:id(),title:row.title,date:row.date,eventId:row.id,unit:'weeks'}])),'event-countdown');linked.title=exists?'已加入倒计时':'加入倒计时（关联日程）';linked.setAttribute('aria-label',`${exists?'已加入':'加入'}倒计时 ${row.title}`);line.append(linked);
      const remove=button('×',()=>api.run(()=>api.change('events',items=>items.filter(item=>item.id!==row.id))),'remove');remove.setAttribute('aria-label',`删除 ${row.title}`);line.append(remove);list.append(line);
    }
    widget.body.append(nav,strip,caption,list);
  }
  widget.actions.append(button('+ 添加日程',()=>api.run(()=>edit()),'add-button'));
  return {render};
}
