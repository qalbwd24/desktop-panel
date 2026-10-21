import {el,button,id,reorder} from '../core.js';
export function createTodo(api) {
  const widget=api.card('todo','待办','');
  let dragged;
  async function edit(row) {
    const value=await api.edit(row?'编辑待办':'新增待办',[{name:'title',label:'待办内容',type:'text',value:row?.title??''}]);
    if(!value) return; const title=value.title.trim(); if(!title) throw Error('请填写待办内容');
    await api.change('todos',items=>row?items.map(item=>item.id===row.id?{...item,title}:item):[...items,{id:id(),title,done:false,order:Math.max(-1,...items.map(item=>item.order??0))+1,createdAt:new Date().toISOString()}]);
  }
  function render() {
    const items=[...api.data.todos].sort((a,b)=>(a.order??0)-(b.order??0)); const remaining=items.filter(row=>!row.done).length;
    widget.subtitle.textContent=items.length?`${remaining} 项未完成`:'';
    widget.body.replaceChildren();
    if(!items.length) widget.body.append(api.empty('暂无待办',''));
    for(const row of items) {
      const line=el('div',`todo-row ${row.done?'completed':''}`);line.draggable=true;line.dataset.id=row.id;
      const check=button(row.done?'✓':'',()=>api.run(()=>api.change('todos',items=>items.map(item=>item.id===row.id?{...item,done:!item.done,completedAt:!item.done?new Date().toISOString():null}:item))),'check');
      check.setAttribute('aria-label',`${row.done?'取消完成':'完成'} ${row.title}`); check.setAttribute('aria-pressed',String(row.done));
      const title=button(row.title,()=>api.run(()=>edit(row)),'item-title');
      const remove=button('×',()=>api.run(()=>api.change('todos',items=>items.filter(item=>item.id!==row.id))),'remove');remove.title='删除待办';remove.setAttribute('aria-label',`删除 ${row.title}`);
      const grip=el('span','row-grip','⠿');grip.title='拖动排序';line.append(check,title,remove,grip);
      line.addEventListener('dragstart',event=>{dragged=row.id;event.dataTransfer.effectAllowed='move';event.dataTransfer.setData('text/plain',row.id);});
      line.addEventListener('dragover',event=>{event.preventDefault();line.classList.add('drop-target');});
      line.addEventListener('dragleave',()=>line.classList.remove('drop-target'));
      line.addEventListener('drop',event=>{event.preventDefault();api.run(()=>api.change('todos',items=>reorder(items.sort((a,b)=>(a.order??0)-(b.order??0)),dragged,row.id)));});
      widget.body.append(line);
    }
  }
  widget.actions.append(button('+ 添加',()=>api.run(()=>edit()),'add-button'));
  return {render};
}
