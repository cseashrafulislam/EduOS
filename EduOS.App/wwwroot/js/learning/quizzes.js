(()=>{'use strict';const $=id=>document.getElementById(id),root=$('quizApp');if(!root)return;
const author=root.dataset.author==='true',learner=root.dataset.learner==='true';
let quizzes=[],editing=null,candidate=null,attempt=null,enrollment=0,requestId=null,seq=0;
const el=(tag,text)=>{const n=document.createElement(tag);if(text!=null)n.textContent=text;return n;};
const msg=(type,text)=>{const n=$('quizAlert');n.className='alert alert-'+type;n.textContent=text;n.focus();};
async function api(url,method='GET',data){const headers={Accept:'application/json'};
if(data!==undefined)headers['Content-Type']='application/json';
if(method!=='GET'){const token=document.querySelector('meta[name="request-verification-token"]')?.content;
if(!token)throw Error('Missing security token');headers.RequestVerificationToken=token;}
const r=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,...(data===undefined?{}:{body:JSON.stringify(data)})});
const j=await r.json().catch(()=>null);if(!r.ok||!j?.success)throw Error(j?.message||'Request failed');return j.data;}
function select(id,rows,placeholder){const n=$(id),old=n.value;n.replaceChildren(new Option(placeholder,''));rows.forEach(x=>n.add(new Option(x.title,x.reference)));if(rows.some(x=>x.reference===old))n.value=old;}
async function refresh(){const course=$('quizCourse').value,k=++seq;candidate=null;attempt=null;requestId=null;
$('quizAnswerForm')?.classList.add('d-none');if(!course){select('quizList',[],'Choose quiz');return;}
try{const rows=await api('/api/lms/quizzes/course/'+course+'/'+(author?'author':'available'));if(k!==seq)return;
quizzes=rows||[];select('quizList',quizzes,'Choose quiz');
if(learner)enrollment=await api('/api/lms/quizzes/course/'+course+'/my-enrollment');
}catch(e){if(k===seq)msg('danger',e.message);}}
function question(q){const box=el('fieldset');box.className='border rounded p-2';
const title=el('input');title.className='form-control mb-2 question-text';title.required=true;title.maxLength=4000;title.placeholder='Question';title.value=q?.questionText||'';
const marks=el('input');marks.type='number';marks.min='.01';marks.step='.01';marks.required=true;marks.className='form-control mb-2 question-marks';marks.value=q?.marks||1;
box.append(title,marks);const opts=q?.options||[{optionText:''},{optionText:''}];
const correct=el('select');correct.className='form-select question-correct';
opts.forEach((o,i)=>{const input=el('input');input.className='form-control mb-1 question-option';input.required=true;input.maxLength=2000;input.placeholder='Option '+(i+1);input.value=o.optionText||'';box.append(input);correct.add(new Option('Correct: '+(i+1),i));if(o.isCorrect)correct.value=i;});
const add=el('button','Add option');add.type='button';add.className='btn btn-sm btn-outline-primary';add.onclick=()=>{if(box.querySelectorAll('.question-option').length>=10)return;const i=box.querySelectorAll('.question-option').length;const input=el('input');input.className='form-control mb-1 question-option';input.required=true;input.placeholder='Option '+(i+1);box.insertBefore(input,correct);correct.add(new Option('Correct: '+(i+1),i));};
const remove=el('button','Remove question');remove.type='button';remove.className='btn btn-sm btn-outline-danger ms-2';remove.onclick=()=>box.remove();
box.append(correct,add,remove);$('quizQuestions').append(box);}
function reset(){editing=null;$('quizAuthorForm').reset();$('quizQuestions').replaceChildren();question();$('quizSave').disabled=false;}
async function chosen(){const ref=$('quizList').value;if(!ref||!author)return;
try{const q=await api('/api/lms/quizzes/'+ref+'/author');editing=q;
for(const [id,value] of [['quizTitle',q.title],['quizDuration',q.durationMinutes],['quizAttempts',q.maxAttempts],['quizPass',q.passMarks]])$(id).value=value;
$('quizPublished').checked=q.isPublished;
$('quizOpens').value=q.opensAt?new Date(q.opensAt).toISOString().slice(0,16):'';
$('quizCloses').value=q.closesAt?new Date(q.closesAt).toISOString().slice(0,16):'';
$('quizQuestions').replaceChildren();q.questions.forEach(question);$('quizSave').disabled=q.isPublished;
}catch(e){msg('danger',e.message);}}
async function save(e){e.preventDefault();if(!e.currentTarget.reportValidity())return;
const questions=[...$('quizQuestions').children].map((box,i)=>({questionText:box.querySelector('.question-text').value.trim(),
questionType:'SingleChoice',marks:Number(box.querySelector('.question-marks').value),displayOrder:i+1,
options:[...box.querySelectorAll('.question-option')].map((o,j)=>({optionText:o.value.trim(),isCorrect:j===Number(box.querySelector('.question-correct').value),displayOrder:j+1}))}));
if(!questions.length)return msg('warning','Add a question.');
const iso=id=>$(id).value?new Date($(id).value).toISOString():null,q=editing;
const data={courseReference:$('quizCourse').value,title:$('quizTitle').value.trim(),durationMinutes:Number($('quizDuration').value),
maxAttempts:Number($('quizAttempts').value),passMarks:Number($('quizPass').value),opensAt:iso('quizOpens'),
closesAt:iso('quizCloses'),isPublished:$('quizPublished').checked,questions,rowVersion:q?.rowVersion||null};
$('quizSave').disabled=true;try{await api('/api/lms/quizzes'+(q?'/'+q.reference:''),q?'PUT':'POST',data);msg('success','Quiz saved');reset();await refresh();}
catch(err){msg('danger',err.message);}finally{if(!editing||!editing.isPublished)$('quizSave').disabled=false;}}
async function start(){const ref=$('quizList').value;if(!ref)return msg('warning','Choose quiz');
$('quizStart').disabled=true;try{candidate=await api('/api/lms/quizzes/'+ref+'/candidate');
requestId??=crypto.randomUUID();attempt=await api('/api/lms/quizzes/attempts','POST',{clientRequestId:requestId,quizReference:ref,courseEnrollmentId:enrollment});
const list=$('quizCandidateQuestions');list.replaceChildren();candidate.questions.forEach(q=>{const box=el('fieldset');box.className='border rounded p-2';box.append(el('legend',q.questionText));
q.options.forEach(o=>{const label=el('label');label.className='d-block';const input=el('input');input.type='radio';input.name='q'+q.id;input.value=o.id;label.append(input,document.createTextNode(' '+o.optionText));box.append(label);});list.append(box);});
$('quizAnswerForm').classList.remove('d-none');$('quizTimer').textContent='Started '+new Date(attempt.startedAt).toLocaleString();
}catch(e){msg('danger',e.message);}finally{$('quizStart').disabled=false;}}
async function submit(e){e.preventDefault();if(!attempt)return;const answers=candidate.questions.map(q=>({quizQuestionId:q.id,
selectedOptionId:Number(document.querySelector('input[name="q'+q.id+'"]:checked')?.value)||null}));
$('quizSubmit').disabled=true;try{const result=await api('/api/lms/quizzes/attempts/'+attempt.id+'/submit','POST',{answers,rowVersion:attempt.rowVersion});
$('quizAnswerForm').classList.add('d-none');$('quizResult').textContent='Marks: '+result.obtainedMarks;msg('success','Quiz graded');
}catch(e){msg('danger',e.message);}finally{$('quizSubmit').disabled=false;}}
$('quizCourse').onchange=refresh;$('quizList').onchange=chosen;$('quizRefresh').onclick=refresh;
if(author){$('quizAddQuestion').onclick=()=>question();$('quizReset').onclick=reset;$('quizAuthorForm').onsubmit=save;reset();}
if(learner){$('quizStart').onclick=start;$('quizAnswerForm').onsubmit=submit;}
api('/api/lms/courses?page=1&pageSize=100').then(x=>{select('quizCourse',x?.items||[],'Choose course');refresh();}).catch(e=>msg('danger',e.message));
})();
