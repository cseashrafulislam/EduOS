(() => {
'use strict';
const root=document.getElementById('employeeAttendanceRoot');if(!root)return;
const $=id=>document.getElementById('employeeAttendance'+id);
const token=document.querySelector('meta[name="request-verification-token"]')?.content;
const statuses={1:'Present',2:'Absent',3:'Late',4:'Leave',5:'Excused'};
let page=1,pages=1;
function notice(message,ok=false){const e=$('Alert');e.className='alert alert-'+(ok?'success':'danger');e.textContent=message;e.focus();}
async function api(url,method='GET',body){
 const headers={Accept:'application/json'};
 if(method!=='GET'){if(!token)throw Error('Security token missing. Reload.');headers.RequestVerificationToken=token;headers['Content-Type']='application/json';}
 const response=await fetch(url,{method,headers,credentials:'same-origin',cache:'no-store',...(body===undefined?{}:{body:JSON.stringify(body)})});
 const result=await response.json().catch(()=>null);if(!response.ok||!result?.success)throw Error(result?.message||'Request failed.');return result.data;
}
function td(value){const c=document.createElement('td');c.textContent=value==null?'—':String(value);return c;}
function empty(target,count,message){target.replaceChildren();const row=document.createElement('tr'),c=td(message);c.colSpan=count;row.append(c);target.append(row);}
function localDate(d){const x=new Date(d.getTime()-d.getTimezoneOffset()*60000);return x.toISOString().slice(0,10);}
async function employees(){
 const rows=[];let p=1,total=1;
 do{const data=await api('/api/hr/employees?state=1&page='+p+'&pageSize=100');rows.push(...(data.items||[]));total=Math.max(1,data.totalPages||1);p++;if(total>30)throw Error('Too many employees for dropdown; use HR employee directory.');}while(p<=total);
 for(const id of ['Employee','FilterEmployee']){const sel=$(id),old=sel.value;sel.replaceChildren(new Option(id==='Employee'?'Choose employee':'All employees',''));
  for(const e of rows)sel.add(new Option(e.employeeCode+' — '+e.fullName,e.reference));sel.value=old;}
}
function stateFields(){const active=[1,3].includes(Number($('State').value));for(const id of ['In','Out','Ot']){$(id).disabled=!active;if(!active)$(id).value=id==='Ot'?'0':'';}}
async function load(){
 const q=new URLSearchParams({fromDate:$('From').value,toDate:$('To').value,page:String(page),pageSize:'25'});
 if($('FilterEmployee').value)q.set('employeeReference',$('FilterEmployee').value);
 const data=await api('/api/hr/employee-attendance?'+q);
 pages=Math.max(1,data.totalPages||1);$('Page').textContent='Page '+page+' of '+pages+' · '+data.totalCount+' records';$('Prev').disabled=page<=1;$('Next').disabled=page>=pages;
 const body=$('Rows');body.replaceChildren();if(!data.items?.length){empty(body,8,'No attendance records.');return;}
 for(const row of data.items){const tr=document.createElement('tr');
 [row.attendanceDate,row.employeeCode+' — '+row.employeeName,statuses[row.state]||row.state,row.inTime?.slice(0,5),row.outTime?.slice(0,5),row.overtimeHours,row.remarks].forEach(x=>tr.append(td(x)));
 const action=td('');const correct=document.createElement('button');correct.type='button';correct.className='btn btn-sm btn-outline-warning me-1';correct.textContent='Correct';
 correct.addEventListener('click',()=>{$('CorrectionId').value=row.id;$('CorrectionVersion').value=row.rowVersion;$('CorrectionState').value=row.state;$('CorrectionReason').value='';$('CorrectionCard').classList.remove('d-none');$('CorrectionCard').scrollIntoView({block:'center'});});
 const history=document.createElement('button');history.type='button';history.className='btn btn-sm btn-outline-secondary';history.textContent='History';
 history.addEventListener('click',async()=>{try{const data=await api('/api/hr/employee-attendance/'+row.id+'/corrections');const body=$('HistoryRows');body.replaceChildren();if(!data.length)empty(body,5,'No corrections.');
 for(const h of data){const r=document.createElement('tr');[h.changedAt,statuses[h.previousState],statuses[h.newState],h.reason,h.changedByUserId].forEach(x=>r.append(td(x)));body.append(r);}
 $('HistoryCard').classList.remove('d-none');$('HistoryCard').scrollIntoView({block:'center'});}catch(e){notice(e.message);}});
 action.append(correct,history);tr.append(action);body.append(tr);}
}
$('Date').value=localDate(new Date());$('To').value=$('Date').value;$('From').value=localDate(new Date(Date.now()-14*86400000));$('State').addEventListener('change',stateFields);stateFields();
$('Form').addEventListener('submit',async event=>{event.preventDefault();const btn=$('Save');btn.disabled=true;
 try{const active=[1,3].includes(Number($('State').value));await api('/api/hr/employee-attendance','POST',{
 clientRequestId:crypto.randomUUID(),employeeReference:$('Employee').value,attendanceDate:$('Date').value,state:Number($('State').value),
 inTime:active&&$('In').value?$('In').value+':00':null,outTime:active&&$('Out').value?$('Out').value+':00':null,
 overtimeHours:active?Number($('Ot').value||0):null,remarks:$('Remarks').value.trim()||null});
 notice('Attendance recorded.',true);page=1;await load();}catch(e){notice(e.message);}finally{btn.disabled=false;}});
$('CorrectionForm').addEventListener('submit',async event=>{event.preventDefault();const btn=$('CorrectionForm').querySelector('[type=submit]');btn.disabled=true;
 try{await api('/api/hr/employee-attendance/'+$('CorrectionId').value+'/correct','POST',{newState:Number($('CorrectionState').value),reason:$('CorrectionReason').value.trim(),rowVersion:$('CorrectionVersion').value});
 $('CorrectionCard').classList.add('d-none');notice('Correction saved with audit history.',true);await load();}catch(e){notice(e.message);}finally{btn.disabled=false;}});
$('Filter').addEventListener('submit',event=>{event.preventDefault();page=1;load().catch(e=>notice(e.message));});
$('Prev').addEventListener('click',()=>{if(page>1){page--;load().catch(e=>notice(e.message));}});
$('Next').addEventListener('click',()=>{if(page<pages){page++;load().catch(e=>notice(e.message));}});
employees().then(load).catch(e=>notice(e.message));
})();
