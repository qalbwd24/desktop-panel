import {el,button,id,localDate,validDate,countdownUnits,countdownDistance,nextUnit,resolveCountdown} from '../core.js';
export function createCountdown(api) {
  const widget=api.card('countdown','倒计时','');
  async function edit(row) {
    const current=row?resolveCountdown(row,api.data.events):null;
    const options=[{value:'',label:'独立日期'},...api.data.events.map(event=>({value:event.id,label:`${event.date} ${event.time} · ${event.title}`}))];
    const presets=Object.fromEntries(api.data.events.map(event=>[event.id,{title:event.title,date:event.date}]));
    const value=await api.edit(row?'编辑倒计时':'新增倒计时',[
      {name:'eventId',label:'关联日程',type:'select',options,presets,value:row?.eventId??''},
      {name:'title',label:'名称',type:'text',value:current?.title??''},
      {name:'date',label:'目标日期',type:'date',value:current?.date??localDate()},
      {name:'unit',label:'显示单位',type:'select',options:countdownUnits,value:row?.unit??'days'}
    ]);
    if(!value)return;
    const source=value.eventId?api.data.events.find(event=>event.id===value.eventId):null;
    if(value.eventId&&!source)throw Error('关联日程已更新或删除，请重新选择');
    const title=source?.title??value.title.trim(),date=source?.date??value.date;
    if(!title||!validDate(date))throw Error('请填写名称和有效日期');
    const entry={...row,id:row?.id??id(),title,date,eventId:value.eventId||null,unit:value.unit};
    await api.change('countdowns',items=>row?items.map(item=>item.id===row.id?entry:item):[...items,entry]);
  }
  function render() {
    widget.body.replaceChildren();
    const rows=api.data.countdowns.map(row=>resolveCountdown(row,api.data.events)).sort((a,b)=>a.date.localeCompare(b.date));
    if(!rows.length) widget.body.append(api.empty('暂无倒计时',''));
    for(const row of rows) {
      const distance=countdownDistance(row.date,row.unit);const line=el('div','countdown-row');line.dataset.id=row.id;const detail=el('div','countdown-detail');
      const target=el('span','target-date');const [year,month,day]=row.date.split('-');
      target.append(el('span','date-regular',`${row.linked?'↗ ':''}${row.date.replaceAll('-',' / ')}`),el('span','date-simple',`${year}年${Number(month)}月${Number(day)}日`));
      if(row.missingSource)target.append(el('span','',' · 日程已删除'));target.title=row.linked?'跟随关联日程的名称和日期':row.missingSource?'原日程已删除，保留最后保存的日期；可重新关联或改为独立日期':'';
      detail.append(button(row.title,()=>api.run(()=>edit(row)),'item-title'),target);
      const number=button('',()=>api.run(()=>api.change('countdowns',items=>items.map(item=>item.id===row.id?{...item,unit:nextUnit(item.unit)}:item))),`remaining ${distance.days<0?'past':''}`);
      number.dataset.mixed=String(distance.whole>0);
      number.title=distance.hint;number.setAttribute('aria-label',`${row.title}倒计时 ${distance.value}${distance.label}，切换单位`);number.append(el('strong','',distance.value),el('span','',distance.label));
      const remove=button('×',()=>api.run(()=>api.change('countdowns',items=>items.filter(item=>item.id!==row.id))),'remove');remove.setAttribute('aria-label',`删除 ${row.title}`);
      line.append(detail,number,remove);widget.body.append(line);
    }
  }
  widget.actions.append(button('+ 添加',()=>api.run(()=>edit()),'add-button'));
  return {render};
}
