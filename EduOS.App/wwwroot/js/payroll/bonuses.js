(() => {
'use strict';
const root=document.getElementById('bonusWorkspace');if(!root)return;
const el=id=>document.getElementById(id);
const token=root.querySelector('input[name="__RequestVerificationToken"]')?.value;
const state={page:1,pages:1,requestId:null,busy:false};
const option=(value,label)=>{const o=document.createElement('option');o.value=String(value);o.textContent=label;return o;};
const cell=value=>{const td=document.createElement('td');td.textContent=value==null?'':String(value);return td;};
function notice(message,error=false){const x=el('bonusNotice');x.className='alert '+(error?'alert-danger':'alert-success');x.textContent=message;}
async function api(url,method='GET',body){
 const headers={Accept:'application/json'};
 if(method!=='GET'){if(!token)throw Error('Antiforgery token missing; reload.');headers.RequestVerificationToken=token;}
 if(body!==undefined)headers['Content-Type']='application/json';
 const response=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,
   ...(body===undefined?{}:{body:JSON.stringify(body)})});
 const data=await response.json().catch(()=>null);
 if(!response.ok||!data?.success)throw Error(data?.message||'Bonus request failed.');
 return data.data;
}
async function searchEmployees(){
 const term=el('bonusEmployeeSearch').value.trim();
 if(term.length<2){notice('Enter at least two characters to search.',true);return;}
 try{
  const data=await api('/api/hr-payroll/employee-options?'+new URLSearchParams({search:term,page:'1',pageSize:'50'}));
  const select=el('bonusEmployee');select.replaceChildren(option('','Select employee'));
  for(const employee of data.items||[])select.append(option(employee.reference,employee.fullName+' ('+employee.employeeCode+')'));
  if(data.totalCount>50)notice('Showing 50 employees. Narrow the search.');
 }catch(e){notice(e.message,true);}
}
function render(data){
 const body=el('bonusRows');body.replaceChildren();
 for(const row of data.items||[]){
  const tr=document.createElement('tr');
  for(const value of [row.id,row.bonusDate,row.employeeName,Number(row.amount).toFixed(2),row.reason])tr.append(cell(value));
  body.append(tr);
 }
 if(!data.items?.length){const tr=document.createElement('tr'),td=cell('No bonuses found.');td.colSpan=5;tr.append(td);body.append(tr);}
 state.pages=Math.max(1,data.totalPages||1);
 el('bonusPage').textContent='Page '+state.page+' / '+state.pages+' · '+data.totalCount+' bonus(es)';
 el('bonusPrev').disabled=state.page<=1;el('bonusNext').disabled=state.page>=state.pages;
}
async function load(){
 const q=new URLSearchParams({page:String(state.page),pageSize:'20'});
 const year=el('bonusYear').value,month=el('bonusMonth').value;
 if(year)q.set('year',year);if(month)q.set('month',month);
 if(el('bonusSelectedOnly').checked){
  const employee=el('bonusEmployee').value;
  if(!employee){notice('Select an employee before applying the employee filter.',true);return;}
  q.set('employeeReference',employee);
 }
 try{render(await api('/api/hr-payroll/bonuses?'+q));}catch(e){notice(e.message,true);}
}
el('bonusFind').addEventListener('click',searchEmployees);
el('bonusFilter').addEventListener('submit',e=>{e.preventDefault();state.page=1;load();});
el('bonusPrev').addEventListener('click',()=>{if(state.page>1){state.page--;load();}});
el('bonusNext').addEventListener('click',()=>{if(state.page<state.pages){state.page++;load();}});
el('bonusCreate').addEventListener('submit',async e=>{
 e.preventDefault();if(state.busy||!e.currentTarget.reportValidity())return;
 const employee=el('bonusEmployee').value,date=el('bonusDate').value,amount=Number(el('bonusAmount').value);
 if(!employee||!/^\d{4}-\d{2}-\d{2}$/.test(date)||!Number.isFinite(amount)||
    amount<=0||amount>999999999999||Math.abs(amount*100-Math.round(amount*100))>0.000001){
  notice('Select an employee, valid date and positive amount with at most two decimals.',true);return;
 }
 if(!confirm('Record this bonus for a future payroll calculation? Existing payroll runs cannot be changed.'))return;
 state.requestId??=crypto.randomUUID();state.busy=true;el('bonusSave').disabled=true;
 try{
  await api('/api/hr-payroll/bonuses','POST',{clientRequestId:state.requestId,employeeReference:employee,
    bonusDate:date,amount,reason:el('bonusReason').value.trim()||null});
  state.requestId=null;el('bonusAmount').value='';el('bonusReason').value='';
  notice('Bonus recorded. Generate payroll after all bonuses are entered.');state.page=1;await load();
 }catch(error){notice(error.message+' Check the register before retrying; a timed-out request may have succeeded.',true);}
 finally{state.busy=false;el('bonusSave').disabled=false;}
});
for(const id of ['bonusEmployee','bonusDate','bonusAmount','bonusReason'])el(id).addEventListener('change',()=>{state.requestId=null;});
const today=new Date();el('bonusYear').value=String(today.getFullYear());el('bonusDate').value=today.toLocaleDateString('en-CA');
load();
})();
