(()=>{'use strict';const root=document.getElementById('quizApp');if(!root)return;
const $=id=>document.getElementById(id),el=(tag,text)=>{const x=document.createElement(tag);if(text!==undefined)x.textContent=text;return x;};
const state={author:root.dataset.author==='true',learner:root.dataset.learner==='true',
quizzes:[],editing:null,candidate:null,attempt:null,enrollment:0,requestId:null,epoch:0};
function msg(type,message){const n=$('quizAlert');n.className='alert alert-'+type;n.textContent=message;n.focus();}
function select(id,rows,placeholder){const n=$(id),old=n.value;n.replaceChildren(new Option(placeholder,''));
rows.forEach(x=>n.add(new Option(x.title,x.reference)));
if(rows.some(x=>String(x.reference)===old))n.value=old;}
async function refresh(){const course=$('quizCourse').value,k=++state.epoch;
state.candidate=null;state.attempt=null;state.requestId=null;$('quizAnswerForm')?.classList.add('d-none');$('quizResult')?.replaceChildren();
if(!course){select('quizList',[],'Choose quiz');return;}
try{const rows=await window.EduQuizApi('/api/lms/quizzes/course/'+course+'/'+(state.author?'author':'available'));
if(k!==state.epoch)return;state.quizzes=rows||[];select('quizList',state.quizzes,'Choose quiz');
if(state.learner)state.enrollment=await window.EduQuizApi('/api/lms/quizzes/course/'+course+'/my-enrollment');
}catch(e){if(k===state.epoch)msg('danger',e.message);}}
window.EduQuiz={$,el,state,msg,api:window.EduQuizApi,select,refresh};
$('quizCourse').onchange=refresh;$('quizRefresh').onclick=refresh;
window.EduQuizApi('/api/lms/courses?page=1&pageSize=100').then(x=>{
select('quizCourse',x?.items||[],'Choose course');refresh();}).catch(e=>msg('danger',e.message));
})();
