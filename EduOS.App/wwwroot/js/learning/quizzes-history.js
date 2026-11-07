(()=>{'use strict';const app=window.EduQuiz;if(!app?.state.learner)return;
const {$,el,msg,api}=app;let page=1,rows=[],total=0,epoch=0;
const course=$('quizHistoryCourse'),body=$('quizHistoryRows');let analysisEpoch=0;let currentPlan=null,currentPlanLocale=null;
function cell(tr,value){const td=el('td',String(value??''));tr.append(td);}
function clearProgress(){const b=$('quizProgressRows');if(b)b.replaceChildren();const s=$('quizProgressStatus');if(s)s.textContent='';}
function clearPlan(){currentPlan=null;currentPlanLocale=null;const csv=$('quizPlanCsv'),print=$('quizPlanPrint');if(csv)csv.disabled=true;if(print)print.disabled=true;const list=$('quizPlanItems');if(list)list.replaceChildren();const status=$('quizPlanStatus');if(status)status.textContent='';const note=$('quizPlanNote');if(note)note.textContent='';}
function renderPlan(progress,locale){
 const plan=window.EduQuizStudyPlan?.build(progress,locale);
 if(!plan||plan.status==='invalid')throw Error('Invalid study plan evidence');
 clearPlan();currentPlan={...plan,scanned:progress.scanned,totalCount:progress.totalCount};currentPlanLocale=locale;$('quizPlanCsv').disabled=plan.status!=='ready';$('quizPlanPrint').disabled=plan.status!=='ready';$('quizPlanStatus').textContent=plan.message+' ('+plan.items.length+')';
 $('quizPlanNote').textContent=plan.note;
 for(const item of plan.items){const li=el('li');li.className='list-group-item';
 li.append(el('strong',item.title),el('span',' — '+item.guidance));$('quizPlanItems').append(li);}
}
function studyScope(value){const node=$('quizStudyScope');if(node)node.textContent=value;}
async function analyzeCourse(){
 const selected=course.value,k=++analysisEpoch,button=$('quizStudyAnalyze');
 if(!selected)return;
 button.disabled=true;clearProgress();clearPlan();studyScope('Loading authorized course history / কোর্সের ফল লোড হচ্ছে…');
 const pages=[];let count=null;
 try{
  for(let p=1;p<=10;p++){
   const data=await api('/api/lms/quizzes/course/'+encodeURIComponent(selected)+'/history?page='+p+'&pageSize=20');
   if(k!==analysisEpoch||selected!==course.value)return;
   if(!data||!Array.isArray(data.items)||!Number.isSafeInteger(data.totalCount)||data.totalCount<0||
      data.items.length>20||(count!==null&&count!==data.totalCount))throw Error('Invalid history response');
   count=data.totalCount;pages.push(data.items);
   if(p*20>=count||data.items.length===0)break;
  }
  const locale=document.documentElement.lang==='bn-BD'?'bn-BD':'en-BD';
  const result=window.EduQuizStudyGuidance?.summarize(pages,count,locale);
  if(!result||result.status==='invalid')throw Error('Invalid study evidence');
  const progress=window.EduQuizProgress?.build(pages,count,locale);
  if(!progress||progress.status==='invalid')throw Error('Invalid quiz progress evidence');
  renderPlan(progress,locale);
  const progressRows=$('quizProgressRows');progressRows.replaceChildren();
  $('quizProgressStatus').textContent=(locale==='bn-BD'?'পর্যালোচিত প্রচেষ্টা: ':'Scanned attempts: ')+progress.scanned+'/'+progress.totalCount+(progress.complete?'':(locale==='bn-BD'?' (আংশিক)':' (partial)'));
  $('quizProgressNote').textContent=progress.note;
  progress.items.forEach(item=>{const tr=el('tr');cell(tr,item.title);cell(tr,item.graded+'/'+item.attempts);cell(tr,item.passed);cell(tr,item.advice);
   const td=el('td'),btn=el('button',locale==='bn-BD'?'অনুশীলন দেখুন':'Find practice');btn.type='button';btn.className='btn btn-sm btn-outline-primary';
   btn.onclick=async()=>{const selected=course.value;if(!selected)return;const mainCourse=$('quizCourse');
    const option=[...mainCourse.options].find(o=>o.value===selected);
    if(!option){msg('warning',locale==='bn-BD'?'এই কোর্সে নতুন অনুশীলন উপলব্ধ নয়।':'New practice is unavailable for this course.');return;}
    mainCourse.value=selected;await app.refresh();
    const available=[...$('quizList').options].some(o=>o.value===item.quizReference);
    if(available){$('quizList').value=item.quizReference;$('quizList').dispatchEvent(new Event('change'));$('quizStart').focus();}
    else msg('warning',locale==='bn-BD'?'অনুমোদিত পুনঃচেষ্টা উপলব্ধ নয়।':'No eligible retry is currently available.');
   };td.append(btn);tr.append(td);progressRows.append(tr);});
  const list=$('quizStudyItems');list.replaceChildren();
  $('quizStudyMessage').textContent=result.message;$('quizStudyNote').textContent=result.note;
  result.items.forEach(item=>{const li=el('li');li.className='list-group-item';
    li.append(el('strong',item.title),el('span',' — '+item.advice));list.append(li);});
  studyScope((locale==='bn-BD'?'যাচাই করা প্রচেষ্টা: ':'Scanned attempts: ')+result.scanned+'/'+result.totalCount+
   (result.complete?'':(locale==='bn-BD'?' (আংশিক, সর্বোচ্চ ২০০)':' (partial, capped at 200)')));
 }catch(e){if(k===analysisEpoch){clearProgress();clearPlan();studyScope('History analysis unavailable / বিশ্লেষণ সম্ভব হয়নি');msg('danger',e.message);}}
 finally{if(k===analysisEpoch)button.disabled=!course.value;}
}
$('quizStudyAnalyze').onclick=analyzeCourse;
$('quizPlanCsv').onclick=()=>{
 const csv=window.EduQuizStudyPlan?.exportCsv(currentPlan,currentPlanLocale);
 if(!csv){msg('warning','Study priorities unavailable / পড়ার অগ্রাধিকার পাওয়া যায়নি');return;}
 const url=URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8'}));
 const a=el('a');a.href=url;a.download='study-priorities.csv';a.click();URL.revokeObjectURL(url);
};
$('quizPlanPrint').onclick=()=>{
 if(!currentPlan||currentPlan.status!=='ready')return;
 window.print();
};
function renderStudy(){
 const message=$('quizStudyMessage'),note=$('quizStudyNote'),list=$('quizStudyItems');
 if(!message||!note||!list)return;
 list.replaceChildren();note.textContent='';
 const locale=document.documentElement.lang==='bn-BD'?'bn-BD':'en-BD';
 const result=window.EduQuizStudyGuidance?.build(rows,locale);
 if(!result||result.status==='invalid'){message.textContent=locale==='bn-BD'?'পরামর্শ দেখানো যাচ্ছে না।':'Study guidance unavailable.';return;}
 message.textContent=result.message;note.textContent=result.note;
 result.items.forEach(item=>{
   const li=el('li');li.className='list-group-item';
   const title=el('strong',item.title),detail=el('span',' — '+item.advice);
   li.append(title,detail);list.append(li);
 });
}
function render(){renderStudy();body.replaceChildren();rows.forEach(row=>{const tr=el('tr');
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
course.onchange=()=>{analysisEpoch++;clearProgress();clearPlan();$('quizStudyAnalyze').disabled=!course.value;studyScope('');page=1;search();};$('quizHistoryRefresh').onclick=()=>{page=1;search();};
$('quizHistoryPrev').onclick=()=>{if(page>1){page--;search();}};
$('quizHistoryNext').onclick=()=>{if(page*20<total){page++;search();}};
$('quizHistoryCsv').onclick=csv;$('quizHistoryPrint').onclick=()=>window.print();
api('/api/lms/courses?page=1&pageSize=100&includeArchived=true').then(x=>{
course.replaceChildren(new Option('Select course',''));(x?.items||[]).forEach(c=>course.add(new Option(c.title+(c.isActive?'':' (Archived)'),c.reference)));})
.catch(e=>msg('danger',e.message));
})();
