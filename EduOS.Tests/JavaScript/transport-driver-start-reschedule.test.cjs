const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,
 '../../EduOS.App/wwwroot/js/transport-operations/vehicle-driver-reschedule.js'),'utf8');
function harness(answer,failFirst=false){
 const calls=[],messages=[];
 const u={today:()=> '2026-10-25',endpoint:'/api/transport/vehicle-driver-links',
  api:async(...args)=>{calls.push(args);if(failFirst&&calls.length===1)throw Error('temporary error');return{};},
  notify:(...args)=>messages.push(args),
  loadLinks:async()=>{calls.push(['reload']);}};
 vm.runInNewContext(source,{window:{vehicleDriverUI:u,prompt:()=>answer,
   crypto:{randomUUID:()=> '33333333-3333-4333-8333-333333333333'}},
  Date,Number,encodeURIComponent});
 return {u,calls,messages};
}
const row={id:19,effectiveFrom:'2026-11-01',effectiveTo:'2026-12-01',
 licenseExpiresOn:'2026-12-31',isScheduled:true,isCancelled:false,rowVersion:'version'};
test('reschedule posts authorized existing assignment and refreshes',async()=>{
 const h=harness('2026-11-10');
 await h.u.rescheduleUpcomingDriver(row);
 assert.equal(h.calls[0][0],'/api/transport/vehicle-driver-links/19/reschedule-start');
 assert.equal(h.calls[0][1],'POST');
 assert.equal(h.calls[0][2].effectiveFrom,'2026-11-10');
 assert.equal(h.calls[0][2].rowVersion,'version');
 assert.equal(h.calls[0][2].clientRequestId,'33333333-3333-4333-8333-333333333333');
 assert.deepEqual(h.calls[1],['reload']);
});
test('retry after a network error preserves the request ID',async()=>{
 const h=harness('2026-11-10',true);
 await h.u.rescheduleUpcomingDriver(row);
 await h.u.rescheduleUpcomingDriver(row);
 assert.equal(h.calls.length,3);
 assert.equal(h.calls[0][2].clientRequestId,h.calls[1][2].clientRequestId);
});
test('rejects start beyond license or assignment end',async()=>{
 for(const date of ['2027-01-01','2026-10-25','2026-11-31']){
  const h=harness(date);await h.u.rescheduleUpcomingDriver(row);
  assert.equal(h.calls.length,0);assert.equal(h.messages.length,1);
 }
});
test('cannot reschedule cancelled or operating assignments',async()=>{
 const h=harness('2026-11-10');
 await h.u.rescheduleUpcomingDriver({...row,isScheduled:false});
 await h.u.rescheduleUpcomingDriver({...row,isCancelled:true});
 assert.equal(h.calls.length,0);
});
