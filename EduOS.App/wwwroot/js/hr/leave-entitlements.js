(() => {
'use strict';
const root=document.getElementById('leaveEntitlementRoot');if(!root)return;
const $=id=>document.getElementById('leaveEntitlement'+id);
const token=document.querySelector('meta[name="request-verification-token"]')?.content;
let types=[],entitlements=[],employee='',loadedContext='',searchSeq=0,loadSeq=0,busy=false,accessRevoked=false;
const forms={type:$('TypeForm'),allowance:$('AllowanceForm'),adjustment:$('AdjustmentForm')};
const guards={};
for(const [name,form] of Object.entries(forms))
 if(form?.elements&&window.EduOSFormGuard)guards[name]=window.EduOSFormGuard.create(form);
const currentContext=()=>$('Employee').value+'|'+$('Year').value;
function confirmDiscard(){return ['allowance','adjustment'].every(name=>
 !guards[name]?.isDirty()||guards[name].confirmDiscard());}
function hideEmployee(){
 loadedContext='';employee='';entitlements=[];++loadSeq;
 for(const id of ['AllowanceRows','BalanceRows'])$(id).replaceChildren();
 $('AllowanceVersion').value='';guards.allowance?.markClean();guards.adjustment?.markClean();
 forms.allowance.inert=true;forms.adjustment.inert=true;
}
function notice(message,ok=false){const e=$('Alert');e.className='alert alert-'+(ok?'success':'danger');e.textContent=message;e.focus();}
async function api(url,method='GET',body){
 const headers={Accept:'application/json'};
 if(method!=='GET'){if(!token)throw Error('Security token missing. Reload.');headers.RequestVerificationToken=token;headers['Content-Type']='application/json';}
 const r=await fetch(url,{method,headers,credentials:'same-origin',cache:'no-store',...(body===undefined?{}:{body:JSON.stringify(body)})});
 const x=await r.json().catch(()=>null);
 if(!r.ok||!x?.success){const error=Error(x?.message||'Request failed.');error.statusCode=r.status;throw error;}
 return x.data;
}
function handleError(error){
 if([401,403].includes(error.statusCode)){
  accessRevoked=true;
  ++searchSeq;hideEmployee();$('Employee').replaceChildren(new Option('Choose employee',''));
  $('EmployeeSearchStatus').textContent='';$('TypeRows').replaceChildren();
  forms.type.inert=true;
 }
 notice(error.message);
}
function cell(value){const x=document.createElement('td');x.textContent=value==null?'—':String(value);return x;}
function table(id,rows,columns){const body=$(id);body.replaceChildren();if(!rows.length){const tr=document.createElement('tr'),c=cell('No records.');c.colSpan=columns;tr.append(c);body.append(tr);}return body;}
function select(id,rows){const e=$(id),old=e.value;e.replaceChildren(new Option('Choose leave type',''));for(const row of rows)e.add(new Option(row.code+' — '+row.name,String(row.id)));e.value=old;}
function chosen(){const reference=$('Employee').value,year=Number($('Year').value);
 if(!reference||!Number.isInteger(year)||year<2000||year>new Date().getFullYear()+1)
  throw Error('Select an employee and valid year.');
 return {employeeReference:reference,year};}
function resetType(){forms.type.reset();$('TypeId').value='';$('TypeVersion').value='';
 $('TypePaid').checked=true;$('TypeActive').checked=true;guards.type?.markClean();}
async function loadTypes(){
 types=await api('/api/hr/leave-entitlements/types');const body=table('TypeRows',types,6);
 for(const row of types){const tr=document.createElement('tr');[row.code,row.name,row.maxDaysPerYear,row.isPaid?'Yes':'No',row.isActive?'Yes':'No'].forEach(v=>tr.append(cell(v)));
 const td=cell(''),btn=document.createElement('button');btn.type='button';btn.className='btn btn-outline-secondary btn-sm';btn.textContent='Edit';
 btn.addEventListener('click',()=>{if(busy||guards.type?.isDirty()&&!guards.type.confirmDiscard())return;
  $('TypeId').value=row.id;$('TypeVersion').value=row.rowVersion;$('TypeName').value=row.name;
  $('TypeCode').value=row.code;$('TypeMax').value=row.maxDaysPerYear;
  $('TypePaid').checked=row.isPaid;$('TypeActive').checked=row.isActive;
  guards.type?.markClean();$('TypeName').focus();});
 td.append(btn);tr.append(td);body.append(tr);}
 select('AllowanceType',types.filter(x=>x.isActive));select('AdjustmentType',types.filter(x=>x.isActive));
}
async function searchEmployees(){
 const term=$('EmployeeSearch').value.trim(),seq=++searchSeq;
 if(accessRevoked)return;
 if(term.length<2||term.length>100){notice('Enter 2–100 characters to search employees.');return;}
 if(busy||!confirmDiscard())return;
 hideEmployee();$('Employee').replaceChildren(new Option('Choose employee',''));
 $('EmployeeFind').disabled=true;$('EmployeeSearchStatus').textContent='Searching…';
 try{
  const data=await api('/api/hr/employees?'+new URLSearchParams({state:1,search:term,page:1,pageSize:50}));
  if(seq!==searchSeq||$('EmployeeSearch').value.trim()!==term)return;
  for(const row of data.items||[])$('Employee').add(new Option(row.employeeCode+' — '+row.fullName,row.reference));
  $('EmployeeSearchStatus').textContent=(data.totalCount||0)+' result(s)'+
   (data.totalCount>50?' · Showing first 50; narrow your search.':'');
 }catch(error){if(seq===searchSeq){$('Employee').replaceChildren(new Option('Choose employee',''));
  $('EmployeeSearchStatus').textContent='';handleError(error);}}
 finally{if(seq===searchSeq)$('EmployeeFind').disabled=false;}
}
function editAllowance(row){
 if(busy||guards.allowance?.isDirty()&&!guards.allowance.confirmDiscard())return;
 $('AllowanceType').value=String(row.leaveTypeId);$('Allowed').value=row.entitledDays;
 $('Carried').value=row.carriedForwardDays;$('AllowanceVersion').value=row.rowVersion;
 lastAllowanceType=$('AllowanceType').value;guards.allowance?.markClean();$('Allowed').focus();
}
function syncAllowance(){const row=entitlements.find(x=>String(x.leaveTypeId)===$('AllowanceType').value);
 $('AllowanceVersion').value=row?.rowVersion||'';$('Allowed').value=row?.entitledDays??0;
 $('Carried').value=row?.carriedForwardDays??0;lastAllowanceType=$('AllowanceType').value;
 guards.allowance?.markClean();}
async function loadEmployee(refresh=false){
 if(accessRevoked)return;
 if(!refresh&&(busy||!confirmDiscard()))return;
 const {employeeReference,year}=chosen(),context=currentContext();
 hideEmployee();const current=loadSeq;
 const q='?year='+encodeURIComponent(year);
 const [e,b]=await Promise.all([api('/api/hr/leave-entitlements/employees/'+encodeURIComponent(employeeReference)+'/entitlements'+q),
 api('/api/hr/leave-entitlements/employees/'+encodeURIComponent(employeeReference)+'/balances'+q)]);
 if(current!==loadSeq||currentContext()!==context)return;
 entitlements=e;employee=employeeReference;loadedContext=context;
 const body=table('AllowanceRows',e,4);
 for(const row of e){const tr=document.createElement('tr');[types.find(t=>t.id===row.leaveTypeId)?.name||row.leaveTypeId,row.entitledDays,row.carriedForwardDays].forEach(v=>tr.append(cell(v)));
 const td=cell(''),btn=document.createElement('button');btn.type='button';btn.className='btn btn-outline-secondary btn-sm';btn.textContent='Edit';btn.addEventListener('click',()=>editAllowance(row));td.append(btn);tr.append(td);body.append(tr);}
 const balances=table('BalanceRows',b,4);for(const row of b){const tr=document.createElement('tr');[row.leaveTypeName,row.allowedDays,row.usedDays,row.remainingDays].forEach(v=>tr.append(cell(v)));balances.append(tr);}
 syncAllowance();forms.allowance.inert=false;forms.adjustment.inert=false;
 guards.adjustment?.markClean();
}
async function submit(form,name,write,refresh,success){
 if(accessRevoked||busy||form.reportValidity?.()===false)return;
 busy=true;form.inert=true;const btn=form.querySelector('[type=submit]');btn.disabled=true;
 try{
  await write();guards[name]?.markClean();
  try{await refresh();notice(success,true);}
  catch(readError){if([401,403].includes(readError.statusCode))handleError(readError);
   notice(success+' Refresh unavailable: '+readError.message,true);}
 }catch(error){handleError(error);}
 finally{busy=false;form.inert=accessRevoked||(name!=='type'&&loadedContext!==currentContext());
  btn.disabled=accessRevoked;}
}
$('Year').value=new Date().getFullYear();hideEmployee();
$('TypeReset').addEventListener('click',()=>{
 if(busy||guards.type?.isDirty()&&!guards.type.confirmDiscard())return;resetType();
});
$('TypeForm').addEventListener('submit',event=>{event.preventDefault();submit(event.currentTarget,'type',async()=>{
 const id=$('TypeId').value,body={name:$('TypeName').value.trim(),code:$('TypeCode').value.trim(),maxDaysPerYear:Number($('TypeMax').value),isPaid:$('TypePaid').checked,isActive:$('TypeActive').checked,rowVersion:$('TypeVersion').value||null};
 await api('/api/hr/leave-entitlements/types'+(id?'/'+id:''),id?'PUT':'POST',body);
 resetType();},loadTypes,'Leave type saved.');});
$('EmployeeSearchForm').addEventListener('submit',event=>{event.preventDefault();searchEmployees();});
$('EmployeeSearch').addEventListener('input',()=>{++searchSeq;});
$('EmployeeForm').addEventListener('submit',event=>{event.preventDefault();loadEmployee().catch(handleError);});
let lastAllowanceType='';
for(const id of ['Employee','Year'])$(id).addEventListener('change',()=>{
 if(busy||!confirmDiscard()){
  if(loadedContext){const [reference,year]=loadedContext.split('|');
   $('Employee').value=reference;$('Year').value=year;}
  return;
 }
 hideEmployee();
});
$('AllowanceType').addEventListener('change',()=>{
 if(busy||guards.allowance?.isDirty()&&!guards.allowance.confirmDiscard()){
  $('AllowanceType').value=lastAllowanceType;return;
 }
 lastAllowanceType=$('AllowanceType').value;syncAllowance();
});
function ensureLoaded(){const context=chosen();
 if(loadedContext!==currentContext()||!employee)throw Error('Load the selected employee and year before saving.');
 return context;
}
$('AllowanceForm').addEventListener('submit',event=>{event.preventDefault();submit(event.currentTarget,'allowance',async()=>{
 const context=ensureLoaded();if(!$('AllowanceType').value)throw Error('Choose a leave type.');
 await api('/api/hr/leave-entitlements/entitlements','PUT',{...context,leaveTypeId:Number($('AllowanceType').value),
 entitledDays:Number($('Allowed').value),carriedForwardDays:Number($('Carried').value),rowVersion:$('AllowanceVersion').value||null});
 },()=>loadEmployee(true),'Annual entitlement saved.');});
$('AdjustmentForm').addEventListener('submit',event=>{event.preventDefault();submit(event.currentTarget,'adjustment',async()=>{
 const context=ensureLoaded();if(!$('AdjustmentType').value)throw Error('Choose a leave type.');
 await api('/api/hr/leave-entitlements/adjustments','POST',{...context,leaveTypeId:Number($('AdjustmentType').value),
 days:Number($('AdjustmentDays').value),reason:$('AdjustmentReason').value.trim()});
 $('AdjustmentDays').value='';$('AdjustmentReason').value='';
 },()=>loadEmployee(true),'Audited leave adjustment saved.');});
loadTypes().catch(handleError);
})();
