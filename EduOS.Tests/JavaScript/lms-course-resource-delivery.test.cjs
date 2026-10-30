'use strict';
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const script=read('EduOS.App/wwwroot/js/learning/course-content.js');
const view=read('EduOS.App/Views/Learning/CourseContent.cshtml');
const controller=read('EduOS.App/Controllers/Api/LmsCourseContentController.cs');
const service=read('EduOS.Service/Services/LMS/LmsCourseContentService.ResourceFiles.cs');
test('lesson file upload uses private multipart and CSRF with bounded size',()=>{
 assert.doesNotThrow(()=>new Function(script));
 assert.match(script,/new FormData\(\)/);
 assert.match(script,/RequestVerificationToken:token/);
 assert.match(script,/selected.size>5242880/);
 assert.match(controller,/\[RequireModule\("LMS"\)\]/);
 assert.match(controller,/\[RequestSizeLimit\(6291456\)\]/);
 assert.match(controller,/UploadResourceFileAsync/);
 assert.match(service,/UploadForLmsAsync/);
 assert.match(view,/id="contentResourceUpload"/);
});
test('file links use server authorization, never storage keys or raw HTML',()=>{
 assert.match(script,/course-content\/resources\/.*\/download/);
 assert.doesNotMatch(script,/innerHTML\s*=/);
 assert.match(controller,/DownloadResourceFileAsync/);
 assert.match(controller,/nosniff/);
 assert.match(controller,/private, no-store/);
 assert.match(service,/IsolationLevel.Serializable/);
 assert.match(service,/IsLmsPrivateAsset/);
 assert.match(service,/SHA256.HashData/);
});

test('course CSV is generated from fresh authorized server read and formula-safe',()=>{
 assert.match(controller,/HttpGet\("\{courseReference:guid\}\/export"\)/);
 assert.match(controller,/GetCourseContentAsync\(courseReference, ct\)/);
 assert.match(controller,/private, no-store/);
 assert.match(controller,/CsvCell\(data.CourseTitle\)/);
 assert.match(script,/course-content\/'.*'\/export'/);
 assert.match(script,/response.blob\(\)/);
 assert.doesNotMatch(script,/rows.map\(r=>r.map\(quote\)/);
});

test('course content clears private data after tenant or session transition',()=>{
 assert.match(script,/authTenantId/);
 assert.match(script,/authSessionReference/);
 assert.match(script,/EduOSSessionTransitionPending/);
 assert.match(script,/MutationObserver/);
 assert.match(script,/pagehide',clearPrivate/);
 assert.match(script,/state\.course=null;state\.courses=\[\]/);
});
