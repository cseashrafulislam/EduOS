(() => {
 'use strict';const root=document.getElementById('payrollApp');if(!root)return;
 const el=id=>document.getElementById(id),manager=root.dataset.manager==='true';
 const state={request:null,signature:null,rows:[],busy:false};
 const opt=(value,label)=>{const x=document.createElement('option');x.value=String(value);x.textContent=label;return x;};
 const td=value=>{const x=document.createElement('td');x.textContent=value==null?'':String(value);return x;};
 function msg(type,text){const x=el('payrollAlert');x.className='alert alert-'+type;x.textContent=text;x.focus();}
 async function api(url,method='GET',body){
   const r=await fetch(url,{method,cache:'no-store',credentials:'same-origin',headers:{Accept:'application/json',...(body===undefined?{}:{'Content-Type':'application/json'})},...(body===undefined?{}:{body:JSON.stringify(body)})});
   const j=await r.json().catch(()=>null);if(!r.ok||!j?.success)throw new Error(j?.message||'Request failed.');return j.data;
 }
 function render(rows){
   state.rows=rows||[];const body=el('payrollRows');body.replaceChildren();
   for(const row of state.rows){
     const tr=document.createElement('tr');tr.append(td(row.employeeName+' ('+row.employeeCode+')'),td(row.month+'/'+row.year),td(row.grossSalary),
       td((Number(row.attendanceDeduction)||0)+(Number(row.loanDeduction)||0)),td(row.netSalary),td(row.status));
     if(manager){const x=document.createElement('td');if(row.status!=='Paid'&&Number(row.netSalary)>0){
       const b=document.createElement('button');b.type='button';b.textContent='Record payment';b.className='btn btn-sm btn-outline-primary';b.onclick=()=>pay(row);x.append(b);}
       tr.append(x);}
     body.append(tr);
   }
   if(!state.rows.length){const tr=document.createElement('tr'),x=td('No records.');x.colSpan=manager?7:6;tr.append(x);body.append(tr);}
 }
 async function searchEmployees(){
   try{const items=await api('/api/hr-payroll/employee-options?search='+encodeURIComponent(el('payrollSearchInput').value.trim()));
     const box=el('payrollEmployee');box.replaceChildren(opt('','Select employee'));for(const item of items||[])box.append(opt(item.reference,item.name+' ('+item.employeeCode+')'));
     if(items?.length===100)msg('warning','Showing first 100 results. Narrow the search.');}
   catch(e){msg('danger',e.message);}
 }
 async function saveSalary(e){
   e.preventDefault();if(!e.currentTarget.reportValidity())return;
   const request={employeeReference:el('payrollEmployee').value,basicSalary:Number(el('payrollBasic').value),houseRent:Number(el('payrollHouse').value),
     medical:Number(el('payrollMedical').value),transport:Number(el('payrollTransport').value),others:Number(el('payrollOthers').value),
     effectiveFrom:el('payrollEffectiveFrom').value+'T00:00:00'};
   if(!confirm('Save a new salary structure effective '+el('payrollEffectiveFrom').value+'? The previous current structure will be closed.'))return;
   el('payrollSaveSalary').disabled=true;
   try{await api('/api/hr-payroll/salary-structure','PUT',request);msg('success','Salary structure saved.');}
   catch(ex){msg('danger',ex.message);}finally{el('payrollSaveSalary').disabled=false;}
 }
 async function loadPeriod(){
   const year=Number(el('payrollYear').value),month=Number(el('payrollMonth').value),page=Number(el('payrollPage').value);
   try{const result=await api('/api/hr-payroll/period?year='+year+'&month='+month+'&page='+page+'&pageSize=25');
     render(result.rows);el('payrollCount').textContent=result.totalCount+' payroll row(s), page '+result.page;}
   catch(e){msg('danger',e.message);}
 }
 async function generate(){
   const year=Number(el('payrollYear').value),month=Number(el('payrollMonth').value);
   if(year<2000||year>2200||month<1||month>12)return msg('danger','Select a valid payroll period.');
   if(!confirm('Generate payroll for '+month+'/'+year+'? Verify salary structures and attendance first.'))return;
   const key=year+'-'+month;if(state.signature!==key){state.signature=key;state.request=crypto.randomUUID();}
   el('payrollGenerate').disabled=true;
   try{const result=await api('/api/hr-payroll/generate','POST',{clientRequestId:state.request,year,month});
     render(result.rows);el('payrollSummary').textContent='Generated: '+result.generated+', existing: '+result.existing;
     state.request=null;state.signature=null;msg('success','Payroll generated.');}
   catch(e){msg('danger',e.message+' Request ID retained for safe retry.');}
   finally{el('payrollGenerate').disabled=false;}
 }
 async function pay(row){
   const method=prompt('Payment method (Bank, Cash, Bkash, Nagad)','Bank');if(method===null)return;
   if(!['Bank','Cash','Bkash','Nagad'].includes(method))return msg('warning','Unsupported payment method.');
   if(!confirm('Confirm '+row.netSalary+' payment to '+row.employeeName+'? This is a financial transaction.'))return;
   try{const result=await api('/api/hr-payroll/pay','POST',{payrollReference:row.reference,paymentMethod:method,rowVersion:row.rowVersion,note:null});
     const n=state.rows.findIndex(x=>x.reference===result.reference);if(n>=0)state.rows[n]=result;render(state.rows);msg('success','Payroll payment recorded.');}
   catch(e){msg('danger',e.message);}
 }
 async function my(){
   try{const rows=await api('/api/hr-payroll/my');render(rows);el('payrollCount').textContent=(rows||[]).length+' payslip(s)';}
   catch(e){msg('danger',e.message);}
 }
 const now=new Date();
 if(manager){
   el('payrollMonth').value=String(now.getMonth()+1);el('payrollYear').value=String(now.getFullYear());
   el('payrollSearch').addEventListener('click',searchEmployees);el('payrollSalaryForm').addEventListener('submit',saveSalary);
   el('payrollLoad').addEventListener('click',loadPeriod);el('payrollGenerate').addEventListener('click',generate);
   for(const id of ['payrollYear','payrollMonth'])el(id).addEventListener('change',()=>{state.request=null;state.signature=null;});
   searchEmployees();
 }else my();
 el('payrollMy').addEventListener('click',my);
})();
