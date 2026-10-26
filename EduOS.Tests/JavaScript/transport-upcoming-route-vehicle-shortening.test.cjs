const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,
 '../../EduOS.App/wwwroot/js/transport-operations/route-vehicle-upcoming-shorten.js'),'utf8');
function harness(answer,confirmation=true){
 const calls=[],messages=[];
 const u={today:()=> '2026-10-26',api:async(...args)=>{calls.push(args);return{};},
  notify:(...args)=>messages.push(args),load:async()=>{calls.push(['reload']);}};
 vm.runInNewContext(source,{window:{transportLink:u,prompt:()=>answer,confirm:()=>confirmation,
  crypto:{randomUUID:()=> '12345678-1234-4234-8234-123456789abc'}},
  Date,Number,encodeURIComponent});
 return {u,calls,messages};
}
const row={id:19,effectiveFrom:'2026-11-01',effectiveTo:'2026-11-30',
 isCancelled:false,isCurrent:false,rowVersion:'version'};
test('shortens canonical future link and refreshes list',async()=>{
 const h=harness('2026-11-15');await h.u.shortenUpcomingLink(row);
 assert.equal(h.calls[0][0],'/api/transport/route-vehicle-links/19/shorten-upcoming');
 assert.equal(h.calls[0][1],'POST');
 assert.equal(h.calls[0][2].effectiveTo,'2026-11-15');
 assert.equal(h.calls[0][2].rowVersion,'version');
 assert.equal(h.calls[0][2].clientRequestId,'12345678-1234-4234-8234-123456789abc');
 assert.deepEqual(h.calls[1],['reload']);
});
test('rejects impossible and out-of-range dates',async()=>{
 for(const date of ['2026-11-30','2026-12-01','2026-10-31','2026-11-31','bad']){
  const h=harness(date);await h.u.shortenUpcomingLink(row);
  assert.equal(h.calls.length,0);assert.equal(h.messages.length,1);
 }
});
test('rejects cancelled, operating and open-ended links',async()=>{
 const h=harness('2026-11-15');
 for(const x of [{...row,isCancelled:true},{...row,effectiveFrom:'2026-10-20'},
  {...row,effectiveTo:null}])await h.u.shortenUpcomingLink(x);
 assert.equal(h.calls.length,0);
});
test('confirmation cancellation does not mutate',async()=>{
 const h=harness('2026-11-15',false);await h.u.shortenUpcomingLink(row);
 assert.equal(h.calls.length,0);
});
