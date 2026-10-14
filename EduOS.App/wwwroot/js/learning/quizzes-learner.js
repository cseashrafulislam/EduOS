(()=>{'use strict';const app=window.EduQuiz;if(!app?.state.learner)return;
const {$,el,state,msg,api}=app;
async function start(){const ref=$('quizList').value;if(!ref)return msg('warning','Choose quiz');
$('quizStart').disabled=true;try{state.candidate=await api('/api/lms/quizzes/'+ref+'/candidate');
state.requestId??=crypto.randomUUID();state.attempt=await api('/api/lms/quizzes/attempts','POST',
{clientRequestId:state.requestId,quizReference:ref,courseEnrollmentId:state.enrollment});
const list=$('quizCandidateQuestions');list.replaceChildren();
state.candidate.questions.forEach(q=>{const box=el('fieldset');box.className='border rounded p-2';
box.append(el('legend',q.questionText+(q.questionType==='MultipleChoice'?' (select all correct answers)':'')));
q.options.forEach(o=>{const label=el('label');label.className='d-block';const input=el('input');
input.type=q.questionType==='MultipleChoice'?'checkbox':'radio';input.name='q'+q.id;input.value=o.id;
label.append(input,document.createTextNode(' '+o.optionText));box.append(label);});list.append(box);});
$('quizAnswerForm').classList.remove('d-none');$('quizTimer').textContent='Started '+new Date(state.attempt.startedAt).toLocaleString();
}catch(e){msg('danger',e.message);}finally{$('quizStart').disabled=false;}}
async function submit(e){e.preventDefault();if(!state.attempt)return;
const answers=state.candidate.questions.map(q=>{const selected=[...document.querySelectorAll('input[name="q'+q.id+'"]:checked')].map(x=>Number(x.value));
return q.questionType==='MultipleChoice'?{quizQuestionId:q.id,selectedOptionIds:selected}:
{quizQuestionId:q.id,selectedOptionId:selected[0]||null};});
$('quizSubmit').disabled=true;try{const result=await api('/api/lms/quizzes/attempts/'+state.attempt.id+'/submit','POST',
{answers,rowVersion:state.attempt.rowVersion});$('quizAnswerForm').classList.add('d-none');
const review=await api('/api/lms/quizzes/attempts/'+state.attempt.id+'/review');
const heading=el('strong','Marks: '+result.obtainedMarks),list=el('ul');
review.answers.forEach(a=>{list.append(el('li',a.questionText+' — '+(a.selectedOptions.map(o=>o.optionText).join(', ')||'Unanswered')+' (marks: '+(a.awardedMarks??0)+')'));});
$('quizResult').replaceChildren(heading,list);msg('success','Quiz graded');
}catch(e){msg('danger',e.message);}finally{$('quizSubmit').disabled=false;}}
$('quizList').onchange=()=>{state.candidate=null;state.attempt=null;state.requestId=null;
$('quizAnswerForm').classList.add('d-none');$('quizResult').replaceChildren();};
$('quizStart').onclick=start;$('quizAnswerForm').onsubmit=submit;
})();
