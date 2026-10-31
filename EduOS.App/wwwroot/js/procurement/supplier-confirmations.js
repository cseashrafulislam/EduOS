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
function alert(message, ok = false) {
 const x = $('ackAlert'); x.className = 'alert alert-' + (ok ? 'success' : 'danger');
 x.textContent = message;
}
function params(obj) { return '?' + new URLSearchParams(Object.entries(obj)
 .filter(([,v]) => v !== '' && v !== undefined && v !== null)).toString(); }
async function api(url, method = 'GET', body) {
 const headers = {Accept:'application/json'};
 if (body !== undefined) headers['Content-Type'] = 'application/json';
 if (method !== 'GET' && token) headers.RequestVerificationToken = token;
 const res = await fetch(url, {method, headers, credentials:'same-origin', cache:'no-store',
  ...(body === undefined ? {} : {body:JSON.stringify(body)})});
 const data = await res.json().catch(() => null);
 if (!res.ok || !data?.success) throw new Error(data?.message || t('failed'));
 return data.data;
}
function el(tag, value) { const x=document.createElement(tag); if(value!==undefined)x.textContent=String(value??''); return x; }
function button(label, fn, css='btn-outline-primary') {
 const x=el('button',t(label));x.type='button';x.className='btn btn-sm '+css;x.addEventListener('click',fn);return x;
}
function filters() { return {campusId:$('ackCampus').value,state:$('ackState').value}; }
async function load() {
 const seq=++epoch;
 try {
  const data=await api(base+params({page,pageSize:25,...filters()}));
  if(seq!==epoch)return;
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
 } catch(e){if(seq===epoch)alert(e.message);}
}
function close(x){if(x.open)x.close();}
$('ackHelp').addEventListener('click',()=>{const open=!$('ackHelpText').classList.toggle('d-none');$('ackHelp').setAttribute('aria-expanded',String(open));});
$('ackFilters').addEventListener('submit',e=>{e.preventDefault();page=1;load();});
$('ackPrev').addEventListener('click',()=>{if(page>1){page--;load();}});
$('ackNext').addEventListener('click',()=>{if(page<totalPages){page++;load();}});
$('ackExport').addEventListener('click',()=>window.location.assign(base+'/export'+params(filters())));
$('ackPrint').addEventListener('click',()=>window.print());
$('ackClose').addEventListener('click',()=>close(dialog));
$('ackCancel').addEventListener('click',()=>close(dialog));
$('ackDecisionClose').addEventListener('click',()=>close(decisionDialog));
if(maker) $('ackNew')?.addEventListener('click',()=>{
 $('ackForm').reset();$('ackId').value='';$('ackVersion').value='';
 $('ackRequest').value=crypto.randomUUID();$('ackOrderId').value='';
 $('ackOrderSelected').textContent='';$('ackOrderResults').replaceChildren();
 $('ackOrderSearch').disabled=false;$('ackFindOrder').disabled=false;
 dialog.showModal();
});
$('ackFindOrder').addEventListener('click',async()=>{
 const seq=++lookupEpoch, term=$('ackOrderSearch').value.trim();
 $('ackOrderResults').replaceChildren();
 try{
  const data=await api('/api/procurement/purchase-orders'+params({
   page:1,pageSize:25,state:'Approved',search:term,campusId:$('ackCampus').value}));
  if(seq!==lookupEpoch||!dialog.open)return;
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
 try{
  const row=await api(base+'/'+id);
  $('ackForm').reset();$('ackId').value=row.id;$('ackVersion').value=row.rowVersion;
  $('ackRequest').value=row.clientRequestId;$('ackOrderId').value=row.purchaseOrderId;
  $('ackOrderSelected').textContent=row.orderNumber+' · '+row.supplierId;
  $('ackOrderSearch').disabled=true;$('ackFindOrder').disabled=true;
  $('ackOrderResults').replaceChildren();
  $('ackReference').value=row.supplierReference;
  $('ackResponseDate').value=String(row.responseDate).slice(0,10);
  $('ackPromisedDate').value=String(row.promisedDeliveryDate).slice(0,10);
  dialog.showModal();
 }catch(e){alert(e.message);}
}
$('ackForm').addEventListener('submit',async e=>{
 e.preventDefault();if(busy)return;
 const id=$('ackId').value;
 const orderId=Number($('ackOrderId').value);
 if(!Number.isSafeInteger(orderId)||orderId<=0){alert(t('order'));return;}
 const body={clientRequestId:$('ackRequest').value,purchaseOrderId:orderId,
  supplierReference:$('ackReference').value.trim(),responseDate:$('ackResponseDate').value,
  promisedDeliveryDate:$('ackPromisedDate').value,rowVersion:$('ackVersion').value};
 if(!body.responseDate||!body.promisedDeliveryDate||body.promisedDeliveryDate<body.responseDate){
  alert(t('promised'));return;
 }
 busy=true;$('ackSave').disabled=true;
 try{await api(base+(id?'/'+id:''),id?'PUT':'POST',body);close(dialog);alert(t('saved'),true);await load();}
 catch(err){alert(err.message);}finally{busy=false;$('ackSave').disabled=false;}
});
function decide(row,action){
 selected=row;currentAction=action;$('ackReason').value='';
 $('ackReason').required=action==='reject';
 $('ackConfirm').textContent=t(action);decisionDialog.showModal();
}
$('ackDecisionForm').addEventListener('submit',async e=>{
 e.preventDefault();if(busy||!selected||!currentAction)return;
 const reason=$('ackReason').value.trim();
 if(currentAction==='reject'&&reason.length<10){alert(t('reason'));return;}
 busy=true;$('ackConfirm').disabled=true;
 try{
  await api(base+'/'+selected.id+'/'+currentAction,'POST',{rowVersion:selected.rowVersion,reason});
  close(decisionDialog);selected=null;currentAction=null;alert(t('saved'),true);await load();
 }catch(err){alert(err.message);}finally{busy=false;$('ackConfirm').disabled=false;}
});
window.addEventListener('pagehide',()=>{epoch++;lookupEpoch++;selected=null;currentAction=null;});
load();
})();
