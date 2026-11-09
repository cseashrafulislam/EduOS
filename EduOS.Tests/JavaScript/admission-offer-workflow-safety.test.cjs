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
 assert.match(js,/if \(currentReference\(reference\)\) notice/);
 assert.match(core,/ctx.reference === reference/);
});
test('busy state and bilingual accessible journey exist',()=>{
 assert.match(core,/setBusy\(value\)/);
 assert.match(core,/busy \|\| !qualified/);
 assert.match(view,/id="offerBusy"/);
 assert.match(view,/aria-describedby="offerJourneyStage"/);
 assert.match(view,/অনুরোধ প্রক্রিয়াধীন/);
});
