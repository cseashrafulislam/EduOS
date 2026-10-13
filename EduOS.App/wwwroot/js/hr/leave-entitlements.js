(() => {
'use strict';
const root=document.getElementById('leaveEntitlementRoot');if(!root)return;
const $=id=>document.getElementById('leaveEntitlement'+id);
const token=document.querySelector('meta[name="request-verification-token"]')?.content;
let types=[],entitlements=[],employee='';
function notice(message,ok=false){const e=$('Alert');e.className='alert alert-'+(ok?'success':'danger');e.textContent=message;e.focus();}
async function api(url,method='GET',body){
 const headers={Accept:'application/json'};
 if(method!=='GET'){if(!token)throw Error('Security token missing. Reload.');headers.RequestVerificationToken=token;headers['Content-Type']='application/json';}
 const r=await fetch(url,{method,headers,credentials:'same-origin',cache:'no-store',...(body===undefined?{}:{body:JSON.stringify(body)})});
 const x=await r.json().catch(()=>null);if(!r.ok||!x?.success)throw Error(x?.message||'Request failed.');return x.data;
}
function cell(value){const x=document.createElement('td');x.textContent=value==null?'—':String(value);return x;}
function table(id,rows,columns){const body=$(id);body.replaceChildren();if(!rows.length){const tr=document.createElement('tr'),c=cell('No records.');c.colSpan=columns;tr.append(c);body.append(tr);}return body;}
function select(id,rows){const e=$(id),old=e.value;e.replaceChildren(new Option('Choose leave type',''));for(const row of rows)e.add(new Option(row.code+' — '+row.name,String(row.id)));e.value=old;}
function chosen(){employee=$('Employee').value;const year=Number($('Year').value);if(!employee||!Number.isInteger(year)||year<2000||year>new Date().getUTCFullYear()+1)throw Error('Select an employee and valid year.');return {employeeReference:employee,year};}
function resetType(){$('TypeId').value='';$('TypeVersion').value='';$('TypeForm').reset();$('TypePaid').checked=true;$('TypeActive').checked=true;}
async function loadTypes(){
 types=await api('/api/hr/leave-entitlements/types');const body=table('TypeRows',types,6);
 for(const row of types){const tr=document.createElement('tr');[row.code,row.name,row.maxDaysPerYear,row.isPaid?'Yes':'No',row.isActive?'Yes':'No'].forEach(v=>tr.append(cell(v)));
 const td=cell(''),btn=document.createElement('button');btn.type='button';btn.className='btn btn-outline-secondary btn-sm';btn.textContent='Edit';
 btn.addEventListener('click',()=>{$('TypeId').value=row.id;$('TypeVersion').value=row.rowVersion;$('TypeName').value=row.name;$('TypeCode').value=row.code;$('TypeMax').value=row.maxDaysPerYear;$('TypePaid').checked=row.isPaid;$('TypeActive').checked=row.isActive;$('TypeName').focus();});
 td.append(btn);tr.append(td);body.append(tr);}
 select('AllowanceType',types.filter(x=>x.isActive));select('AdjustmentType',types.filter(x=>x.isActive));
}
async function loadEmployees(){
 const rows=[];let page=1,pages=1;
 do{const d=await api('/api/hr/employees?state=1&page='+page+'&pageSize=100');rows.push(...(d.items||[]));pages=Math.max(1,d.totalPages||1);page++;if(pages>30)throw Error('Employee list exceeds dropdown limit.');}while(page<=pages);
 const e=$('Employee');e.replaceChildren(new Option('Choose employee',''));for(const row of rows)e.add(new Option(row.employeeCode+' — '+row.fullName,row.reference));
}
function editAllowance(row){$('AllowanceType').value=String(row.leaveTypeId);$('Allowed').value=row.entitledDays;$('Carried').value=row.carriedForwardDays;$('AllowanceVersion').value=row.rowVersion;$('Allowed').focus();}
function syncAllowance(){const row=entitlements.find(x=>String(x.leaveTypeId)===$('AllowanceType').value);$('AllowanceVersion').value=row?.rowVersion||'';$('Allowed').value=row?.entitledDays??0;$('Carried').value=row?.carriedForwardDays??0;}
async function loadEmployee(){
 const {employeeReference,year}=chosen();const q='?year='+encodeURIComponent(year);
 const [e,b]=await Promise.all([api('/api/hr/leave-entitlements/employees/'+encodeURIComponent(employeeReference)+'/entitlements'+q),
 api('/api/hr/leave-entitlements/employees/'+encodeURIComponent(employeeReference)+'/balances'+q)]);
 entitlements=e;const body=table('AllowanceRows',e,4);
 for(const row of e){const tr=document.createElement('tr');[types.find(t=>t.id===row.leaveTypeId)?.name||row.leaveTypeId,row.entitledDays,row.carriedForwardDays].forEach(v=>tr.append(cell(v)));
 const td=cell(''),btn=document.createElement('button');btn.type='button';btn.className='btn btn-outline-secondary btn-sm';btn.textContent='Edit';btn.addEventListener('click',()=>editAllowance(row));td.append(btn);tr.append(td);body.append(tr);}
 const balances=table('BalanceRows',b,4);for(const row of b){const tr=document.createElement('tr');[row.leaveTypeName,row.allowedDays,row.usedDays,row.remainingDays].forEach(v=>tr.append(cell(v)));balances.append(tr);}
 syncAllowance();
}
async function submit(form,fn){const btn=form.querySelector('[type=submit]');btn.disabled=true;try{await fn();notice('Saved successfully.',true);}catch(e){notice(e.message);}finally{btn.disabled=false;}}
$('Year').value=new Date().getUTCFullYear();
$('TypeReset').addEventListener('click',resetType);
$('TypeForm').addEventListener('submit',event=>{event.preventDefault();submit(event.currentTarget,async()=>{
 const id=$('TypeId').value,body={name:$('TypeName').value.trim(),code:$('TypeCode').value.trim(),maxDaysPerYear:Number($('TypeMax').value),isPaid:$('TypePaid').checked,isActive:$('TypeActive').checked,rowVersion:$('TypeVersion').value||null};
 await api('/api/hr/leave-entitlements/types'+(id?'/'+id:''),id?'PUT':'POST',body);resetType();await loadTypes();});});
$('EmployeeForm').addEventListener('submit',event=>{event.preventDefault();loadEmployee().catch(e=>notice(e.message));});
$('AllowanceType').addEventListener('change',syncAllowance);
$('AllowanceForm').addEventListener('submit',event=>{event.preventDefault();submit(event.currentTarget,async()=>{
 const context=chosen();if(!$('AllowanceType').value)throw Error('Choose a leave type.');
 await api('/api/hr/leave-entitlements/entitlements','PUT',{...context,leaveTypeId:Number($('AllowanceType').value),
 entitledDays:Number($('Allowed').value),carriedForwardDays:Number($('Carried').value),rowVersion:$('AllowanceVersion').value||null});
 await loadEmployee();});});
$('AdjustmentForm').addEventListener('submit',event=>{event.preventDefault();submit(event.currentTarget,async()=>{
 const context=chosen();if(!$('AdjustmentType').value)throw Error('Choose a leave type.');
 await api('/api/hr/leave-entitlements/adjustments','POST',{...context,leaveTypeId:Number($('AdjustmentType').value),
 days:Number($('AdjustmentDays').value),reason:$('AdjustmentReason').value.trim()});
 $('AdjustmentDays').value='';$('AdjustmentReason').value='';await loadEmployee();});});
Promise.all([loadTypes(),loadEmployees()]).catch(e=>notice(e.message));
})();
