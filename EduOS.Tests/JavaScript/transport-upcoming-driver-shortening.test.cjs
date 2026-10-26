const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,
 '../../EduOS.App/wwwroot/js/transport-operations/vehicle-driver-upcoming-shorten.js'),'utf8');
function harness(answer,confirmation=true,failFirst=false){
 const calls=[],messages=[];
 const u={today:()=> '2026-10-26',endpoint:'/api/transport/vehicle-driver-links',
  api:async(...args)=>{calls.push(args);if(failFirst&&calls.length===1)throw Error('temporary error');return{};},
  notify:(...args)=>messages.push(args),loadLinks:async()=>{calls.push(['reload']);}};
 vm.runInNewContext(source,{window:{vehicleDriverUI:u,prompt:()=>answer,confirm:()=>confirmation,
   crypto:{randomUUID:()=> '44444444-4444-4444-8444-444444444444'}},
  Date,Number,encodeURIComponent});
 return {u,calls,messages};
}
const row={id:19,effectiveFrom:'2026-11-01',effectiveTo:'2026-11-30',
 isScheduled:true,isCancelled:false,rowVersion:'version'};
test('shortens canonical future driver reservation and refreshes',async()=>{
 const h=harness('2026-11-15');await h.u.shortenUpcomingDriver(row);
 assert.equal(h.calls[0][0],'/api/transport/vehicle-driver-links/19/shorten-upcoming');
 assert.equal(h.calls[0][1],'POST');
 assert.equal(h.calls[0][2].effectiveTo,'2026-11-15');
 assert.equal(h.calls[0][2].rowVersion,'version');
 assert.equal(h.calls[0][2].clientRequestId,'44444444-4444-4444-8444-444444444444');
 assert.deepEqual(h.calls[1],['reload']);
});
test('failed request retries with the same key',async()=>{
 const h=harness('2026-11-15',true,true);
 await h.u.shortenUpcomingDriver(row);
 await h.u.shortenUpcomingDriver(row);
 assert.equal(h.calls.length,3);
 assert.equal(h.calls[0][2].clientRequestId,h.calls[1][2].clientRequestId);
});
test('rejects invalid and out-of-range dates',async()=>{
 for(const date of ['2026-11-30','2026-12-01','2026-10-31','2026-11-31','bad']){
  const h=harness(date);await h.u.shortenUpcomingDriver(row);
  assert.equal(h.calls.length,0);assert.equal(h.messages.length,1);
 }
});
test('rejects cancelled, started, operating and open-ended',async()=>{
 const h=harness('2026-11-15');
 for(const x of [{...row,isCancelled:true},{...row,effectiveFrom:'2026-10-20'},
  {...row,effectiveTo:null},{...row,isScheduled:false}])
  await h.u.shortenUpcomingDriver(x);
 assert.equal(h.calls.length,0);
});
test('confirmation cancellation does not mutate',async()=>{
 const h=harness('2026-11-15',false);
 await h.u.shortenUpcomingDriver(row);
 assert.equal(h.calls.length,0);
});
