(() => {
'use strict';
if (!document.getElementById('feeApp')) return;
const e=id=>document.getElementById(id),base='/api/finance/fees';
const activeFeature=e('feeApp').dataset.activeFeature||'structures';
const uiLabel=value=>globalThis.eduTaskStaticLabel?.(value)||value;
// Only fixed presentation vocabulary is translated; tenant records and financial values stay intact.
const bn=(document.documentElement.lang||'').toLowerCase().startsWith('bn');
const t=(en,bd)=>bn?bd:en;
const pageLabel=(p,n)=>t('Page ','পৃষ্ঠা ')+p+t(' of ',' / ')+n;
const token=document.querySelector('meta[name="request-verification-token"]')?.content;
const state={options:null,student:null,invoice:null,invoicePage:1,receiptPage:1,invoicePages:1,receiptPages:1,createKey:null,createId:null,generateKey:null,generateId:null,paymentKey:null,paymentId:null,editingStructure:null,structurePage:1,structurePages:1,auditInvoice:null,auditPage:1,auditPages:1,auditData:null,savingStructure:false,structureRefreshFailed:false,billingBusy:false,paymentBusy:false,registerRequest:0,editorRequest:0,closureRequest:0};
const structureGuard=e('feeStructureForm')&&globalThis.EduOSFormGuard?.create(e('feeStructureForm'));
const closureGuard=e('feeStructureCloseForm')&&globalThis.EduOSFormGuard?.create(e('feeStructureCloseForm'));
const paymentGuard=e('feePaymentForm')&&globalThis.EduOSFormGuard?.create(e('feePaymentForm'));
// Read-only finance responses are scoped to the currently selected student and page.
// Abort is an optimization; generation checks remain mandatory if a response races abort.
const readSlots={search:null,ledger:null,invoice:null,receipt:null,audit:null};
function cancelRead(kind){if(readSlots[kind])readSlots[kind].abort();readSlots[kind]=null;}
function beginRead(kind){cancelRead(kind);const controller=new AbortController();readSlots[kind]=controller;return controller;}
function isCurrent(kind,controller){return readSlots[kind]===controller&&!controller.signal.aborted;}
function resetLedgerView(){
 for(const kind of ['ledger','invoice','receipt','audit'])cancelRead(kind);
 state.student=null;state.invoice=null;state.invoicePage=1;state.receiptPage=1;
 state.invoicePages=1;state.receiptPages=1;state.auditInvoice=null;state.auditData=null;
 state.auditPage=1;state.auditPages=1;clear('payment');
 e('feePaymentPanel').classList.add('d-none');
 paymentGuard?.markClean();
 e('feeLedgerSummary').textContent=t('No ledger loaded.','কোনো লেজার লোড হয়নি।');
 empty('feeInvoices',7,t('Select a student.','একজন শিক্ষার্থী বাছুন।'));
 empty('feeReceipts',6,t('Select a student.','একজন শিক্ষার্থী বাছুন।'));
 for(const kind of ['Invoice','Receipt']){e('fee'+kind+'Page').textContent='—';e('fee'+kind+'Prev').disabled=true;e('fee'+kind+'Next').disabled=true;}
 if(e('feeAllocationAudit')){
  e('feeAllocationAudit').classList.add('d-none');e('feeAuditRows').replaceChildren();
  e('feeAuditSummary').textContent='';e('feeAuditPage').textContent='';
  for(const id of ['feeAuditCsv','feeAuditPrev','feeAuditNext'])e(id).disabled=true;
 }
}
const today=()=>{const d=new Date();return [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');};
const dt=x=>x?String(x).slice(0,10):'',amount=x=>Number(x).toFixed(2);
const option=(v,t)=>{const o=document.createElement('option');o.value=String(v);o.textContent=t;return o;};
const cell=x=>{const t=document.createElement('td');t.textContent=x==null?'':String(x);return t;};
function alert(type,msg){const a=e('feeAlert');a.className='alert alert-'+type;a.textContent=msg;a.focus();}
async function api(path,method='GET',data,signal){
 const headers={Accept:'application/json'};
 if(method!=='GET'){if(!token)throw Error(t('Antiforgery token missing. Reload the page.','নিরাপত্তা যাচাইয়ের টোকেন নেই। পৃষ্ঠাটি রিলোড করুন।'));headers.RequestVerificationToken=token;}
 if(data!==undefined)headers['Content-Type']='application/json';
 const r=await fetch(base+path,{method,credentials:'same-origin',cache:'no-store',headers,...(signal?{signal}:{}),...(data===undefined?{}:{body:JSON.stringify(data)})});
 const x=await r.json().catch(()=>null);if(!r.ok||!x?.success)throw Error(x?.message||t('Request failed (','অনুরোধ ব্যর্থ (')+r.status+').');return x.data;
}
function fill(id,rows,label){const s=e(id),prev=s.value;s.replaceChildren(option('',uiLabel(label)));for(const x of rows)s.append(option(x.id,x.name));if(rows.some(x=>String(x.id)===prev))s.value=prev;}
function line(source=null){
 const tr=document.createElement('tr'),h=document.createElement('select'),f=document.createElement('select'),a=document.createElement('input'),d=document.createElement('input'),m=document.createElement('input'),b=document.createElement('button');
 tr.dataset.lineId=source?.id||'';tr.dataset.rowVersion=source?.rowVersion||'';
 h.className='form-select fee-line-head';h.required=true;h.append(option('',uiLabel('Choose fee head')));
 for(const x of state.options?.feeHeads||[])h.append(option(x.id,x.name));
 if(source&&!Array.from(h.options).some(x=>x.value===String(source.feeHeadId)))h.append(option(source.feeHeadId,source.feeHeadName+' (inactive)'));
 h.value=source?.feeHeadId||'';
 f.className='form-select fee-line-frequency';
 for(const [id,name] of [[1,'One time'],[2,'Monthly'],[3,'Quarterly'],[4,'Half yearly'],[5,'Yearly'],[6,'Per term']])f.append(option(id,uiLabel(name)));
 f.value=String(source?.frequency||2);
 a.className='form-control fee-line-amount';a.type='number';a.min='.01';a.step='.01';a.required=true;a.value=source?.amount??'';
 d.className='form-control fee-line-due';d.type='number';d.min='1';d.max='31';d.placeholder='—';d.value=source?.dueDayOfMonth??'';
 m.className='form-check-input fee-line-mandatory';m.type='checkbox';m.checked=source?.isMandatory??true;
 b.type='button';b.className='btn btn-sm btn-outline-danger';b.textContent=uiLabel(source?.id?'Retained':'Remove');b.disabled=!!source?.id;
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
 if(activeFeature==='structures'){
  for(const [id,prop,label] of [
   ['feeCampus','campuses','Choose campus'],
   ['feeYear','academicYears','Choose academic year'],
   ['feeLevel','academicLevels','Choose academic level']
  ])fill(id,state.options[prop]||[],label);
  e('feeLineRows').replaceChildren();line();batches();
  if(e('feeStructureRegister')){fill('feeRegisterCampus',state.options.campuses||[],'All campuses');await register();}
 }
 if(activeFeature==='billing'){
  fill('feeBatch',state.options.academicBatches||[],'Choose active batch');structures();
 }
}
function stable(prefix,key){if(state[prefix+'Key']!==key){state[prefix+'Key']=key;state[prefix+'Id']=crypto.randomUUID();}return state[prefix+'Id'];}
function clear(prefix){state[prefix+'Key']=null;state[prefix+'Id']=null;}
async function save(ev){
 ev.preventDefault();if(state.savingStructure||!ev.currentTarget.reportValidity())return;
 if(state.structureRefreshFailed)return alert('warning',t('The last save succeeded but its list refresh failed. Reload this page to prevent a duplicate.','সর্বশেষ সংরক্ষণ সফল হলেও তালিকা হালনাগাদ ব্যর্থ হয়েছে। একই রেকর্ড আবার তৈরি হওয়া ঠেকাতে পৃষ্ঠাটি রিলোড করুন।'));
 const campusId=Number(e('feeCampus').value),academicYearId=Number(e('feeYear').value),academicLevelId=Number(e('feeLevel').value);
 const academicBatchId=e('feeStructureBatch').value?Number(e('feeStructureBatch').value):null;
 const year=state.options.academicYears.find(x=>x.id===academicYearId),batch=state.options.academicBatches.find(x=>x.id===academicBatchId);
 const effectiveFrom=e('feeEffectiveFrom').value,effectiveTo=e('feeEffectiveTo').value||null;
 if(effectiveTo&&effectiveTo<effectiveFrom)return alert('danger',t('Effective to must be after effective from.','শেষের তারিখ শুরুর তারিখের পরে হতে হবে।'));
 if(year?.campusId&&year.campusId!==campusId||year?.effectiveFrom&&effectiveFrom<dt(year.effectiveFrom)||year?.effectiveTo&&effectiveFrom>dt(year.effectiveTo))return alert('danger',t('Campus/year or effective date mismatch.','ক্যাম্পাস, শিক্ষাবর্ষ বা কার্যকর তারিখ মিলছে না।'));
 if(academicBatchId&&(!batch||batch.campusId!==campusId||batch.academicYearId!==academicYearId||batch.academicLevelId!==academicLevelId))return alert('danger',t('Batch scope mismatch.','ব্যাচের পরিসর মিলছে না।'));
 const lines=[],seen=new Set();
 for(const row of e('feeLineRows').children){
 const feeHeadId=Number(row.querySelector('.fee-line-head').value),frequency=Number(row.querySelector('.fee-line-frequency').value),raw=row.querySelector('.fee-line-amount').value;
 const dueRaw=row.querySelector('.fee-line-due').value,dueDayOfMonth=dueRaw?Number(dueRaw):null;
 const key=feeHeadId+'|'+frequency;
 if(!feeHeadId||seen.has(key)||!/^[0-9]+(\.[0-9]{1,2})?$/.test(raw)||Number(raw)<=0||dueDayOfMonth!==null&&(!Number.isInteger(dueDayOfMonth)||dueDayOfMonth<1||dueDayOfMonth>31))return alert('danger',t('Fee lines need unique head/frequency, positive two-decimal amount and valid due day.','ফি খাত ও পুনরাবৃত্তি অনন্য হতে হবে; ধনাত্মক দুই দশমিকের পরিমাণ এবং সঠিক পরিশোধের দিন দিন।'));
 seen.add(key);const line={feeHeadId,frequency,amount:Number(raw),dueDayOfMonth,isMandatory:row.querySelector('.fee-line-mandatory').checked};
 if(state.editingStructure&&row.dataset.lineId){line.id=Number(row.dataset.lineId);line.rowVersion=row.dataset.rowVersion;}
 lines.push(line);
 }
 if(!lines.length)return alert('danger',t('Add at least one fee head.','অন্তত একটি ফি খাত যোগ করুন।'));
 const body={campusId,academicYearId,academicLevelId,academicBatchId,name:e('feeStructureName').value.trim(),currencyCode:state.editingStructure?.currencyCode||'BDT',effectiveFrom,effectiveTo,isActive:e('feeStructureActive').checked,lines};
 if(state.editingStructure)body.rowVersion=state.editingStructure.rowVersion;
 body.clientRequestId=stable('create',JSON.stringify(body));
 if(!confirm(state.editingStructure?t('Amend this unbilled fee structure?','এই বিল না হওয়া ফি কাঠামো সংশোধন করবেন?'):t('Create this fee structure?','এই ফি কাঠামো তৈরি করবেন?')))return;
 state.savingStructure=true;e('feeSaveStructure').disabled=true;e('feeStructureForm').inert=true;
 try{
 const id=state.editingStructure?.id,method=id?'PUT':'POST',url=id?'/structures/'+id:'/structures';
 await api(url,method,body);clear('create');state.editingStructure=null;
 try{await options();resetStructureForm();}
 catch(refreshError){
  state.structureRefreshFailed=true;
  alert('warning',t('Fee structure was saved, but the list did not refresh. Reload before making another change. ','ফি কাঠামো সংরক্ষিত হয়েছে, কিন্তু তালিকা হালনাগাদ হয়নি। পরবর্তী পরিবর্তনের আগে পৃষ্ঠা রিলোড করুন। ')+refreshError.message);
  return;
 }
 alert('success',id?t('Fee structure amended.','ফি কাঠামো সংশোধিত হয়েছে।'):t('Fee structure created.','ফি কাঠামো তৈরি হয়েছে।'));
 }catch(err){alert('danger',err.message+t(' Existing billed structures cannot be amended; reload before retrying.',' ইতিমধ্যে বিল হওয়া কাঠামো সংশোধন করা যায় না; আবার চেষ্টা করার আগে রিলোড করুন।'));}
 finally{state.savingStructure=false;e('feeSaveStructure').disabled=state.structureRefreshFailed;e('feeStructureForm').inert=false;}
}
function resetStructureForm(){
 ++state.editorRequest;
 state.editingStructure=null;clear('create');e('feeStructureForm').reset();e('feeStructureMode').textContent=t('Creating a new fee structure.','নতুন ফি কাঠামো তৈরি হচ্ছে।');
 e('feeEffectiveFrom').value=today();e('feeEffectiveTo').value='';e('feeStructureActive').checked=true;
 e('feeLineRows').replaceChildren();line();batches();
 structureGuard?.markClean();
}
async function register(){
 if(!e('feeStructureRegister'))return;
 const campus=e('feeRegisterCampus').value,page=state.structurePage,request=++state.registerRequest;
 const r=await api('/structures?page='+page+'&pageSize=20'+(campus?'&campusId='+encodeURIComponent(campus):''));
 if(request!==state.registerRequest||campus!==e('feeRegisterCampus').value||page!==state.structurePage)return;
 state.structurePages=Math.max(1,Math.ceil(r.totalCount/20));
 e('feeRegisterPage').textContent=pageLabel(state.structurePage,state.structurePages);
 e('feeRegisterPrev').disabled=state.structurePage<=1;e('feeRegisterNext').disabled=state.structurePage>=state.structurePages;
 e('feeRegisterRows').replaceChildren();
 for(const x of r.items||[]){
 const tr=document.createElement('tr'),action=document.createElement('td');
 const campusName=state.options.campuses.find(c=>c.id===x.campusId)?.name||x.campusId;
 const yearName=state.options.academicYears.find(c=>c.id===x.academicYearId)?.name||x.academicYearId;
 for(const [label,mode] of [['Edit unbilled','edit'],['Copy as new','copy']]){
 const b=document.createElement('button');b.type='button';b.className='btn btn-sm btn-outline-primary me-1';b.textContent=uiLabel(label);
 b.onclick=()=>loadStructure(x.id,mode).catch(err=>alert('danger',err.message));action.append(b);
 }
 if(x.isActive){
  const b=document.createElement('button');b.type='button';b.className='btn btn-sm btn-outline-danger';b.textContent=uiLabel('Close future billing');
  b.onclick=()=>selectClosure(x.id).catch(err=>alert('danger',err.message));action.append(b);
 }
 tr.append(cell(x.name),cell(campusName),cell(yearName),cell(dt(x.effectiveFrom)+' – '+(dt(x.effectiveTo)||uiLabel('Open'))),cell(uiLabel(x.isActive?'Active':'Inactive')),action);
 e('feeRegisterRows').append(tr);
 }
 if(!e('feeRegisterRows').children.length)empty('feeRegisterRows',6,t('No fee structures.','কোনো ফি কাঠামো পাওয়া যায়নি।'));
}
async function selectClosure(id){
 if(state.closingStructure||closureGuard&&!closureGuard.confirmDiscard())return;
 const request=++state.closureRequest;
 const draft=globalThis.EduOSFormGuard?.fingerprint(e('feeStructureCloseForm'));
 const x=await api('/structures/'+encodeURIComponent(id));
 if(request!==state.closureRequest||state.closingStructure)return;
 if(draft!==globalThis.EduOSFormGuard?.fingerprint(e('feeStructureCloseForm'))&&
    closureGuard&&!closureGuard.confirmDiscard())return;
 if(!x.isActive)throw Error(t('Only active fee structures can be closed.','শুধু সক্রিয় ফি কাঠামো বন্ধ করা যায়।'));
 e('feeCloseId').value=String(x.id);e('feeCloseVersion').value=x.rowVersion;
 e('feeCloseName').value=x.name+t(' — Campus ',' — ক্যাম্পাস ')+x.campusId;
 e('feeCloseDate').value='';
 e('feeStructureCloseForm').classList.remove('d-none');
 closureGuard?.markClean();
 e('feeCloseDate').focus();
}
async function loadStructure(id,mode){
 if(state.savingStructure||state.structureRefreshFailed||structureGuard&&!structureGuard.confirmDiscard())return;
 const previousDraft=globalThis.EduOSFormGuard?.fingerprint(e('feeStructureForm'));
 const request=++state.editorRequest;
 const x=await api('/structures/'+encodeURIComponent(id));
 if(request!==state.editorRequest||state.savingStructure)return;
 if(previousDraft!==globalThis.EduOSFormGuard?.fingerprint(e('feeStructureForm'))&&
    structureGuard&&!structureGuard.confirmDiscard())return;
 if(!x.lines?.length)throw Error(t('Structure has no active fee lines.','এই কাঠামোয় কোনো সক্রিয় ফি খাত নেই।'));
 state.editingStructure=mode==='edit'?x:null;clear('create');
 e('feeCampus').value=String(x.campusId);e('feeYear').value=String(x.academicYearId);
 e('feeLevel').value=x.academicLevelId==null?'':String(x.academicLevelId);batches();
 e('feeStructureBatch').value=x.academicBatchId==null?'':String(x.academicBatchId);
 e('feeStructureName').value=mode==='copy'?x.name+t(' - New',' - নতুন'):x.name;
 e('feeEffectiveFrom').value=mode==='copy'?'':dt(x.effectiveFrom);
 e('feeEffectiveTo').value=mode==='copy'?'':dt(x.effectiveTo);
 e('feeStructureActive').checked=mode==='copy'?true:x.isActive;
 e('feeLineRows').replaceChildren();for(const row of x.lines)line(mode==='copy'?{...row,id:null,rowVersion:null}:row);
 e('feeStructureMode').textContent=mode==='copy'?t('New version template: enter a new effective date. Existing overlapping scopes are rejected.','নতুন সংস্করণ: নতুন কার্যকর তারিখ দিন। একই সময়ের পরিসর গ্রহণযোগ্য নয়।'):t('Editing unbilled structure #','বিল না হওয়া কাঠামো #')+x.id+t('; issued invoices block amendments.','; ইস্যু করা ইনভয়েস থাকলে সংশোধন করা যাবে না।');
 structureGuard?.markClean();
 e('feeStructureForm').scrollIntoView({behavior:'smooth',block:'start'});
}
let invoicePreviewRevision = 0, invoicePreviewRequest = 0;
function invalidateInvoicePreview(){
 invoicePreviewRevision++;invoicePreviewRequest++;
 state.invoicePreview = null; state.invoicePreviewKey = null;
 e('feePreview').disabled = false;
 e('feeGenerate').disabled = true; e('feePreviewCsv').disabled = true;
 e('feePreviewSummary').textContent = '';
 e('feePreviewRows').replaceChildren();
}
function invoiceBatchBody(){
 const academicBatchId=Number(e('feeBatch').value),feeStructureId=Number(e('feeInvoiceStructure').value);
 const month=Number(e('feeMonth').value),year=Number(e('feeBillingYear').value);
 const invoiceDate=year+'-'+String(month).padStart(2,'0')+'-01',dueDate=e('feeDueDate').value;
 if(!academicBatchId||!feeStructureId||!Number.isInteger(year)||year<2000||year>2200||
  month<1||month>12||!dueDate||dueDate<invoiceDate)throw Error(t('Choose a batch, fee structure and valid due date.','ব্যাচ, ফি কাঠামো এবং সঠিক পরিশোধের শেষ তারিখ বাছুন।'));
 const body={academicBatchId,feeStructureId,month,year,invoiceDate,dueDate};
 body.clientRequestId=stable('generate',JSON.stringify(body));
 return body;
}
async function previewInvoices(){
 const revision=invoicePreviewRevision,request=++invoicePreviewRequest;
 let body;try{body=invoiceBatchBody();}catch(err){return alert('danger',err.message);}
 e('feePreview').disabled=true;
 try{
  const preview=await api('/invoices/preflight','POST',body);
  if(revision!==invoicePreviewRevision||request!==invoicePreviewRequest)return;
  state.invoicePreview=preview;state.invoicePreviewKey=JSON.stringify(body);
  e('feePreviewSummary').textContent=t('Eligible: ','যোগ্য: ')+preview.eligibleCount+t('; new: ','; নতুন: ')+preview.newCount+
   t('; existing: ','; বিদ্যমান: ')+preview.existingCount+t('; conflicts: ','; সংঘাত: ')+preview.conflictCount+
   t('; new net BDT ','; নতুন নিট টাকা ')+amount(preview.newTotal)+'.';
  e('feePreviewRows').replaceChildren();
  for(const x of preview.rows||[]){
   const tr=document.createElement('tr');
   tr.append(cell(x.studentCode),cell(x.studentName),cell(uiLabel(x.status)),cell(x.invoiceNumber||''),
    cell(amount(x.subtotal)),cell(amount(x.discount)),cell(amount(x.total)));
   e('feePreviewRows').append(tr);
  }
  e('feeGenerate').disabled=!preview.canGenerate;
  e('feePreviewCsv').disabled=false;
  if(!preview.canGenerate)alert('warning',t('Resolve conflicting invoices before generating.','ইনভয়েস তৈরির আগে সংঘাতগুলো সমাধান করুন।'));
 }catch(err){if(revision===invoicePreviewRevision&&request===invoicePreviewRequest)alert('danger',err.message);}
 finally{if(request===invoicePreviewRequest)e('feePreview').disabled=false;}
}
function invoicePreviewCsv(){
 const preview=state.invoicePreview;if(!preview)return;
 const csvCell=value=>{
  let v=String(value??'');
  if(/^[\s\u0000-\u001f]*[=+\-@]/.test(v))v="'"+v;
  return '"'+v.replace(/"/g,'""')+'"';
 };
 const rows=[['Student code','Name','Status','Invoice','Subtotal','Discount','Net BDT'].map(uiLabel),
  ...(preview.rows||[]).map(x=>[x.studentCode,x.studentName,x.status,x.invoiceNumber||'',
    amount(x.subtotal),amount(x.discount),amount(x.total)])];
 const blob=new Blob(['\uFEFF'+rows.map(row=>row.map(csvCell).join(',')).join('\r\n')+'\r\n'],
  {type:'text/csv;charset=utf-8'});
 const url=URL.createObjectURL(blob),link=document.createElement('a');
 link.href=url;link.download='invoice-batch-preflight.csv';
 document.body.append(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);
}
async function generate(ev){
 ev.preventDefault();if(state.billingBusy||!ev.currentTarget.reportValidity())return;
 let body;try{body=invoiceBatchBody();}catch(err){return alert('danger',err.message);}
 if(!state.invoicePreview?.canGenerate||state.invoicePreviewKey!==JSON.stringify(body))
  return alert('warning',t('Run billing preview again after changing inputs.','তথ্য পরিবর্তনের পর আবার বিলিং পূর্বদর্শন চালান।'));
 if(!confirm(t('Generate monthly invoices for the previewed batch?','পূর্বদর্শনে থাকা ব্যাচের মাসিক ইনভয়েস তৈরি করবেন?')))return;
 state.billingBusy=true;e('feeGenerate').disabled=true;e('feeInvoiceForm').inert=true;
 try{const r=await api('/invoices/generate','POST',body);clear('generate');
  invalidateInvoicePreview();
  e('feeGenerationInfo').textContent=t('Generated: ','তৈরি: ')+r.generated+t('; existing: ','; বিদ্যমান: ')+r.existing+t('; failed: ','; ব্যর্থ: ')+r.failed;
  alert('success',t('Invoices generated.','ইনভয়েস তৈরি হয়েছে।'));}
 catch(err){alert('danger',err.message+t(' Reload preview before retrying with the same request ID.',' একই অনুরোধ আইডি দিয়ে আবার চেষ্টা করার আগে পূর্বদর্শন রিলোড করুন।'));invalidateInvoicePreview();}
 finally{state.billingBusy=false;e('feeInvoiceForm').inert=false;}
}
async function findStudent(){
 const s=e('feeStudentSearch').value.trim();
 if(s.length<2||s.length>100)return alert('warning',t('Search requires 2–100 characters.','খোঁজার জন্য ২–১০০ অক্ষর প্রয়োজন।'));
 const request=beginRead('search');
 try{
  const rows=await api('/students/search?search='+encodeURIComponent(s)+'&take=25','GET',undefined,request.signal);
  if(!isCurrent('search',request)||e('feeStudentSearch').value.trim()!==s)return;
  fill('feeStudent',rows.map(x=>({id:x.reference,name:x.studentCode+' — '+x.name})),'Choose student');
 }catch(err){if(isCurrent('search',request)&&err.name!=='AbortError')alert('danger',err.message);}
}
function empty(id,cols,msg){const tr=document.createElement('tr'),c=cell(msg);c.colSpan=cols;tr.append(c);e(id).replaceChildren(tr);}
function pager(kind){const p=state[kind+'Page'],max=state[kind+'Pages'];e('fee'+(kind==='invoice'?'Invoice':'Receipt')+'Page').textContent=pageLabel(p,max);
 e('fee'+(kind==='invoice'?'Invoice':'Receipt')+'Prev').disabled=p<=1;e('fee'+(kind==='invoice'?'Invoice':'Receipt')+'Next').disabled=p>=max;}
async function invoices(){
 const student=state.student,page=state.invoicePage,request=beginRead('invoice');
 if(!student)return;
 const r=await api('/students/'+encodeURIComponent(student)+'/invoices?page='+page+'&pageSize=25','GET',undefined,request.signal);
 if(!isCurrent('invoice',request)||state.student!==student||state.invoicePage!==page||e('feeStudent').value!==student)return;
 state.invoicePages=Math.max(1,Math.ceil(r.totalCount/25));pager('invoice');e('feeInvoices').replaceChildren();
 for(const x of r.items||[]){const tr=document.createElement('tr'),action=document.createElement('td');
 if(Number(x.dueAmount)>0&&(x.state===2||x.state===3)){const b=document.createElement('button');b.type='button';b.className='btn btn-sm btn-outline-primary';b.textContent=t('Collect cash','নগদ গ্রহণ');b.onclick=()=>{
  if(state.paymentBusy||state.student!==student||e('feeStudent').value!==student||
     paymentGuard&&!paymentGuard.confirmDiscard())return;
  state.invoice=x;clear('payment');e('feePaymentPanel').classList.remove('d-none');
  e('feePaymentInvoice').value=x.invoiceNumber;e('feePaymentAmount').value=amount(x.dueAmount);e('feePaymentAmount').max=x.dueAmount;
  paymentGuard?.markClean();
 };action.append(b);}
 if(e('feeAllocationAudit')){const audit=document.createElement('button');audit.type='button';audit.className='btn btn-sm btn-outline-secondary ms-1';audit.textContent=t('Receipt audit','রসিদ নিরীক্ষা');audit.onclick=()=>openAudit(x.reference).catch(err=>{if(err.name!=='AbortError'&&state.student===student)alert('danger',err.message);});action.append(audit);}
 const view=document.createElement('a');view.className='btn btn-sm btn-outline-secondary ms-1';view.textContent=t('View / Print','দেখুন / প্রিন্ট');view.href='/FeeDocuments/Invoice?studentReference='+encodeURIComponent(student)+'&documentReference='+encodeURIComponent(x.reference);action.append(view);
 tr.append(cell(x.invoiceNumber),cell(dt(x.invoiceDate)),cell(dt(x.dueDate)),cell(amount(x.totalAmount)),cell(amount(x.paidAmount)),cell(amount(x.dueAmount)),action);e('feeInvoices').append(tr);}
 if(!e('feeInvoices').children.length)empty('feeInvoices',7,t('No invoices.','কোনো ইনভয়েস পাওয়া যায়নি।'));
}
async function openAudit(invoiceReference){
 const student=state.student;if(!student||!e('feeAllocationAudit'))return;
 if(state.auditInvoice!==invoiceReference){state.auditInvoice=invoiceReference;state.auditPage=1;}
 const page=state.auditPage,request=beginRead('audit');
 state.auditData=null;e('feeAuditCsv').disabled=true;
 const result=await api('/students/'+encodeURIComponent(student)+'/invoices/'+
  encodeURIComponent(invoiceReference)+'/allocation-audit?page='+page+'&pageSize=25','GET',undefined,request.signal);
 if(!isCurrent('audit',request)||state.student!==student||e('feeStudent').value!==student||
    state.auditInvoice!==invoiceReference||state.auditPage!==page)return;
 state.auditData=result;state.auditPages=Math.max(1,Math.ceil(result.totalAllocations/25));
 e('feeAuditSummary').textContent=result.invoiceNumber+t(' — Invoice BDT ',' — ইনভয়েস টাকা ')+amount(result.invoiceTotal)+
   t('; current paid BDT ','; বর্তমান পরিশোধ টাকা ')+amount(result.currentPaid)+t('; due BDT ','; বকেয়া টাকা ')+amount(result.currentDue)+
   t('; gross successful allocations BDT ','; মোট সফল বণ্টন টাকা ')+amount(result.grossSuccessfulAllocations)+
   t('; gross-to-net difference BDT ','; মোট-নিট পার্থক্য টাকা ')+amount(result.grossToNetDifference)+'. '+
   (result.matchesCurrentInvoice?t('Amounts agree; GL posting is not verified.','পরিমাণ মিলেছে; জিএল পোস্টিং যাচাই করা হয়নি।'):
   t('Difference or invalid allocation requires Finance review; refunds may explain a difference.','পার্থক্য বা অবৈধ বণ্টনের জন্য ফাইন্যান্স পর্যালোচনা প্রয়োজন; ফেরতের কারণে পার্থক্য হতে পারে।'));
 e('feeAuditRows').replaceChildren();
 for(const x of result.items||[]){
  const tr=document.createElement('tr');
  tr.append(cell(x.receiptNumber),cell(dt(x.paymentDate)),cell(amount(x.allocationAmount)),
   cell(String(x.paymentState)),cell(x.journalId==null?t('Not posted','পোস্ট হয়নি'):x.journalId));
  e('feeAuditRows').append(tr);
 }
 if(!e('feeAuditRows').children.length)empty('feeAuditRows',5,t('No allocations on this page.','এই পৃষ্ঠায় কোনো বণ্টন নেই।'));
 e('feeAuditPage').textContent=pageLabel(page,state.auditPages)+
  ' ('+result.totalAllocations+t(' allocations)','টি বণ্টন)');
 e('feeAuditPrev').disabled=page<=1;
 e('feeAuditNext').disabled=page>=state.auditPages;
 e('feeAuditCsv').disabled=false;
 e('feeAllocationAudit').classList.remove('d-none');
}
function exportAudit(){
 const r=state.auditData;if(!r)return;
 const safe=v=>{let t=String(v??'');if(/^[\s\u0000-\u001f]*[=+\-@]/.test(t))t="'"+t;return '"'+t.replace(/"/g,'""')+'"';};
 const rows=[['Invoice','Receipt','Date','Allocation BDT','Payment state','GL journal'].map(uiLabel),
  ...(r.items||[]).map(x=>[r.invoiceNumber,x.receiptNumber,dt(x.paymentDate),
   amount(x.allocationAmount),String(x.paymentState),x.journalId??t('Not posted','পোস্ট হয়নি')])];
 const csv='\uFEFF'+rows.map(row=>row.map(safe).join(',')).join('\r\n')+'\r\n';
 const url=URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8'}));
 const a=document.createElement('a');a.href=url;a.download='invoice-receipt-audit-page-'+r.page+'.csv';
 document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);
}
async function receipts(){
 const student=state.student,page=state.receiptPage,request=beginRead('receipt');
 if(!student)return;
 const r=await api('/students/'+encodeURIComponent(student)+'/payments?page='+page+'&pageSize=25','GET',undefined,request.signal);
 if(!isCurrent('receipt',request)||state.student!==student||state.receiptPage!==page||e('feeStudent').value!==student)return;
 state.receiptPages=Math.max(1,Math.ceil(r.totalCount/25));pager('receipt');e('feeReceipts').replaceChildren();
 for(const x of r.items||[]){const tr=document.createElement('tr'),action=document.createElement('td'),view=document.createElement('a');
 view.className='btn btn-sm btn-outline-secondary';view.textContent=t('View / Print','দেখুন / প্রিন্ট');view.href='/FeeDocuments/Receipt?studentReference='+encodeURIComponent(student)+'&documentReference='+encodeURIComponent(x.reference);action.append(view);
 tr.append(cell(x.receiptNumber),cell(dt(x.paymentDate)),cell(amount(x.amount)),cell(x.paymentMethod===1?t('Cash','নগদ'):x.paymentMethod),cell((x.allocations||[]).map(a=>a.invoiceNumber+' ('+amount(a.amount)+')').join(', ')),action);e('feeReceipts').append(tr);}
 if(!e('feeReceipts').children.length)empty('feeReceipts',6,t('No receipts.','কোনো রসিদ পাওয়া যায়নি।'));
}
async function ledger(){
 const ref=e('feeStudent').value;
 if(!ref)return alert('warning',t('Select a student.','একজন শিক্ষার্থী বাছুন।'));
 resetLedgerView();state.student=ref;
 const request=beginRead('ledger');
 try{
  const r=await api('/students/'+encodeURIComponent(ref)+'/ledger','GET',undefined,request.signal);
  if(!isCurrent('ledger',request)||state.student!==ref||e('feeStudent').value!==ref)return;
  e('feeLedgerSummary').textContent=r.studentName+' ('+r.studentCode+')'+t(' — Gross billed: ',' — মোট বিল: ')+amount(r.totalBilled)+t(' Credit notes: ',' ক্রেডিট নোট: ')+amount(r.totalCreditNotes)+t(' Net billed: ',' নিট বিল: ')+amount(r.netBilled)+t(' Net paid: ',' নিট পরিশোধ: ')+amount(r.netPaid)+t(' Due: ',' বকেয়া: ')+amount(r.totalDue)+' BDT';
  await Promise.all([invoices(),receipts()]);
 }catch(err){if(isCurrent('ledger',request)&&err.name!=='AbortError')alert('danger',err.message);}
}
async function collect(ev){
 ev.preventDefault();if(state.paymentBusy||!state.invoice||!ev.currentTarget.reportValidity())return;
 const raw=e('feePaymentAmount').value,paymentDate=e('feePaymentDate').value,invoice=state.invoice,student=state.student;
 if(!student||e('feeStudent').value!==student)return alert('warning',t('Reload the selected student ledger.','নির্বাচিত শিক্ষার্থীর লেজার আবার লোড করুন।'));
 if(!/^\d+(\.\d{1,2})?$/.test(raw)||Number(raw)<=0||Number(raw)>Number(invoice.dueAmount)||paymentDate>today())return alert('danger',t('Invalid cash amount or payment date.','নগদের পরিমাণ বা পরিশোধের তারিখ সঠিক নয়।'));
 const body={paymentDate,paymentMethod:1,amount:Number(raw),bankAccountId:null,externalReference:null,allocations:[{invoiceReference:invoice.reference,amount:Number(raw)}]};
 body.clientRequestId=stable('payment',JSON.stringify(body));
 state.paymentBusy=true;e('feeCollect').disabled=true;
 e('feePaymentForm').inert=true;
 for(const id of ['feeStudentSearch','feeStudent','feeFindStudent','feeLoadLedger'])e(id).disabled=true;
 try{
 // Read-only preflight provides current, authorized balances; the write
 // revalidates independently in its own transaction.
 let preview;
 try{preview=await api('/payments/preflight','POST',body);}
 catch(err){return alert('danger',err.message+t(' Reload invoice balances before retrying.',' আবার চেষ্টা করার আগে ইনভয়েসের বকেয়া রিলোড করুন।'));}
 if(state.student!==student||e('feeStudent').value!==student||state.invoice!==invoice)return;
 if(preview.amount!==body.amount||preview.currencyCode!=='BDT'||
    preview.invoices?.length!==1||
    preview.invoices[0].invoiceReference!==invoice.reference||
    preview.invoices[0].allocating!==body.amount)
    return alert('danger',t('Receipt preview changed. Reload and retry.','রসিদের পূর্বদর্শন পরিবর্তিত হয়েছে। রিলোড করে আবার চেষ্টা করুন।'));
 if(!confirm((preview.alreadyRecorded?t('Retrieve existing receipt for ','বিদ্যমান রসিদ দেখুন: '):t('Record ','রসিদ নথিভুক্ত করুন: '))+
     amount(preview.amount)+t(' BDT for ',' টাকা, ইনভয়েস ')+preview.invoices[0].invoiceNumber+
     t('? Current remaining due: ','? বর্তমান অবশিষ্ট বকেয়া: ')+amount(preview.invoices[0].dueAfter)+' BDT.'))return;
 if(state.student!==student||e('feeStudent').value!==student||state.invoice!==invoice)return;
 try{const r=await api('/payments','POST',body);if(state.student!==student||e('feeStudent').value!==student)return;clear('payment');state.invoice=null;e('feePaymentPanel').classList.add('d-none');paymentGuard?.markClean();await ledger();if(state.student===student)alert('success',t('Receipt recorded: ','রসিদ নথিভুক্ত হয়েছে: ')+r.receiptNumber);}
 catch(err){alert('danger',err.message+t(' Keep values unchanged for a safe retry.',' নিরাপদে আবার চেষ্টা করতে তথ্য অপরিবর্তিত রাখুন।'));}
 }finally{
  state.paymentBusy=false;e('feeCollect').disabled=false;e('feePaymentForm').inert=false;
  for(const id of ['feeStudentSearch','feeStudent','feeFindStudent','feeLoadLedger'])e(id).disabled=false;
 }
}
function page(kind,delta){const next=state[kind+'Page']+delta;if(!state.student||next<1||next>state[kind+'Pages'])return;state[kind+'Page']=next;(kind==='invoice'?invoices():receipts()).catch(err=>{if(err.name!=='AbortError'&&state.student===e('feeStudent').value)alert('danger',err.message);});}
if(e('feeStructureCloseForm')){
 e('feeCloseCancel').addEventListener('click',()=>{
  if(state.closingStructure||closureGuard&&!closureGuard.confirmDiscard())return;
  ++state.closureRequest;
  e('feeStructureCloseForm').classList.add('d-none');closureGuard?.markClean();
 });
 e('feeStructureCloseForm').addEventListener('submit',ev=>{ev.preventDefault();(async()=>{
  if(state.closingStructure||!ev.currentTarget.reportValidity())return;
  const id=e('feeCloseId').value,date=e('feeCloseDate').value,version=e('feeCloseVersion').value;
  if(!/^\d+$/.test(id)||!version||!/^\d{4}-\d{2}-\d{2}$/.test(date))throw Error(t('Select a structure and valid month-end date.','কাঠামো ও মাসের শেষের সঠিক তারিখ বাছুন।'));
  const parts=date.split('-').map(Number);
  if(parts[2]!==new Date(parts[0],parts[1],0).getDate())throw Error(t('Closure must be on the last day of a month.','বন্ধের তারিখ অবশ্যই মাসের শেষ দিন হতে হবে।'));
  if(!confirm(t('Close future billing for ','ভবিষ্যৎ বিলিং বন্ধ করবেন: ')+e('feeCloseName').value+t(' through ',' পর্যন্ত ')+date+t('? Issued invoices remain unchanged. This cannot be extended or reopened.','? ইস্যু করা ইনভয়েস অপরিবর্তিত থাকবে। এটি বাড়ানো বা আবার খোলা যাবে না।')))return;
  state.closingStructure=true;e('feeCloseSubmit').disabled=true;e('feeStructureCloseForm').inert=true;
  try{
   await api('/structures/'+encodeURIComponent(id)+'/close','POST',{effectiveTo:date,rowVersion:version});
   e('feeStructureCloseForm').classList.add('d-none');
   closureGuard?.markClean();
   try{await register();}
   catch(refreshError){
    alert('warning',t('Future billing was closed, but the register did not refresh. Reload before another closure. ','ভবিষ্যৎ বিলিং বন্ধ হয়েছে, কিন্তু তালিকা হালনাগাদ হয়নি। আবার বন্ধ করার আগে পৃষ্ঠা রিলোড করুন। ')+refreshError.message);
    return;
   }
   alert('success',t('Future billing closed. Existing invoices and fee lines are unchanged.','ভবিষ্যৎ বিলিং বন্ধ হয়েছে। বিদ্যমান ইনভয়েস ও ফি খাত অপরিবর্তিত।'));
  }finally{state.closingStructure=false;e('feeCloseSubmit').disabled=false;e('feeStructureCloseForm').inert=false;}
 })().catch(err=>alert('danger',err.message));});
}
// One scoped entry view owns one workflow. The legacy ledger route remains
// valid, but never mounts or initializes unrelated billing/structure editors.
if(activeFeature==='structures' && e('feeStructureForm')){
 e('feeAddLine').addEventListener('click',()=>line());
 e('feeStructureReset').addEventListener('click',()=>{
  if(state.savingStructure||state.structureRefreshFailed||structureGuard&&!structureGuard.confirmDiscard())return;
  resetStructureForm();
 });
 e('feeStructureForm').addEventListener('submit',save);
 for(const id of ['feeCampus','feeYear','feeLevel'])e(id).addEventListener('change',batches);
 if(e('feeStructureRegister')){
  e('feeRegisterCampus').addEventListener('change',()=>{state.structurePage=1;register().catch(err=>alert('danger',err.message));});
  for(const [id,delta] of [['feeRegisterPrev',-1],['feeRegisterNext',1]])
   e(id).addEventListener('click',()=>{const p=state.structurePage+delta;if(p<1||p>state.structurePages)return;state.structurePage=p;register().catch(err=>alert('danger',err.message));});
 }
 e('feeEffectiveFrom').value=today();e('feeStructureForm').inert=true;
 options().then(()=>{structureGuard?.markClean();e('feeStructureForm').inert=false;})
  .catch(err=>alert('danger',err.message));
}
if(activeFeature==='billing' && e('feeInvoiceForm')){
 e('feeInvoiceForm').addEventListener('submit',generate);
 for(const id of ['feeBatch','feeMonth','feeBillingYear'])e(id).addEventListener('change',structures);
 const now=new Date();e('feeMonth').value=String(now.getMonth()+1);
 e('feeBillingYear').value=String(now.getFullYear());
 e('feeInvoiceForm').inert=true;
 options().then(()=>{e('feeInvoiceForm').inert=false;}).catch(err=>alert('danger',err.message));
 // A preview is advisory and becomes invalid on every invoice-scope edit.
 for(const id of ['feeBatch','feeInvoiceStructure','feeMonth','feeBillingYear','feeDueDate']){
  const field=e(id);field.addEventListener('change',invalidateInvoicePreview);
  field.addEventListener('input',invalidateInvoicePreview);
 }
 e('feePreview').addEventListener('click',previewInvoices);
 e('feePreviewCsv').addEventListener('click',invoicePreviewCsv);
}
if(activeFeature==='ledger' && e('feeStudent')){
 let lastSearchValue=e('feeStudentSearch').value;
 e('feeStudentSearch').addEventListener('input',()=>{
  if(state.paymentBusy||paymentGuard?.isDirty()&&!paymentGuard.confirmDiscard()){
   e('feeStudentSearch').value=lastSearchValue;return;
  }
  lastSearchValue=e('feeStudentSearch').value;
  cancelRead('search');fill('feeStudent',[],'Choose student');resetLedgerView();
 });
 e('feeStudent').addEventListener('change',()=>{
  if(state.paymentBusy||paymentGuard?.isDirty()&&!paymentGuard.confirmDiscard()){
   e('feeStudent').value=state.student||'';return;
  }
  resetLedgerView();
 });
 e('feeFindStudent').addEventListener('click',findStudent);
 e('feeLoadLedger').addEventListener('click',ledger);
 e('feePaymentForm').addEventListener('submit',collect);
 e('feePaymentCancel')?.addEventListener('click',()=>{
  if(state.paymentBusy||paymentGuard&&!paymentGuard.confirmDiscard())return;
  state.invoice=null;clear('payment');e('feePaymentPanel').classList.add('d-none');paymentGuard?.markClean();
 });
 if(e('feeAllocationAudit')){
  e('feeAuditClose').addEventListener('click',()=>{cancelRead('audit');state.auditInvoice=null;state.auditData=null;e('feeAuditCsv').disabled=true;e('feeAllocationAudit').classList.add('d-none');});
  e('feeAuditCsv').addEventListener('click',exportAudit);
  for(const [id,delta] of [['feeAuditPrev',-1],['feeAuditNext',1]])
   e(id).addEventListener('click',()=>{const next=state.auditPage+delta;
    if(next<1||next>state.auditPages||!state.auditInvoice)return;
    state.auditPage=next;openAudit(state.auditInvoice).catch(err=>{if(err.name!=='AbortError'&&state.student===e('feeStudent').value)alert('danger',err.message);});});
 }
 e('feeInvoicePrev').addEventListener('click',()=>page('invoice',-1));
 e('feeInvoiceNext').addEventListener('click',()=>page('invoice',1));
 e('feeReceiptPrev').addEventListener('click',()=>page('receipt',-1));
 e('feeReceiptNext').addEventListener('click',()=>page('receipt',1));
 e('feePaymentDate').value=today();
 paymentGuard?.markClean();
}
})();
