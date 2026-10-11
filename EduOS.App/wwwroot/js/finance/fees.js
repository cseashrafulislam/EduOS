(() => {
'use strict';
if (!document.getElementById('feeApp')) return;
const e=id=>document.getElementById(id),base='/api/finance/fees';
const token=document.querySelector('meta[name="request-verification-token"]')?.content;
const state={options:null,student:null,invoice:null,invoicePage:1,receiptPage:1,invoicePages:1,receiptPages:1,createKey:null,createId:null,generateKey:null,generateId:null,paymentKey:null,paymentId:null};
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
function line(){
 const tr=document.createElement('tr'),h=document.createElement('select'),a=document.createElement('input'),b=document.createElement('button');
 h.className='form-select fee-line-head';h.required=true;h.append(option('','Choose fee head'));for(const x of state.options?.feeHeads||[])h.append(option(x.id,x.name));
 a.className='form-control fee-line-amount';a.type='number';a.min='.01';a.step='.01';a.required=true;
 b.type='button';b.className='btn btn-sm btn-outline-danger';b.textContent='Remove';b.onclick=()=>{if(e('feeLineRows').children.length>1)tr.remove();};
 for(const x of [h,a,b]){const td=document.createElement('td');td.append(x);tr.append(td);}e('feeLineRows').append(tr);
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
 e('feeLineRows').replaceChildren();line();batches();structures();
}
function stable(prefix,key){if(state[prefix+'Key']!==key){state[prefix+'Key']=key;state[prefix+'Id']=crypto.randomUUID();}return state[prefix+'Id'];}
function clear(prefix){state[prefix+'Key']=null;state[prefix+'Id']=null;}
async function save(ev){
 ev.preventDefault();if(!ev.currentTarget.reportValidity())return;
 const campusId=Number(e('feeCampus').value),academicYearId=Number(e('feeYear').value),academicLevelId=Number(e('feeLevel').value);
 const academicBatchId=e('feeStructureBatch').value?Number(e('feeStructureBatch').value):null;
 const year=state.options.academicYears.find(x=>x.id===academicYearId),batch=state.options.academicBatches.find(x=>x.id===academicBatchId);
 const effectiveFrom=e('feeEffectiveFrom').value;
 if(year?.campusId&&year.campusId!==campusId||year?.effectiveFrom&&effectiveFrom<dt(year.effectiveFrom)||year?.effectiveTo&&effectiveFrom>dt(year.effectiveTo))return alert('danger','Campus/year or effective date mismatch.');
 if(academicBatchId&&(!batch||batch.campusId!==campusId||batch.academicYearId!==academicYearId||batch.academicLevelId!==academicLevelId))return alert('danger','Batch scope mismatch.');
 const lines=[],seen=new Set();
 for(const row of e('feeLineRows').children){const feeHeadId=Number(row.querySelector('.fee-line-head').value),raw=row.querySelector('.fee-line-amount').value;
 if(!feeHeadId||seen.has(feeHeadId)||!/^\d+(\.\d{1,2})?$/.test(raw)||Number(raw)<=0)return alert('danger','Fee heads must be unique with positive two-decimal amounts.');
 seen.add(feeHeadId);lines.push({feeHeadId,frequency:2,amount:Number(raw),isMandatory:true});}
 if(!lines.length)return alert('danger','Add at least one fee head.');
 const body={campusId,academicYearId,academicLevelId,academicBatchId,name:e('feeStructureName').value.trim(),currencyCode:'BDT',effectiveFrom,effectiveTo:null,isActive:true,lines};
 const signature=JSON.stringify(body);body.clientRequestId=stable('create',signature);
 if(!confirm('Create this fee structure?'))return;
 e('feeSaveStructure').disabled=true;
 try{await api('/structures','POST',body);clear('create');await options();alert('success','Fee structure created.');}
 catch(err){alert('danger',err.message+' Check existing structures before retrying.');}
 finally{e('feeSaveStructure').disabled=false;}
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
 tr.append(cell(x.invoiceNumber),cell(dt(x.invoiceDate)),cell(dt(x.dueDate)),cell(amount(x.totalAmount)),cell(amount(x.paidAmount)),cell(amount(x.dueAmount)),action);e('feeInvoices').append(tr);}
 if(!e('feeInvoices').children.length)empty('feeInvoices',7,'No invoices.');
}
async function receipts(){
 const r=await api('/students/'+encodeURIComponent(state.student)+'/payments?page='+state.receiptPage+'&pageSize=25');
 state.receiptPages=Math.max(1,Math.ceil(r.totalCount/25));pager('receipt');e('feeReceipts').replaceChildren();
 for(const x of r.items||[]){const tr=document.createElement('tr');tr.append(cell(x.receiptNumber),cell(dt(x.paymentDate)),cell(amount(x.amount)),cell(x.paymentMethod===1?'Cash':x.paymentMethod),cell((x.allocations||[]).map(a=>a.invoiceNumber+' ('+amount(a.amount)+')').join(', ')));e('feeReceipts').append(tr);}
 if(!e('feeReceipts').children.length)empty('feeReceipts',5,'No receipts.');
}
async function ledger(){
 const ref=e('feeStudent').value;if(!ref)return alert('warning','Select a student.');
 if(state.student!==ref){state.student=ref;state.invoicePage=1;state.receiptPage=1;state.invoice=null;clear('payment');e('feePaymentPanel').classList.add('d-none');}
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
e('feeAddLine').addEventListener('click',line);e('feeStructureForm').addEventListener('submit',save);e('feeInvoiceForm').addEventListener('submit',generate);
e('feeFindStudent').addEventListener('click',findStudent);e('feeLoadLedger').addEventListener('click',ledger);e('feePaymentForm').addEventListener('submit',collect);
for(const id of ['feeCampus','feeYear','feeLevel'])e(id).addEventListener('change',batches);
for(const id of ['feeBatch','feeMonth','feeBillingYear'])e(id).addEventListener('change',structures);
e('feeInvoicePrev').addEventListener('click',()=>page('invoice',-1));e('feeInvoiceNext').addEventListener('click',()=>page('invoice',1));
e('feeReceiptPrev').addEventListener('click',()=>page('receipt',-1));e('feeReceiptNext').addEventListener('click',()=>page('receipt',1));
const now=new Date();e('feeMonth').value=String(now.getMonth()+1);e('feeBillingYear').value=String(now.getFullYear());e('feePaymentDate').value=today();e('feeEffectiveFrom').value=today();
options().catch(err=>alert('danger',err.message));
})();
