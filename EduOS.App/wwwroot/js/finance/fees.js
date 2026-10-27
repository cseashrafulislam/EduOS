(() => {
'use strict';
if (!document.getElementById('feeApp')) return;
const e=id=>document.getElementById(id),base='/api/finance/fees';
const token=document.querySelector('meta[name="request-verification-token"]')?.content;
const state={options:null,student:null,invoice:null,invoicePage:1,receiptPage:1,invoicePages:1,receiptPages:1,createKey:null,createId:null,generateKey:null,generateId:null,paymentKey:null,paymentId:null,editingStructure:null,structurePage:1,structurePages:1,auditInvoice:null,auditPage:1,auditPages:1,auditData:null};
const today=()=>{const d=new Date();return [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');};
const dt=x=>x?String(x).slice(0,10):'',amount=x=>Number(x).toFixed(2);
const option=(v,t)=>{const o=document.createElement('option');o.value=String(v);o.textContent=t;return o;};
const cell=x=>{const t=document.createElement('td');t.textContent=x==null?'':String(x);return t;};
function alert(type,msg){const a=e('feeAlert');a.className='alert alert-'+type;a.textContent=msg;a.focus();}
async function api(path,method='GET',data){
 const headers={Accept:'application/json'};
 if(method!=='GET'){if(!token)throw Error('Antiforgery token missing. Reload the page.');headers.RequestVerificationToken=token;}
 if(data!==undefined)headers['Content-Type']='application/json';
 const r=await fetch(base+path,{method,credentials:'same-origin',cache:'no-store',headers,...(data===undefined?{}:{body:JSON.stringify(data)})});
 const x=await r.json().catch(()=>null);if(!r.ok||!x?.success)throw Error(x?.message||'Request failed ('+r.status+').');return x.data;
}
function fill(id,rows,label){const s=e(id),prev=s.value;s.replaceChildren(option('',label));for(const x of rows)s.append(option(x.id,x.name));if(rows.some(x=>String(x.id)===prev))s.value=prev;}
function line(source=null){
 const tr=document.createElement('tr'),h=document.createElement('select'),f=document.createElement('select'),a=document.createElement('input'),d=document.createElement('input'),m=document.createElement('input'),b=document.createElement('button');
 tr.dataset.lineId=source?.id||'';tr.dataset.rowVersion=source?.rowVersion||'';
 h.className='form-select fee-line-head';h.required=true;h.append(option('','Choose fee head'));
 for(const x of state.options?.feeHeads||[])h.append(option(x.id,x.name));
 if(source&&!Array.from(h.options).some(x=>x.value===String(source.feeHeadId)))h.append(option(source.feeHeadId,source.feeHeadName+' (inactive)'));
 h.value=source?.feeHeadId||'';
 f.className='form-select fee-line-frequency';
 for(const [id,name] of [[1,'One time'],[2,'Monthly'],[3,'Quarterly'],[4,'Half yearly'],[5,'Yearly'],[6,'Per term']])f.append(option(id,name));
 f.value=String(source?.frequency||2);
 a.className='form-control fee-line-amount';a.type='number';a.min='.01';a.step='.01';a.required=true;a.value=source?.amount??'';
 d.className='form-control fee-line-due';d.type='number';d.min='1';d.max='31';d.placeholder='—';d.value=source?.dueDayOfMonth??'';
 m.className='form-check-input fee-line-mandatory';m.type='checkbox';m.checked=source?.isMandatory??true;
 b.type='button';b.className='btn btn-sm btn-outline-danger';b.textContent=source?.id?'Retained':'Remove';b.disabled=!!source?.id;
 b.onclick=()=>{if(e('feeLineRows').children.length>1)tr.remove();};
 for(const x of [h,f,a,d,m,b]){const td=document.createElement('td');td.append(x);tr.append(td);}e('feeLineRows').append(tr);
}
function batches(){
 const c=Number(e('feeCampus').value),y=Number(e('feeYear').value),l=Number(e('feeLevel').value);
 fill('feeStructureBatch',(state.options?.academicBatches||[]).filter(x=>x.campusId===c&&x.academicYearId===y&&x.academicLevelId===l),'All batches');
}
function structures(){
 const batch=state.options?.academicBatches.find(x=>String(x.id)===e('feeBatch').value);
 const y=Number(e('feeBillingYear').value),m=Number(e('feeMonth').value);
 if(!batch||y<2000||y>2200||m<1||m>12){fill('feeInvoiceStructure',[],'Select batch and period');return;}
 const date=y+'-'+String(m).padStart(2,'0')+'-01';
 fill('feeInvoiceStructure',(state.options?.feeStructures||[]).filter(x=>x.campusId===batch.campusId&&x.academicYearId===batch.academicYearId&&
 (x.academicLevelId==null||x.academicLevelId===batch.academicLevelId)&&(x.academicProgramId==null||x.academicProgramId===batch.academicProgramId)&&
 (x.academicBatchId==null||x.academicBatchId===batch.id)&&dt(x.effectiveFrom)<=date&&(!x.effectiveTo||dt(x.effectiveTo)>=date)),'Choose fee structure');
}
async function options(){
 state.options=await api('/options');
 for(const [id,prop] of [['feeCampus','campuses'],['feeYear','academicYears'],['feeLevel','academicLevels'],['feeBatch','academicBatches']])fill(id,state.options[prop]||[],'Choose '+prop);
 e('feeLineRows').replaceChildren();line();batches();structures();if(e('feeStructureRegister')){fill('feeRegisterCampus',state.options.campuses||[],'All campuses');await register();}
}
function stable(prefix,key){if(state[prefix+'Key']!==key){state[prefix+'Key']=key;state[prefix+'Id']=crypto.randomUUID();}return state[prefix+'Id'];}
function clear(prefix){state[prefix+'Key']=null;state[prefix+'Id']=null;}
async function save(ev){
 ev.preventDefault();if(!ev.currentTarget.reportValidity())return;
 const campusId=Number(e('feeCampus').value),academicYearId=Number(e('feeYear').value),academicLevelId=Number(e('feeLevel').value);
 const academicBatchId=e('feeStructureBatch').value?Number(e('feeStructureBatch').value):null;
 const year=state.options.academicYears.find(x=>x.id===academicYearId),batch=state.options.academicBatches.find(x=>x.id===academicBatchId);
 const effectiveFrom=e('feeEffectiveFrom').value,effectiveTo=e('feeEffectiveTo').value||null;
 if(effectiveTo&&effectiveTo<effectiveFrom)return alert('danger','Effective to must be after effective from.');
 if(year?.campusId&&year.campusId!==campusId||year?.effectiveFrom&&effectiveFrom<dt(year.effectiveFrom)||year?.effectiveTo&&effectiveFrom>dt(year.effectiveTo))return alert('danger','Campus/year or effective date mismatch.');
 if(academicBatchId&&(!batch||batch.campusId!==campusId||batch.academicYearId!==academicYearId||batch.academicLevelId!==academicLevelId))return alert('danger','Batch scope mismatch.');
 const lines=[],seen=new Set();
 for(const row of e('feeLineRows').children){
 const feeHeadId=Number(row.querySelector('.fee-line-head').value),frequency=Number(row.querySelector('.fee-line-frequency').value),raw=row.querySelector('.fee-line-amount').value;
 const dueRaw=row.querySelector('.fee-line-due').value,dueDayOfMonth=dueRaw?Number(dueRaw):null;
 const key=feeHeadId+'|'+frequency;
 if(!feeHeadId||seen.has(key)||!/^[0-9]+(\.[0-9]{1,2})?$/.test(raw)||Number(raw)<=0||dueDayOfMonth!==null&&(!Number.isInteger(dueDayOfMonth)||dueDayOfMonth<1||dueDayOfMonth>31))return alert('danger','Fee lines need unique head/frequency, positive two-decimal amount and valid due day.');
 seen.add(key);const line={feeHeadId,frequency,amount:Number(raw),dueDayOfMonth,isMandatory:row.querySelector('.fee-line-mandatory').checked};
 if(state.editingStructure&&row.dataset.lineId){line.id=Number(row.dataset.lineId);line.rowVersion=row.dataset.rowVersion;}
 lines.push(line);
 }
 if(!lines.length)return alert('danger','Add at least one fee head.');
 const body={campusId,academicYearId,academicLevelId,academicBatchId,name:e('feeStructureName').value.trim(),currencyCode:state.editingStructure?.currencyCode||'BDT',effectiveFrom,effectiveTo,isActive:e('feeStructureActive').checked,lines};
 if(state.editingStructure)body.rowVersion=state.editingStructure.rowVersion;
 body.clientRequestId=stable('create',JSON.stringify(body));
 if(!confirm(state.editingStructure?'Amend this unbilled fee structure?':'Create this fee structure?'))return;
 e('feeSaveStructure').disabled=true;
 try{
 const id=state.editingStructure?.id,method=id?'PUT':'POST',url=id?'/structures/'+id:'/structures';
 await api(url,method,body);clear('create');state.editingStructure=null;await options();resetStructureForm();
 alert('success',id?'Fee structure amended.':'Fee structure created.');
 }catch(err){alert('danger',err.message+' Existing billed structures cannot be amended; reload before retrying.');}
 finally{e('feeSaveStructure').disabled=false;}
}
function resetStructureForm(){
 state.editingStructure=null;clear('create');e('feeStructureForm').reset();e('feeStructureMode').textContent='Creating a new fee structure.';
 e('feeEffectiveFrom').value=today();e('feeEffectiveTo').value='';e('feeStructureActive').checked=true;
 e('feeLineRows').replaceChildren();line();batches();
}
async function register(){
 if(!e('feeStructureRegister'))return;
 const campus=e('feeRegisterCampus').value;
 const r=await api('/structures?page='+state.structurePage+'&pageSize=20'+(campus?'&campusId='+encodeURIComponent(campus):''));
 state.structurePages=Math.max(1,Math.ceil(r.totalCount/20));
 e('feeRegisterPage').textContent='Page '+state.structurePage+' of '+state.structurePages;
 e('feeRegisterPrev').disabled=state.structurePage<=1;e('feeRegisterNext').disabled=state.structurePage>=state.structurePages;
 e('feeRegisterRows').replaceChildren();
 for(const x of r.items||[]){
 const tr=document.createElement('tr'),action=document.createElement('td');
 const campusName=state.options.campuses.find(c=>c.id===x.campusId)?.name||x.campusId;
 const yearName=state.options.academicYears.find(c=>c.id===x.academicYearId)?.name||x.academicYearId;
 for(const [label,mode] of [['Edit unbilled','edit'],['Copy as new','copy']]){
 const b=document.createElement('button');b.type='button';b.className='btn btn-sm btn-outline-primary me-1';b.textContent=label;
 b.onclick=()=>loadStructure(x.id,mode).catch(err=>alert('danger',err.message));action.append(b);
 }
 if(x.isActive){
  const b=document.createElement('button');b.type='button';b.className='btn btn-sm btn-outline-danger';b.textContent='Close future billing';
  b.onclick=()=>selectClosure(x.id).catch(err=>alert('danger',err.message));action.append(b);
 }
 tr.append(cell(x.name),cell(campusName),cell(yearName),cell(dt(x.effectiveFrom)+' – '+(dt(x.effectiveTo)||'Open')),cell(x.isActive?'Active':'Inactive'),action);
 e('feeRegisterRows').append(tr);
 }
 if(!e('feeRegisterRows').children.length)empty('feeRegisterRows',6,'No fee structures.');
}
async function selectClosure(id){
 const x=await api('/structures/'+encodeURIComponent(id));
 if(!x.isActive)throw Error('Only active fee structures can be closed.');
 e('feeCloseId').value=String(x.id);e('feeCloseVersion').value=x.rowVersion;
 e('feeCloseName').value=x.name+' — Campus '+x.campusId;
 e('feeCloseDate').value='';
 e('feeStructureCloseForm').classList.remove('d-none');
 e('feeCloseDate').focus();
}
async function loadStructure(id,mode){
 const x=await api('/structures/'+encodeURIComponent(id));
 if(!x.lines?.length)throw Error('Structure has no active fee lines.');
 state.editingStructure=mode==='edit'?x:null;clear('create');
 e('feeCampus').value=String(x.campusId);e('feeYear').value=String(x.academicYearId);
 e('feeLevel').value=x.academicLevelId==null?'':String(x.academicLevelId);batches();
 e('feeStructureBatch').value=x.academicBatchId==null?'':String(x.academicBatchId);
 e('feeStructureName').value=mode==='copy'?x.name+' - New':x.name;
 e('feeEffectiveFrom').value=mode==='copy'?'':dt(x.effectiveFrom);
 e('feeEffectiveTo').value=mode==='copy'?'':dt(x.effectiveTo);
 e('feeStructureActive').checked=mode==='copy'?true:x.isActive;
 e('feeLineRows').replaceChildren();for(const row of x.lines)line(mode==='copy'?{...row,id:null,rowVersion:null}:row);
 e('feeStructureMode').textContent=mode==='copy'?'New version template: enter a new effective date. Existing overlapping scopes are rejected.':'Editing unbilled structure #'+x.id+'; issued invoices block amendments.';
 e('feeStructureForm').scrollIntoView({behavior:'smooth',block:'start'});
}
async function generate(ev){
 ev.preventDefault();if(!ev.currentTarget.reportValidity())return;
 const academicBatchId=Number(e('feeBatch').value),feeStructureId=Number(e('feeInvoiceStructure').value),month=Number(e('feeMonth').value),year=Number(e('feeBillingYear').value);
 const invoiceDate=year+'-'+String(month).padStart(2,'0')+'-01',dueDate=e('feeDueDate').value;
 if(!academicBatchId||!feeStructureId||dueDate<invoiceDate)return alert('danger','Select an applicable structure and valid due date.');
 const body={academicBatchId,feeStructureId,month,year,invoiceDate,dueDate};body.clientRequestId=stable('generate',JSON.stringify(body));
 if(!confirm('Generate monthly invoices for this batch?'))return;
 e('feeGenerate').disabled=true;
 try{const r=await api('/invoices/generate','POST',body);clear('generate');e('feeGenerationInfo').textContent='Generated: '+r.generated+'; existing: '+r.existing+'; failed: '+r.failed;alert('success','Invoices generated.');}
 catch(err){alert('danger',err.message+' Retry with the same request ID.');}
 finally{e('feeGenerate').disabled=false;}
}
async function findStudent(){
 const s=e('feeStudentSearch').value.trim();if(s.length<2||s.length>100)return alert('warning','Search requires 2–100 characters.');
 try{const rows=await api('/students/search?search='+encodeURIComponent(s)+'&take=25');fill('feeStudent',rows.map(x=>({id:x.reference,name:x.studentCode+' — '+x.name})),'Choose student');}
 catch(err){alert('danger',err.message);}
}
function empty(id,cols,msg){const tr=document.createElement('tr'),c=cell(msg);c.colSpan=cols;tr.append(c);e(id).replaceChildren(tr);}
function pager(kind){const p=state[kind+'Page'],max=state[kind+'Pages'];e('fee'+(kind==='invoice'?'Invoice':'Receipt')+'Page').textContent='Page '+p+' of '+max;
 e('fee'+(kind==='invoice'?'Invoice':'Receipt')+'Prev').disabled=p<=1;e('fee'+(kind==='invoice'?'Invoice':'Receipt')+'Next').disabled=p>=max;}
async function invoices(){
 const r=await api('/students/'+encodeURIComponent(state.student)+'/invoices?page='+state.invoicePage+'&pageSize=25');
 state.invoicePages=Math.max(1,Math.ceil(r.totalCount/25));pager('invoice');e('feeInvoices').replaceChildren();
 for(const x of r.items||[]){const tr=document.createElement('tr'),action=document.createElement('td');
 if(Number(x.dueAmount)>0&&(x.state===2||x.state===3)){const b=document.createElement('button');b.type='button';b.className='btn btn-sm btn-outline-primary';b.textContent='Collect cash';b.onclick=()=>{state.invoice=x;clear('payment');e('feePaymentPanel').classList.remove('d-none');e('feePaymentInvoice').value=x.invoiceNumber;e('feePaymentAmount').value=amount(x.dueAmount);e('feePaymentAmount').max=x.dueAmount;};action.append(b);}
 if(e('feeAllocationAudit')){const audit=document.createElement('button');audit.type='button';audit.className='btn btn-sm btn-outline-secondary ms-1';audit.textContent='Receipt audit';audit.onclick=()=>openAudit(x.reference).catch(err=>alert('danger',err.message));action.append(audit);}
 const view=document.createElement('a');view.className='btn btn-sm btn-outline-secondary ms-1';view.textContent='View / Print';view.href='/FeeDocuments/Invoice?studentReference='+encodeURIComponent(state.student)+'&documentReference='+encodeURIComponent(x.reference);action.append(view);
 tr.append(cell(x.invoiceNumber),cell(dt(x.invoiceDate)),cell(dt(x.dueDate)),cell(amount(x.totalAmount)),cell(amount(x.paidAmount)),cell(amount(x.dueAmount)),action);e('feeInvoices').append(tr);}
 if(!e('feeInvoices').children.length)empty('feeInvoices',7,'No invoices.');
}
async function openAudit(invoiceReference){
 if(!state.student||!e('feeAllocationAudit'))return;
 if(state.auditInvoice!==invoiceReference){state.auditInvoice=invoiceReference;state.auditPage=1;}
 state.auditData=null;e('feeAuditCsv').disabled=true;
 const result=await api('/students/'+encodeURIComponent(state.student)+'/invoices/'+
  encodeURIComponent(invoiceReference)+'/allocation-audit?page='+state.auditPage+'&pageSize=25');
 if(state.auditInvoice!==invoiceReference)return;
 state.auditData=result;state.auditPages=Math.max(1,Math.ceil(result.totalAllocations/25));
 e('feeAuditSummary').textContent=result.invoiceNumber+' — Invoice BDT '+amount(result.invoiceTotal)+
  '; current paid BDT '+amount(result.currentPaid)+'; due BDT '+amount(result.currentDue)+
  '; gross successful allocations BDT '+amount(result.grossSuccessfulAllocations)+
  '; gross-to-net difference BDT '+amount(result.grossToNetDifference)+'. '+
  (result.matchesCurrentInvoice?'Amounts agree; GL posting is not verified.':
  'Difference or invalid allocation requires Finance review; refunds may explain a difference.');
 e('feeAuditRows').replaceChildren();
 for(const x of result.items||[]){
  const tr=document.createElement('tr');
  tr.append(cell(x.receiptNumber),cell(dt(x.paymentDate)),cell(amount(x.allocationAmount)),
   cell(String(x.paymentState)),cell(x.journalId==null?'Not posted':x.journalId));
  e('feeAuditRows').append(tr);
 }
 if(!e('feeAuditRows').children.length)empty('feeAuditRows',5,'No allocations on this page.');
 e('feeAuditPage').textContent='Page '+state.auditPage+' of '+state.auditPages+
  ' ('+result.totalAllocations+' allocations)';
 e('feeAuditPrev').disabled=state.auditPage<=1;
 e('feeAuditNext').disabled=state.auditPage>=state.auditPages;
 e('feeAuditCsv').disabled=false;
 e('feeAllocationAudit').classList.remove('d-none');
}
function exportAudit(){
 const r=state.auditData;if(!r)return;
 const safe=v=>{let t=String(v??'');if(/^[\\s\\u0000-\\u001f]*[=+\\-@]/.test(t))t="'"+t;return '"'+t.replace(/"/g,'""')+'"';};
 const rows=[['Invoice','Receipt','Date','Allocation BDT','Payment state','GL journal'],
  ...(r.items||[]).map(x=>[r.invoiceNumber,x.receiptNumber,dt(x.paymentDate),
   amount(x.allocationAmount),String(x.paymentState),x.journalId??'Not posted'])];
 const csv='\\uFEFF'+rows.map(row=>row.map(safe).join(',')).join('\\r\\n')+'\\r\\n';
 const url=URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8'}));
 const a=document.createElement('a');a.href=url;a.download='invoice-receipt-audit-page-'+r.page+'.csv';
 document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);
}
async function receipts(){
 const r=await api('/students/'+encodeURIComponent(state.student)+'/payments?page='+state.receiptPage+'&pageSize=25');
 state.receiptPages=Math.max(1,Math.ceil(r.totalCount/25));pager('receipt');e('feeReceipts').replaceChildren();
 for(const x of r.items||[]){const tr=document.createElement('tr'),action=document.createElement('td'),view=document.createElement('a');
 view.className='btn btn-sm btn-outline-secondary';view.textContent='View / Print';view.href='/FeeDocuments/Receipt?studentReference='+encodeURIComponent(state.student)+'&documentReference='+encodeURIComponent(x.reference);action.append(view);
 tr.append(cell(x.receiptNumber),cell(dt(x.paymentDate)),cell(amount(x.amount)),cell(x.paymentMethod===1?'Cash':x.paymentMethod),cell((x.allocations||[]).map(a=>a.invoiceNumber+' ('+amount(a.amount)+')').join(', ')),action);e('feeReceipts').append(tr);}
 if(!e('feeReceipts').children.length)empty('feeReceipts',6,'No receipts.');
}
async function ledger(){
 const ref=e('feeStudent').value;if(!ref)return alert('warning','Select a student.');
 if(state.student!==ref){if(e('feeAllocationAudit'))e('feeAllocationAudit').classList.add('d-none');state.auditInvoice=null;state.auditData=null;state.student=ref;state.invoicePage=1;state.receiptPage=1;state.invoice=null;clear('payment');e('feePaymentPanel').classList.add('d-none');}
 try{const r=await api('/students/'+encodeURIComponent(ref)+'/ledger');e('feeLedgerSummary').textContent=r.studentName+' ('+r.studentCode+') — Billed: '+amount(r.totalBilled)+' Paid: '+amount(r.totalPaid)+' Due: '+amount(r.totalDue)+' BDT';await Promise.all([invoices(),receipts()]);}
 catch(err){alert('danger',err.message);}
}
async function collect(ev){
 ev.preventDefault();if(!state.invoice||!ev.currentTarget.reportValidity())return;
 const raw=e('feePaymentAmount').value,paymentDate=e('feePaymentDate').value,invoice=state.invoice;
 if(!/^\d+(\.\d{1,2})?$/.test(raw)||Number(raw)<=0||Number(raw)>Number(invoice.dueAmount)||paymentDate>today())return alert('danger','Invalid cash amount or payment date.');
 const body={paymentDate,paymentMethod:1,amount:Number(raw),bankAccountId:null,externalReference:null,allocations:[{invoiceReference:invoice.reference,amount:Number(raw)}]};
 body.clientRequestId=stable('payment',JSON.stringify(body));
 if(!confirm('Record cash receipt for '+invoice.invoiceNumber+'?'))return;
 e('feeCollect').disabled=true;
 try{const r=await api('/payments','POST',body);clear('payment');state.invoice=null;e('feePaymentPanel').classList.add('d-none');await ledger();alert('success','Receipt recorded: '+r.receiptNumber);}
 catch(err){alert('danger',err.message+' Keep values unchanged for a safe retry.');}
 finally{e('feeCollect').disabled=false;}
}
function page(kind,delta){const next=state[kind+'Page']+delta;if(next<1||next>state[kind+'Pages'])return;state[kind+'Page']=next;(kind==='invoice'?invoices():receipts()).catch(err=>alert('danger',err.message));}
if(e('feeStructureCloseForm')){
 e('feeCloseCancel').addEventListener('click',()=>e('feeStructureCloseForm').classList.add('d-none'));
 e('feeStructureCloseForm').addEventListener('submit',ev=>{ev.preventDefault();(async()=>{
  const id=e('feeCloseId').value,date=e('feeCloseDate').value,version=e('feeCloseVersion').value;
  if(!/^\d+$/.test(id)||!version||!/^\d{4}-\d{2}-\d{2}$/.test(date))throw Error('Select a structure and valid month-end date.');
  const parts=date.split('-').map(Number);
  if(parts[2]!==new Date(parts[0],parts[1],0).getDate())throw Error('Closure must be on the last day of a month.');
  if(!confirm('Close future billing for '+e('feeCloseName').value+' through '+date+'? Issued invoices remain unchanged. This cannot be extended or reopened.'))return;
  e('feeCloseSubmit').disabled=true;
  try{
   await api('/structures/'+encodeURIComponent(id)+'/close','POST',{effectiveTo:date,rowVersion:version});
   e('feeStructureCloseForm').classList.add('d-none');
   await options();alert('success','Future billing closed. Existing invoices and fee lines are unchanged.');
  }finally{e('feeCloseSubmit').disabled=false;}
 })().catch(err=>alert('danger',err.message));});
}
e('feeAddLine').addEventListener('click',()=>line());e('feeStructureReset').addEventListener('click',resetStructureForm);
if(e('feeStructureRegister')){e('feeRegisterCampus').addEventListener('change',()=>{state.structurePage=1;register().catch(err=>alert('danger',err.message));});for(const [id,delta] of [['feeRegisterPrev',-1],['feeRegisterNext',1]])e(id).addEventListener('click',()=>{const p=state.structurePage+delta;if(p<1||p>state.structurePages)return;state.structurePage=p;register().catch(err=>alert('danger',err.message));});}e('feeStructureForm').addEventListener('submit',save);e('feeInvoiceForm').addEventListener('submit',generate);
e('feeFindStudent').addEventListener('click',findStudent);e('feeLoadLedger').addEventListener('click',ledger);e('feePaymentForm').addEventListener('submit',collect);
for(const id of ['feeCampus','feeYear','feeLevel'])e(id).addEventListener('change',batches);
for(const id of ['feeBatch','feeMonth','feeBillingYear'])e(id).addEventListener('change',structures);
if(e('feeAllocationAudit')){
 e('feeAuditClose').addEventListener('click',()=>e('feeAllocationAudit').classList.add('d-none'));
 e('feeAuditCsv').addEventListener('click',exportAudit);
 for(const [id,delta] of [['feeAuditPrev',-1],['feeAuditNext',1]])
  e(id).addEventListener('click',()=>{const next=state.auditPage+delta;
   if(next<1||next>state.auditPages||!state.auditInvoice)return;
   state.auditPage=next;openAudit(state.auditInvoice).catch(err=>alert('danger',err.message));});
}
e('feeInvoicePrev').addEventListener('click',()=>page('invoice',-1));e('feeInvoiceNext').addEventListener('click',()=>page('invoice',1));
e('feeReceiptPrev').addEventListener('click',()=>page('receipt',-1));e('feeReceiptNext').addEventListener('click',()=>page('receipt',1));
const now=new Date();e('feeMonth').value=String(now.getMonth()+1);e('feeBillingYear').value=String(now.getFullYear());e('feePaymentDate').value=today();e('feeEffectiveFrom').value=today();
options().catch(err=>alert('danger',err.message));
})();
