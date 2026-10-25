(()=>{'use strict';const root=document.getElementById('quizApp');if(!root)return;
const $=id=>document.getElementById(id),el=(tag,value)=>{const x=document.createElement(tag);if(value!==undefined)x.textContent=value;return x;};
const state={author:root.dataset.author==='true',learner:root.dataset.learner==='true',
quizzes:[],editing:null,candidate:null,attempt:null,enrollment:0,canAttempt:false,requestId:null,epoch:0};
function msg(type,message){const n=$('quizAlert');n.className='alert alert-'+type;n.textContent=message;n.focus();}
function select(id,rows,placeholder){const n=$(id),old=n.value;n.replaceChildren(new Option(placeholder,''));
rows.forEach(x=>n.add(new Option(x.title,x.reference)));
if(rows.some(x=>String(x.reference)===old))n.value=old;}
function learnerStatus(message){const n=$('quizAttemptStatus');if(n)n.textContent=message;}
async function refresh(){const course=$('quizCourse').value,k=++state.epoch;
state.candidate=null;state.attempt=null;state.requestId=null;state.enrollment=0;state.canAttempt=false;state.quizzes=[];
select('quizList',[],'Choose quiz');$('quizAnswerForm')?.classList.add('d-none');$('quizResult')?.replaceChildren();
if($('quizStart'))$('quizStart').disabled=true;
if(!course){learnerStatus('Choose an active course to check quiz availability.');return;}
try{
if(state.learner){
learnerStatus('Checking enrollment eligibility...');
const access=await window.EduQuizApi('/api/lms/quizzes/course/'+encodeURIComponent(course)+'/eligibility');
if(k!==state.epoch)return;
if(!access?.canAttempt){learnerStatus((access?.reason||'New attempts are unavailable.')+' See graded quiz history below.');return;}
state.enrollment=Number(access.courseEnrollmentId)||0;state.canAttempt=state.enrollment>0;
if(!state.canAttempt){learnerStatus('No eligible active enrollment. See graded quiz history below.');return;}
}
const rows=await window.EduQuizApi('/api/lms/quizzes/course/'+encodeURIComponent(course)+'/'+(state.author?'author':'available'));
if(k!==state.epoch)return;state.quizzes=rows||[];select('quizList',state.quizzes,'Choose quiz');
if(state.learner)learnerStatus(state.quizzes.length?'Select a quiz to start a new attempt.':'No quizzes are currently available. Historical results remain below.');
}catch(e){if(k!==state.epoch)return;state.enrollment=0;state.canAttempt=false;
learnerStatus('New attempts are unavailable. Historical results remain below.');
msg(e.message==='Course enrollment required.'?'warning':'danger',e.message);}}
window.EduQuiz={$,el,state,msg,api:window.EduQuizApi,select,refresh};
$('quizCourse').onchange=refresh;$('quizRefresh').onclick=refresh;
window.EduQuizApi('/api/lms/courses?page=1&pageSize=100').then(x=>{
select('quizCourse',x?.items||[],'Choose course');refresh();}).catch(e=>msg('danger',e.message));
})();
