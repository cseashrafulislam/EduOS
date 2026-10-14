const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../..');
const api=fs.readFileSync(path.join(root,'EduOS.App/Controllers/Api/LmsAssessmentController.cs'),'utf8');
const service=fs.readFileSync(path.join(root,'EduOS.Service/Services/LMS/LmsAssessmentService.Read.cs'),'utf8');
const submit=fs.readFileSync(path.join(root,'EduOS.Service/Services/LMS/LmsAssessmentService.Submit.cs'),'utf8');
const view=fs.readFileSync(path.join(root,'EduOS.App/Views/Learning/Quizzes.cshtml'),'utf8');
const js=fs.readFileSync(path.join(root,'EduOS.App/wwwroot/js/learning/quizzes-learner.js'),'utf8');
const selections=fs.readFileSync(path.join(root,'EduOS.Service/Services/LMS/LmsAssessmentService.Selections.cs'),'utf8');
const site=fs.readFileSync(path.join(root,'EduOS.App/wwwroot/js/site.js'),'utf8');
test('quiz routes require LMS module, antiforgery and student/author roles',()=>{
 assert.match(api,/RequireModule\("LMS"\)/);
 assert.match(api,/AutoValidateAntiforgeryToken/);
 assert.match(api,/Authorize\(Roles = "Student"\)/);
 assert.match(api,/Authorize\(Roles = "TenantAdmin,Principal,Teacher"\)/);
});
test('candidate query projects options without correct-answer flags',()=>{
 assert.match(service,/new QuizCandidateOptionDto/);
 assert.doesNotMatch(service,/new QuizCandidateOptionDto\s*\{[^}]*IsCorrect/);
});
test('grading verifies enrollment, row version and option ownership',()=>{
 assert.match(submit,/EnrollmentAsync/);
 assert.match(submit,/VersionMatches/);
 assert.match(selections,/o.QuizQuestionId == question.Id/);
});
test('quiz UI fields and browser request contracts are connected',()=>{
 for(const id of ['quizCourse','quizList','quizTitle','quizQuestions','quizStart','quizAnswerForm','quizSubmit'])
  assert.ok(view.includes('id="'+id+'"'),id);
 assert.match(site,/RequestVerificationToken/);
 assert.match(js,/crypto.randomUUID/);
 assert.match(js,/courseEnrollmentId:state.enrollment/);
 new Function(js);
});
