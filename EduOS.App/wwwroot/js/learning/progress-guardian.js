(() => {
'use strict';
const studentRoot=document.getElementById('lmsProgressConsentApp');
const guardianRoot=document.getElementById('lmsGuardianProgressApp');
const root=studentRoot||guardianRoot;if(!root)return;
const $=id=>document.getElementById(id);
const bn=root.dataset.lang==='bn'||(document.documentElement.lang||'').startsWith('bn');
const t=(en,bd)=>bn?bd:en;
const initialSession=String(document.body?.dataset.authSessionReference||'');
const initialTenant=String(document.body?.dataset.authTenantId||'');
const valid=()=>/^[0-9a-f]{64}$/i.test(initialSession)&&
 String(document.body?.dataset.authSessionReference||'')===initialSession&&
 String(document.body?.dataset.authTenantId||'')===initialTenant&&
 window.EduOSSessionTransitionPending?.()!==true;
let sequence=0,busy=false,page=1,pages=1,rows=[],selected=0,lessonPage=1,lessonPages=1;
const alert=$('lmsGuardianProgressAlert')||$('lmsProgressConsentAlert');
function warn(message){alert.className='alert alert-danger my-2';alert.textContent=message;}
function reset(){
 sequence++;rows=[];selected=0;page=1;pages=1;lessonPage=1;
 for(const id of ['lmsGuardianProgressRows','lmsGuardianLessonRows','lmsProgressConsentGrants','lmsProgressConsentGuardian','lmsProgressConsentEnrollment']){
  const node=$(id);if(node)node.replaceChildren();
 }
 if($('lmsGuardianLessonPanel'))$('lmsGuardianLessonPanel').classList.add('d-none');
}
async function api(path,method='GET',payload){
 if(!valid())throw Error(t('Session changed. Sign in again.','সেশন পরিবর্তিত হয়েছে। আবার লগইন করুন।'));
 const headers={Accept:'application/json'};
 if(method!=='GET'){
  const token=document.querySelector('meta[name="request-verification-token"]')?.content;
  if(!token)throw Error(t('Security token missing.','নিরাপত্তা টোকেন পাওয়া যায়নি।'));
  headers.RequestVerificationToken=token;headers['Content-Type']='application/json';
 }
 const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),15000);
 try{
  const response=await fetch(path,{method,credentials:'same-origin',cache:'no-store',headers,
   signal:controller.signal,...(payload===undefined?{}:{body:JSON.stringify(payload)})});
  const json=await response.json().catch(()=>null);
  if(!valid())throw Error(t('Session changed.','সেশন পরিবর্তিত হয়েছে।'));
  if(!response.ok||!json?.success)throw Error(json?.message||t('Request failed.','অনুরোধ ব্যর্থ হয়েছে।'));
  return json.data;
 }finally{clearTimeout(timer);}
}
function td(tr,value){const cell=document.createElement('td');cell.textContent=String(value??'');tr.append(cell);return cell;}
function option(value,label){const el=document.createElement('option');el.value=String(value);el.textContent=label;return el;}
function csvValue(value){
 let text=String(value??'');
 if(/^[\s]*[=+\-@\t\r]/.test(text))text="'"+text;
 return '"'+text.replace(/"/g,'""')+'"';
}
function csv(data,name){
 if(!valid())return;
 const content='\uFEFF'+data.map(row=>row.map(csvValue).join(',')).join('\r\n');
 const blob=new Blob([content],{type:'text/csv;charset=utf-8'});
 const url=URL.createObjectURL(blob),a=document.createElement('a');
 a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
}
async function studentCourses(){
 const seq=++sequence;
 try{
  const data=await api('/api/lms/my-progress?'+new URLSearchParams({page:String(page),pageSize:'20'}));
  if(seq!==sequence||!valid())return;
  pages=Math.max(1,data.totalPages||1);
  const select=$('lmsProgressConsentEnrollment');select.replaceChildren();
  for(const row of data.items||[])select.append(option(row.courseEnrollmentId,row.courseTitle));
  if(!select.options.length)select.append(option('',t('No enrolled courses','কোনো কোর্স নেই')));
  $('lmsProgressConsentPrev').disabled=page<=1;$('lmsProgressConsentNext').disabled=page>=pages;
  $('lmsProgressConsentPage').textContent=page+' / '+pages;
  await studentGrants();
 }catch(e){if(seq===sequence)warn(e.message);}
}
async function studentGrants(){
 const enrollment=Number($('lmsProgressConsentEnrollment').value);
 const seq=++sequence;selected=enrollment;
 $('lmsProgressConsentGrants').replaceChildren();$('lmsProgressConsentGuardian').replaceChildren();
 $('lmsProgressConsentGrant').disabled=true;
 if(!enrollment)return;
 try{
  const [choices,grants]=await Promise.all([
   api('/api/lms/progress-sharing/'+enrollment+'/guardians'),
   api('/api/lms/progress-sharing/'+enrollment+'/grants')
  ]);
  if(seq!==sequence||selected!==enrollment||!valid())return;
  for(const choice of choices||[])$('lmsProgressConsentGuardian').append(option(choice.guardianLinkId,
   choice.guardianName+' ('+choice.relationCode+')'));
  $('lmsProgressConsentGrant').disabled=!(choices||[]).length;
  for(const grant of grants||[]){
   const line=document.createElement('div');line.className='d-flex flex-wrap gap-2 align-items-center mb-2';
   const label=document.createElement('span');label.textContent=grant.guardianName;
   const revoke=document.createElement('button');revoke.type='button';revoke.className='btn btn-sm btn-outline-danger';
   revoke.textContent=t('Remove access','অনুমতি বাতিল');
   revoke.onclick=async()=>{
    if(busy||!valid()||!window.confirm(t('Revoke this progress access?','এই অগ্রগতির অনুমতি বাতিল করবেন?')))return;
    busy=true;revoke.disabled=true;
    try{await api('/api/lms/progress-sharing/grants/'+encodeURIComponent(grant.reference)+'/revoke',
     'POST',{rowVersion:grant.rowVersion});await studentGrants();}
    catch(e){warn(e.message);}finally{busy=false;revoke.disabled=false;}
   };
   line.append(label,revoke);$('lmsProgressConsentGrants').append(line);
  }
 }catch(e){if(seq===sequence)warn(e.message);}
}
async function grant(){
 const enrollment=Number($('lmsProgressConsentEnrollment').value);
 const guardian=Number($('lmsProgressConsentGuardian').value);
 if(busy||!enrollment||!guardian||!valid()||
  !window.confirm(t('Share this course progress with the selected guardian?','এই কোর্সের অগ্রগতি নির্বাচিত অভিভাবককে দেখাবেন?')))return;
 busy=true;$('lmsProgressConsentGrant').disabled=true;
 try{await api('/api/lms/progress-sharing/grants','POST',{
  courseEnrollmentId:enrollment,guardianLinkId:guardian,clientRequestId:crypto.randomUUID()
 });await studentGrants();}
 catch(e){warn(e.message);}finally{busy=false;}
}
function renderCourses(){
 const body=$('lmsGuardianProgressRows');body.replaceChildren();
 for(const row of rows){
  const tr=document.createElement('tr');
  td(tr,row.learnerName);td(tr,row.courseTitle);
  td(tr,row.completedLessons+' / '+row.publishedLessons);
  td(tr,row.lessonCompletionPercent+'%');
  const action=td(tr,'');
  const button=document.createElement('button');button.type='button';button.className='btn btn-sm btn-outline-primary';
  button.textContent=t('Lessons','পাঠসমূহ');
  button.onclick=()=>{selected=row.courseEnrollmentId;lessonPage=1;
   $('lmsGuardianLessonHeading').textContent=row.courseTitle;
   $('lmsGuardianLessonPanel').classList.remove('d-none');loadLessons();};
  action.append(button);body.append(tr);
 }
 if(!rows.length){const tr=document.createElement('tr'),cell=td(tr,t('No shared courses.','শেয়ার করা কোর্স নেই।'));cell.colSpan=5;body.append(tr);}
 $('lmsGuardianProgressPage').textContent=page+' / '+pages;
 $('lmsGuardianProgressPrev').disabled=page<=1;$('lmsGuardianProgressNext').disabled=page>=pages;
}
async function guardianCourses(){
 const seq=++sequence;selected=0;
 $('lmsGuardianLessonPanel').classList.add('d-none');
 try{
  const data=await api('/api/lms/progress-sharing/mine?'+new URLSearchParams({page:String(page),pageSize:'20'}));
  if(seq!==sequence||!valid())return;
  rows=data.items||[];pages=Math.max(1,data.totalPages||1);renderCourses();
 }catch(e){if(seq===sequence)warn(e.message);}
}
async function loadLessons(){
 const enrollment=selected,seq=++sequence;
 try{
  const data=await api('/api/lms/progress-sharing/mine/'+enrollment+'/lessons?'+
   new URLSearchParams({page:String(lessonPage),pageSize:'20'}));
  if(seq!==sequence||selected!==enrollment||!valid())return;
  lessonPages=Math.max(1,data.totalPages||1);
  const body=$('lmsGuardianLessonRows');body.replaceChildren();
  for(const item of data.items||[]){
   const tr=document.createElement('tr');td(tr,item.lessonTitle);
   td(tr,item.progressPercent+'%');td(tr,item.isCompleted?t('Yes','হ্যাঁ'):t('No','না'));body.append(tr);
  }
  $('lmsGuardianLessonPage').textContent=lessonPage+' / '+lessonPages;
  $('lmsGuardianLessonPrev').disabled=lessonPage<=1;
  $('lmsGuardianLessonNext').disabled=lessonPage>=lessonPages;
 }catch(e){if(seq===sequence)warn(e.message);}
}
if(studentRoot){
 $('lmsProgressConsentEnrollment').onchange=studentGrants;
 $('lmsProgressConsentGrant').onclick=grant;
 $('lmsProgressConsentPrev').onclick=()=>{if(page>1){page--;studentCourses();}};
 $('lmsProgressConsentNext').onclick=()=>{if(page<pages){page++;studentCourses();}};
 studentCourses();
}else{
 $('lmsGuardianProgressPrev').onclick=()=>{if(page>1){page--;guardianCourses();}};
 $('lmsGuardianProgressNext').onclick=()=>{if(page<pages){page++;guardianCourses();}};
 $('lmsGuardianLessonClose').onclick=()=>{$('lmsGuardianLessonPanel').classList.add('d-none');selected=0;sequence++;};
 $('lmsGuardianLessonPrev').onclick=()=>{if(lessonPage>1){lessonPage--;loadLessons();}};
 $('lmsGuardianLessonNext').onclick=()=>{if(lessonPage<lessonPages){lessonPage++;loadLessons();}};
 $('lmsGuardianProgressExport').onclick=()=>csv([
  [t('Learner','শিক্ষার্থী'),t('Course','কোর্স'),t('Lessons completed','সম্পন্ন পাঠ'),t('Lessons published','মোট পাঠ'),t('Completion','সম্পন্ন')],
  ...rows.map(x=>[x.learnerName,x.courseTitle,x.completedLessons,x.publishedLessons,x.lessonCompletionPercent+'%'])
 ],'lms-guardian-progress-visible.csv');
 guardianCourses();
}
const observer=new MutationObserver(()=>{if(!valid()){reset();observer.disconnect();}});
observer.observe(document.body,{attributes:true,attributeFilter:['data-auth-session-reference','data-auth-tenant-id']});
window.addEventListener('pagehide',reset);
})();
