(()=>{'use strict';const app=window.EduQuiz;if(!app?.state.learner)return;
const {$,el,msg,api}=app;let page=1,rows=[],total=0,epoch=0;
const course=$('quizHistoryCourse'),body=$('quizHistoryRows');
function cell(tr,value){const td=el('td',String(value??''));tr.append(td);}
function render(){body.replaceChildren();rows.forEach(row=>{const tr=el('tr');
cell(tr,row.quizTitle);cell(tr,row.attemptNo);
cell(tr,row.submittedAt?new Date(row.submittedAt).toLocaleString():'');
cell(tr,row.obtainedMarks??'Pending');cell(tr,row.passed===null?'Pending':row.passed?'Pass':'Fail');
const td=el('td'),button=el('button','Review');button.type='button';button.className='btn btn-sm btn-outline-primary';
button.onclick=()=>review(row.attemptId);td.append(button);tr.append(td);body.append(tr);});
$('quizHistoryCount').textContent=total+' attempts · page '+page;
$('quizHistoryPrev').disabled=page<=1;$('quizHistoryNext').disabled=page*20>=total;}
async function search(){const ref=course.value,k=++epoch;rows=[];total=0;$('quizHistoryReview').replaceChildren();
if(!ref){page=1;render();return;}
try{const data=await api('/api/lms/quizzes/course/'+encodeURIComponent(ref)+'/history?page='+page+'&pageSize=20');
if(k!==epoch)return;rows=data.items||[];total=data.totalCount||0;render();}
catch(e){if(k===epoch){render();msg('danger',e.message);}}}
async function review(id){try{const data=await api('/api/lms/quizzes/attempts/'+id+'/review');
const heading=el('strong','Marks: '+(data.obtainedMarks??'Pending')),list=el('ul');
(data.answers||[]).forEach(a=>list.append(el('li',a.questionText+' — '+(a.selectedOptions||[]).map(x=>x.optionText).join(', ')+' (marks: '+(a.awardedMarks??0)+')')));
$('quizHistoryReview').replaceChildren(heading,list);}
catch(e){msg('danger',e.message);}}
function csv(){const cells=[['Quiz','Attempt','Submitted','Marks','Pass']];
rows.forEach(x=>cells.push([x.quizTitle,x.attemptNo,x.submittedAt||'',x.obtainedMarks??'',x.passed===null?'Pending':x.passed?'Pass':'Fail']));
const content=cells.map(row=>row.map(v=>{const s=String(v);const safe=/^[=+\-@\t\r]/.test(s)?"'"+s:s;return '"'+safe.replace(/"/g,'""')+'"';}).join(',')).join('\r\n');
const url=URL.createObjectURL(new Blob([content],{type:'text/csv;charset=utf-8'}));const a=el('a');a.href=url;a.download='quiz-history-page-'+page+'.csv';a.click();URL.revokeObjectURL(url);}
course.onchange=()=>{page=1;search();};$('quizHistoryRefresh').onclick=()=>{page=1;search();};
$('quizHistoryPrev').onclick=()=>{if(page>1){page--;search();}};
$('quizHistoryNext').onclick=()=>{if(page*20<total){page++;search();}};
$('quizHistoryCsv').onclick=csv;$('quizHistoryPrint').onclick=()=>window.print();
api('/api/lms/courses?page=1&pageSize=100&includeArchived=true').then(x=>{
course.replaceChildren(new Option('Select course',''));(x?.items||[]).forEach(c=>course.add(new Option(c.title+(c.isActive?'':' (Archived)'),c.reference)));})
.catch(e=>msg('danger',e.message));
})();
