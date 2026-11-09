(()=>{'use strict';const app=window.EduQuiz;if(!app?.state.author)return;
const {$,state,msg,api,refresh}=app,question=window.EduQuizQuestionEditor;
let sectionsEpoch=0;
async function chapters(){const ref=$('quizCourse').value,k=++sectionsEpoch,select=$('quizChapter');
select.replaceChildren(new Option('Not mapped',''));if(!ref)return;
try{const data=await api('/api/lms/adaptive/teacher/course/'+encodeURIComponent(ref)+'/chapters');
if(k!==sectionsEpoch||ref!==$('quizCourse').value)return;
for(const c of data||[])select.add(new Option(c.title,String(c.id)));
}catch(e){if(k===sectionsEpoch)msg('warning',e.message);}}
function reset(){state.editing=null;$('quizAuthorForm').reset();$('quizQuestions').replaceChildren();
question();$('quizSave').disabled=false;}
async function chosen(){const ref=$('quizList').value;if(!ref)return;
try{const q=await api('/api/lms/quizzes/'+ref+'/author');state.editing=q;await chapters();
for(const [id,value] of [['quizTitle',q.title],['quizDuration',q.durationMinutes],['quizAttempts',q.maxAttempts],['quizPass',q.passMarks]])$(id).value=value;
$('quizPublished').checked=q.isPublished;
$('quizChapter').value=q.courseSectionId==null?'':String(q.courseSectionId);
$('quizSkill').value=q.skillCode||'';$('quizDifficulty').value=String(q.practiceDifficulty||1);
$('quizOpens').value=q.opensAt?new Date(q.opensAt).toISOString().slice(0,16):'';
$('quizCloses').value=q.closesAt?new Date(q.closesAt).toISOString().slice(0,16):'';
$('quizQuestions').replaceChildren();q.questions.forEach(question);$('quizSave').disabled=q.isPublished;
}catch(e){msg('danger',e.message);}}
async function save(e){e.preventDefault();if(!e.currentTarget.reportValidity())return;
const questions=[...$('quizQuestions').children].map((box,i)=>({questionText:box.querySelector('.question-text').value.trim(),
explanation:box.querySelector('.question-explanation').value.trim()||null,
questionType:box.querySelector('.question-type').value,marks:Number(box.querySelector('.question-marks').value),displayOrder:i+1,
options:[...box.querySelectorAll('.question-option')].map((o,j)=>({optionText:o.value.trim(),
isCorrect:box.querySelector('.question-type').value==='MultipleChoice'
?box.querySelectorAll('.question-option-correct')[j].checked:j===Number(box.querySelector('.question-correct').value),displayOrder:j+1}))}));
if(!questions.length)return msg('warning','Add a question.');
if(questions.some(q=>q.questionType==='MultipleChoice'&&(q.options.filter(o=>o.isCorrect).length<1||q.options.every(o=>o.isCorrect))))
return msg('warning','Multiple choice requires at least one correct and one incorrect option.');
const iso=id=>$(id).value?new Date($(id).value).toISOString():null,q=state.editing;
const data={courseReference:$('quizCourse').value,title:$('quizTitle').value.trim(),
durationMinutes:Number($('quizDuration').value),maxAttempts:Number($('quizAttempts').value),
passMarks:Number($('quizPass').value),opensAt:iso('quizOpens'),closesAt:iso('quizCloses'),
isPublished:$('quizPublished').checked,questions,rowVersion:q?.rowVersion||null};
data.courseSectionId=$('quizChapter').value?Number($('quizChapter').value):null;
data.skillCode=$('quizSkill').value.trim()||null;
data.practiceDifficulty=Number($('quizDifficulty').value);
$('quizSave').disabled=true;try{await api('/api/lms/quizzes'+(q?'/'+q.reference:''),q?'PUT':'POST',data);
msg('success','Quiz saved');reset();await refresh();}catch(err){msg('danger',err.message);}
finally{if(!state.editing?.isPublished)$('quizSave').disabled=false;}}
$('quizList').onchange=chosen;$('quizAddQuestion').onclick=()=>question();
$('quizCourse').addEventListener('change',()=>{sectionsEpoch++;chapters();});
$('quizReset').onclick=reset;$('quizAuthorForm').onsubmit=save;reset();
})();
