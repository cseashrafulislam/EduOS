(() => {
 'use strict';
 const root = document.getElementById('supplierInvoiceMatch'); if (!root) return;
 const $ = id => document.getElementById(id);
 const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
 if (bn) root.querySelectorAll('[data-bn]').forEach(x => { x.textContent = x.dataset.bn; });
 const el = (tag, text) => { const node = document.createElement(tag);
  if (text !== undefined) node.textContent = String(text ?? ''); return node; };
 const button = (label, onClick) => { const x = el('button', label); x.type = 'button';
  x.className = 'btn btn-outline-secondary btn-sm w-100 text-start mb-1'; x.addEventListener('click',onClick); return x; };
 const token = document.querySelector('meta[name="request-verification-token"]')?.content;
 const params = value => '?' + new URLSearchParams(value).toString();
 const localDate = () => {const day = new Date(); return [day.getFullYear(),
  String(day.getMonth()+1).padStart(2,'0'),String(day.getDate()).padStart(2,'0')].join('-');};
 const currency = value => Number(value).toLocaleString(bn ? 'bn-BD' : 'en-BD',
  {minimumFractionDigits:2, maximumFractionDigits:2});
 function status(message, isError = true) { const x = $('simStatus');
  x.className = 'alert alert-' + (isError ? 'danger' : 'success'); x.textContent = String(message); }
 async function api(url, method = 'GET', payload) {
  const headers = {Accept:'application/json'};
  if (payload !== undefined) headers['Content-Type'] = 'application/json';
  if (method !== 'GET' && token) headers.RequestVerificationToken = token;
  const response = await fetch(url, {method, credentials:'same-origin', cache:'no-store', headers,
   ...(payload === undefined ? {} : {body:JSON.stringify(payload)})});
  const data = await response.json().catch(() => null);
  if (!response.ok || !data?.success) {
   const error=new Error(data?.message || 'Request failed / অনুরোধ ব্যর্থ');
   error.statusCode=response.status;throw error;
  }
  return data.data;
 }
 $('simHelp').addEventListener('click', () => { const x = $('simGuidance');
  x.classList.toggle('d-none'); $('simHelp').setAttribute('aria-expanded', String(!x.classList.contains('d-none'))); });
 let selected = null, searchSeq = 0, pending = false;
 const draftForm = $('simPreviewForm');
 const draftGuard = draftForm?.elements && window.EduOSFormGuard?.create(draftForm);
 function canDiscardDraft(){return !selected || !draftGuard?.isDirty() || draftGuard.confirmDiscard();}
 $('simSearchForm').addEventListener('submit', async e => {
  e.preventDefault(); if(pending || !canDiscardDraft())return;
  const seq = ++searchSeq;
  selected = null; $('simPreviewForm').classList.add('d-none'); $('simResult').classList.add('d-none');
  $('simOrderResults').replaceChildren();
  try {
   const data = await api('/api/procurement/purchase-orders' + params({page:1, pageSize:25,
    state:'Approved', search:$('simSearch').value.trim()}));
   if (seq !== searchSeq) return;
   for (const po of data.items || []) $('simOrderResults').append(button(
    `${po.orderNumber} · ${po.currencyCode} ${currency(po.totalAmount)} · Campus ${po.campusId}`,
    () => choose(po.id)));
   if (!(data.items || []).length) $('simOrderResults').append(el('p',
    bn ? 'কোনো অনুমোদিত ক্রয়াদেশ পাওয়া যায়নি' : 'No approved purchase orders found'));
  } catch (err) { if (seq === searchSeq) status(err.message); }
 });
 async function choose(id) {
  if(pending || !canDiscardDraft())return;
  const seq = ++searchSeq;
  selected=null;saveKey=null;
  $('simOrderResults').replaceChildren(); $('simRows').replaceChildren();
  $('simPreviewForm').classList.add('d-none'); $('simResult').classList.add('d-none');
  try {
   const po = await api('/api/procurement/purchase-orders/' + id);
   if (po.stateCode !== 'Approved') throw new Error('Purchase order is no longer approved.');
   const first = await api('/api/procurement/goods-receipts' + params({purchaseOrderId:id,
    state:'Posted',page:1,pageSize:100}));
   if (first.totalCount > 500) throw new Error('Order has more than 500 posted deliveries; narrow the review before matching.');
   const receipts = [...(first.items || [])];
   for (let page = 2; page <= first.totalPages; page++) {
    const more = await api('/api/procurement/goods-receipts' + params({purchaseOrderId:id,
     state:'Posted',page,pageSize:100})); receipts.push(...(more.items || []));
   }
   if (seq !== searchSeq) return;
   selected = po;saveKey=null;$('simOrderTitle').textContent = `${po.orderNumber} · Campus ${po.campusId}`;
   for (const receipt of receipts) for (const line of receipt.lines || []) addLine(receipt,line);
   $('simMode').value = 'Automatic';lastMode='Automatic';adjustMode();
   for(const id of ['simInvoiceNo','simInvoiceDate','simDueDate','simReason'])if($(id))$(id).value='';
   if($('simInvoiceDate'))$('simInvoiceDate').value=localDate();
   draftGuard?.markClean();
   if (!receipts.length) status('No accepted deliveries yet / কোনো অনুমোদিত পণ্য গ্রহণ নেই');
   $('simPreviewForm').classList.remove('d-none');
  } catch (err) { if (seq === searchSeq) status(err.message); }
 }
 function addLine(receipt, line) {
  const tr = el('tr'); tr.dataset.reference = receipt.reference; tr.dataset.lineNo = String(line.lineNo);
  tr.dataset.poPrice = String(line.approvedPoUnitPrice);
  for (const text of [receipt.reference.slice(0, 8),line.inventoryItemId,line.lotOrBatchNo,
   line.quantity,line.approvedPoUnitPrice,line.postedUnitCost]) tr.append(el('td',text));
  const qtyCell = el('td'), qty = el('input'); qty.type = 'number'; qty.min = '0';
  qty.max = String(line.quantity); qty.step = '0.000001'; qty.value = '0';
  qty.className = 'form-control form-control-sm'; qty.setAttribute('aria-label','Invoice quantity for receipt '+receipt.reference+' line '+line.lineNo);
  qtyCell.append(qty); tr.append(qtyCell);
  const priceCell = el('td'), price = el('input'); price.type = 'number'; price.step = '0.0001';
  price.value = '0'; price.className = 'form-control form-control-sm sim-price';
  price.setAttribute('aria-label','Invoice price or correction for receipt line '+line.lineNo);
  priceCell.append(price); tr.append(priceCell);
  const reasonCell = el('td'), reason = el('input'); reason.type = 'text'; reason.maxLength = 800;
  reason.className = 'form-control form-control-sm sim-reason';
  reason.setAttribute('aria-label','Price variance reason for receipt line '+line.lineNo);
  reasonCell.append(reason); tr.append(reasonCell); $('simRows').append(tr);
 }
 let lastMode='Automatic';
 function adjustMode() {
  const mode = $('simMode').value; const automatic = mode === 'Automatic';
  $('simAmountTitle').textContent = mode === 'ManualFinal' ? 'Final unit price / চূড়ান্ত মূল্য' :
   mode === 'AutoPlusManual' ? 'Signed correction / সংশোধন (+/-)' : 'Automatic PO price';
  for (const tr of $('simRows').children) {
   const price = tr.querySelector('.sim-price'), reason = tr.querySelector('.sim-reason');
   price.disabled = automatic; reason.disabled = automatic;
   price.value = mode === 'ManualFinal' ? tr.dataset.poPrice : '0'; reason.value = '';
  }
  $('simResult').classList.add('d-none');
 }
 $('simMode').addEventListener('change',()=>{
  const overrides=[...$('simRows').children].some(tr=>{
   const price=tr.querySelector('.sim-price'),reason=tr.querySelector('.sim-reason');
   const defaultPrice=lastMode==='ManualFinal'?tr.dataset.poPrice:'0';
   return reason.value.trim()||(!price.disabled&&price.value!==defaultPrice);
  });
  if(overrides&&draftGuard?.isDirty()&&!draftGuard.confirmDiscard()){
   $('simMode').value=lastMode;return;
  }
  lastMode=$('simMode').value;adjustMode();
 });
 function collectLines() {
  const priceMode = $('simMode').value, lines = [];
  for (const tr of $('simRows').children) {
   const [quantityInput] = tr.querySelectorAll('input');
   if (!quantityInput.value.trim()) continue;
   const quantity = Number(quantityInput.value);
   if (!Number.isFinite(quantity) || quantity < 0) throw new Error('Invalid invoice quantity.');
   if (quantity === 0) continue;
   const price = tr.querySelector('.sim-price'), reason = tr.querySelector('.sim-reason');
   const amount = priceMode === 'Automatic' ? 0 : Number(price.value);
   if (!Number.isFinite(amount) || (!price.disabled && !price.value.trim()))
    throw new Error('Valid final unit price or correction required.');
   lines.push({goodsReceiptReference:tr.dataset.reference,receiptLineNo:Number(tr.dataset.lineNo),
    quantity,amount,varianceReason:reason.disabled ? null : reason.value.trim() || null});
  }
  if (!lines.length) throw new Error('Enter a positive invoice quantity / ইনভয়েস পরিমাণ দিন');
  return {purchaseOrderId:selected.id,priceMode,lines};
 }
 $('simPreviewForm').addEventListener('submit', async e => {
  e.preventDefault(); if (pending || !selected) return;
  try {
   const command=collectLines(),scope=searchSeq;
   pending = true; draftForm.inert=true;$('simRun').disabled = true;
   const preview = await api('/api/procurement/supplier-invoices/preview','POST',command);
   if(scope!==searchSeq)return;
   $('simSummary').textContent = `${preview.orderNumber} · BDT ${currency(preview.proposedAmount)} · `+
    `Stock value difference BDT ${currency(preview.differenceFromStockValue)} · `+
    (preview.requiresIndependentFinanceReview ? 'Finance review needed' : 'No detected price variance') +
    ' · Preview only: no payable or invoice created';
   $('simResultLines').replaceChildren();
   for (const line of preview.lines) {
    const tr = el('tr');
    for (const value of [line.goodsReceiptReference.slice(0,8),line.inventoryItemId,line.quantity,
     line.proposedInvoiceUnitPrice,currency(line.proposedAmount),currency(line.differenceFromStockValue)])
     tr.append(el('td',value));
    $('simResultLines').append(tr);
   }
   $('simResult').classList.remove('d-none');
   status('Matching preflight completed; this is not an approved invoice or payable.',false);
  } catch (err) { $('simResult').classList.add('d-none'); status(err.message); }
  finally { pending = false;draftForm.inert=false;$('simRun').disabled = false; }
 });

 const base = '/api/procurement/supplier-invoices';
 const canMake = root.dataset.maker === 'true';
 const canReview = root.dataset.reviewer === 'true';
 const currentUser = Number(root.dataset.userId);
 let page = 1, pages = 1, listSeq = 0, detailSeq = 0, openInvoice = null;
 let saveKey = null, submitKey = null, reviewKey = null, reversalKey = null;
 let reviewSignature = null, reversalSignature = null;
 const detailDialog = $('simDetailDialog'), reviewDialog = $('simReviewDialog'), reversalDialog = $('simReversalDialog'),
  contraReviewDialog = $('simContraReviewDialog');
 const dialogEditors=[['simReviewDialog','simReviewForm'],
  ['simReversalDialog','simReversalForm'],['simContraReviewDialog','simContraReviewForm']];
 const guards={};
 for(const [dialogId,formId] of dialogEditors){const form=$(formId);
  if(form?.elements&&window.EduOSFormGuard)guards[formId]=window.EduOSFormGuard.create(form);
  $(dialogId)?.addEventListener('cancel',e=>{
   if(pending||guards[formId]?.isDirty()&&!guards[formId].confirmDiscard())e.preventDefault();
   else guards[formId]?.markClean();
  });
 }
 function close(dialog,force=false){
  const formId=dialogEditors.find(x=>$(x[0])===dialog)?.[1];
  if(!force&&(pending||guards[formId]?.isDirty()&&!guards[formId].confirmDiscard()))return false;
  guards[formId]?.markClean();if(dialog.open)dialog.close();return true;
 }
 function clearUnauthorized(){
  ++searchSeq;++detailSeq;selected=null;openInvoice=null;saveKey=null;submitKey=null;
  reviewKey=null;reversalKey=null;reviewSignature=null;reversalSignature=null;
  $('simOrderResults').replaceChildren();$('simRows').replaceChildren();
  draftForm.classList.add('d-none');$('simResult').classList.add('d-none');
  draftGuard?.markClean();close(detailDialog,true);
  for(const [dialogId] of dialogEditors)close($(dialogId),true);
 }
 function signatureKey(body,previous,key){const signature=JSON.stringify(body);
  return {signature,key:signature===previous?key:crypto.randomUUID()};}
 const action = (label, callback) => { const x = button(label,callback);
  x.className = 'btn btn-outline-primary btn-sm'; return x; };
 async function loadInvoices() {
  const seq = ++listSeq;
  try {
   const data = await api(base+params({page,pageSize:25,state:$('simState').value}));
   if (seq !== listSeq) return false;
   pages = data.totalPages || 1; $('simInvoiceRows').replaceChildren();
   for (const invoice of data.items || []) {
    const tr = el('tr');
    for (const value of [invoice.supplierInvoiceNumber,invoice.orderNumber,invoice.stateCode,
     currency(invoice.totalAmount),invoice.journalReference?.slice(0,8) || '—']) tr.append(el('td',value));
    const cell = el('td'); cell.append(action('Open / দেখুন',()=>detail(invoice.reference)));
    tr.append(cell); $('simInvoiceRows').append(tr);
   }
   if (!(data.items || []).length) { const tr = el('tr'),cell = el('td',
    'No supplier invoices / কোনো ইনভয়েস নেই'); cell.colSpan = 6; tr.append(cell); $('simInvoiceRows').append(tr); }
   $('simPage').textContent = `${page} / ${pages}`;
   $('simPrev').disabled = page<=1; $('simNext').disabled = page>=pages;
   return true;
  } catch (err) {
   if(seq===listSeq){
    $('simInvoiceRows').replaceChildren();$('simPage').textContent='—';
    $('simPrev').disabled=true;$('simNext').disabled=true;
    if([401,403].includes(err.statusCode))clearUnauthorized();
    status(err.message);
   }
   return false;
  }
 }
 $('simInvoiceFilters').addEventListener('submit', e=>{ e.preventDefault(); page=1;loadInvoices(); });
 $('simPrev').addEventListener('click',()=>{ if(page>1){page--;loadInvoices();} });
 $('simNext').addEventListener('click',()=>{ if(page<pages){page++;loadInvoices();} });
 $('simExport').addEventListener('click',()=>{
  window.location.assign(base+'/export'+params({state:$('simState').value}));
 });
 function invoiceDraft() {
  if (!selected) throw new Error('Choose an approved purchase order.');
  const invoiceNo = $('simInvoiceNo').value.trim(), invoiceDate = $('simInvoiceDate').value,
   dueDate = $('simDueDate').value, reason = $('simReason').value.trim();
  if (!invoiceNo || invoiceNo.length>100 || !invoiceDate || !dueDate || dueDate<invoiceDate ||
   reason.length<10 || reason.length>800)
   throw new Error('Supplier invoice number, valid dates and reason (10–800) required.');
  return {...collectLines(),clientRequestId:saveKey ||= crypto.randomUUID(),
   supplierInvoiceNumber:invoiceNo,invoiceDate,dueDate,reason};
 }
 if (canMake) $('simSave')?.addEventListener('click', async()=>{
  if (pending) return;
  pending=true;draftForm.inert=true;$('simSave').disabled=true;
  try {
   const saved = await api(base,'POST',invoiceDraft());
   saveKey = null;draftGuard?.markClean();
   await afterWrite(saved.reference,'Invoice draft saved. GL unchanged.');
  } catch (err) { status(err.message); }
  finally { pending=false;draftForm.inert=false;$('simSave').disabled=false; }
 });
 ['simMode','simInvoiceNo','simInvoiceDate','simDueDate','simReason'].forEach(id=>{
  for(const type of ['input','change'])$(id)?.addEventListener(type,()=>{saveKey=null;});
 });
 for(const type of ['input','change'])$('simRows').addEventListener(type,()=>{saveKey=null;});
 async function detail(reference) {
  const seq=++detailSeq;
  try {
   const loaded=await api(base+'/'+reference);
   if(seq!==detailSeq)return false;
   openInvoice=loaded;submitKey=null;reviewKey=null;reversalKey=null;
   const x = openInvoice;
   $('simDetailSummary').textContent = `${x.supplierInvoiceNumber} · ${x.orderNumber} · Campus ${x.campusId} · `+
    `${x.stateCode} · BDT ${currency(x.totalAmount)} · Stock BDT ${currency(x.stockValue)} · `+
    `Variance BDT ${currency(x.priceVariance)} · Maker ${x.createdByUserId ?? '—'} · `+
    `Finance ${x.reviewedByUserId ?? '—'} · ${x.reviewReason ?? x.reason}`;
   $('simDetailLines').replaceChildren();
   for (const line of x.lines || []) {
    const tr = el('tr'); for (const value of [line.inventoryItemId,line.quantity,line.poUnitPrice,
     line.stockUnitCost,line.invoiceUnitPrice,currency(line.invoiceAmount)]) tr.append(el('td',value));
    $('simDetailLines').append(tr);
   }
   const actions=$('simDetailActions'); actions.replaceChildren();
   const own = Number(x.createdByUserId)===currentUser;
   if (canMake && own && x.stateCode==='Draft') actions.append(action('Submit / জমা',submitInvoice));
   if (canReview && !own && x.stateCode==='Submitted') actions.append(action('Finance review / যাচাই',openReview));
   if (canMake && x.stateCode==='Posted' && !x.reversalJournalReference)
    actions.append(action('Request reversal / রিভার্স',openReversal));
   if (canReview && x.stateCode==='Posted' && x.reversalJournalReference && x.reversalJournalRowVersion && !own)
    actions.append(action('Review contra / রিভার্স যাচাই',()=>openContraReview()));
   if (x.stateCode==='Posted') actions.append(action('Settlement / পরিশোধ নির্দেশনা',()=>{
    close(detailDialog);window.dispatchEvent(new CustomEvent('procurement-settlement-select',{detail:{reference:x.reference,
     invoiceNumber:x.supplierInvoiceNumber,rowVersion:x.rowVersion}}));
    $('supplierSettlement')?.scrollIntoView({block:'start',behavior:'smooth'});
   }));
   if (x.stateCode==='Posted') {
    const link=el('a','Return credit / পণ্য ফেরত');link.className='btn btn-sm btn-outline-primary';
    link.href='/ProcurementSupplierReturns?invoice='+encodeURIComponent(x.reference);
    actions.append(link);
   }
   if (x.journalReference) actions.append(el('small','GL: '+x.journalReference));
   detailDialog.showModal();
   return true;
  } catch (err) {if(seq===detailSeq){openInvoice=null;status(err.message);}return false;}
 }
 async function afterWrite(reference,message){
  const listOk=await loadInvoices(),detailOk=await detail(reference);
  status(message+(!listOk||!detailOk?' Register refresh incomplete; reload to verify the saved state.':''),false);
 }
 $('simDetailClose').addEventListener('click',()=>{if(pending)return;++detailSeq;close(detailDialog);});
 detailDialog.addEventListener('cancel',e=>{if(pending)e.preventDefault();else ++detailSeq;});
 async function submitInvoice(){
  if (pending || !openInvoice) return;
  const source=openInvoice;submitKey ||= crypto.randomUUID();pending=true;
  try {
   const x = await api(base+'/'+source.reference+'/submit','POST',
    {clientRequestId:submitKey,rowVersion:source.rowVersion});
   submitKey=null;close(detailDialog,true);
   await afterWrite(x.reference,'Invoice submitted to independent Finance reviewer.');
  }catch(err){status(err.message);}finally{pending=false;}
 }
 async function loadOptions(){return api(base+'/options');}
 function setOptions(selectId, items){ const x=$(selectId); x.replaceChildren(new Option('Select / নির্বাচন করুন',''));
  for(const a of items)x.append(new Option(`${a.code||a.name} · ${a.name}${a.type?' ('+a.type+')':''}`,String(a.id)));
 }
 function updateDecision(){ $('simAccountPanel').classList.toggle('d-none',$('simDecision').value!=='Approve'); }
 $('simDecision').addEventListener('change',updateDecision);
 async function openReview(){
  if (!openInvoice || pending) return;
  const seq=detailSeq,source=openInvoice;
  try {
   const data=await loadOptions();
   if(seq!==detailSeq||openInvoice?.reference!==source.reference)return;
   setOptions('simPeriod',data.periods);
   setOptions('simAssetAccount',data.accounts.filter(x=>x.type==='Asset'));
   setOptions('simPayableAccount',data.accounts.filter(x=>x.type==='Liability'));
   setOptions('simVarianceAccount',data.accounts.filter(x=>x.type==='Expense'));
   $('simReviewForm').reset();updateDecision();reviewKey=null;reviewSignature=null;
   guards.simReviewForm?.markClean();reviewDialog.showModal();
  }catch(err){status(err.message);}
 }
 $('simReviewClose').addEventListener('click',()=>close(reviewDialog));
 $('simReviewCancel').addEventListener('click',()=>close(reviewDialog));
 $('simReviewForm').addEventListener('submit',async e=>{
  e.preventDefault();if(!openInvoice||pending)return;
  const source=openInvoice,decision=$('simDecision').value;
  pending=true;$('simReviewSubmit').disabled=true;$('simReviewForm').inert=true;
  try{
   const body={rowVersion:source.rowVersion,decision,
    reason:$('simReviewReason').value.trim(),accountingPeriodId:Number($('simPeriod').value)||0,
    inventoryAccountId:Number($('simAssetAccount').value)||0,
    payableAccountId:Number($('simPayableAccount').value)||0,
    priceVarianceAccountId:Number($('simVarianceAccount').value)||0};
   const key=signatureKey({reference:source.reference,...body},reviewSignature,reviewKey);
   reviewSignature=key.signature;reviewKey=key.key;
   const updated=await api(base+'/'+source.reference+'/review','POST',
    {clientRequestId:reviewKey,...body});
   reviewKey=null;reviewSignature=null;close(reviewDialog,true);close(detailDialog,true);
   await afterWrite(updated.reference,'Finance decision recorded; '+updated.stateCode);
  }catch(err){status(err.message);}finally{pending=false;$('simReviewSubmit').disabled=false;$('simReviewForm').inert=false;}
 });
 async function openReversal(){
  if(!openInvoice||pending)return;
  const seq=detailSeq,source=openInvoice;
  try{
   const data=await loadOptions();
   if(seq!==detailSeq||openInvoice?.reference!==source.reference)return;
   setOptions('simReversalPeriod',data.periods);
   $('simReversalForm').reset();$('simReversalDate').value=localDate();
   reversalKey=null;reversalSignature=null;guards.simReversalForm?.markClean();reversalDialog.showModal();
  }catch(err){status(err.message);}
 }
 $('simReversalClose').addEventListener('click',()=>close(reversalDialog));
 $('simReversalCancel').addEventListener('click',()=>close(reversalDialog));
 $('simReversalForm').addEventListener('submit',async e=>{
  e.preventDefault();if(!openInvoice||pending)return;
  const source=openInvoice;pending=true;$('simReversalSubmit').disabled=true;$('simReversalForm').inert=true;
  try{
   const body={rowVersion:source.rowVersion,
    accountingPeriodId:Number($('simReversalPeriod').value),reversalDate:$('simReversalDate').value,
    reason:$('simReversalReason').value.trim()};
   const key=signatureKey({reference:source.reference,...body},reversalSignature,reversalKey);
   reversalSignature=key.signature;reversalKey=key.key;
   const updated=await api(base+'/'+source.reference+'/reversal','POST',
    {clientRequestId:reversalKey,...body});
   reversalKey=null;reversalSignature=null;close(reversalDialog,true);close(detailDialog,true);
   await afterWrite(updated.reference,'Unposted payable contra submitted; reviewer required.');
  }catch(err){status(err.message);}finally{pending=false;$('simReversalSubmit').disabled=false;$('simReversalForm').inert=false;}
 });
 function openContraReview(){
  if(!openInvoice?.reversalJournalRowVersion||pending)return;
  $('simContraReviewForm').reset();guards.simContraReviewForm?.markClean();contraReviewDialog.showModal();
 }
 $('simContraReviewClose').addEventListener('click',()=>close(contraReviewDialog));
 $('simContraReviewCancel').addEventListener('click',()=>close(contraReviewDialog));
 $('simContraReviewForm').addEventListener('submit',async e=>{
  e.preventDefault();if(pending||!openInvoice?.reversalJournalRowVersion)return;
  const x=openInvoice,approve=$('simContraDecision').value==='Approve',
   reason=$('simContraReason').value.trim();
  if(reason.length<10||reason.length>800)return;
  pending=true;$('simContraReviewSubmit').disabled=true;$('simContraReviewForm').inert=true;
  try{
   const updated=await api(base+'/'+x.reference+'/reversal/review','POST',
    {rowVersion:x.reversalJournalRowVersion,approve,reason});
   close(contraReviewDialog,true);close(detailDialog,true);
   await afterWrite(updated.reference,approve?'Compensating GL reversal posted.':'Reversal rejected; payable unchanged.');
  }catch(err){status(err.message);}finally{pending=false;$('simContraReviewSubmit').disabled=false;$('simContraReviewForm').inert=false;}
 });
 loadInvoices();
})();
