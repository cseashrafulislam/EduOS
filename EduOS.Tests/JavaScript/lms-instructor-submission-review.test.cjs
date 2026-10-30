'use strict';
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=name=>fs.readFileSync(path.join(root,name),'utf8');
const js=read('EduOS.App/wwwroot/js/learning/index.js');
const view=read('EduOS.App/Views/Learning/Index.cshtml');
const api=read('EduOS.App/Controllers/Api/LmsSubmissionFilesController.cs');
const workflowApi=read('EduOS.App/Controllers/Api/LmsWorkflowController.cs');
const service=read('EduOS.Service/Services/LMS/LmsWorkflowService.SubmissionFiles.cs');

test('instructor can review submitted evidence using scoped metadata and private byte route',()=>{
 assert.doesNotThrow(()=>new Function(js));
 assert.match(view,/learningReviewFilesPanel/);
 assert.match(view,/learningReviewFilesClose/);
 assert.match(js,/Review answer \/ files.*showReviewFiles\(row\)/);
 assert.match(view,/learningReviewFullAnswer/);
 assert.match(js,/learningReviewFullAnswer'\)\.textContent=submission\.submissionText/);
 assert.match(js,/\/api\/lms\/assignment-work\/submissions\//);
 assert.match(js,/files\/'\+\s*encodeURIComponent\(file\.fileAssetReference\)\+'\/download'/);
 assert.match(js,/response\.blob\(\)/);
 assert.match(js,/credentials:'same-origin',cache:'no-store'/);
 assert.match(api,/DownloadSubmissionFileAsync/);
 assert.match(service,/GetSubmissionFilesAsync\(submissionId, token\)/);
 assert.match(service,/IsPrivateSubmissionAsset\(asset/);
});

test('instructor search is query-driven and retains assigned teacher when not on the first page',()=>{
 assert.match(view,/learningInstructorSearch/);
 assert.match(js,/instructors\(search=''/);
 assert.match(js,/encodeURIComponent\(search\.trim\(\)\)/);
 assert.match(js,/&take=25/);
 assert.match(js,/edited\.primaryInstructorReference/);
 assert.doesNotMatch(js,/\/api\/lms\/instructors\?take=100/);
});

test('course managers can find an eligible student and enroll with a retry-safe request reference',()=>{
 assert.match(view,/learningEnrollForm/);
 assert.match(view,/learningStudentSearch/);
 assert.match(view,/learningStudentChoice/);
 assert.ok(js.includes('/api/lms/students/search?search='));
 assert.ok(js.includes('/api/lms/courses/enrollments'));
 assert.match(js,/clientRequestId:state\.enrollRequestId/);
 assert.match(js,/learningStudentChoice'\)\.addEventListener\('change'/);
 assert.match(workflowApi,/HttpGet\("students\/search"\)/);
 assert.match(workflowApi,/SearchEligibleStudentsAsync/);
});

test('instructors author criterion-based rubrics, students see criteria, and scores are checked server-side',()=>{
 const domain=read('EduOS.Service/Services/LMS/LmsWorkflowService.Rubrics.cs');
 const model=read('EduOS.Core/Entities/LMS/LmsEntities.cs');
 const submission=read('EduOS.App/wwwroot/js/learning/my-submissions.js');
 assert.match(view,/learningRubricAssignment/);
 assert.match(view,/learningRubricSave/);
 assert.match(view,/learningStudentRubricRows/);
 assert.ok(js.includes("+'/rubric'"));
 assert.ok(js.includes('rubricScores:scores'));
 assert.match(js,/criterionInputs\.reduce/);
 assert.match(domain,/IsolationLevel\.Serializable/);
 assert.match(domain,/Submitted work locks the rubric/);
 assert.match(model,/class AssignmentRubricScore/);
 assert.match(submission,/mySubmissionRubricBreakdown/);
 assert.match(workflowApi,/SaveAssignmentRubricAsync/);
 assert.match(workflowApi,/GetSubmissionRubricScoresAsync/);
});

test('grading export is limited to the authorized current page and escapes spreadsheet formulas',()=>{
 assert.match(view,/learningReviewExport/);
 assert.match(js,/state\.reviewRows=data\?\.items/);
 assert.match(js,/state\.reviewRows\.map\(x=>\[x\.studentName/);
 assert.match(js,/if\(!sessionValid\(\)\)return clearSensitive\(\)/);
 assert.match(js,/\^\[=\+@\\-\]/);
 assert.match(js,/lms-submission-review-page-/);
});

test('session switch removes review evidence and interrupts pending private file reads',()=>{
 assert.match(js,/initialTenant=document\.body\?\.dataset\.authTenantId/);
 assert.match(js,/initialSession=document\.body\?\.dataset\.authSessionReference/);
 assert.match(js,/EduOSSessionTransitionPending/);
 assert.match(js,/new MutationObserver/);
 assert.match(js,/pagehide',clearSensitive/);
 assert.match(js,/controller\.abort\(\)/);
 assert.match(js,/learningReviewFilesRows'\)\.replaceChildren/);
 assert.doesNotMatch(js,/innerHTML\s*=|insertAdjacentHTML|localStorage|sessionStorage/);
});
