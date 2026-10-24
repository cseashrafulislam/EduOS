(() => {
'use strict';
if(!document.getElementById('adjustRoot'))return;
const el=id=>document.getElementById('adjust'+id),base='/api/finance/adjustments';
const token=document.querySelector('meta[name="request-verification-token"]')?.content;
let discounts=[],fines=[];
function notice(message,ok=false){const a=el('Alert');a.className='alert alert-'+(ok?'success':'danger');a.textContent=message;a.focus();}
async function api(url,method='GET',body){
 const headers={Accept:'application/json'};
 if(method!=='GET'){if(!token)throw Error('Security token missing; reload the page.');headers.RequestVerificationToken=token;headers['Content-Type']='application/json';}
 const response=await fetch(url,{method,headers,credentials:'same-origin',cache:'no-store',...(body===undefined?{}:{body:JSON.stringify(body)})});
 const json=await response.json().catch(()=>null);
 if(!response.ok||!json?.success)throw Error(json?.message||'Request failed.');
 return json.data;
}
function cell(value){const td=document.createElement('td');td.textContent=value==null?'—':String(value);return td;}
function table(id,rows,colspan){const t=el(id);t.replaceChildren();if(!rows.length){const tr=document.createElement('tr'),td=cell('No records.');td.colSpan=colspan;tr.append(td);t.append(tr);}return t;}
function button(label,fn){const b=document.createElement('button');b.type='button';b.className='btn btn-sm btn-outline-primary';b.textContent=label;b.addEventListener('click',fn);return b;}
function fill(id,rows,placeholder,format){const s=el(id),previous=s.value;s.replaceChildren(new Option(placeholder,''));for(const r of rows)s.add(new Option(format(r),String(r.id)));s.value=previous;}
function number(id){const v=Number(el(id).value);if(!Number.isFinite(v))throw Error('Enter a valid number.');return v;}
function date(id){return el(id).value||null;}
function enrollment(){const value=el('Enrollment').value;if(!value)throw Error('Select a student enrollment.');return value;}
function newDiscount(){el('DiscountRuleForm').reset();el('DrId').value='';el('DrVersion').value='';el('DrActive').checked=true;}
function newFine(){el('FineRuleForm').reset();el('FrId').value='';el('FrVersion').value='';el('FrGrace').value=0;el('FrActive').checked=true;}
function renderDiscounts(){
 const body=table('DiscountRuleRows',discounts,4);
 for(const r of discounts){const tr=document.createElement('tr');[r.name,r.value+(r.isPercentage?'%':' BDT'),r.isActive?'Yes':'No'].forEach(v=>tr.append(cell(v)));
 const td=cell('');td.append(button('Edit',()=>{el('DrId').value=r.id;el('DrVersion').value=r.rowVersion;el('DrName').value=r.name;el('DrValue').value=r.value;el('DrPercentage').checked=r.isPercentage;el('DrHead').value=r.feeHeadId||'';el('DrFrom').value=r.effectiveFrom||'';el('DrTo').value=r.effectiveTo||'';el('DrActive').checked=r.isActive;el('DrName').focus();}));tr.append(td);body.append(tr);}
 fill('AssignRule',discounts.filter(x=>x.isActive),'Select discount',x=>x.name+' — '+x.value+(x.isPercentage?'%':' BDT'));
}
function renderFines(){
 const body=table('FineRuleRows',fines,4);
 for(const r of fines){const tr=document.createElement('tr');[r.name,r.amount,r.isActive?'Yes':'No'].forEach(v=>tr.append(cell(v)));
 const td=cell('');td.append(button('Edit',()=>{el('FrId').value=r.id;el('FrVersion').value=r.rowVersion;el('FrName').value=r.name;el('FrAmount').value=r.amount;el('FrGrace').value=r.graceDays;el('FrHead').value=r.feeHeadId||'';el('FrActive').checked=r.isActive;el('FrName').focus();}));tr.append(td);body.append(tr);}
 fill('ApplyRule',fines.filter(x=>x.isActive&&!x.isDaily),'Select fixed fine',x=>x.name+' — '+x.amount+' BDT');
}
async function refreshRules(){
 const [d,f]=await Promise.all([api(base+'/discount-rules'),api(base+'/fine-rules')]);
 discounts=d;fines=f;renderDiscounts();renderFines();
}
async function refreshRegister(){
 const ref=enrollment(),result=await api(base+'/enrollments/'+encodeURIComponent(ref));
 const d=table('StudentDiscountRows',result.discounts,4);
 for(const row of result.discounts){const tr=document.createElement('tr');[row.discountRuleName,row.effectiveFrom,row.effectiveTo||'Open',row.reason||'—'].forEach(v=>tr.append(cell(v)));d.append(tr);}
 const f=table('StudentFineRows',result.fines,5);
 for(const row of result.fines){
 const tr=document.createElement('tr');[row.fineRuleName,row.fineDate,row.amount,row.isWaived?'Waived':'Recorded (unposted)'].forEach(v=>tr.append(cell(v)));
 const td=cell('');if(!row.isWaived)td.append(button('Waive',async()=>{const reason=window.prompt('Reason for waiver (required, audited):');if(reason===null)return;if(!reason.trim())return notice('Waiver reason is required.');await run(null,async()=>{await api(base+'/fines/'+row.id+'/waive','POST',{reason:reason.trim(),rowVersion:row.rowVersion});await refreshRegister();});}));tr.append(td);f.append(tr);
 }
}
async function run(form,action){
 const submit=form?.querySelector('[type="submit"]');if(submit)submit.disabled=true;
 try{await action();notice('Operation saved successfully.',true);}catch(error){notice(error.message);}finally{if(submit)submit.disabled=false;}
}
el('DrNew').addEventListener('click',newDiscount);
el('FrNew').addEventListener('click',newFine);
el('DiscountRuleForm').addEventListener('submit',e=>{e.preventDefault();run(e.currentTarget,async()=>{
 const id=el('DrId').value,body={name:el('DrName').value.trim(),feeHeadId:el('DrHead').value?Number(el('DrHead').value):null,
 isPercentage:el('DrPercentage').checked,value:number('DrValue'),effectiveFrom:date('DrFrom'),effectiveTo:date('DrTo'),
 isActive:el('DrActive').checked,rowVersion:id?el('DrVersion').value:null};
 await api(base+'/discount-rules'+(id?'/'+id:''),id?'PUT':'POST',body);newDiscount();await refreshRules();
});});
el('FineRuleForm').addEventListener('submit',e=>{e.preventDefault();run(e.currentTarget,async()=>{
 const id=el('FrId').value,body={name:el('FrName').value.trim(),feeHeadId:el('FrHead').value?Number(el('FrHead').value):null,
 amount:number('FrAmount'),isDaily:false,graceDays:number('FrGrace'),isActive:el('FrActive').checked,rowVersion:id?el('FrVersion').value:null};
 await api(base+'/fine-rules'+(id?'/'+id:''),id?'PUT':'POST',body);newFine();await refreshRules();
});});
el('SearchButton').addEventListener('click',()=>run(null,async()=>{
 const term=el('StudentSearch').value.trim();if(term.length<2||term.length>100)throw Error('Enter 2–100 characters.');
 const rows=await api(base+'/enrollments/search?search='+encodeURIComponent(term));
 const select=el('Enrollment');select.replaceChildren(new Option('Select enrollment',''));
 for(const row of rows)select.add(new Option(row.studentCode+' — '+row.name+' / Campus '+row.campusId+' / Roll '+row.rollNo,row.reference));
 if(!rows.length)notice('No matching enrollments.');
}));
el('Enrollment').addEventListener('change',()=>{if(el('Enrollment').value)run(null,refreshRegister);});
el('Refresh').addEventListener('click',()=>run(null,refreshRegister));
el('AssignForm').addEventListener('submit',e=>{e.preventDefault();run(e.currentTarget,async()=>{
 const body={clientRequestId:crypto.randomUUID(),studentEnrollmentReference:enrollment(),discountRuleId:number('AssignRule'),
 effectiveFrom:date('AssignFrom'),effectiveTo:date('AssignTo'),reason:el('AssignReason').value.trim()||null};
 await api(base+'/discounts','POST',body);await refreshRegister();
});});
el('ApplyForm').addEventListener('submit',e=>{e.preventDefault();run(e.currentTarget,async()=>{
 const body={clientRequestId:crypto.randomUUID(),studentEnrollmentReference:enrollment(),fineRuleId:number('ApplyRule'),
 fineDate:date('ApplyDate'),reason:el('ApplyReason').value.trim()||null};
 await api(base+'/fines','POST',body);await refreshRegister();
});});

let refundPage=1,refundTotal=0,refundSize=25;
const refundStates={1:'Draft',2:'Approved',3:'Paid',4:'Rejected',5:'Cancelled'};
function moneyText(value){return Number(value).toFixed(2);}
function validRefundAmount(value){return /^\d{1,12}(?:\.\d{1,2})?$/.test(value)&&Number(value)>0;}
async function loadRefunds(){
 const campus=el('RefundCampus').value.trim();
 if(campus && (!/^\d+$/.test(campus)||Number(campus)<1))throw Error('Invalid campus ID.');
 const q=new URLSearchParams({page:String(refundPage),pageSize:String(refundSize)});
 if(campus)q.set('campusId',campus);
 const data=await api(base+'/refunds?'+q);
 refundTotal=data.totalCount;
 el('RefundPage').textContent='Page '+refundPage+' / '+Math.max(1,Math.ceil(refundTotal/refundSize));
 el('RefundPrev').disabled=refundPage===1;
 el('RefundNext').disabled=refundPage*refundSize>=refundTotal;
 const select=el('RefundPayment');select.replaceChildren(new Option('Select posted receipt',''));
 const rows=table('RefundPaymentRows',data.payments,6);
 for(const p of data.payments){
  const tr=document.createElement('tr');
  [p.receiptNumber,p.studentCode+' — '+p.studentName,p.campusId,moneyText(p.amount),moneyText(p.reservedAmount)].forEach(x=>tr.append(cell(x)));
  const action=cell('');action.append(button('Select',()=>{select.value=p.paymentReference;el('RefundAmount').focus();}));
  tr.append(action);rows.append(tr);
  select.add(new Option(p.receiptNumber+' — '+p.studentName+' / '+moneyText(p.amount),p.paymentReference));
 }
 const refunds=table('RefundRows',data.refunds,5);
 for(const r of data.refunds){
  const tr=document.createElement('tr');
  [r.receiptNumber,moneyText(r.amount),refundStates[r.state]||r.state,r.requestedByUserId].forEach(x=>tr.append(cell(x)));
  const actions=cell('');
  if(r.state===1){
   actions.append(button('Approve',()=>run(null,async()=>{
    if(!confirm('Approve refund '+r.id+'? Requester and approver must differ.'))return;
    await api(base+'/refunds/'+r.id+'/review','POST',{approve:true,note:'Approved after reviewing source cash receipt.',rowVersion:r.rowVersion});await loadRefunds();
   })));
   actions.append(' ');
   actions.append(button('Reject',()=>run(null,async()=>{
    const note=prompt('Rejection reason (required):');if(note===null)return;if(!note.trim())throw Error('Reason required.');
    await api(base+'/refunds/'+r.id+'/review','POST',{approve:false,note:note.trim(),rowVersion:r.rowVersion});await loadRefunds();
   })));
  }
  if(r.state===2){
   actions.append(button('Record verified payout',()=>run(null,async()=>{
    const reference=prompt('Independently verified cash payout voucher/reference (max 100 characters):');
    if(reference===null)return;
    if(!reference.trim()||reference.trim().length>100)throw Error('Voucher reference required (max 100).');
    const period=prompt('Open accounting period ID covering the actual payout date:');if(period===null)return;
    if(!/^\d+$/.test(period)||Number(period)<1)throw Error('Invalid period.');
    const date=prompt('Actual payout date (YYYY-MM-DD):',new Date().toISOString().slice(0,10));if(date===null)return;
    if(!/^\d{4}-\d{2}-\d{2}$/.test(date))throw Error('Invalid payout date.');
    if(!confirm('Confirm cash was paid, independently verified, and voucher is correct?'))return;
    await api(base+'/refunds/'+r.id+'/record-payout','POST',{clientRequestId:crypto.randomUUID(),
     accountingPeriodId:Number(period),payoutDate:date,payoutEvidenceReference:reference.trim(),rowVersion:r.rowVersion});
    await loadRefunds();
   })));
  }
  tr.append(actions);refunds.append(tr);
 }
}
el('RefundRefresh').addEventListener('click',()=>{refundPage=1;run(null,loadRefunds);});
el('RefundPrev').addEventListener('click',()=>{if(refundPage>1){refundPage--;run(null,loadRefunds);}});
el('RefundNext').addEventListener('click',()=>{if(refundPage*refundSize<refundTotal){refundPage++;run(null,loadRefunds);}});
el('RefundRequestForm').addEventListener('submit',e=>{e.preventDefault();run(e.currentTarget,async()=>{
 const amount=el('RefundAmount').value.trim();
 if(!validRefundAmount(amount))throw Error('Refund amount must have at most two decimal places.');
 if(!el('RefundPayment').value)throw Error('Select a posted cash receipt.');
 await api(base+'/refunds','POST',{clientRequestId:crypto.randomUUID(),
  studentPaymentReference:el('RefundPayment').value,amount,reason:el('RefundReason').value.trim()});
 el('RefundRequestForm').reset();await loadRefunds();
});});
(async()=>{
 try{
  await loadRefunds();
  const options=await api('/api/finance/fees/options');
  const heads=options.feeHeads||[];
  for(const id of ['DrHead','FrHead']){const select=el(id);for(const row of heads)select.add(new Option(row.name,String(row.id)));}
  await refreshRules();
 }catch(e){notice(e.message);}
})();
})();
