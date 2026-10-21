import test from 'node:test';
import assert from 'node:assert/strict';
import {validDate,weekDates,daysUntil,addDays,sortedEvents,reorder,fitRect,fitScreenRect,normalizeStyle,defaultRects,countdownDistance,resolveCountdown,nextUnit} from '../ui/core.js';
test('日期边界与跨年周一开始的周历',()=>{
  assert.equal(validDate('2026-02-29'),false);assert.equal(validDate('2028-02-29'),true);
  assert.deepEqual(weekDates('2027-01-01'),['2026-12-28','2026-12-29','2026-12-30','2026-12-31','2027-01-01','2027-01-02','2027-01-03']);
  assert.equal(addDays('2028-02-28',1),'2028-02-29');
});
test('天周月按整单位加余数显示，覆盖月末、闰月和未满一周/月',()=>{
  assert.equal(countdownDistance('2026-10-12','weeks','2026-09-30').value,'1周5天');
  assert.equal(countdownDistance('2026-02-28','months','2026-01-31').value,'1个月');
  assert.equal(countdownDistance('2028-02-29','months','2028-01-31').value,'1个月');
  assert.equal(countdownDistance('2026-03-05','months','2026-01-31').value,'1个月5天');
  assert.equal(countdownDistance('2026-10-01','months','2026-09-30').value,'1');
  assert.equal(countdownDistance('2026-10-05','weeks','2026-09-30').value,'5');
  assert.equal(countdownDistance('2026-09-30','months','2026-09-30').value,'今天');
  assert.equal(countdownDistance('2026-01-31','months','2026-02-28').label,'前');
  assert.equal(nextUnit('days'),'weeks');assert.equal(nextUnit('weeks'),'months');assert.equal(nextUnit('months'),'days');
});
test('关联倒计时跟随日程编辑，原记录保留；来源删除可识别并使用保存日期',()=>{
  const row={id:'countdown',eventId:'event',title:'旧标题',date:'2026-09-30'};
  const resolved=resolveCountdown(row,[{id:'event',title:'新标题',date:'2026-10-12'}]);
  assert.equal(resolved.title,'新标题');assert.equal(resolved.date,'2026-10-12');assert.equal(resolved.linked,true);assert.equal(row.date,'2026-09-30');
  const missing=resolveCountdown(row,[]);assert.equal(missing.missingSource,true);assert.equal(missing.date,row.date);
});
test('倒计时按日期而非 24 小时计算，含今天及过去',()=>{
  assert.equal(daysUntil('2026-09-30','2026-09-30'),0);assert.equal(daysUntil('2026-10-01','2026-09-30'),1);
  assert.equal(daysUntil('2026-03-09','2026-03-07'),2);assert.equal(daysUntil('2026-09-29','2026-09-30'),-1);
});
test('日程只取选定日期，按时间排序且不改变来源',()=>{
  const rows=[{date:'2026-09-30',time:'19:00',title:'晚'},{date:'2026-09-30',time:'09:00',title:'早'},{date:'2026-10-01',time:'08:00',title:'明天'}];
  assert.deepEqual(sortedEvents(rows,'2026-09-30').map(r=>r.title),['早','晚']);assert.equal(rows[0].title,'晚');
});
test('待办排序双向移动、状态保留及未知编号',()=>{
  const rows=[{id:'a',done:true},{id:'b',done:false},{id:'c',done:false}];
  assert.deepEqual(reorder(rows,'a','c').map(r=>r.id),['b','c','a']);
  assert.deepEqual(reorder(rows,'c','a').map(r=>r.id),['c','a','b']);
  assert.equal(reorder(rows,'a','c')[2].done,true);assert.deepEqual(reorder(rows,'z','a'),rows);
});
test('小屏、竖屏、超宽屏、DPI 逻辑尺寸内组件不会越界',()=>{
  for(const [width,height] of [[640,480],[900,1600],[1920,1080],[3440,1440],[1280,720]]) {
    const r=fitRect({x:1.4,y:-.5,width:1200,height:1700},width,height);
    assert.ok(r.left>=0&&r.top>=0&&r.left+r.width<=width&&r.top+r.height<=height);
  }
});
test('保存的尺寸跟随屏幕比例缩放；反复切换不会改写原布局',()=>{
  const source={x:.98,y:.8,width:600,height:400},viewport={width:1920,height:1080};
  const original=structuredClone(source);
  for(const [width,height] of [[1280,720],[2560,1440],[1080,1920],[3440,1440],[640,480]]){
    const rect=fitScreenRect(source,width,height,viewport);
    assert.ok(rect.left>=16&&rect.top>=16&&rect.left+rect.width<=width-16&&rect.top+rect.height<=height-16);
    assert.equal(rect.width,600*width/1920);
  }
  assert.deepEqual(source,original);assert.deepEqual(fitScreenRect(source,1920,1080,viewport),fitRect(source,1920,1080));
  assert.equal(fitScreenRect(source,1280,720,viewport).height,400*720/1080);
});
test('外观范围及背景路径约束',()=>{
  const style=normalizeStyle({fontSize:99,transparency:100,blur:-1,image:'javascript:alert(1)',tint:'red'});
  assert.equal(style.fontSize,32);assert.equal(style.transparency,100);assert.equal(style.blur,0);assert.equal(style.image,'');assert.equal(style.tint,'#202632');
  assert.equal(normalizeStyle({fontSize:'bad'}).fontSize,15);
  assert.equal(normalizeStyle({theme:'浅色'}).color,'#253a32');assert.equal(normalizeStyle(null).theme,'深色');
  assert.equal(normalizeStyle({}).numberWeight,600);
  assert.equal(normalizeStyle({fontWeight:1200,numberWeight:20}).fontWeight,900);
  assert.equal(normalizeStyle({fontWeight:1200,numberWeight:20}).numberWeight,100);
  assert.equal(normalizeStyle({fontWeight:'bad'}).fontWeight,500);
});
test('手动宽度保留，不再强制恢复 260 像素宽；可扩大至屏幕边缘',()=>{
  assert.equal(fitRect({width:160,height:200},1920,1080).width,160);
  assert.equal(fitRect({width:1800,height:200},1920,1080).width,1800);
  assert.equal(fitRect({width:5000,height:200},1920,1080).width,1888);
});
test('旧背景配置兼容迁移，图片在调透明度和切换背景时保留',()=>{
  const image='https://assets.desk.local/backgrounds/abcd1234.png';
  const legacy=normalizeStyle({mode:'图片',image,transparency:20});
  assert.equal(legacy.background,'图片');assert.equal(legacy.effect,'无模糊');
  assert.equal(normalizeStyle({mode:'纯透明'}).background,'无');
  assert.equal(normalizeStyle({mode:'半透明'}).effect,'无模糊');
  const changed=normalizeStyle({...legacy,transparency:75,effect:'毛玻璃',presentation:'简约'});
  assert.equal(changed.image,image);assert.equal(changed.background,'图片');assert.equal(changed.effect,'毛玻璃');assert.equal(changed.presentation,'简约');
  const switched=normalizeStyle({...changed,background:'颜色'});
  assert.equal(switched.image,image);assert.equal(normalizeStyle({...switched,background:'图片'}).image,image);
});
test('默认布局在小屏、竖屏和桌面尺寸下互不遮挡',()=>{
  for(const [width,height] of [[640,480],[900,1600],[1920,1080],[3440,1440],[1280,720],[1128,790],[580,730],[400,480],[400,600]]) {
    const rectangles=Object.values(defaultRects(width,height)).map(row=>fitRect(row,width,height));
    for(let i=0;i<rectangles.length;i++)for(let j=i+1;j<rectangles.length;j++){
      const a=rectangles[i],b=rectangles[j];
      const intersects=Math.min(a.left+a.width,b.left+b.width)>Math.max(a.left,b.left)+1&&Math.min(a.top+a.height,b.top+b.height)>Math.max(a.top,b.top)+1;
      assert.equal(intersects,false,`${width}×${height}: ${i}/${j}`);
    }
  }
});
