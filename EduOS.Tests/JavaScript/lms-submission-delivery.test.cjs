'use strict';
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=file=>fs.readFileSync(path.join(root,file),'utf8');
const js=read('EduOS.App/wwwroot/js/learning/my-submissions.js');
const view=read('EduOS.App/Views/Learning/MySubmissions.cshtml');
const controller=read('EduOS.App/Controllers/Api/LmsSubmissionFilesController.cs');
const service=read('EduOS.Service/Services/LMS/LmsWorkflowService.SubmissionFiles.cs');

test('student submission UI stages actual multipart files using verified upload and CSRF',()=>{
 assert.doesNotThrow(()=>new Function(js));
 assert.match(view,/type="file" multiple/);
 assert.doesNotMatch(view,/Verified file GUIDs/);
 assert.match(js,/new FormData\(\)/);
 assert.match(js,/RequestVerificationToken:token/);
 assert.match(js,/x\.size>5242880/);
 assert.match(js,/state\.uploadKeys/);
 assert.match(controller,/UploadSubmissionFileAsync/);
 assert.match(service,/UploadPortalAsync\(row\.Student\.PublicId/);
});

test('private byte route is submission-bound, authenticated, scanned and hash-verified',()=>{
 assert.match(controller,/\[RequireModule\("LMS"\)\]/);
 assert.match(controller,/Authorize\(Roles = "TenantAdmin,Principal,Teacher,Student"\)/);
 assert.match(controller,/DownloadSubmissionFileAsync/);
 assert.match(controller,/nosniff/);
 assert.match(controller,/private, no-store/);
 assert.match(service,/GetSubmissionFilesAsync\(submissionId, token\)/);
 assert.match(service,/IsPrivateSubmissionAsset/);
 assert.match(service,/MalwareScanStateCode != "Clean"/);
 assert.match(service,/SHA256\.HashData/);
 assert.match(service,/IsolationLevel\.Serializable/);
 assert.match(service,/asset!\.UploadedByUserId != submittingUserId/);
});

test('browser downloads use private bytes, not storage keys or guessed generic URLs',()=>{
 assert.match(js,/files\/'.*'\/download'/);
 assert.match(js,/response\.blob\(\)/);
 assert.match(js,/cache:'no-store'/);
 assert.match(js,/URL\.createObjectURL/);
 assert.doesNotMatch(js,/innerHTML\s*=|insertAdjacentHTML|localStorage|sessionStorage/);
 assert.doesNotMatch(view,/StorageKey|fileAssetReference/);
});

test('student reads teacher marks and full feedback in their own scoped submission details',()=>{
 assert.match(view,/mySubmissionFeedback/);
 assert.match(js,/View result \/ files/);
 assert.match(js,/const grade=row\.marks==null/);
 assert.match(js,/row\.feedback\|\|'No instructor feedback yet\.'/);
 assert.match(js,/mySubmissionFeedback'\)\.textContent=/);
 assert.doesNotMatch(js,/innerHTML\s*=/);
});

test('tenant/session switch removes private submission state and aborts pending bytes',()=>{
 assert.match(js,/authTenantId/);
 assert.match(js,/authSessionReference/);
 assert.match(js,/EduOSSessionTransitionPending/);
 assert.match(js,/new MutationObserver/);
 assert.match(js,/pagehide',clearSensitive/);
 assert.match(js,/controller\.abort\(\)/);
 assert.match(js,/mySubmissionFileRows'\)\.replaceChildren/);
});
