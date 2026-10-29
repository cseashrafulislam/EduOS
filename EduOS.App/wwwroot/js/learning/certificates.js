(() => {
'use strict';
const root = document.getElementById('lmsCertificateApp');
if (!root) return;
const $ = id => document.getElementById(id);
const student = root.dataset.studentMode === 'true';
const guardian = root.dataset.guardianMode === 'true';
const lang = (document.documentElement.lang || 'en').toLowerCase().startsWith('bn') ? 'bn' : 'en';
const messages = {
 en: {title:'LMS Completion Certificates',help:'Certificates require verified course completion. Revocation preserves the original; reissue creates a new revision.',
 course:'Course',previous:'Previous',next:'Next',register:'Certificate register',learner:'Learner',
 revision:'Revision',issued:'Issued',status:'Status',action:'Action',loading:'Loading…',
 print:'Print / Save PDF',revoke:'Revoke',reissue:'Reissue',documentTitle:'Certificate of Course Completion',
 documentSubtitle:'EduOS Learning Management System',completed:'has completed the course',
 revoked:'REVOKED',valid:'VALID',empty:'No certificates found.',view:'View',
 choose:'Choose a course',select:'Select a course to view certificates.',reason:'Reason for revocation:',
 confirm:'Revoke this certificate? The original will remain in history.',error:'Request failed.',
 missing:'Your session changed. Sign in again.',refused:'Revoked certificates cannot be printed as valid.',
 retry:'Reload the certificate and try again.',history:'Issuance history',event:'Event',when:'When',actor:'Actor ID',
 issuedEvent:'Issued',reissuedEvent:'Reissued',revokedEvent:'Revoked',noHistory:'No audit events found.',sharing:'Guardian access',chooseGuardian:'Guardian',share:'Allow access'},
 bn: {title:'এলএমএস কোর্স সমাপ্তির সনদ',help:'যাচাইকৃত কোর্স সমাপ্তির পর সনদ দেওয়া হয়। বাতিল হলেও মূল ইতিহাস থাকে; পুনরায় দিলে নতুন সংস্করণ তৈরি হয়।',
 course:'কোর্স',previous:'আগের',next:'পরের',register:'সনদ নিবন্ধন',learner:'শিক্ষার্থী',
 revision:'সংস্করণ',issued:'ইস্যুর তারিখ',status:'অবস্থা',action:'কার্যক্রম',loading:'লোড হচ্ছে…',
 print:'প্রিন্ট / পিডিএফ সংরক্ষণ',revoke:'বাতিল',reissue:'পুনরায় ইস্যু',documentTitle:'কোর্স সমাপ্তির সনদ',
 documentSubtitle:'EduOS লার্নিং ম্যানেজমেন্ট সিস্টেম',completed:'কোর্সটি সম্পন্ন করেছেন',
 revoked:'বাতিল',valid:'বৈধ',empty:'কোনো সনদ পাওয়া যায়নি।',view:'দেখুন',
 choose:'কোর্স নির্বাচন করুন',select:'সনদ দেখতে কোর্স নির্বাচন করুন।',reason:'বাতিলের কারণ:',
 confirm:'এই সনদ বাতিল করবেন? মূল ইতিহাস সংরক্ষিত থাকবে।',error:'অনুরোধ ব্যর্থ হয়েছে।',
 missing:'সেশন পরিবর্তিত হয়েছে। আবার লগইন করুন।',refused:'বাতিল সনদ বৈধ হিসেবে প্রিন্ট করা যাবে না।',
 retry:'সনদ পুনরায় লোড করুন।',history:'ইস্যুর ইতিহাস',event:'ঘটনা',when:'সময়',actor:'কার্যসম্পাদনকারী আইডি',
 issuedEvent:'ইস্যু',reissuedEvent:'পুনরায় ইস্যু',revokedEvent:'বাতিল',noHistory:'কোনো অডিট তথ্য পাওয়া যায়নি।',sharing:'অভিভাবকের প্রবেশাধিকার',chooseGuardian:'অভিভাবক',share:'প্রবেশাধিকার দিন'}
};
const t = key => messages[lang][key] || messages.en[key] || key;
root.querySelectorAll('[data-lms-i18n]').forEach(node => {node.textContent=t(node.dataset.lmsI18n);});
const initialSession = document.body?.dataset.authSessionReference || '';
const initialTenant = document.body?.dataset.authTenantId || '';
const currentSession = () => /^[0-9A-F]{64}$/.test(initialSession) &&
 document.body?.dataset.authSessionReference === initialSession &&
 document.body?.dataset.authTenantId === initialTenant &&
 window.EduOSSessionTransitionPending?.() !== true;
const state = {coursePage:1,coursePages:1,course:'',page:1,pages:1,rows:[],
 selected:null,request:0,detailRequest:0,controller:null,busy:false};
function notice(type, message) {
 const node=$('lmsCertificateAlert');node.className='alert alert-'+type;
 node.textContent=message;node.focus();
}
function option(value,label){const el=document.createElement('option');el.value=value;el.textContent=label;return el;}
function td(tr,text){const el=document.createElement('td');el.textContent=String(text??'');tr.append(el);return el;}
const date = value => value ? new Intl.DateTimeFormat(lang==='bn'?'bn-BD':'en-BD',
 {dateStyle:'medium',timeZone:'UTC'}).format(new Date(value)) : '—';
async function api(path,method='GET',body,signal) {
 if(!currentSession())throw Error(t('missing'));
 const headers={Accept:'application/json'};
 if(method!=='GET'){
  const token=document.querySelector('meta[name="request-verification-token"]')?.content;
  if(!token)throw Error(t('missing'));
  headers.RequestVerificationToken=token;headers['Content-Type']='application/json';
 }
 const controller=new AbortController();
 const timeout=setTimeout(()=>controller.abort(),15000);
 const abort=()=>controller.abort();
 if(signal)signal.addEventListener('abort',abort,{once:true});
 try {
  const response=await fetch(path,{method,credentials:'same-origin',cache:'no-store',headers,
   signal:controller.signal,...(body===undefined?{}:{body:JSON.stringify(body)})});
  const result=await response.json().catch(()=>null);
  if(!currentSession())throw Error(t('missing'));
  if(!response.ok||!result?.success)throw Error(result?.message||t('error'));
  return result.data;
 } finally {clearTimeout(timeout);if(signal)signal.removeEventListener('abort',abort);}
}
function clearDetail(){
 state.detailRequest++;state.selected=null;
 $('lmsCertificateDetail').classList.add('d-none');
 const print=$('lmsCertificatePrint');print.removeAttribute('href');print.classList.add('disabled');
 print.setAttribute('aria-disabled','true');print.tabIndex=-1;
 for(const id of ['lmsCertificateStudent','lmsCertificateTitle','lmsCertificateCode',
  'lmsCertificateDates','lmsCertificateEvidence','lmsCertificateReference','lmsCertificateStatus'])
  $(id).textContent='';
 if($('lmsCertificateAuditRows'))$('lmsCertificateAuditRows').replaceChildren();
 root.dispatchEvent(new CustomEvent('lms-certificate-cleared'));
}
function renderRows(){
 const tbody=$('lmsCertificateRows');tbody.replaceChildren();
 for(const row of state.rows){
  const tr=document.createElement('tr');
  td(tr,row.studentName+' ('+row.studentCode+')');td(tr,row.courseTitle);
  td(tr,row.revision);td(tr,date(row.issuedAt));td(tr,row.isRevoked?t('revoked'):t('valid'));
  const actions=td(tr,'');const btn=document.createElement('button');
  btn.type='button';btn.className='btn btn-sm btn-outline-primary';btn.textContent=t('view');
  btn.addEventListener('click',()=>showCertificate(row.reference));actions.append(btn);tbody.append(tr);
 }
 if(!state.rows.length){const tr=document.createElement('tr');const el=td(tr,t('empty'));el.colSpan=6;tbody.append(tr);}
 $('lmsCertificatePage').textContent=state.page+' / '+state.pages;
 $('lmsCertificatePrev').disabled=state.page<=1;
 $('lmsCertificateNext').disabled=state.page>=state.pages;
}
async function loadRows(){
 const seq=++state.request;
 state.controller?.abort();state.controller=new AbortController();
 // A failed filter/page request must not leave a previous learner's records visible.
 state.rows=[];state.pages=1;renderRows();
 if(!student&&!guardian&&!state.course)return;
 const url=guardian?'/api/lms/certificate-sharing/mine':student?'/api/lms/certificates/mine':
  '/api/lms/certificates/course/'+encodeURIComponent(state.course);
 try {
  const data=await api(url+'?page='+state.page+'&pageSize=20','GET',undefined,state.controller.signal);
  if(seq!==state.request||!currentSession())return;
  state.rows=data?.items||[];state.pages=Math.max(1,data?.totalPages||1);renderRows();
 }catch(e){if(seq===state.request&&e.name!=='AbortError')notice('danger',e.message);}
}
async function loadCourses(){
 const old=state.course;
 try{
  const data=await api('/api/lms/courses?page='+state.coursePage+'&pageSize=25');
  const select=$('lmsCertificateCourse');select.replaceChildren(option('',t('choose')));
  (data?.items||[]).forEach(x=>select.append(option(x.reference,x.title+' ('+x.code+')')));
  state.coursePages=Math.max(1,data?.totalPages||1);
  state.course=(data?.items||[]).some(x=>x.reference===old)?old:'';
  select.value=state.course;
  $('lmsCertificateCoursePage').textContent=state.coursePage+' / '+state.coursePages;
  $('lmsCertificateCoursePrev').disabled=state.coursePage<=1;
  $('lmsCertificateCourseNext').disabled=state.coursePage>=state.coursePages;
  state.page=1;clearDetail();await loadRows();
 }catch(e){notice('danger',e.message);}
}
async function showCertificate(reference){
 const seq=++state.detailRequest;
 try {
  const row=await api((guardian?'/api/lms/certificate-sharing/':'/api/lms/certificates/')+encodeURIComponent(reference));
  if(!currentSession()||seq!==state.detailRequest)return;
  state.selected=row;
  $('lmsCertificateStudent').textContent=row.studentName+' ('+row.studentCode+')';
  $('lmsCertificateTitle').textContent=row.courseTitle;
  $('lmsCertificateCode').textContent=row.courseCode;
  $('lmsCertificateDates').textContent=date(row.completedAt)+' · '+date(row.issuedAt);
  $('lmsCertificateEvidence').textContent=
   'Lessons '+row.completedLessons+'/'+row.requiredLessons+' · Assignments '+
   row.gradedAssignments+'/'+row.requiredAssignments+' · Quizzes '+row.passedQuizzes+'/'+row.requiredQuizzes;
  $('lmsCertificateReference').textContent='Reference: '+row.reference+' · '+t('revision')+' '+row.revision;
  $('lmsCertificateStatus').textContent=row.isRevoked?t('revoked'):t('valid');
  $('lmsCertificateStatus').className=row.isRevoked?'text-danger':'text-success';
  $('lmsCertificateDetail').classList.remove('d-none');
  const print=$('lmsCertificatePrint');
  if(row.isRevoked){print.removeAttribute('href');print.classList.add('disabled');
   print.setAttribute('aria-disabled','true');print.tabIndex=-1;}
  else{print.href=(guardian?'/LmsGuardianCertificates/Print?reference=':'/Learning/CertificatePrint?reference=')+encodeURIComponent(row.reference);
   print.classList.remove('disabled');print.setAttribute('aria-disabled','false');print.tabIndex=0;}
  if($('lmsCertificateAuditRows'))loadAudit(row.reference,seq);
  if(student)root.dispatchEvent(new CustomEvent('lms-certificate-selected',{detail:{reference:row.reference}}));
  if($('lmsCertificateRevoke')){
   $('lmsCertificateRevoke').disabled=row.isRevoked;
   $('lmsCertificateReissue').disabled=!row.isRevoked;
  }
 }catch(e){if(seq===state.detailRequest){clearDetail();notice('danger',e.message);}}
}
async function loadAudit(reference,seq){
 const tbody=$('lmsCertificateAuditRows');if(!tbody)return;
 tbody.replaceChildren();
 try{
  const events=await api('/api/lms/certificates/'+encodeURIComponent(reference)+'/history');
  if(seq!==state.detailRequest||state.selected?.reference!==reference||!currentSession())return;
  for(const entry of events||[]){
   const tr=document.createElement('tr');
   const key=entry.action==='LMS.CertificateIssued'?'issuedEvent':
    entry.action==='LMS.CertificateReissued'?'reissuedEvent':'revokedEvent';
   td(tr,t(key));td(tr,date(entry.occurredAt));td(tr,entry.actorUserId??'—');tbody.append(tr);
  }
  if(!tbody.children.length){const tr=document.createElement('tr');const cell=td(tr,t('noHistory'));
   cell.colSpan=3;tbody.append(tr);}
 }catch(e){if(seq===state.detailRequest)notice('danger',e.message);}
}
async function revoke(){
 const row=state.selected;if(!row||row.isRevoked||state.busy)return;
 const reason=window.prompt(t('reason'));
 if(reason===null)return;
 if(!reason.trim()||reason.trim().length>500){notice('danger',t('reason'));return;}
 if(!window.confirm(t('confirm')))return;
 state.busy=true;
 try {
  const updated=await api('/api/lms/certificates/'+encodeURIComponent(row.reference)+'/revoke',
   'POST',{reason:reason.trim(),rowVersion:row.rowVersion});
  await loadRows();await showCertificate(updated.reference);
 }catch(e){notice('danger',e.message);}
 finally{state.busy=false;}
}
async function reissue(){
 const row=state.selected;if(!row||!row.isRevoked||state.busy)return;
 state.busy=true;
 try{
  const next=await api('/api/lms/certificates','POST',
   {courseEnrollmentId:row.courseEnrollmentId,clientRequestId:crypto.randomUUID()});
  await loadRows();await showCertificate(next.reference);
 }catch(e){notice('danger',e.message);}
 finally{state.busy=false;}
}
$('lmsCertificatePrev').addEventListener('click',()=>{if(state.page>1){state.page--;loadRows();}});
$('lmsCertificateNext').addEventListener('click',()=>{if(state.page<state.pages){state.page++;loadRows();}});

if(!student&&!guardian){
 $('lmsCertificateCourse').addEventListener('change',e=>{state.course=e.target.value;state.page=1;clearDetail();loadRows();});
 $('lmsCertificateCoursePrev').addEventListener('click',()=>{if(state.coursePage>1){state.coursePage--;loadCourses();}});
 $('lmsCertificateCourseNext').addEventListener('click',()=>{if(state.coursePage<state.coursePages){state.coursePage++;loadCourses();}});
 if($('lmsCertificateRevoke')){
  $('lmsCertificateRevoke').addEventListener('click',revoke);
  $('lmsCertificateReissue').addEventListener('click',reissue);
 }
}
// A tab can survive an institute switch, a cross-tab logout or BFCache navigation.
 // Scrub private learner/guardian data rather than merely hiding the old document.
function clearSensitive(){
 state.request++;state.controller?.abort();
 state.rows=[];state.pages=1;renderRows();clearDetail();
 state.course='';state.coursePages=1;
 const course=$('lmsCertificateCourse');
 if(course){course.replaceChildren(option('',t('choose')));course.value='';
  $('lmsCertificateCoursePage').textContent='';}
 const alert=$('lmsCertificateAlert');alert.textContent='';alert.className='d-none';
}
const sessionObserver=new MutationObserver(()=>{
 if(!currentSession()){clearSensitive();sessionObserver.disconnect();}
});
sessionObserver.observe(document.body,{attributes:true,
 attributeFilter:['data-auth-session-reference','data-auth-tenant-id']});
window.addEventListener('pagehide',clearSensitive);
window.addEventListener('pageshow',event=>{
 if(event.persisted&&currentSession()){
  if(student||guardian)loadRows();else loadCourses();
 }else if(!currentSession())clearSensitive();
});
const help=$('lmsCertificateHelp');
document.addEventListener('keydown',e=>{if(e.key==='Escape')help.open=false;});
document.addEventListener('pointerdown',e=>{if(!help.contains(e.target))help.open=false;});
if(student||guardian)loadRows();else loadCourses();
const deepLink=new URLSearchParams(location.search).get('certificate');
if(deepLink&&/^[0-9a-f-]{36}$/i.test(deepLink))showCertificate(deepLink);
})();
