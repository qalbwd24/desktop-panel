import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile,mkdir,writeFile} from 'node:fs/promises';
import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
test('本地 API：隔离、校验、冲突、并发写入和持久化',{skip:process.platform!=='win32',timeout:30000},async()=>{
  const folder=path.join(root,'.verification',`api-${Date.now()}`);await mkdir(folder,{recursive:true});
  const port=18879,base=`http://127.0.0.1:${port}`;
  let child;
  async function start(){
    child=spawn(path.join(root,'bin','DesktopPanel.exe'),['--api-only','--data-dir',folder,'--api-port',String(port)],{cwd:root,windowsHide:true,stdio:'ignore'});
    for(let i=0;i<80;i++){try{if((await fetch(`${base}/api/health`)).ok)return;}catch{}await new Promise(resolve=>setTimeout(resolve,100));}
    throw Error('API 启动超时');
  }
  async function stop(){if(child&&child.exitCode===null){const exited=new Promise(resolve=>child.once('exit',resolve));child.kill();await exited;}}
  try{
    await start();const config=JSON.parse(await readFile(path.join(folder,'api.json'),'utf8'));
    const call=async(route,method='GET',body,revision)=>{
      const response=await fetch(`${base}/api/v1/${route}`,{method,headers:{Authorization:`Bearer ${config.token}`,...(body?{'Content-Type':'application/json'}:{}),...(revision?{'If-Match':`"${revision}"`}:{})},...(body?{body:JSON.stringify(body)}:{})});
      return {status:response.status,data:await response.json(),etag:response.headers.get('etag')};
    };
    assert.equal((await fetch(`${base}/api/v1/todos`)).status,401);
    assert.equal((await fetch(`${base}/api/v1/todos`,{headers:{Authorization:`Bearer ${config.token}`,Origin:'https://unrelated.example'}})).status,403);
    assert.equal((await fetch(`${base}/data/api.json`)).status,404);
    assert.equal((await fetch(`${base}/session/nonexistent`)).status,401);
    assert.equal((await fetch(`${base}/admin.html`)).status,200);
    const first=await call('todos');assert.deepEqual(first.data.items,[]);
    const added=await call('todos','POST',{title:'中文 <b>保留</b>'},first.data.revision);assert.equal(added.status,201);
    const id=added.data.item.id;assert.ok(id);assert.ok(added.data.item.createdAt);assert.equal(added.data.item.done,false);
    assert.equal((await call(`todos/${id}`,'PATCH',{done:true},first.data.revision)).status,409);
    const updated=await call(`todos/${id}`,'PATCH',{done:true},added.data.revision);assert.equal(updated.status,200);
    assert.ok(updated.data.item.completedAt);
    assert.equal((await call(`todos/${id}`,'PATCH',{order:100})).status,200);
    const ordered=await call('todos','POST',{title:'放在末尾'});assert.equal(ordered.data.item.order,101);
    assert.equal((await call(`todos/${ordered.data.item.id}`,'DELETE')).status,200);
    assert.equal((await call(`todos/${id}`,'PATCH',{id:'other'})).status,400);
    assert.equal((await call('events','POST',{title:'错误日期',date:'2026-02-30',time:'10:00'})).status,400);
    assert.equal((await call('events','POST',{title:'图书馆',date:'2026-09-30',time:'14:30'})).status,201);
    assert.equal((await call('countdowns','POST',{title:'项目截止',date:'2026-10-12'})).status,201);
    assert.equal((await call('countdowns','POST',{title:'错误单位',date:'2026-10-12',unit:'hours'})).status,400);
    assert.equal((await call('layout','PUT',{screens:{}})).status,428);
    const layout=await call('layout');assert.equal((await call('layout','PUT',{screens:{},styles:{}},layout.data.revision)).status,200);
    assert.equal((await call('layout','PUT',{screens:{}},layout.data.revision)).status,409);
    const currentLayout=await call('layout');assert.equal((await call('layout','PUT',{styles:{todo:null}},currentLayout.data.revision)).status,400);
    assert.equal((await call('layout','PUT',{styles:{countdown:{background:'invalid'}}},currentLayout.data.revision)).status,400);
    assert.equal((await call('layout','PUT',{styles:{countdown:{effect:'invalid'}}},currentLayout.data.revision)).status,400);
    assert.equal((await call('layout','PUT',{styles:{countdown:{presentation:'invalid'}}},currentLayout.data.revision)).status,400);
    assert.equal((await call('layout','PUT',{styles:{countdown:{numberWeight:50}}},currentLayout.data.revision)).status,400);
    assert.equal((await call('layout','PUT',{styles:{countdown:{fontWeight:'bold'}}},currentLayout.data.revision)).status,400);
    const newStyle={background:'图片',effect:'毛玻璃',transparency:70,presentation:'简约',fontWeight:700,numberWeight:800,image:'https://assets.desk.local/backgrounds/abcd1234.png'};
    assert.equal((await call('layout','PUT',{styles:{countdown:newStyle}},currentLayout.data.revision)).status,200);
    const concurrent=await Promise.all(Array.from({length:12},(_,i)=>call('todos','POST',{title:`并发 ${i}`})));
    assert.ok(concurrent.every(result=>result.status===201));assert.equal((await call('todos')).data.items.length,13);
    await stop();await start();const persisted=await call('todos');assert.equal(persisted.data.items.length,13);assert.equal(persisted.data.items.find(row=>row.id===id).done,true);
    assert.deepEqual((await call('layout')).data.items.styles.countdown,newStyle);
    assert.equal((await call(`todos/${id}`,'DELETE',undefined,persisted.data.revision)).status,200);
    assert.equal((await call('todos')).data.items.length,12);
    await writeFile(path.join(folder,'result.json'),JSON.stringify({passed:true,checks:['authentication','origin','no-file-exposure','crud','utf8','validation','version-conflict','concurrency','restart'],folder},null,2));
    console.log(`API 验证目录：${folder}`);
  }finally{await stop();}
});
