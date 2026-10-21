const native = Boolean(globalThis.chrome?.webview);
let receiver;
let serial = 0;
const pending = new Map();
const initial = ()=>({todos:[],events:[],countdowns:[],layout:{}});
export const bridge = {
  native,
  listen(handler) {
    receiver=handler;
    if(native) chrome.webview.addEventListener('message',event=>{
      const message=event.data;
      if(message.type==='edit-preview'){pending.get(message.requestId)?.onPreview?.(message.value);}
      else if(message.type==='result') { const callbacks=pending.get(message.requestId); if(callbacks) { pending.delete(message.requestId); message.error?callbacks.reject(Error(message.error)):callbacks.resolve(message.value); } }
      else handler(message);
    });
  },
  ready() {
    if(native) this.send({type:'ready'});
    else { let data; try { data=JSON.parse(localStorage.getItem('desktop-panel-preview') || 'null'); } catch {} receiver({type:'init',data:data ?? initial(),environment:{monitor:'browser-preview',desktop:false,wallpaper:'',smoke:false}}); }
  },
  send(message) { if(native) chrome.webview.postMessage(message); },
  request(message,onPreview) {
    if(!native) {
      if(message.type==='save') { const data=JSON.parse(localStorage.getItem('desktop-panel-preview') || 'null') ?? initial(); data[message.key]=message.value; localStorage.setItem('desktop-panel-preview',JSON.stringify(data)); return Promise.resolve(true); }
      if(message.type==='edit') return browserEditor(message.title,message.fields,onPreview,message.previewWidget);
      if(message.type==='pick-image') return Promise.resolve(null);
    }
    const requestId=String(++serial);
    return new Promise((resolve,reject)=>{pending.set(requestId,{resolve,reject,onPreview});this.send({...message,requestId});});
  }
};
export function browserEditor(title,fields,onPreview,previewWidget) {
  return new Promise(resolve=>{
    const dialog=document.createElement('dialog'); const form=document.createElement('form'); form.method='dialog';
    const heading=document.createElement('h2'); heading.textContent=title; form.append(heading);
    let preview;
    if(previewWidget){preview=document.createElement('div');preview.className='appearance-preview';preview.setAttribute('aria-label','外观预览');form.append(preview);}
    const fieldArea=document.createElement('div');fieldArea.className='editor-fields';form.append(fieldArea);
    if(fields.length>5)dialog.className='settings-dialog';
    for(const field of fields) {
      const label=document.createElement('label'); label.textContent=field.label;
      let input;
      if(field.type==='select') {input=document.createElement('select'); for(const value of field.options) {const option=document.createElement('option');option.value=typeof value==='object'?value.value:value;option.textContent=typeof value==='object'?value.label:value;input.append(option);}}
      else {input=document.createElement('input');input.type=field.type==='number'?'number':['date','time','color','range'].includes(field.type)?field.type:'text';}
      input.name=field.name;if(field.min!==undefined) input.min=field.min; if(field.max!==undefined) input.max=field.max;
      if(field.step!==undefined)input.step=field.step;
      input.value=field.value??'';
      if(field.name==='title') {input.required=true;input.maxLength=1000;}
      label.append(input);if(field.type==='range'){const output=document.createElement('output');output.value=input.value;input.addEventListener('input',()=>{output.value=input.value;});label.append(output);}fieldArea.append(label);
      if(field.presets){if(field.name==='theme')dialog.dataset.theme=field.value;input.onchange=()=>{if(field.name==='theme')dialog.dataset.theme=input.value;for(const [name,value] of Object.entries(field.presets[input.value]??{})){const control=fieldArea.querySelector(`[name="${name}"]`);if(control)control.value=value;}};}
    }
    const refreshPreview=()=>{
      onPreview?.(Object.fromEntries(new FormData(form)));
      if(preview){const source=document.querySelector(`#widget-${previewWidget}`);if(source){const clone=source.cloneNode(true);for(const node of [clone,...clone.querySelectorAll('[id]')])node.removeAttribute('id');for(const node of clone.querySelectorAll('button')){node.tabIndex=-1;node.setAttribute('aria-hidden','true');}clone.style.left='0';clone.style.top='0';clone.style.width='100%';clone.style.height='100%';clone.classList.remove('compact');clone.classList.add('preview-card');preview.replaceChildren(clone);}}
    };
    form.addEventListener('input',refreshPreview);form.addEventListener('change',refreshPreview);
    const actions=document.createElement('div');actions.className='dialog-actions';
    const cancel=document.createElement('button');cancel.type='button';cancel.textContent='取消';cancel.onclick=()=>dialog.close('cancel');
    const save=document.createElement('button');save.type='submit';save.textContent='保存';actions.append(cancel,save);form.append(actions);dialog.append(form);document.body.append(dialog);
    form.onsubmit=event=>{event.preventDefault();dialog.close('save');};
    dialog.addEventListener('close',()=>{const value=dialog.returnValue==='save'?Object.fromEntries(new FormData(form)):null;dialog.remove();resolve(value);});
    dialog.showModal();if(previewWidget)refreshPreview();
  });
}
