const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const read=file=>fs.readFileSync(path.join(__dirname,'../../',file),'utf8');
const script=read('EduOS.App/wwwroot/js/learning/progress-guardian.js');
const student=read('EduOS.App/Views/Learning/Index.cshtml');
const guardian=read('EduOS.App/Views/LmsGuardianProgress/Index.cshtml');
const controller=read('EduOS.App/Controllers/Api/LmsProgressSharingController.cs');

test('progress consent UI parses and never injects untrusted HTML',()=>{
 assert.doesNotThrow(()=>new vm.Script(script));
 assert.doesNotMatch(script,/innerHTML|insertAdjacentHTML|document\.write|localStorage|sessionStorage/);
 assert.match(script,/textContent=grant\.guardianName/);
 assert.match(script,/textContent=String\(value/);
});
test('consent requires explicit confirmation, CSRF, idempotency and revocation version',()=>{
 assert.match(script,/window\.confirm/);
 assert.match(script,/RequestVerificationToken=token/);
 assert.match(script,/clientRequestId:crypto\.randomUUID\(\)/);
 assert.match(script,/rowVersion:grant\.rowVersion/);
 assert.match(script,/authTenantId/);
 assert.match(script,/authSessionReference/);
 assert.match(script,/EduOSSessionTransitionPending/);
});
test('guardian access is separately authorized and bounded',()=>{
 assert.match(controller,/Authorize\(Roles = "Guardian,Parent"\)/);
 assert.match(controller,/RequireModule\("LMS"\)/);
 assert.match(controller,/AutoValidateAntiforgeryToken/);
 assert.match(controller,/service\.LessonsAsync/);
 assert.match(student,/lmsProgressConsentApp/);
 assert.match(guardian,/lmsGuardianProgressApp/);
 assert.match(script,/pageSize:'20'/);
 assert.match(script,/cache:'no-store'/);
 assert.match(script,/credentials:'same-origin'/);
});

test('private enrollment choices and guardian data are scrubbed on tenant transition',()=>{
 assert.match(script,/lmsProgressConsentEnrollment'.*\]/);
 assert.match(script,/MutationObserver/);
 assert.match(script,/pagehide',reset/);
 assert.match(script,/EduOSSessionTransitionPending/);
});

test('guardian export reauthorizes server-side rather than exporting stale browser rows',()=>{
 assert.match(controller,/HttpGet\("mine\/export"\)/);
 assert.match(controller,/service\.SharedAsync\(page, pageSize, ct\)/);
 assert.match(controller,/CsvCell\(row\.LearnerName\)/);
 assert.match(controller,/private, no-store/);
 assert.match(script,/progress-sharing\/mine\/export/);
 assert.match(script,/response\.blob\(\)/);
 assert.doesNotMatch(script,/rows\.map\(x=>\[x\.learnerName/);
});
test('guardian lesson export is bounded and reauthorized on the server',()=>{
 assert.match(controller,/HttpGet\("mine\/\{enrollmentId:long\}\/lessons\/export"\)/);
 assert.match(controller,/service\.LessonsAsync\(enrollmentId, page, pageSize, ct\)/);
 assert.match(controller,/CsvCell\(row\.LessonTitle\)/);
 assert.match(script,/progress-sharing\/mine\/'\+enrollment\+'\/lessons\/export/);
 assert.match(script,/lmsGuardianLessonRows'\)\.replaceChildren/);
 assert.match(guardian,/lmsGuardianLessonExport/);
});
test('guardian progress labels are translated in Bangla and English',()=>{
 assert.match(script,/data-progress-i18n/);
 assert.match(script,/শেয়ার করা শেখার অগ্রগতি/);
 assert.match(script,/অভিভাবক সম্পর্ক বাতিল/);
});
