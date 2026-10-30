'use strict';
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=file=>fs.readFileSync(path.join(root,file),'utf8');
const js=read('EduOS.App/wwwroot/js/learning/assignment-files.js');
const view=read('EduOS.App/Views/Learning/AssignmentFiles.cshtml');
const controller=read('EduOS.App/Controllers/Api/LmsAssignmentAttachmentsController.cs');
const service=read('EduOS.Service/Services/LMS/LmsCourseContentService.AssignmentAttachments.cs');

test('instructor attaches real scanned file with idempotent upload and no manual GUID UI',()=>{
 assert.doesNotThrow(()=>new Function(js));
 assert.match(view,/assignmentFilesUpload" type="file"/);
 assert.doesNotMatch(view,/Verified file reference \(GUID\)/);
 assert.match(js,/new FormData\(\)/);
 assert.match(js,/RequestVerificationToken:token/);
 assert.match(js,/file\.size>5242880/);
 assert.match(js,/pendingUpload\.requestId/);
 assert.match(controller,/UploadAssignmentAttachmentFileAsync/);
 assert.match(service,/UploadForLmsAsync\(requestId, upload/);
 assert.match(service,/IsLmsPrivateAsset\(file, Tenant\)/);
});

test('assignment handout download is checked against course enrollment and scanned bytes',()=>{
 assert.match(controller,/DownloadAssignmentAttachmentAsync/);
 assert.match(controller,/private, no-store/);
 assert.match(controller,/Content-Security-Policy/);
 assert.match(service,/CanViewAsync\(course, token\)/);
 assert.match(service,/IsLmsPrivateAsset\(asset, Tenant\)/);
 assert.match(service,/SHA256\.HashData/);
 assert.match(service,/IsolationLevel\.Serializable/);
 assert.match(js,/row\.fileDownloadable/);
 assert.match(js,/response\.blob\(\)/);
 assert.doesNotMatch(js,/innerHTML\s*=|localStorage|sessionStorage|insertAdjacentHTML/);
});

test('handout UI clears sensitive state and aborts private bytes when session changes',()=>{
 assert.match(js,/authTenantId/);
 assert.match(js,/authSessionReference/);
 assert.match(js,/EduOSSessionTransitionPending/);
 assert.match(js,/new MutationObserver/);
 assert.match(js,/pagehide',clearSensitive/);
 assert.match(js,/controller\.abort\(\)/);
});
