(() => {
'use strict';
const root=document.getElementById('studentOptionalSubjects'); if(!root)return;
const canManage=root.dataset.canManage==='true';
const el=id=>document.getElementById(id), token=root.querySelector('input[name="__RequestVerificationToken"]').value;
let student=null, workspace=null, requestId=null, busy=false;
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
 el('subjectsOffering').disabled=busy||!workspace;
 el('subjectsRequestButton').disabled=busy||!workspace||!chosen||chosen.isEligible===false;
 el('subjectsEligibility').textContent=missing.length?'Complete prerequisites: '+missing.join(', '):'Eligibility is rechecked on approval.';
 el('subjectsCapacity').textContent=chosen?(chosen.seatsRemaining+' of '+chosen.capacity+' seats remaining'+(chosen.seatsRemaining<=0?' — Full':'')):'Select an elective to check seats.';
}
async function load(ref){
 const data=await api('/api/student-optional-subjects/students/'+encodeURIComponent(ref));
 if(student?.reference!==ref)return;
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
}
async function choose(item){
 student=item;workspace=null;requestId=null;
 el('subjectsStudent').classList.add('d-none');
 el('subjectsOffering').replaceChildren(new Option('Loading electives…',''));
 el('subjectsRegistrations').textContent='Loading registrations…';
 refresh();try{await load(item.reference);}catch(e){notice(e.message,true);}
}
async function loadLinkedStudents(){
 const menu=el('subjectsLinkedStudents');
 try{
  const linked=await api('/api/portal/students');
  menu.replaceChildren(new Option('Choose linked student',''));
  for(const item of linked||[]){
   if(!item.reference)continue;
   menu.add(new Option((item.name||'Student')+' · '+(item.studentCode||''),item.reference));
  }
  const requested=new URLSearchParams(location.search).get('studentReference');
  const match=(linked||[]).find(x=>x.reference===requested)||(linked||[])[0];
  if(match?.reference){menu.value=match.reference;await choose(match);}
  else notice('No student is linked to your account.',true);
 }catch(e){notice(e.message,true);}
}
async function search(e){
 e.preventDefault();if(val('subjectsSearch').length<2)return notice('Enter at least two characters.',true);
 const box=el('subjectsMatches');box.textContent='Searching...';
 try{
  const p=await api('/api/students?'+new URLSearchParams({search:val('subjectsSearch'),page:1,pageSize:20}));
  box.replaceChildren();
  for(const item of p.items||[]){const btn=document.createElement('button');btn.type='button';btn.className='list-group-item list-group-item-action';btn.textContent=item.fullName+' — '+item.studentCode;btn.addEventListener('click',()=>choose(item));box.append(btn);}
  if(!box.children.length)box.textContent='No students found.';
 }catch(err){notice(err.message,true);}
}
async function submit(e){
 e.preventDefault();if(busy||!workspace||!val('subjectsOffering'))return;
  const chosen=(workspace.offerings||[]).find(x=>x.reference===val('subjectsOffering'));
  if(!chosen||chosen.isEligible===false)return notice('Complete prerequisites and reload the workspace.',true);
 requestId??=crypto.randomUUID();busy=true;refresh();
 try{
  await api('/api/academic-enrollments/subjects/requests',{clientRequestId:requestId,studentEnrollmentReference:workspace.enrollmentReference,subjectOfferingReference:val('subjectsOffering'),remarks:val('subjectsRemarks')||null});
  requestId=null;await load(student.reference);notice('Optional subject requested.');
 }catch(err){notice(err.message,true);}finally{busy=false;refresh();}
}
async function decide(item,decision){
 if(!canManage)return;
 if(busy||!confirm('Confirm subject decision?'))return;busy=true;refresh();
 try{
  await api('/api/academic-enrollments/subjects/'+item.id+'/decision',{state:decision,rowVersion:item.rowVersion});
  await load(student.reference);notice('Decision saved.');
 }catch(err){notice(err.message,true);}finally{busy=false;refresh();}
}
if(canManage)el('subjectsSearchForm').addEventListener('submit',search);
else el('subjectsLinkedStudents').addEventListener('change',e=>{
 const ref=e.target.value;if(ref)choose({reference:ref});
 else {student=null;workspace=null;el('subjectsStudent').classList.add('d-none');refresh();}
});
el('subjectsRequestForm').addEventListener('submit',submit);
el('subjectsOffering').addEventListener('change',()=>{requestId=null;refresh();});
el('subjectsRemarks').addEventListener('input',()=>{requestId=null;});
const ref=new URLSearchParams(location.search).get('studentReference');
if(canManage&&ref&&/^[0-9a-f-]{36}$/i.test(ref))choose({reference:ref});
if(!canManage)loadLinkedStudents();
})();
