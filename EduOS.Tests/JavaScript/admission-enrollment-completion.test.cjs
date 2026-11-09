const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../../EduOS.App');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const journey=read('wwwroot/js/admissions/offers-journey.js');
const core=read('wwwroot/js/admissions/offers-core.js');
const view=read('Views/Admissions/Offers.cshtml');
test('admitted state is terminal before qualification guard',()=>{
 assert.ok(journey.indexOf("===8)return 'complete'")<journey.indexOf("!==6)return 'review'"));
 assert.match(core,/stage === 'complete'/);
});
test('completion state is accessible and bilingual',()=>{
 assert.match(view,/id="offerCompleted"[^>]*role="status"/);
 assert.match(view,/ভর্তি সম্পন্ন/);
 assert.match(core,/completed.hidden = stage !== 'complete'/);
});
test('progress reflects server-authoritative state',()=>{
 for(const id of ['offerProgressIssue','offerProgressAccept','offerProgressEnroll'])assert.ok(view.includes('id="'+id+'"'));
 assert.match(core,/setAttribute\('aria-current', 'step'\)/);
 assert.match(core,/currentReference\(reference\)/);
});
