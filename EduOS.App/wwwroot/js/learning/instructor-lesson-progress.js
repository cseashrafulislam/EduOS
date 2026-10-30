(() => {
'use strict';
const root=document.getElementById('lmsInstructorLessonPanel');
if(!root)return;
const $=id=>document.getElementById(id);
const bn=root.dataset.lang==='bn';
const t=(en,bd)=>bn?bd:en;
const state={course:'',enrollment:0,student:'',page:1,pages:1,rows:[],sequence:0,
    exporting:false,session:''};
const session=()=>String(document.body?.dataset.authSessionReference||'')+'|'+
    String(document.body?.dataset.authTenantId||'');
const current=(course,enrollment,sessionKey)=>state.course===course &&
    state.enrollment===enrollment && state.session===sessionKey &&
    session()===sessionKey && window.EduOSSessionTransitionPending?.()!==true;
function close(){
 state.sequence++;state.course='';state.enrollment=0;state.rows=[];state.page=1;
 root.classList.add('d-none');$('lmsInstructorLessonRows').replaceChildren();
}
function alertUser(message){
 const el=$('lmsInstructorLessonAlert');el.className='alert alert-danger m-2';
 el.textContent=message;
}
async function api(url,signal){
 const response=await fetch(url,{credentials:'same-origin',cache:'no-store',
  headers:{Accept:'application/json'},...(signal?{signal}:{})});
 const result=await response.json().catch(()=>null);
 if(!response.ok||!result?.success)throw Error(result?.message||t('Unable to load progress.','অগ্রগতি লোড করা যায়নি।'));
 return result.data;
}
function cell(row,value){
 const td=document.createElement('td');td.textContent=String(value??'');row.append(td);
}
function render(){
 const body=$('lmsInstructorLessonRows');body.replaceChildren();
 for(const item of state.rows){
  const tr=document.createElement('tr');
  cell(tr,item.lessonTitle);
  cell(tr,String(item.progressPercent??0)+'%');
  cell(tr,item.isCompleted?t('Yes','হ্যাঁ'):t('No','না'));
  cell(tr,item.completedAt?String(item.completedAt).replace('T',' ').slice(0,19)+' UTC':'—');
  body.append(tr);
 }
 if(!state.rows.length){const tr=document.createElement('tr');
  const td=document.createElement('td');td.colSpan=4;
  td.textContent=t('No published lessons.','প্রকাশিত পাঠ নেই।');tr.append(td);body.append(tr);}
 $('lmsInstructorLessonPage').textContent=t('Page ','পৃষ্ঠা ')+state.page+' / '+state.pages;
 $('lmsInstructorLessonPrev').disabled=state.page<=1;
 $('lmsInstructorLessonNext').disabled=state.page>=state.pages;
}
function url(course,enrollment,suffix){
 return '/api/lms/courses/'+encodeURIComponent(course)+'/enrollments/'+
    encodeURIComponent(enrollment)+'/'+suffix;
}
async function load(){
 if(!state.course)return;
 const course=state.course,enrollment=state.enrollment,sessionKey=state.session,seq=++state.sequence;
 try{
  const data=await api(url(course,enrollment,'lesson-progress')+'?'+
    new URLSearchParams({page:String(state.page),pageSize:'20'}));
  if(seq!==state.sequence||!current(course,enrollment,sessionKey)){close();return;}
  state.rows=data?.items||[];state.pages=Math.max(1,data?.totalPages||1);render();
 }catch(error){if(seq===state.sequence&&current(course,enrollment,sessionKey))alertUser(error.message);}
}
function open(course,enrollment,student){
 if(!/^[0-9a-f-]{36}$/i.test(String(course))||!Number.isSafeInteger(Number(enrollment))||
    Number(enrollment)<=0)return;
 close();state.course=course;state.enrollment=Number(enrollment);state.student=student;
 state.session=session();root.classList.remove('d-none');
 $('lmsInstructorLessonHeading').textContent=t('Lesson progress — ','পাঠের অগ্রগতি — ')+student;
 $('lmsInstructorLessonAlert').className='d-none';
 root.scrollIntoView({block:'nearest',behavior:'smooth'});
 load();
}
async function exportCsv(){
 if(!state.course||state.exporting)return;
 const course=state.course,enrollment=state.enrollment,sessionKey=state.session;
 const isCurrent=()=>current(course,enrollment,sessionKey);
 const report=window.EduOSLmsCompletionReport;
 if(!report)return alertUser(t('Report exporter unavailable.','রিপোর্ট এক্সপোর্ট উপলব্ধ নয়।'));
 state.exporting=true;$('lmsInstructorLessonExport').disabled=true;
 const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),15000);
 try{
  const data=await api(url(course,enrollment,'lesson-progress-export'),controller.signal);
  const rows=report.validateLessonSnapshot(data,enrollment,isCurrent);
  if(!isCurrent())throw Error(t('Account or learner changed.','অ্যাকাউন্ট বা শিক্ষার্থী পরিবর্তিত হয়েছে।'));
  const blob=new Blob([report.toLessonCsv(rows)],{type:'text/csv;charset=utf-8'});
  const objectUrl=URL.createObjectURL(blob),a=document.createElement('a');
  a.href=objectUrl;a.download='lms-learner-lesson-progress.csv';a.click();
  setTimeout(()=>URL.revokeObjectURL(objectUrl),1000);
 }catch(error){if(isCurrent())alertUser(error.message);}
 finally{clearTimeout(timeout);state.exporting=false;$('lmsInstructorLessonExport').disabled=false;}
}
$('lmsInstructorLessonClose').addEventListener('click',close);
$('lmsInstructorLessonPrev').addEventListener('click',()=>{if(state.page>1){state.page--;load();}});
$('lmsInstructorLessonNext').addEventListener('click',()=>{if(state.page<state.pages){state.page++;load();}});
$('lmsInstructorLessonExport').addEventListener('click',exportCsv);
document.addEventListener('visibilitychange',()=>{if(state.course&&session()!==state.session)close();});
window.EduOSLmsInstructorProgress=Object.freeze({open,close});
})();
