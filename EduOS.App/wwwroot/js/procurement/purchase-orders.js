(() => {
'use strict';
const root = document.getElementById('poWorkspace'); if (!root) return;
const $ = id => document.getElementById(id);
const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
const en = {title:'Purchase orders',requisitions:'Requisitions',suppliers:'Suppliers',export:'CSV',new:'New order',
 help:'Create from an approved requisition and approved campus supplier. A separate checker must approve the order.',
 campus:'Campus ID',status:'Status',all:'All',search:'Search',filter:'Filter',number:'Order',supplier:'Supplier',
 total:'Total BDT',actions:'Actions',previous:'Previous',next:'Next',expected:'Expected delivery',
 requisition:'Approved requisition',find:'Find',close:'Close',create:'Create draft',details:'Order details',
 item:'Item ID',quantity:'Quantity',price:'Unit price',reason:'Reason',confirm:'Confirm',
 submit:'Submit',approve:'Approve',reject:'Reject',revise:'Revise',cancel:'Cancel',view:'View',
 empty:'No orders found.',failed:'Request failed.',saved:'Saved.',page:'Page',of:'of',selected:'Selected'};
const bd = {title:'ক্রয়াদেশ',requisitions:'ক্রয় চাহিদাপত্র',suppliers:'সরবরাহকারী',export:'CSV',new:'নতুন ক্রয়াদেশ',
 help:'অনুমোদিত চাহিদাপত্র ও ক্যাম্পাস সরবরাহকারী থেকে ক্রয়াদেশ তৈরি করুন। পৃথক অনুমোদনকারী প্রয়োজন।',
 campus:'ক্যাম্পাস',status:'অবস্থা',all:'সব',search:'খুঁজুন',filter:'ফিল্টার',number:'ক্রয়াদেশ',supplier:'সরবরাহকারী',
 total:'মোট টাকা',actions:'কার্যক্রম',previous:'আগের',next:'পরের',expected:'প্রত্যাশিত সরবরাহ',
 requisition:'অনুমোদিত চাহিদাপত্র',find:'খুঁজুন',close:'বন্ধ',create:'খসড়া তৈরি',details:'ক্রয়াদেশের বিবরণ',
 item:'পণ্য আইডি',quantity:'পরিমাণ',price:'একক মূল্য',reason:'কারণ',confirm:'নিশ্চিত',
 submit:'জমা',approve:'অনুমোদন',reject:'প্রত্যাখ্যান',revise:'সংশোধন',cancel:'বাতিল',view:'দেখুন',
 empty:'ক্রয়াদেশ পাওয়া যায়নি।',failed:'অনুরোধ ব্যর্থ।',saved:'সংরক্ষিত।',page:'পৃষ্ঠা',of:'এর',selected:'নির্বাচিত'};
en.commitments='Commitments';bd.commitments='ক্রয় প্রতিশ্রুতি';
en.edit='Amend draft order';en.amendReason='Amendment reason';en.save='Save amendment';
bd.edit='খসড়া ক্রয়াদেশ সংশোধন';bd.amendReason='সংশোধনের কারণ';bd.save='সংশোধন সংরক্ষণ';
Object.assign(en,{approvalQueue:'Approval queue',allOrders:'All orders',history:'Decision history',
 occurred:'Occurred',actor:'Actor ID',auditAction:'Action',transition:'Transition'});
Object.assign(bd,{approvalQueue:'অনুমোদনের অপেক্ষায়',allOrders:'সব ক্রয়াদেশ',history:'সিদ্ধান্তের ইতিহাস',
 occurred:'সময়',actor:'ব্যবহারকারী আইডি',auditAction:'কার্যক্রম',transition:'অবস্থার পরিবর্তন'});
const t = k => (bn ? bd : en)[k] || k;
root.querySelectorAll('[data-i18n]').forEach(x => { x.textContent = t(x.dataset.i18n); });
const base = '/api/procurement/purchase-orders', token = document.querySelector('meta[name="request-verification-token"]')?.content;
const maker = root.dataset.maker === 'true', checker = root.dataset.checker === 'true';
const userId = Number(root.dataset.userId);
let page = 1, totalPages = 1, epoch = 0, detailEpoch = 0, lookupEpoch = 0, historyEpoch = 0, historyPage = 1, historyPages = 1, historyId = null, queueOnly = false, busy = false, current = null, pendingAction = null;
const createDialog = $('poCreateDialog'), detailDialog = $('poDetailDialog'),
 decisionDialog = $('poDecisionDialog'), editDialog = $('poEditDialog'), historyDialog = $('poHistoryDialog');
function alert(message, success = false) { $('poAlert').className = 'alert alert-' + (success ? 'success' : 'danger'); $('poAlert').textContent = message; }
function params(obj) { return '?' + new URLSearchParams(Object.entries(obj).filter(([,v]) => v !== '' && v !== null && v !== undefined)).toString(); }
async function api(url, method = 'GET', body) {
 const headers = {Accept:'application/json'};
 if (body !== undefined) headers['Content-Type'] = 'application/json';
 if (method !== 'GET' && token) headers.RequestVerificationToken = token;
 const response = await fetch(url, {method, credentials:'same-origin', cache:'no-store', headers,
  ...(body === undefined ? {} : {body:JSON.stringify(body)})});
 const result = await response.json().catch(() => null);
 if (!response.ok || !result?.success) throw new Error(result?.message || t('failed'));
 return result.data;
}
function el(tag, value) { const x = document.createElement(tag); if (value !== undefined) x.textContent = String(value ?? ''); return x; }
function button(key, handler, css = 'btn-outline-primary') {
 const b = el('button', t(key)); b.type = 'button'; b.className = 'btn ' + css + ' btn-sm';
 b.addEventListener('click', handler); return b;
}
function filters() { return {campusId:$('poCampus').value, state:$('poState').value, search:$('poSearch').value.trim()}; }
async function load() {
 const seq = ++epoch; const f = filters();
 if (queueOnly) delete f.state;
 try {
  const data = await api(base + (queueOnly ? '/approvals' : '') + params({page, pageSize:25, ...f}));
  if (seq !== epoch) return;
  totalPages = data.totalPages || 1; $('poRows').replaceChildren();
  for (const row of data.items || []) {
   const tr = el('tr');
   for (const v of [row.orderNumber,row.campusId,row.supplierId,row.stateCode,Number(row.totalAmount).toFixed(2)]) tr.append(el('td',v));
   const action = el('td'); action.append(button('view', () => showDetail(row.id))); tr.append(action); $('poRows').append(tr);
  }
  if (!data.items?.length) { const tr=el('tr'),td=el('td',t('empty'));td.colSpan=6;tr.append(td);$('poRows').append(tr); }
  $('poPage').textContent = t('page') + ' ' + page + ' ' + t('of') + ' ' + totalPages;
  $('poPrev').disabled = page <= 1; $('poNext').disabled = page >= totalPages;
 } catch (e) { if (seq === epoch) alert(e.message); }
}
$('poFilters').addEventListener('submit', e => {e.preventDefault();page=1;load();});
if (checker) $('poQueueToggle')?.addEventListener('click', () => {
 queueOnly = !queueOnly; page = 1;
 $('poQueueToggle').setAttribute('aria-pressed', String(queueOnly));
 $('poQueueToggle').textContent = t(queueOnly ? 'allOrders' : 'approvalQueue');
 $('poState').disabled = queueOnly;
 $('poExport').disabled = queueOnly;
 load();
});
$('poPrev').addEventListener('click', () => {if(page>1){page--;load();}});
$('poNext').addEventListener('click', () => {if(page<totalPages){page++;load();}});
$('poHelp').addEventListener('click', () => {const open=$('poHelpText').classList.toggle('d-none')===false;$('poHelp').setAttribute('aria-expanded',String(open));});
$('poExport').addEventListener('click', () => {
 const url=base+'/export'+params(filters()); window.location.assign(url);
});
function close(dialog) {if(dialog.open) dialog.close();}
$('poCreateClose').addEventListener('click',()=>close(createDialog));
$('poCreateCancel').addEventListener('click',()=>close(createDialog));
$('poDetailClose').addEventListener('click',()=>close(detailDialog));
$('poHistoryClose').addEventListener('click',()=>close(historyDialog));
$('poHistoryPrev').addEventListener('click',()=>{if(historyPage>1){historyPage--;loadHistory();}});
$('poHistoryNext').addEventListener('click',()=>{if(historyPage<historyPages){historyPage++;loadHistory();}});
$('poDecisionCancel').addEventListener('click',()=>close(decisionDialog));
$('poEditClose').addEventListener('click',()=>close(editDialog));
$('poEditCancel').addEventListener('click',()=>close(editDialog));
if (maker) $('poNew')?.addEventListener('click', () => {
 $('poCreateForm').reset();$('poFormCampus').value=$('poCampus').value;
 $('poRequestId').value=crypto.randomUUID();$('poReqId').value='';$('poSupplierId').value='';
 $('poReqSelected').textContent='';$('poSupplierSelected').textContent='';
 $('poReqResults').replaceChildren();$('poSupplierResults').replaceChildren();createDialog.showModal();
});
async function lookup(kind) {
 const seq=++lookupEpoch, isReq=kind==='req';
 const campus=Number($('poFormCampus').value),term=$(isReq?'poReqSearch':'poSupplierSearch').value.trim();
 if (!Number.isSafeInteger(campus) || campus<=0) {alert(t('campus'));return;}
 const target=$(isReq?'poReqResults':'poSupplierResults');target.replaceChildren();
 try {
  const url=(isReq?'/api/procurement/requisitions':'/api/procurement/suppliers')+
   params({page:1,pageSize:25,campusId:campus,state:'Approved',search:term});
  const data=await api(url);if(seq!==lookupEpoch || !createDialog.open)return;
  for(const item of data.items || []) {
   const name=isReq?item.requestNumber+' · '+item.purpose:item.supplierCode+' · '+item.name;
   target.append(button('selected',()=> {
    $(isReq?'poReqId':'poSupplierId').value=item.id;
    $(isReq?'poReqSelected':'poSupplierSelected').textContent=name;
    target.replaceChildren();
   }));
   target.lastChild.textContent=name;
   target.lastChild.classList.add('d-block','w-100','text-start','mb-1');
  }
  if (!data.items?.length) target.append(el('div',t('empty')));
 } catch(e){if(seq===lookupEpoch)alert(e.message);}
}
$('poFindReq').addEventListener('click',()=>lookup('req'));
$('poFindSupplier').addEventListener('click',()=>lookup('supplier'));
$('poFormCampus').addEventListener('change',()=>{
 ++lookupEpoch;$('poReqId').value='';$('poSupplierId').value='';
 $('poReqSelected').textContent='';$('poSupplierSelected').textContent='';
 $('poReqResults').replaceChildren();$('poSupplierResults').replaceChildren();
});
$('poCreateForm').addEventListener('submit',async e=>{
 e.preventDefault();if(busy)return;
 const requisitionId=Number($('poReqId').value),supplierId=Number($('poSupplierId').value);
 if(!requisitionId||!supplierId){alert(t('requisition')+' / '+t('supplier'));return;}
 const date=$('poExpected').value;
 const request={clientRequestId:$('poRequestId').value,requisitionId,supplierId,
  expectedDeliveryDate:date?date+'T00:00:00':null};
 busy=true;$('poCreateSave').disabled=true;
 try{await api(base,'POST',request);close(createDialog);alert(t('saved'),true);page=1;await load();}
 catch(err){alert(err.message);}
 finally{busy=false;$('poCreateSave').disabled=false;}
});
async function showDetail(id) {
 const seq=++detailEpoch;
 try {
  const row=await api(base+'/'+id);if(seq!==detailEpoch)return;
  current=row;$('poDetailSummary').replaceChildren();
  const summary=el('p',row.orderNumber+' · '+row.stateCode+' · '+t('total')+': '+Number(row.totalAmount).toFixed(2));
  $('poDetailSummary').append(summary);$('poDetailLines').replaceChildren();
  for(const line of row.lines||[]) {
   const tr=el('tr');
   for(const v of [line.inventoryItemId,line.quantity,line.unitPrice])tr.append(el('td',v));
   $('poDetailLines').append(tr);
  }
  const actions=$('poDetailActions');actions.replaceChildren();
  actions.append(button('history',()=>showHistory(row.id)));
  const mine=row.createdBy===userId;
  if(maker&&mine&&row.stateCode==='Draft'){
    actions.append(button('edit',openEdit));
    actions.append(button('submit',()=>decide('submit')));
   }
  if(checker&&!mine&&row.stateCode==='Submitted'){
   actions.append(button('approve',()=>decide('approve'),'btn-primary'));
   actions.append(button('reject',()=>decide('reject')));
  }
  if(maker&&mine&&row.stateCode==='Rejected')actions.append(button('revise',()=>decide('revise')));
  if(maker&&mine&&['Draft','Submitted','Rejected'].includes(row.stateCode))actions.append(button('cancel',()=>decide('cancel')));
  detailDialog.showModal();
 }catch(e){if(seq===detailEpoch)alert(e.message);}
}

async function loadHistory() {
 const seq = ++historyEpoch, id = historyId;
 if (!id) return;
 try {
  const data = await api(base + '/' + id + '/history' + params({page:historyPage,pageSize:20}));
  if (seq !== historyEpoch || id !== historyId || !historyDialog.open) return;
  historyPages = data.totalPages || 1;
  $('poHistoryRows').replaceChildren();
  for (const entry of data.items || []) {
   const tr = el('tr');
   const transition = [entry.previousState || '—', entry.state || '—'].join(' → ');
   for (const value of [new Date(entry.occurredAt).toLocaleString(bn?'bn-BD':'en-BD'),
       entry.actorUserId ?? '—',entry.action,transition,entry.reason || '—']) tr.append(el('td',value));
   $('poHistoryRows').append(tr);
  }
  if (!data.items?.length) {
   const tr = el('tr'),td = el('td',t('empty'));td.colSpan=5;tr.append(td);$('poHistoryRows').append(tr);
  }
  $('poHistoryPage').textContent = t('page')+' '+historyPage+' '+t('of')+' '+historyPages;
  $('poHistoryPrev').disabled = historyPage <= 1;
  $('poHistoryNext').disabled = historyPage >= historyPages;
 } catch(err) { if (seq === historyEpoch) alert(err.message); }
}
function showHistory(id) {
 close(detailDialog); historyId=id;historyPage=1;historyPages=1;
 $('poHistoryRows').replaceChildren();historyDialog.showModal();loadHistory();
}
function openEdit() {
 if(!current||current.stateCode!=='Draft')return;
 $('poEditExpected').value=current.expectedDeliveryDate?.slice(0,10)||'';
 $('poEditReason').value='';$('poEditLines').replaceChildren();
 for(const line of current.lines||[]) {
  const tr=el('tr');tr.dataset.requisitionLineId=String(line.requisitionLineId);
  tr.append(el('td',line.inventoryItemId));
  for(const [field,value,step] of [['quantity',line.quantity,'0.000001'],['unitPrice',line.unitPrice,'0.0001']]) {
   const td=el('td'),input=el('input');
   input.type='number';input.min=field==='quantity'?'0.000001':'0';
   input.max='999999999';input.step=step;input.required=true;input.value=String(value);
   input.dataset.field=field;input.className='form-control form-control-sm';
   input.setAttribute('aria-label',t(field)+' '+line.inventoryItemId);
   td.append(input);tr.append(td);
  }
  $('poEditLines').append(tr);
 }
 close(detailDialog);editDialog.showModal();
}
$('poEditForm').addEventListener('submit',async e=>{
 e.preventDefault();if(busy||!current)return;
 const reason=$('poEditReason').value.trim();
 if(reason.length<10||reason.length>500){$('poEditReason').focus();return;}
 const lines=Array.from($('poEditLines').children).map(tr=>({
  requisitionLineId:Number(tr.dataset.requisitionLineId),
  quantity:Number(tr.querySelector('[data-field="quantity"]').value),
  unitPrice:Number(tr.querySelector('[data-field="unitPrice"]').value)
 }));
 const date=$('poEditExpected').value;
 busy=true;$('poEditSave').disabled=true;
 try{
  await api(base+'/'+current.id,'PUT',{rowVersion:current.rowVersion,reason,lines,
   expectedDeliveryDate:date?date+'T00:00:00':null});
  close(editDialog);current=null;alert(t('saved'),true);await load();
 }catch(err){alert(err.message);}
 finally{busy=false;$('poEditSave').disabled=false;}
});
function decide(action) {
 pendingAction=action;$('poDecisionTitle').textContent=t(action);$('poReason').value='';
 close(detailDialog);decisionDialog.showModal();
}
$('poDecisionForm').addEventListener('submit',async e=>{
 e.preventDefault();if(busy||!current||!pendingAction)return;
 const reason=$('poReason').value.trim();
 if(['reject','cancel'].includes(pendingAction)&&!reason){$('poReason').focus();return;}
 busy=true;$('poDecisionConfirm').disabled=true;
 try {
  await api(base+'/'+current.id+'/'+pendingAction,'POST',{rowVersion:current.rowVersion,reason});
  close(decisionDialog);current=null;alert(t('saved'),true);await load();
 }catch(err){alert(err.message);}
 finally{busy=false;$('poDecisionConfirm').disabled=false;}
});
window.addEventListener('pagehide',()=>{++epoch;++detailEpoch;++lookupEpoch;++historyEpoch;historyId=null;current=null;pendingAction=null;
 for(const dialog of [createDialog,detailDialog,decisionDialog,editDialog,historyDialog])close(dialog);
 $('poRows').replaceChildren();$('poHistoryRows').replaceChildren();$('poDetailLines').replaceChildren();$('poEditLines').replaceChildren();$('poReqResults').replaceChildren();$('poSupplierResults').replaceChildren();
});
load();
})();
