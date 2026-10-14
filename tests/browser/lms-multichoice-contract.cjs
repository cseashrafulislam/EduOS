const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const view=read('EduOS.App/Views/Learning/Quizzes.cshtml');
const api=read('EduOS.App/Controllers/Api/LmsAssessmentController.cs');
const author=read('EduOS.App/wwwroot/js/learning/quizzes-author.js');
const editor=read('EduOS.App/wwwroot/js/learning/quizzes-editor.js');
const learner=read('EduOS.App/wwwroot/js/learning/quizzes-learner.js');
const core=read('EduOS.App/wwwroot/js/learning/quizzes-core.js');
const client=read('EduOS.App/wwwroot/js/learning/quizzes-api.js');
const service=read('EduOS.Service/Services/LMS/LmsAssessmentService.Selections.cs');
const review=read('EduOS.Service/Services/LMS/LmsAssessmentService.Review.cs');
test('quiz UI loads the modular single and multiple choice clients',()=>{
 for(const name of ['api','core','editor','author','learner'])
 assert.match(view,new RegExp('quizzes-'+name+'\\.js'));
 for(const source of [author,editor,learner,core,client])new Function(source);
 assert.match(editor,/MultipleChoice/);assert.match(author,/question-type/);
 assert.match(learner,/selectedOptionIds:selected/);
 assert.match(learner,/input\.type=q\.questionType==='MultipleChoice'\?'checkbox':'radio'/);
});
test('grading checks ownership, exact option sets and immutable retries',()=>{
 assert.match(service,/QuizAnswerOption/);assert.match(service,/SequenceEqual/);
 assert.match(service,/QuizQuestionId == question.Id/);
 assert.match(service,/SameSubmittedAnswersAsync/);
 assert.match(review,/EnrollmentAsync/);
 assert.doesNotMatch(review,/IsCorrect\s*=/);
 assert.match(api,/attempts\/\{id:long\}\/review/);
});
test('quiz writes use central same-origin CSRF protection',()=>{
 const site=read('EduOS.App/wwwroot/js/site.js');
 assert.match(site,/RequestVerificationToken/);
 assert.match(client,/credentials:'same-origin'/);
 assert.match(api,/AutoValidateAntiforgeryToken/);
});
