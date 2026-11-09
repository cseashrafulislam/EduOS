(() => {
'use strict';
const root=document.getElementById('teacherCpd');if(!root)return;
const $=id=>document.getElementById(id),bn=root.dataset.lang==='bn';
const initialTenant=document.body?.dataset.authTenantId||'',initialSession=document.body?.dataset.authSessionReference||'';
const safe=()=>document.body?.dataset.authTenantId===initialTenant&&document.body?.dataset.authSessionReference===initialSession&&
 window.EduOSSessionTransitionPending?.()!==true;
let page=1,pages=1,rows=[],selected=null,epoch=0;
const node=(tag,text)=>{const item=document.createElement(tag);if(text!==undefined)item.textContent=String(text);return item;};
function alert(type,message){const el=$('cpdAlert');el.className='alert alert-'+type;el.textContent=message;el.focus();}
async function api(url,method='GET',body){
 if(!safe())throw Error(bn?'সেশন পরিবর্তিত হয়েছে। পুনরায় লোড করুন।':'Session changed. Reload.');
 const headers={Accept:'application/json'};
 if(body!==undefined)headers['Content-Type']='application/json';
 if(method!=='GET'){
  const token=document.querySelector('meta[name="request-verification-token"]')?.content;
  if(!token)throw Error('Security token missing. Reload.');headers.RequestVerificationToken=token;
 }
 const response=await fetch(url,{method,cache:'no-store',credentials:'same-origin',headers,
  ...(body===undefined?{}:{body:JSON.stringify(body)})});
 const value=await response.json().catch(()=>null);
 if(!safe())throw Error('Session changed. Reload.');
 if(!response.ok||!value?.success)throw Error(value?.message||'Request failed.');
 return value.data;
}
function action(text,handler,variant='outline-primary'){
 const b=node('button',text);b.type='button';b.className='btn btn-sm btn-'+variant;b.addEventListener('click',async()=>{
  if(b.disabled)return;b.disabled=true;
  try{await handler();}catch(e){alert('danger',e.message);}finally{b.disabled=false;}
 });return b;
}
async function load(){
 const serial=++epoch;
 try{
  const data=await api('/api/lms/teacher-cpd/courses?page='+page+'&pageSize=15&search='+encodeURIComponent($('cpdTerm').value.trim()));
  if(serial!==epoch)return;
  rows=data?.items||[];pages=Math.max(1,data?.totalPages||1);
  const body=$('cpdRows');body.replaceChildren();
  for(const row of rows){
   const tr=node('tr'),course=node('td'),name=node('strong',row.title);
   course.append(name,node('div',row.code));tr.append(course);
   tr.append(node('td',row.state),node('td',row.completedLessons+' / '+row.totalLessons));
   const controls=node('td'),box=node('div');box.className='d-flex gap-2 flex-wrap';
   if(!row.enrollmentId)box.append(action(bn?'অংশগ্রহণ':'Enroll',async()=>{
    await api('/api/lms/teacher-cpd/enroll','POST',{
     courseReference:row.reference,clientRequestId:crypto.randomUUID()});
    alert('success',bn?'প্রশিক্ষণে অংশগ্রহণ শুরু হয়েছে।':'Enrolled in CPD.');await load();
   }));
   else{
    box.append(action(bn?'পাঠ':'Lessons',()=>lessons(row)));
    if(row.state==='Active')box.append(action(bn?'সম্পন্ন করুন':'Complete',async()=>{
     await api('/api/lms/teacher-cpd/enrollments/'+row.enrollmentId+'/complete','POST',{rowVersion:row.rowVersion});
     alert('success',bn?'প্রশিক্ষণ সম্পন্ন হয়েছে।':'CPD completion recorded.');await load();
    },'primary'));
    if(row.certificateReference){const link=node('a',bn?'সার্টিফিকেট':'Certificate');
     link.className='btn btn-sm btn-success';link.href='/Learning/CertificatePrint?reference='+encodeURIComponent(row.certificateReference);box.append(link);}
   }
   controls.append(box);tr.append(controls);body.append(tr);
  }
  if(rows.length===0){const tr=node('tr'),td=node('td',bn?'কোনো প্রশিক্ষণ পাওয়া যায়নি।':'No training found.');td.colSpan=4;tr.append(td);body.append(tr);}
  $('cpdPage').textContent=page+' / '+pages;$('cpdPrev').disabled=page<=1;$('cpdNext').disabled=page>=pages;
 }catch(e){if(serial===epoch)alert('danger',e.message);}
}
async function lessons(course){
 selected=course;const serial=++epoch;
 const data=await api('/api/lms/teacher-cpd/enrollments/'+course.enrollmentId+'/lessons');
 if(serial!==epoch)return;
 $('cpdLessonHeading').textContent=course.title;$('cpdLessonsPanel').classList.remove('d-none');
 const target=$('cpdLessonRows');target.replaceChildren();
 for(const lesson of data){
  const card=node('article');card.className='border rounded p-3';
  card.append(node('h4',lesson.title));card.lastChild.className='h6';
  if(lesson.content)card.append(node('p',lesson.content));
  if(lesson.contentUrl){try{const u=new URL(lesson.contentUrl,location.origin);
   if(['https:','http:'].includes(u.protocol)){const link=node('a',bn?'পাঠের উৎস':'Lesson resource');
    link.href=u.href;link.rel='noopener noreferrer';link.target='_blank';card.append(link);}}
   catch{/* Existing malformed URLs are not opened. */}}
  const footer=node('div');footer.className='d-flex gap-2 align-items-center mt-2';
  footer.append(node('span',lesson.isCompleted?(bn?'সম্পন্ন':'Completed'):(bn?'চলমান':'In progress')));
  if(!lesson.isCompleted&&course.state==='Active')footer.append(action(bn?'সম্পন্ন করুন':'Mark completed',async()=>{
   await api('/api/lms/teacher-cpd/enrollments/'+course.enrollmentId+'/lessons','POST',{
    lessonReference:lesson.reference,isCompleted:true,progressPercent:100});
   alert('success',bn?'পাঠ সম্পন্ন।':'Lesson completed.');await load();
   const updated=rows.find(x=>x.reference===course.reference);if(updated)await lessons(updated);
  }));
  card.append(footer);target.append(card);
 }
 if(!data.length)target.append(node('p',bn?'প্রকাশিত পাঠ নেই।':'No published lessons.'));
 $('cpdLessonsPanel').scrollIntoView({behavior:'smooth',block:'nearest'});
}
$('cpdSearch').addEventListener('submit',e=>{e.preventDefault();page=1;load();});
$('cpdPrev').addEventListener('click',()=>{if(page>1){page--;load();}});
$('cpdNext').addEventListener('click',()=>{if(page<pages){page++;load();}});
$('cpdClose').addEventListener('click',()=>{$('cpdLessonsPanel').classList.add('d-none');selected=null;});
window.addEventListener('pagehide',()=>{epoch++;rows=[];selected=null;$('cpdRows').replaceChildren();$('cpdLessonRows').replaceChildren();});
new MutationObserver(()=>{if(!safe()){epoch++;rows=[];selected=null;$('cpdRows').replaceChildren();$('cpdLessonRows').replaceChildren();}})
 .observe(document.body,{attributes:true,attributeFilter:['data-auth-tenant-id','data-auth-session-reference']});
load();
})();
