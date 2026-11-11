(() => {
'use strict';
const root = document.getElementById('ackWorkspace'); if (!root) return;
const $ = id => document.getElementById(id);
const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
const en = {title:'Supplier confirmations',orders:'Purchase orders',print:'Print',export:'CSV',
 new:'Record response',help:'Record supplier response to an approved purchase order. A different checker verifies the evidence. No stock, GL or payment is posted.',
 campus:'Campus ID',state:'State',all:'All',filter:'Filter',order:'Order',supplier:'Supplier',
 reference:'Supplier reference',response:'Response date',promised:'Promised delivery',
 actions:'Actions',previous:'Previous',next:'Next',details:'Supplier response',find:'Find',
 close:'Close',save:'Save draft',decision:'Decision',reason:'Reason',confirm:'Confirm',
 submit:'Submit',approve:'Confirm response',reject:'Reject evidence',revise:'Revise',
 edit:'Edit',page:'Page',of:'of',failed:'Request failed.',saved:'Saved.',
 empty:'No supplier confirmations found.',select:'Select'};
const bd = {title:'সরবরাহকারীর নিশ্চিতকরণ',orders:'ক্রয়াদেশ',print:'প্রিন্ট',export:'CSV',
 new:'সরবরাহকারীর জবাব',help:'অনুমোদিত ক্রয়াদেশে সরবরাহকারীর জবাব নথিভুক্ত করুন। আলাদা যাচাইকারী প্রমাণ অনুমোদন করবেন। স্টক, হিসাব বা পেমেন্ট পোস্ট হয় না।',
 campus:'ক্যাম্পাস আইডি',state:'অবস্থা',all:'সব',filter:'ফিল্টার',order:'ক্রয়াদেশ',supplier:'সরবরাহকারী',
 reference:'সরবরাহকারীর রেফারেন্স',response:'জবাবের তারিখ',promised:'সরবরাহের প্রতিশ্রুত তারিখ',
 actions:'কার্যক্রম',previous:'আগের',next:'পরের',details:'সরবরাহকারীর জবাব',find:'খুঁজুন',
 close:'বন্ধ',save:'খসড়া সংরক্ষণ',decision:'সিদ্ধান্ত',reason:'কারণ',confirm:'নিশ্চিত',
 submit:'জমা',approve:'জবাব অনুমোদন',reject:'প্রমাণ প্রত্যাখ্যান',revise:'সংশোধন',
 edit:'সম্পাদনা',page:'পৃষ্ঠা',of:'এর',failed:'অনুরোধ ব্যর্থ।',saved:'সংরক্ষিত।',
 empty:'সরবরাহকারীর নিশ্চিতকরণ পাওয়া যায়নি।',select:'নির্বাচন'};
const t = key => (bn ? bd : en)[key] || key;
root.querySelectorAll('[data-i18n]').forEach(x => { x.textContent = t(x.dataset.i18n); });
const base = '/api/procurement/supplier-confirmations';
const token = document.querySelector('meta[name="request-verification-token"]')?.content;
const maker = root.dataset.maker === 'true', checker = root.dataset.checker === 'true';
const userId = root.dataset.userId;
const dialog = $('ackDialog'), decisionDialog = $('ackDecisionDialog');
let page = 1, totalPages = 1, epoch = 0, lookupEpoch = 0, busy = false, currentAction = null, selected = null;
let editEpoch = 0, createFingerprint = null;
const context = [document.body?.dataset.authTenantId || '', document.body?.dataset.authSessionReference || ''];
const live = () => context[0] === (document.body?.dataset.authTenantId || '') &&
 context[1] === (document.body?.dataset.authSessionReference || '') &&
 window.EduOSSessionTransitionPending?.() !== true;
const guard = window.EduOSFormGuard?.create($('ackForm'));
function alert(message, ok = false) {
 const x = $('ackAlert'); x.className = 'alert alert-' + (ok ? 'success' : 'danger');
 x.textContent = message;
}
function params(obj) { return '?' + new URLSearchParams(Object.entries(obj)
 .filter(([,v]) => v !== '' && v !== undefined && v !== null)).toString(); }
async function api(url, method = 'GET', body) {
 if(!live())throw new Error('Institution session changed. Reload the page.');
 const headers = {Accept:'application/json'};
 if (body !== undefined) headers['Content-Type'] = 'application/json';
 if (method !== 'GET') {
  if(!token)throw new Error('Security token missing. Reload the page.');
  headers.RequestVerificationToken = token;
 }
 const res = await fetch(url, {method, headers, credentials:'same-origin', cache:'no-store',
  ...(body === undefined ? {} : {body:JSON.stringify(body)})});
 const data = await res.json().catch(() => null);
 if(!live())throw new Error('Institution session changed. Reload the page.');
 if (!res.ok || !data?.success) throw new Error(data?.message || t('failed'));
 return data.data;
}
function el(tag, value) { const x=document.createElement(tag); if(value!==undefined)x.textContent=String(value??''); return x; }
function button(label, fn, css='btn-outline-primary') {
 const x=el('button',t(label));x.type='button';x.className='btn btn-sm '+css;x.addEventListener('click',fn);return x;
}
function filters() { return {campusId:$('ackCampus').value,state:$('ackState').value}; }
async function load(propagateError=false) {
 const seq=++epoch;
 try {
  const data=await api(base+params({page,pageSize:25,...filters()}));
  if(seq!==epoch||!live())return;
  totalPages=data.totalPages||1;
  $('ackRows').replaceChildren();
  for(const row of data.items||[]) {
   const tr=el('tr');
   for(const value of [row.orderNumber,row.supplierId,row.supplierReference,
    String(row.responseDate||'').slice(0,10),String(row.promisedDeliveryDate||'').slice(0,10),
    row.stateCode]) tr.append(el('td',value));
   const td=el('td');td.className='ackNoPrint';
   const owner=String(row.createdBy)===userId;
   if(maker&&owner&&row.stateCode==='Draft') {
    td.append(button('edit',()=>edit(row.id)));
    td.append(button('submit',()=>decide(row,'submit')));
   }
   if(maker&&owner&&row.stateCode==='Rejected') td.append(button('revise',()=>decide(row,'revise')));
   if(checker&&!owner&&row.stateCode==='Submitted') {
    td.append(button('approve',()=>decide(row,'approve'),'btn-outline-success'));
    td.append(button('reject',()=>decide(row,'reject'),'btn-outline-danger'));
   }
   tr.append(td);$('ackRows').append(tr);
  }
  if(!data.items?.length){const tr=el('tr'),td=el('td',t('empty'));td.colSpan=7;tr.append(td);$('ackRows').append(tr);}
  $('ackPage').textContent=t('page')+' '+page+' '+t('of')+' '+totalPages;
  $('ackPrev').disabled=page<=1;$('ackNext').disabled=page>=totalPages;
 } catch(e){if(seq===epoch)alert(e.message);if(propagateError)throw e;}
}
function close(x,force=false){
 if(!force&&busy)return;
 if(x===dialog&&!force&&guard?.confirmDiscard()===false)return;
 if(x.open)x.close();
 if(x===dialog){++lookupEpoch;++editEpoch;guard?.markClean();}
}
for(const x of [dialog,decisionDialog])x.addEventListener('cancel',e=>{
 if(busy||(x===dialog&&guard?.confirmDiscard()===false))e.preventDefault();
 else if(x===dialog){++lookupEpoch;++editEpoch;guard?.markClean();}
});
$('ackHelp').addEventListener('click',()=>{const open=!$('ackHelpText').classList.toggle('d-none');$('ackHelp').setAttribute('aria-expanded',String(open));});
$('ackFilters').addEventListener('submit',e=>{e.preventDefault();page=1;load();});
$('ackPrev').addEventListener('click',()=>{if(page>1){page--;load();}});
$('ackNext').addEventListener('click',()=>{if(page<totalPages){page++;load();}});
$('ackExport').addEventListener('click',()=>{if(live())window.location.assign(base+'/export'+params(filters()));});
$('ackPrint').addEventListener('click',()=>{if(live())window.print();});
$('ackClose').addEventListener('click',()=>close(dialog));
$('ackCancel').addEventListener('click',()=>close(dialog));
$('ackDecisionClose').addEventListener('click',()=>close(decisionDialog));
if(maker) $('ackNew')?.addEventListener('click',()=>{
 if(busy||!live()||(dialog.open&&guard?.confirmDiscard()===false))return;
 ++lookupEpoch;++editEpoch;
 $('ackForm').reset();$('ackId').value='';$('ackVersion').value='';
 createFingerprint=null;
 $('ackRequest').value=crypto.randomUUID();$('ackOrderId').value='';
 $('ackOrderSelected').textContent='';$('ackOrderResults').replaceChildren();
 $('ackOrderSearch').disabled=false;$('ackFindOrder').disabled=false;
 guard?.markClean();
 dialog.showModal();
});
$('ackFindOrder').addEventListener('click',async()=>{
 if(busy||!live()||!dialog.open)return;
 const seq=++lookupEpoch, term=$('ackOrderSearch').value.trim();
 $('ackOrderResults').replaceChildren();
 try{
  const data=await api('/api/procurement/purchase-orders'+params({
   page:1,pageSize:25,state:'Approved',search:term,campusId:$('ackCampus').value}));
  if(seq!==lookupEpoch||!dialog.open||!live())return;
  for(const order of data.items||[]){
   const b=button('select',()=>{
    $('ackOrderId').value=String(order.id);
    $('ackOrderSelected').textContent=order.orderNumber+' · '+order.supplierId;
    $('ackOrderResults').replaceChildren();
   });
   b.textContent=order.orderNumber+' · '+order.supplierId;
   b.className='list-group-item list-group-item-action';
   $('ackOrderResults').append(b);
  }
 }catch(e){if(seq===lookupEpoch)alert(e.message);}
});
async function edit(id){
 if(busy||!live()||(dialog.open&&guard?.confirmDiscard()===false))return;
 const seq=++editEpoch;
 try{
  const row=await api(base+'/'+id);
  if(seq!==editEpoch||!live())return;
  $('ackForm').reset();$('ackId').value=row.id;$('ackVersion').value=row.rowVersion;
  createFingerprint=null;
  $('ackRequest').value=row.clientRequestId;$('ackOrderId').value=row.purchaseOrderId;
  $('ackOrderSelected').textContent=row.orderNumber+' · '+row.supplierId;
  $('ackOrderSearch').disabled=true;$('ackFindOrder').disabled=true;
  $('ackOrderResults').replaceChildren();
  $('ackReference').value=row.supplierReference;
  $('ackResponseDate').value=String(row.responseDate).slice(0,10);
  $('ackPromisedDate').value=String(row.promisedDeliveryDate).slice(0,10);
  guard?.markClean();
  dialog.showModal();
 }catch(e){alert(e.message);}
}
$('ackForm').addEventListener('submit',async e=>{
 e.preventDefault();if(busy||!live())return;
 const id=$('ackId').value;
 const orderId=Number($('ackOrderId').value);
 if(!Number.isSafeInteger(orderId)||orderId<=0){alert(t('order'));return;}
 const body={clientRequestId:$('ackRequest').value,purchaseOrderId:orderId,
  supplierReference:$('ackReference').value.trim(),responseDate:$('ackResponseDate').value,
  promisedDeliveryDate:$('ackPromisedDate').value,rowVersion:$('ackVersion').value};
 if(!body.responseDate||!body.promisedDeliveryDate||body.promisedDeliveryDate<body.responseDate){
  alert(t('promised'));return;
 }
 if(!id){
  const fingerprint=JSON.stringify({...body,clientRequestId:null});
  if(fingerprint!==createFingerprint){
   createFingerprint=fingerprint;$('ackRequest').value=crypto.randomUUID();
   body.clientRequestId=$('ackRequest').value;
  }
 }
 busy=true;$('ackSave').disabled=true;
 try{
  await api(base+(id?'/'+id:''),id?'PUT':'POST',body);
  guard?.markClean();close(dialog,true);createFingerprint=null;alert(t('saved'),true);
  try{await load(true);}catch(error){alert(t('saved')+' Refresh list: '+error.message,true);}
 }
 catch(err){alert(err.message);}finally{busy=false;$('ackSave').disabled=false;}
});
function decide(row,action){
 if(busy||!live())return;
 selected=row;currentAction=action;$('ackReason').value='';
 $('ackReason').required=action==='reject';
 $('ackConfirm').textContent=t(action);decisionDialog.showModal();
}
$('ackDecisionForm').addEventListener('submit',async e=>{
 e.preventDefault();if(busy||!live()||!selected||!currentAction)return;
 const reason=$('ackReason').value.trim();
 if(currentAction==='reject'&&reason.length<10){alert(t('reason'));return;}
 busy=true;$('ackConfirm').disabled=true;
 try{
  const row=selected,action=currentAction;
  await api(base+'/'+row.id+'/'+action,'POST',{rowVersion:row.rowVersion,reason});
  close(decisionDialog,true);selected=null;currentAction=null;alert(t('saved'),true);
  try{await load(true);}catch(error){alert(t('saved')+' Refresh list: '+error.message,true);}
 }catch(err){alert(err.message);}finally{busy=false;$('ackConfirm').disabled=false;}
});
window.addEventListener('pagehide',()=>{epoch++;lookupEpoch++;selected=null;currentAction=null;});
const observer=new MutationObserver(()=>{
 if(live())return;
 ++epoch;++lookupEpoch;++editEpoch;selected=null;currentAction=null;createFingerprint=null;
 $('ackRows').replaceChildren();$('ackOrderResults').replaceChildren();
 $('ackForm').reset();$('ackDecisionForm').reset();
 guard?.markClean();close(dialog,true);close(decisionDialog,true);
 alert('Institution session changed. Reload the page.');observer.disconnect();
});
observer.observe(document.body,{attributes:true,
 attributeFilter:['data-auth-tenant-id','data-auth-session-reference']});
load();
})();
