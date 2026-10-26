const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,
 '../../EduOS.App/wwwroot/js/transport-operations/vehicle-driver-upcoming-extend.js'),'utf8');
function harness(answer,confirmation=true,failFirst=false){
 const calls=[],messages=[];
 const u={today:()=> '2026-10-26',endpoint:'/api/transport/vehicle-driver-links',
  api:async(...args)=>{calls.push(args);if(failFirst&&calls.length===1)throw Error('temporary error');return{};},
  notify:(...args)=>messages.push(args),
  loadLinks:async()=>{calls.push(['reload']);}};
 vm.runInNewContext(source,{window:{vehicleDriverUI:u,prompt:()=>answer,confirm:()=>confirmation,
   crypto:{randomUUID:()=> '22222222-2222-4222-8222-222222222222'}},
  Date,Number,encodeURIComponent});
 return {u,calls,messages};
}
const row={id:19,effectiveFrom:'2026-11-01',effectiveTo:'2026-11-30',
 licenseExpiresOn:'2026-12-31',isScheduled:true,isCancelled:false,rowVersion:'version'};
test('posts authorized extension of canonical future reservation and refreshes',async()=>{
 const h=harness('2026-12-15');
 await h.u.extendUpcomingDriver(row);
 assert.equal(h.calls[0][0],'/api/transport/vehicle-driver-links/19/extend-upcoming');
 assert.equal(h.calls[0][1],'POST');
 assert.equal(h.calls[0][2].effectiveTo,'2026-12-15');
 assert.equal(h.calls[0][2].rowVersion,'version');
 assert.equal(h.calls[0][2].clientRequestId,'22222222-2222-4222-8222-222222222222');
 assert.deepEqual(h.calls[1],['reload']);
});
test('retry after a network error keeps the same durable request key',async()=>{
 const h=harness('2026-12-15',true,true);
 await h.u.extendUpcomingDriver(row);
 await h.u.extendUpcomingDriver(row);
 assert.equal(h.calls.length,3);
 assert.equal(h.calls[0][2].clientRequestId,h.calls[1][2].clientRequestId);
 assert.deepEqual(h.calls[2],['reload']);
});
test('rejects invalid, unextended or unlicensed end dates',async()=>{
 for(const date of ['2026-11-30','2027-01-01','2026-12-32']){
  const h=harness(date);await h.u.extendUpcomingDriver(row);
  assert.equal(h.calls.length,0);assert.equal(h.messages.length,1);
 }
});
test('rejects cancelled and operating reservations',async()=>{
 const h=harness('2026-12-15');
 await h.u.extendUpcomingDriver({...row,isScheduled:false});
 await h.u.extendUpcomingDriver({...row,isCancelled:true});
 assert.equal(h.calls.length,0);
});
test('does not post when user cancels confirmation',async()=>{
 const h=harness('2026-12-15',false);
 await h.u.extendUpcomingDriver(row);
 assert.equal(h.calls.length,0);
});
