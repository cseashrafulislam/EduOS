(()=>{
'use strict';const root=document.getElementById('cpdReview');if(!root)return;
const $=id=>document.getElementById(id),bn=root.dataset.lang==='bn';
const tenant=document.body?.dataset.authTenantId,session=document.body?.dataset.authSessionReference;
const safe=()=>tenant===document.body?.dataset.authTenantId&&session===document.body?.dataset.authSessionReference&&
 window.EduOSSessionTransitionPending?.()!==true;
let coursePage=1,coursePages=1,participantPage=1,participantPages=1,courseId='',generation=0;
const el=(tag,text)=>{const n=document.createElement(tag);if(text!==undefined)n.textContent=String(text);return n;};
function message(type,text){const n=$('cpdReviewAlert');n.className='alert alert-'+type;n.textContent=text;n.focus();}
async function api(path,method='GET',data){
 if(!safe())throw Error('Session changed. Reload.');
 const headers={Accept:'application/json'};
 if(method!=='GET'){
  const token=document.querySelector('meta[name="request-verification-token"]')?.content;
  if(!token)throw Error('Security token required.');headers.RequestVerificationToken=token;
  headers['Content-Type']='application/json';
 }
 const response=await fetch(path,{method,credentials:'same-origin',cache:'no-store',headers,
  ...(data===undefined?{}:{body:JSON.stringify(data)})});
 const json=await response.json().catch(()=>null);
 if(!safe())throw Error('Session changed. Reload.');
 if(!response.ok||!json?.success)throw Error(json?.message||'Request failed.');
 return json.data;
}
async function courses(){const k=++generation;
 try{
  const data=await api('/api/lms/teacher-cpd/admin/courses?search='+encodeURIComponent($('cpdReviewTerm').value.trim())+'&page='+coursePage+'&pageSize=20');
  if(k!==generation)return;
  coursePages=Math.max(1,data?.totalPages||1);const select=$('cpdCourseSelect');select.replaceChildren(new Option(bn?'কোর্স নির্বাচন করুন':'Choose course',''));
  for(const course of data?.items||[])select.add(new Option(course.title+' ('+course.code+')',course.reference));
  if(Array.from(select.options).some(x=>x.value===courseId))select.value=courseId;
  else{courseId='';$('cpdParticipantRows').replaceChildren();}
  $('cpdCoursePage').textContent=coursePage+' / '+coursePages;
  $('cpdCoursePrev').disabled=coursePage===1;$('cpdCourseNext').disabled=coursePage===coursePages;
  if(courseId)await participants();
 }catch(e){if(k===generation)message('danger',e.message);}
}
async function participants(){const k=++generation;if(!courseId)return;
 try{
  const data=await api('/api/lms/teacher-cpd/admin/courses/'+encodeURIComponent(courseId)+'/participants?page='+participantPage+'&pageSize=20');
  if(k!==generation)return;
  participantPages=Math.max(1,data?.totalPages||1);const body=$('cpdParticipantRows');body.replaceChildren();
  for(const p of data?.items||[]){const tr=el('tr'),employee=el('td',p.employeeName),code=el('small',p.employeeCode);employee.append(el('div'));employee.lastChild.append(code);
   tr.append(employee,el('td',p.state),el('td',p.completedAt?new Date(p.completedAt).toLocaleDateString(bn?'bn-BD':'en-BD'):'—'));
   const certificate=el('td');
   if(p.certificateReference){const a=el('a',bn?'সনদ দেখুন':'View certificate');a.className='btn btn-sm btn-outline-success';
    a.href='/Learning/CertificatePrint?reference='+encodeURIComponent(p.certificateReference);certificate.append(a);}
   else if(p.state==='Completed'){
    const button=el('button',bn?'সনদ ইস্যু':'Issue certificate');button.type='button';button.className='btn btn-sm btn-primary';
    button.addEventListener('click',async()=>{button.disabled=true;try{
     await api('/api/lms/certificates','POST',{courseEnrollmentId:p.enrollmentId,clientRequestId:crypto.randomUUID()});
     message('success',bn?'সনদ ইস্যু হয়েছে।':'Certificate issued.');await participants();
    }catch(e){message('danger',e.message);}finally{button.disabled=false;}});certificate.append(button);
   }else certificate.textContent='—';
   tr.append(certificate);body.append(tr);
  }
  if(!data?.items?.length){const tr=el('tr'),td=el('td',bn?'এখনো অংশগ্রহণকারী নেই।':'No participants.');td.colSpan=4;tr.append(td);body.append(tr);}
  $('cpdParticipantPage').textContent=participantPage+' / '+participantPages;
  $('cpdParticipantPrev').disabled=participantPage===1;$('cpdParticipantNext').disabled=participantPage===participantPages;
 }catch(e){if(k===generation)message('danger',e.message);}
}
$('cpdReviewSearch').addEventListener('submit',e=>{e.preventDefault();coursePage=1;courses();});
$('cpdCourseSelect').addEventListener('change',e=>{courseId=e.target.value;participantPage=1;participants();});
$('cpdCoursePrev').addEventListener('click',()=>{if(coursePage>1){coursePage--;courses();}});
$('cpdCourseNext').addEventListener('click',()=>{if(coursePage<coursePages){coursePage++;courses();}});
$('cpdParticipantPrev').addEventListener('click',()=>{if(participantPage>1){participantPage--;participants();}});
$('cpdParticipantNext').addEventListener('click',()=>{if(participantPage<participantPages){participantPage++;participants();}});
window.addEventListener('pagehide',()=>{generation++;$('cpdParticipantRows').replaceChildren();});
new MutationObserver(()=>{if(!safe()){generation++;courseId='';$('cpdParticipantRows').replaceChildren();}})
 .observe(document.body,{attributes:true,attributeFilter:['data-auth-tenant-id','data-auth-session-reference']});
courses();
})();
