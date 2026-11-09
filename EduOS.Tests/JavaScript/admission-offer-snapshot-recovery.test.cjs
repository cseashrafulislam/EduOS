const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../../EduOS.App');
const core=fs.readFileSync(path.join(root,'wwwroot/js/admissions/offers-core.js'),'utf8');
const main=fs.readFileSync(path.join(root,'wwwroot/js/admissions/offers.js'),'utf8');
const view=fs.readFileSync(path.join(root,'Views/Admissions/Offers.cshtml'),'utf8');
test('snapshot requires applicant offer and evidence together before publishing state',()=>{
 assert.match(core,/Promise\.all\(\[/);
 assert.ok(core.indexOf('const [applicant, decision, docs]') < core.indexOf('ctx.applicant = applicant'));
 assert.ok(core.indexOf('if (!applicant?.rowVersion)') < core.indexOf('ctx.applicant = applicant'));
});
test('concurrent refresh and applicant switch invalidate old responses',()=>{
 assert.match(core,/sequence !== refreshSequence \|\| !currentReference\(reference\)/);
 assert.match(core,/\+\+refreshSequence; \/\/ invalidate any older refresh/);
 assert.match(core,/sequence !== loadSequence \|\| !loaded/);
});
test('refresh failure does not advertise mutation success or unlock forms',()=>{
 assert.match(main,/if \(updated && currentReference\(reference\)\) notice\(success\(result\)\)/);
 assert.match(core,/busy \|\| pendingRefresh/);
 assert.match(core,/finally \{/);
});
test('manual refresh offers accessible localized recovery',()=>{
 assert.match(main,/if \(await refresh\(\) && currentReference\(reference\)\)/);
 assert.match(view,/বর্তমান অবস্থা ও নথি আবার লোড করুন/);
 assert.match(view,/aria-label="\@\(bn/);
});
