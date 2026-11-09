const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path');
const root=path.join(__dirname,'../../EduOS.App');
const js=fs.readFileSync(path.join(root,'wwwroot/js/admissions/offers.js'),'utf8');
const core=fs.readFileSync(path.join(root,'wwwroot/js/admissions/offers-core.js'),'utf8');
const view=fs.readFileSync(path.join(root,'Views/Admissions/Offers.cshtml'),'utf8');
test('all three mutations share one busy and conflict recovery gate',()=>{
 assert.match(js,/async function runMutation\(/);
 for(const name of ['issue','accept','enroll'])assert.match(js,new RegExp('async function '+name+'\\(\\)'));
 assert.match(js,/mutationInFlight = true/);
 assert.match(js,/mutationInFlight = false/);
 assert.match(js,/409\|412/);
 assert.match(js,/currentReference\(reference\)/);
 assert.match(js,/finally\s*\{\s*mutationInFlight = false;\s*setBusy\(false\)/);
});
test('mutation results never update a different selected applicant',()=>{
 assert.match(js,/if \(!currentReference\(reference\)\) return/);
 assert.match(js,/if \(updated && currentReference\(reference\)\) notice/);
 assert.match(core,/ctx.reference === reference/);
});
test('busy state and bilingual accessible journey exist',()=>{
 assert.match(core,/setBusy\(value\)/);
 assert.match(core,/busy \|\| pendingRefresh \|\| !qualified/);
 assert.match(view,/id="offerBusy"/);
 assert.match(view,/aria-describedby="offerJourneyStage"/);
 assert.match(view,/অনুরোধ প্রক্রিয়াধীন/);
});

test('actual concurrent offer refresh cannot replace a later selected applicant',async t=>{
 const old={document:globalThis.document,window:globalThis.window,fetch:globalThis.fetch,Option:globalThis.Option};
 t.after(()=>Object.assign(globalThis,old));
 const elements=new Map();
 globalThis.document={getElementById(id){
  if(!elements.has(id))elements.set(id,{value:'',checked:false,textContent:'',hidden:false,
   disabled:false,setAttribute(){},removeAttribute(){},replaceChildren(){}});
  return elements.get(id);
 }};
 globalThis.window={EduOSAdmissionOffers:{eligibleEvidence:()=>[]}};
 globalThis.Option=class{constructor(label,value){this.label=label;this.value=value;}};
 const pending=[];
 globalThis.fetch=(url)=>new Promise(resolve=>pending.push({url,resolve}));
 const module=await import('data:text/javascript;base64,'+Buffer.from(core).toString('base64'));
 const firstId='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
 const secondId='bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
 function settle(id,name){
  for(const entry of pending.filter(item=>item.url.includes(id))) {
   const type=entry.url.endsWith('/documents')?'documents':entry.url.endsWith('/offer')?'offer':'applicant';
   const data=type==='documents'?[]:type==='offer'?{state:0}:{rowVersion:'v1',state:6,applicantName:name};
   entry.resolve({ok:true,json:async()=>({success:true,data})});
  }
 }
 module.ctx.reference=firstId;
 const stale=module.refresh();
 module.ctx.reference=secondId;
 const newest=module.refresh();
 assert.equal(pending.length,6);
 settle(secondId,'Selected B');
 assert.equal(await newest,true);
 settle(firstId,'Stale A');
 assert.equal(await stale,false);
 assert.equal(module.ctx.applicant.applicantName,'Selected B');
 assert.equal(globalThis.document.getElementById('offerApplicantName').textContent,'Selected B');
});
