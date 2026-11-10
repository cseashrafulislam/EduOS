(() => {
'use strict';
const root=document.getElementById('studentOptionalSubjects'); if(!root)return;
const canManage=root.dataset.canManage==='true';
const el=id=>document.getElementById(id), token=root.querySelector('input[name="__RequestVerificationToken"]').value;
let student=null, workspace=null, requestId=null, requestFingerprint=null, busy=false;
let loadVersion=0, searchVersion=0, linkedVersion=0;
const form=el('subjectsRequestForm'), guard=window.EduOSFormGuard?.create(form);
const identity=[document.body?.dataset?.authTenantId||'',document.body?.dataset?.authSessionReference||''];
const sameSession=()=>identity[0]===(document.body?.dataset?.authTenantId||'')&&
 identity[1]===(document.body?.dataset?.authSessionReference||'')&&
 window.EduOSSessionTransitionPending?.()!==true;
const val=id=>el(id).value.trim();
function notice(message,error=false){const box=el('subjectsNotice');box.className='alert '+(error?'alert-danger':'alert-success');box.textContent=message;}
async function api(url,body){
 const r=await fetch(url,{method:body?'POST':'GET',credentials:'same-origin',cache:'no-store',
 headers:{Accept:'application/json',...(body?{'Content-Type':'application/json',RequestVerificationToken:token}:{})},
 ...(body?{body:JSON.stringify(body)}:{})});
 const p=await r.json().catch(()=>null);
 if(!r.ok||p?.success!==true)throw Error(p?.message||'Request failed. Reload and retry.');
 return p.data;
}
function refresh(){
 const chosen=(workspace?.offerings||[]).find(x=>x.reference===val('subjectsOffering'));
 const missing=chosen?.missingPrerequisites||[];
 el('subjectsOffering').disabled=busy||!workspace||!sameSession();
 el('subjectsRequestButton').disabled=busy||!workspace||!chosen||chosen.isEligible===false||!sameSession();
 el('subjectsEligibility').textContent=missing.length?'Complete prerequisites: '+missing.join(', '):'Eligibility is rechecked on approval.';
 el('subjectsCapacity').textContent=chosen?(chosen.seatsRemaining+' of '+chosen.capacity+' seats remaining'+(chosen.seatsRemaining<=0?' — Full':'')):'Select an elective to check seats.';
}
async function load(ref){
 const version=++loadVersion;
 const data=await api('/api/student-optional-subjects/students/'+encodeURIComponent(ref));
 if(student?.reference!==ref||version!==loadVersion||!sameSession())return false;
 if(data?.studentReference!==ref)throw Error('Student reference mismatch. Reload and retry.');
 workspace=data;el('subjectsStudent').classList.remove('d-none');
 el('subjectsStudentName').textContent=data.studentName;
 el('subjectsStudentDetails').textContent=data.studentCode+' · Roll: '+data.rollNo;
 const menu=el('subjectsOffering');menu.replaceChildren(new Option('Choose optional subject',''));
 for(const item of data.offerings||[]){
   const missing=item.missingPrerequisites||[];
   const option=new Option(item.subjectName+' ('+item.code+') — '+item.seatsRemaining+'/'+item.capacity+' seats'+(missing.length?' — Requires: '+missing.join(', '):'')+(item.seatsRemaining<=0?' — Full':''),item.reference);
   option.disabled=item.isEligible===false;menu.add(option);
  }
 const table=el('subjectsRegistrations');table.replaceChildren();
 for(const item of data.registrations||[]){
  const row=document.createElement('tr');
  const statuses={1:'Pending',2:'Approved',3:'Rejected',4:'Dropped',5:'Completed'};
  for(const v of [item.subjectName,statuses[item.state]||item.state,item.registeredAt||'—']){
   const td=document.createElement('td');td.textContent=v;row.append(td);
  }
  const td=document.createElement('td');
  if(canManage&&(item.state===1||item.state==='Pending'))for(const [label,decision] of [['Approve',2],['Reject',3]]){
   const btn=document.createElement('button');btn.type='button';btn.className='btn btn-sm btn-outline-primary me-1';btn.textContent=label;
   btn.addEventListener('click',()=>decide(item,decision));td.append(btn);
  }
  row.append(td);table.append(row);
 }
 if(!table.children.length)table.textContent='No registrations.';
 refresh();
 root.dispatchEvent(new CustomEvent('student-subjects:selected',{detail:{reference:ref}}));
 return true;
}
async function choose(item){
 if(busy||!sameSession())return false;
 if(guard?.isDirty()&&!guard.confirmDiscard())return false;
 ++loadVersion;
 student=item;workspace=null;requestId=null;requestFingerprint=null;
 form.reset();guard?.markClean();
 root.dispatchEvent(new CustomEvent('student-subjects:selected',{detail:{reference:null}}));
 el('subjectsStudent').classList.add('d-none');
 el('subjectsOffering').replaceChildren(new Option('Loading electives…',''));
 el('subjectsRegistrations').textContent='Loading registrations…';
 refresh();try{return await load(item.reference);}catch(e){if(sameSession())notice(e.message,true);return false;}
}
async function loadLinkedStudents(){
 if(!sameSession())return;
 const version=++linkedVersion;
 const menu=el('subjectsLinkedStudents');
 try{
  const linked=await api('/api/portal/students');
  if(version!==linkedVersion||!sameSession())return;
  menu.replaceChildren(new Option('Choose linked student',''));
  for(const item of linked||[]){
   if(!item.reference)continue;
   menu.add(new Option((item.name||'Student')+' · '+(item.studentCode||''),item.reference));
  }
  const requested=new URLSearchParams(location.search).get('studentReference');
  const match=(linked||[]).find(x=>x.reference===requested)||(linked||[])[0];
  if(match?.reference){menu.value=match.reference;await choose(match);}
  else notice('No student is linked to your account.',true);
 }catch(e){if(version===linkedVersion&&sameSession())notice(e.message,true);}
}
async function search(e){
 e.preventDefault();if(busy||!sameSession())return;
 const query=val('subjectsSearch');
 if(query.length<2||query.length>100)return notice('Enter 2–100 characters.',true);
 const version=++searchVersion;
 const box=el('subjectsMatches');box.textContent='Searching...';
 try{
  const p=await api('/api/students?'+new URLSearchParams({search:query,page:1,pageSize:20}));
  if(version!==searchVersion||!sameSession())return;
  box.replaceChildren();
  for(const item of p.items||[]){const btn=document.createElement('button');btn.type='button';btn.className='list-group-item list-group-item-action';btn.textContent=item.fullName+' — '+item.studentCode;btn.addEventListener('click',()=>choose(item));box.append(btn);}
  if(!box.children.length)box.textContent='No students found.';
 }catch(err){if(version===searchVersion&&sameSession())notice(err.message,true);}
}
async function submit(e){
 e.preventDefault();if(busy||!workspace||!val('subjectsOffering')||!sameSession())return;
  const chosen=(workspace.offerings||[]).find(x=>x.reference===val('subjectsOffering'));
  if(!chosen||chosen.isEligible===false)return notice('Complete prerequisites and reload the workspace.',true);
 const payload={studentEnrollmentReference:workspace.enrollmentReference,
  subjectOfferingReference:chosen.reference,remarks:val('subjectsRemarks')||null};
 const fingerprint=JSON.stringify(payload);
 if(!requestId||requestFingerprint!==fingerprint)requestId=crypto.randomUUID();
 requestFingerprint=fingerprint;payload.clientRequestId=requestId;
 busy=true;form.inert=true;refresh();let committed=false;
 try{
  const ref=student.reference;
  await api('/api/academic-enrollments/subjects/requests',payload);
  committed=true;requestId=null;requestFingerprint=null;
  if(!sameSession())return;
  form.reset();guard?.markClean();workspace=null;refresh();
  const refreshed=await load(ref);
  if(sameSession())notice(refreshed?'Optional subject requested.':
   'Request saved, but the workspace could not be refreshed. Reload before requesting again.',!refreshed);
 }catch(err){if(sameSession()){
  if(committed)workspace=null;
  notice(committed?'Request saved, but the workspace could not be refreshed. Reload to verify.':
   err.message+' Retry unchanged details with the same request ID.',true);
 }}finally{busy=false;form.inert=false;refresh();}
}
async function decide(item,decision){
 if(!canManage||!sameSession())return;
 if(busy||!confirm('Confirm subject decision?'))return;busy=true;refresh();let committed=false;
 try{
  const ref=student.reference;
  await api('/api/academic-enrollments/subjects/'+item.id+'/decision',{state:decision,rowVersion:item.rowVersion});
  committed=true;workspace=null;refresh();
  const refreshed=await load(ref);
  if(sameSession())notice(refreshed?'Decision saved.':'Decision saved, but readback failed. Reload to verify.',!refreshed);
 }catch(err){if(sameSession()){
  workspace=null;
  notice(committed?'Decision saved, but readback failed. Reload to verify.':
   err.message+' Refresh the current student before retrying an uncertain decision.',true);
 }}finally{busy=false;refresh();}
}
if(canManage){
 el('subjectsSearchForm').addEventListener('submit',search);
 el('subjectsSearch').addEventListener('input',()=>{++searchVersion;});
}
else el('subjectsLinkedStudents').addEventListener('change',e=>{
 const ref=e.target.value;
 if(busy||!sameSession()){e.target.value=student?.reference||'';return;}
 if(ref)void choose({reference:ref}).then(changed=>{if(!changed)e.target.value=student?.reference||'';});
 else if(!guard?.isDirty()||guard.confirmDiscard()){
  student=null;workspace=null;requestId=null;requestFingerprint=null;++loadVersion;
  form.reset();guard?.markClean();el('subjectsStudent').classList.add('d-none');
  root.dispatchEvent(new CustomEvent('student-subjects:selected',{detail:{reference:null}}));refresh();
 }else e.target.value=student?.reference||'';
});
el('subjectsRequestForm').addEventListener('submit',submit);
el('subjectsOffering').addEventListener('change',refresh);
const ref=new URLSearchParams(location.search).get('studentReference');
if(canManage&&ref&&/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(ref))choose({reference:ref});
if(!canManage)loadLinkedStudents();
})();
