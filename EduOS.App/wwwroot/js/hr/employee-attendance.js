(() => {
'use strict';
const root=document.getElementById('employeeAttendanceRoot');if(!root)return;
const $=id=>document.getElementById('employeeAttendance'+id);
const token=document.querySelector('meta[name="request-verification-token"]')?.content;
const statuses={1:'Present',2:'Absent',3:'Late',4:'Leave',5:'Excused'};
let page=1,pages=1,busy=false,accessRevoked=false,searchSeq=0,loadSeq=0,historySeq=0,
    selectedHistory=null,requestKey=null,requestSignature='',lastPosted=null,previousState='1';
const forms={record:$('Form'),correction:$('CorrectionForm')},guards={};
for(const [name,form] of Object.entries(forms))
 if(window.EduOSFormGuard)guards[name]=window.EduOSFormGuard.create(form);
const mayDiscard=name=>!guards[name]?.isDirty()||guards[name].confirmDiscard();
function requestIdFor(command){const signature=JSON.stringify(command);
 if(requestSignature!==signature){requestSignature=signature;requestKey=crypto.randomUUID();}
 return {signature,key:requestKey};}
function notice(message,ok=false){const e=$('Alert');e.className='alert alert-'+(ok?'success':'danger');e.textContent=message;e.focus();}
async function api(url,method='GET',body){
 const headers={Accept:'application/json'};
 if(method!=='GET'){if(!token)throw Error('Security token missing. Reload.');headers.RequestVerificationToken=token;headers['Content-Type']='application/json';}
 const response=await fetch(url,{method,headers,credentials:'same-origin',cache:'no-store',...(body===undefined?{}:{body:JSON.stringify(body)})});
 const result=await response.json().catch(()=>null);
 if(!response.ok||!result?.success){const error=Error(result?.message||'Request failed.');
  error.statusCode=response.status;throw error;}
 return result.data;
}
function handleError(error){
 if([401,403].includes(error.statusCode)){
  accessRevoked=true;++searchSeq;++loadSeq;++historySeq;
  $('Rows').replaceChildren();$('HistoryRows').replaceChildren();
  for(const id of ['Employee','FilterEmployee'])$(id).replaceChildren(new Option('Access unavailable',''));
  forms.record.reset();forms.correction.reset();guards.record?.markClean();guards.correction?.markClean();
  $('CorrectionCard').classList.add('d-none');$('HistoryCard').classList.add('d-none');root.inert=true;
 }
 notice(error.message);
}
function td(value){const c=document.createElement('td');c.textContent=value==null?'—':String(value);return c;}
function empty(target,count,message){target.replaceChildren();const row=document.createElement('tr'),c=td(message);c.colSpan=count;row.append(c);target.append(row);}
function localDate(d){const x=new Date(d.getTime()-d.getTimezoneOffset()*60000);return x.toISOString().slice(0,10);}
async function employees(){
 if(busy||accessRevoked||!mayDiscard('record'))return;
 const term=$('EmployeeSearch').value.trim();
 if(term.length<2||term.length>100)return notice('Enter 2–100 characters to search employees.');
 const seq=++searchSeq;$('EmployeeFind').disabled=true;$('EmployeeSearchStatus').textContent='Searching…';
 try{
  const data=await api('/api/hr/employees?'+new URLSearchParams({state:1,search:term,page:1,pageSize:50}));
  if(seq!==searchSeq||accessRevoked||term!==$('EmployeeSearch').value.trim()||!mayDiscard('record'))return;
  for(const id of ['Employee','FilterEmployee']){
   const sel=$(id);sel.replaceChildren(new Option(id==='Employee'?'Choose employee':'All employees',''));
   for(const employee of data.items||[])sel.add(new Option(employee.employeeCode+' — '+employee.fullName,employee.reference));
  }
  forms.record.reset();$('Date').value=localDate(new Date());previousState=$('State').value;
  stateFields();guards.record?.markClean();
  $('EmployeeSearchStatus').textContent=(data.totalCount||0)+' result(s)'+
   (data.totalCount>50?' · First 50 shown; narrow search.':'');
 }catch(error){if(seq===searchSeq)handleError(error);}
 finally{if(seq===searchSeq)$('EmployeeFind').disabled=false;}
}
function stateFields(){const active=[1,3].includes(Number($('State').value));for(const id of ['In','Out','Ot']){$(id).disabled=!active;if(!active)$(id).value=id==='Ot'?'0':'';}}
async function load(){
 if(accessRevoked)return;
 const seq=++loadSeq;
 const q=new URLSearchParams({fromDate:$('From').value,toDate:$('To').value,page:String(page),pageSize:'25'});
 if($('FilterEmployee').value)q.set('employeeReference',$('FilterEmployee').value);
 let data;
 try{data=await api('/api/hr/employee-attendance?'+q);}
 catch(error){if(seq===loadSeq)throw error;return;}
 if(seq!==loadSeq||accessRevoked||page!==Number(q.get('page'))||
  q.get('fromDate')!==$('From').value||q.get('toDate')!==$('To').value||
  (q.get('employeeReference')||'')!==$('FilterEmployee').value)return;
 pages=Math.max(1,data.totalPages||1);$('Page').textContent='Page '+page+' of '+pages+' · '+data.totalCount+' records';$('Prev').disabled=page<=1;$('Next').disabled=page>=pages;
 const body=$('Rows');body.replaceChildren();if(!data.items?.length){empty(body,8,'No attendance records.');return;}
 for(const row of data.items){const tr=document.createElement('tr');
 [row.attendanceDate,row.employeeCode+' — '+row.employeeName,statuses[row.state]||row.state,row.inTime?.slice(0,5),row.outTime?.slice(0,5),row.overtimeHours,row.remarks].forEach(x=>tr.append(td(x)));
 const action=td('');const correct=document.createElement('button');correct.type='button';correct.className='btn btn-sm btn-outline-warning me-1';correct.textContent='Correct';
 correct.addEventListener('click',()=>{if(busy||accessRevoked||!mayDiscard('correction'))return;
  forms.correction.reset();$('CorrectionId').value=row.id;$('CorrectionVersion').value=row.rowVersion;
  $('CorrectionState').value=row.state;$('CorrectionCard').classList.remove('d-none');
  guards.correction?.markClean();$('CorrectionCard').scrollIntoView({block:'center'});});
 const history=document.createElement('button');history.type='button';history.className='btn btn-sm btn-outline-secondary';history.textContent='History';
 history.addEventListener('click',async()=>{if(busy||accessRevoked)return;
  const seq=++historySeq;selectedHistory=row.id;$('HistoryCard').classList.add('d-none');
  $('HistoryRows').replaceChildren();
  try{const data=await api('/api/hr/employee-attendance/'+encodeURIComponent(row.id)+'/corrections');
   if(seq!==historySeq||selectedHistory!==row.id||accessRevoked)return;
   const body=$('HistoryRows');if(!data.length)empty(body,5,'No corrections.');
   for(const h of data){const r=document.createElement('tr');[h.changedAt,statuses[h.previousState],statuses[h.newState],h.reason,h.changedByUserId].forEach(x=>r.append(td(x)));body.append(r);}
   $('HistoryCard').classList.remove('d-none');$('HistoryCard').scrollIntoView({block:'center'});
  }catch(error){if(seq===historySeq)handleError(error);}});
 action.append(correct,history);tr.append(action);body.append(tr);}
}
$('Date').value=localDate(new Date());$('To').value=$('Date').value;
$('From').value=localDate(new Date(Date.now()-14*86400000));stateFields();
guards.record?.markClean();guards.correction?.markClean();
$('EmployeeSearch').addEventListener('input',()=>{++searchSeq;$('EmployeeFind').disabled=false;});
$('EmployeeSearchForm').addEventListener('submit',event=>{event.preventDefault();employees();});
$('State').addEventListener('change',()=>{
 const wasActive=[1,3].includes(Number(previousState)),isActive=[1,3].includes(Number($('State').value));
 if(wasActive&&!isActive&&
  ([$('In').value,$('Out').value].some(Boolean)||Number($('Ot').value||0)>0)){
  if(!confirm('This status clears recorded hours. Continue?')){$('State').value=previousState;return;}
 }
 stateFields();previousState=$('State').value;
});
$('Form').addEventListener('submit',async event=>{
 event.preventDefault();if(busy||accessRevoked||!forms.record.reportValidity())return;
 const active=[1,3].includes(Number($('State').value));
 const command={employeeReference:$('Employee').value,attendanceDate:$('Date').value,state:Number($('State').value),
  inTime:active&&$('In').value?$('In').value+':00':null,
  outTime:active&&$('Out').value?$('Out').value+':00':null,
  overtimeHours:active?Number($('Ot').value||0):null,remarks:$('Remarks').value.trim()||null};
 const request=requestIdFor(command);
 if(lastPosted===request.signature)return notice('This attendance was already recorded. Refresh the register before recording another entry.');
 busy=true;root.inert=true;
 let committed=false;
 try{
  await api('/api/hr/employee-attendance','POST',{clientRequestId:request.key,...command});
  committed=true;lastPosted=request.signature;
  forms.record.reset();$('Date').value=command.attendanceDate;stateFields();previousState=$('State').value;
  guards.record?.markClean();page=1;
  try{await load();notice('Attendance recorded.',true);}
  catch(error){if([401,403].includes(error.statusCode)){
    handleError(error);notice('Attendance recorded; register access is no longer available. Please sign in again.',true);
   }else notice('Attendance recorded; register refresh unavailable. Please reload to verify.',true);}
 }catch(error){handleError(error);
  if(!accessRevoked&&!committed)notice(error.message+' Check the register before retrying this attendance.');
 }finally{busy=false;if(!accessRevoked)root.inert=false;}
});
$('CorrectionForm').addEventListener('submit',async event=>{
 event.preventDefault();if(busy||accessRevoked||!forms.correction.reportValidity())return;
 const id=$('CorrectionId').value,command={newState:Number($('CorrectionState').value),
  reason:$('CorrectionReason').value.trim(),rowVersion:$('CorrectionVersion').value};
 if(!id||!command.rowVersion)return notice('Choose a valid attendance record.');
 busy=true;root.inert=true;
 try{
  await api('/api/hr/employee-attendance/'+encodeURIComponent(id)+'/correct','POST',command);
  $('CorrectionCard').classList.add('d-none');forms.correction.reset();guards.correction?.markClean();
  ++historySeq;selectedHistory=null;$('HistoryCard').classList.add('d-none');page=1;
  try{await load();notice('Correction saved with audit history.',true);}
  catch(error){if([401,403].includes(error.statusCode)){
    handleError(error);notice('Correction saved with audit history; register access is no longer available. Please sign in again.',true);
   }else notice('Correction saved with audit history; register refresh unavailable. Please reload to verify.',true);}
 }catch(error){handleError(error);
  if(!accessRevoked)notice(error.message+' Check correction history before retrying.');
 }finally{busy=false;if(!accessRevoked)root.inert=false;}
});
$('CorrectionClose').addEventListener('click',()=>{if(busy||!mayDiscard('correction'))return;
 forms.correction.reset();guards.correction?.markClean();$('CorrectionCard').classList.add('d-none');});
$('Filter').addEventListener('submit',event=>{event.preventDefault();if(busy||accessRevoked)return;
 page=1;load().catch(handleError);});
$('Prev').addEventListener('click',()=>{if(busy||accessRevoked)return;
 if(page>1){page--;load().catch(handleError);}});
$('Next').addEventListener('click',()=>{if(busy||accessRevoked)return;
 if(page<pages){page++;load().catch(handleError);}});
load().catch(handleError);
})();
