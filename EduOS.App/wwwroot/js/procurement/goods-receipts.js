(() => {
 'use strict';
 const root = document.getElementById('goodsWorkspace'); if (!root) return;
 const $ = id => document.getElementById(id);
 const maker = root.dataset.maker === 'true', checker = root.dataset.checker === 'true';
 const userId = Number(root.dataset.userId);
 const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
 if (bn) root.querySelectorAll('[data-bn]').forEach(x => { x.textContent = x.dataset.bn; });
 const base = '/api/procurement/goods-receipts';
 const token = document.querySelector('meta[name="request-verification-token"]')?.content;
 const params = obj => '?' + new URLSearchParams(Object.entries(obj).filter(([, v]) => v !== '' && v !== undefined && v !== null)).toString();
 const el = (tag, value) => { const x = document.createElement(tag); if (value !== undefined) x.textContent = String(value ?? ''); return x; };
 const num = value => Number(value).toLocaleString(bn ? 'bn-BD' : 'en-US', {maximumFractionDigits:6});
 const close = dialog => { if (dialog.open) dialog.close(); };
 const btn = (label, handler, css = 'btn-outline-primary') => {
  const x = el('button', label); x.type = 'button'; x.className = 'btn btn-sm ' + css;
  x.addEventListener('click', handler); return x;
 };
 async function api(url, method = 'GET', body) {
  const headers = {Accept:'application/json'};
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (method !== 'GET' && token) headers.RequestVerificationToken = token;
  const response = await fetch(url, {method, headers, credentials:'same-origin', cache:'no-store',
   ...(body === undefined ? {} : {body:JSON.stringify(body)})});
  const data = await response.json().catch(() => null);
  if (!response.ok || !data?.success) throw new Error(data?.message || 'Request failed / অনুরোধ ব্যর্থ');
  return data.data;
 }
 function alert(message, success = false) {
  $('grAlert').className = 'alert alert-' + (success ? 'success' : 'danger');
  $('grAlert').textContent = String(message);
 }
 let page = 1, totalPages = 1, loadSeq = 0, searchSeq = 0, pending = false;
 let chosenPo = null, receipt = null, createRequestId = null, submitRequestId = null, reviewRequestId = null;
 const createDialog = $('grCreateDialog'), detailDialog = $('grDetailDialog'), reviewDialog = $('grReviewDialog');
 async function load() {
  const seq = ++loadSeq;
  try {
   const data = await api(base + params({page, pageSize:25, state:$('grState').value}));
   if (seq !== loadSeq) return;
   totalPages = data.totalPages || 1;
   $('grRows').replaceChildren();
   for (const item of data.items || []) {
    const tr = el('tr');
    for (const value of [item.orderNumber, item.reference.slice(0, 12), item.locationId, item.stateCode,
     item.submittedAtUtc ? new Date(item.submittedAtUtc).toLocaleString() : '—']) tr.append(el('td', value));
    const action = el('td'); action.append(btn('View / দেখুন', () => showDetail(item.reference))); tr.append(action);
    $('grRows').append(tr);
   }
   if (!data.items?.length) { const tr = el('tr'), td = el('td', 'No deliveries / কোনো সরবরাহ নেই');
    td.colSpan = 6; tr.append(td); $('grRows').append(tr); }
   $('grPage').textContent = `${page} / ${totalPages}`;
   $('grPrev').disabled = page <= 1; $('grNext').disabled = page >= totalPages;
  } catch (e) { if (seq === loadSeq) alert(e.message); }
 }
 $('grFilters').addEventListener('submit', e => { e.preventDefault(); page = 1; load(); });
 $('grPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
 $('grNext').addEventListener('click', () => { if (page < totalPages) { page++; load(); } });
 $('grExport').addEventListener('click', () => { window.location.assign(base + '/export' + params({state:$('grState').value})); });
 for (const [id, dialog] of [['grCreateClose',createDialog],['grCreateCancel',createDialog],
  ['grDetailClose',detailDialog],['grReviewClose',reviewDialog],['grReviewCancel',reviewDialog]])
  $(id).addEventListener('click', () => close(dialog));

 if (maker) $('grNew')?.addEventListener('click', () => {
  $('grCreateForm').reset(); $('grPoSelected').textContent = ''; $('grPoResults').replaceChildren();
  $('grCreateLines').replaceChildren(); $('grLocation').replaceChildren(new Option('Select approved order first',''));
  chosenPo = null; createRequestId = crypto.randomUUID(); createDialog.showModal();
 });
 async function searchOrders() {
  const seq = ++searchSeq;
  const list = $('grPoResults'); list.replaceChildren();
  try {
   const data = await api('/api/procurement/purchase-orders' + params({page:1,pageSize:50,
    state:'Approved',search:$('grPoSearch').value.trim()}));
   if (seq !== searchSeq || !createDialog.open) return;
   for (const item of data.items || []) {
    const label = `${item.orderNumber} · Campus ${item.campusId} · ${num(item.totalAmount)} ${item.currencyCode}`;
    const x = btn(label, () => chooseOrder(item), 'btn-outline-secondary');
    x.classList.add('w-100','text-start','mb-1'); list.append(x);
   }
   if (!data.items?.length) list.append(el('p','No approved orders found.'));
  } catch (e) { if (seq === searchSeq) alert(e.message); }
 }
 $('grFindPo').addEventListener('click', searchOrders);
 $('grPoSearch').addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); searchOrders(); } });
 async function chooseOrder(item) {
  const seq = ++searchSeq;
  try {
   const po = await api('/api/procurement/purchase-orders/' + item.id);
   const data = await api('/api/inventory/catalog/locations' + params({page:1,pageSize:100,campusId:po.campusId}));
   if (seq !== searchSeq || !createDialog.open) return;
   if (po.stateCode !== 'Approved') throw new Error('Selected PO is no longer approved.');
   chosenPo = po; $('grPoSelected').textContent = `${po.orderNumber} · Campus ${po.campusId}`;
   $('grPoResults').replaceChildren(); $('grLocation').replaceChildren(new Option('Select a campus store',''));
   for (const store of data.items || []) if (store.isActive && Number(store.campusId) === Number(po.campusId))
    $('grLocation').append(new Option(`${store.code} · ${store.name}`, String(store.id)));
   $('grCreateLines').replaceChildren();
   for (const line of po.lines || []) {
    const tr = el('tr'); tr.dataset.poLine = String(line.lineNo);
    for (const value of [line.inventoryItemId, num(line.quantity), num(line.unitPrice)]) tr.append(el('td', value));
    const qtyCell = el('td'), qty = el('input'); qty.type = 'number'; qty.min = '0';
    qty.max = String(line.quantity); qty.step = '0.000001'; qty.className = 'form-control form-control-sm';
    qty.setAttribute('aria-label', 'Delivered quantity for item ' + line.inventoryItemId);
    qtyCell.append(qty); tr.append(qtyCell);
    const lotCell = el('td'), lot = el('input'); lot.type = 'text'; lot.maxLength = 100;
    lot.className = 'form-control form-control-sm'; lot.setAttribute('aria-label', 'Lot for item ' + line.inventoryItemId);
    lotCell.append(lot); tr.append(lotCell); $('grCreateLines').append(tr);
   }
  } catch (e) { if (seq === searchSeq) alert(e.message); }
 }
 $('grCreateForm').addEventListener('submit', async e => {
  e.preventDefault(); if (pending) return;
  if (!chosenPo) { alert('Select an approved PO.'); return; }
  const lines = [];
  for (const [i, tr] of [...$('grCreateLines').children].entries()) {
   const inputs = tr.querySelectorAll('input'); const quantity = Number(inputs[0].value);
   if (!Number.isFinite(quantity) || quantity < 0) { alert('Invalid delivered quantity.'); return; }
   if (quantity > 0) lines.push({purchaseOrderLineId: chosenPo.lines[i].id,
    quantity, lotOrBatchNo:inputs[1].value.trim()});
  }
  if (!lines.length) { alert('Enter at least one positive delivered quantity.'); return; }
  try {
   pending = true; $('grCreateSave').disabled = true;
   const saved = await api(base, 'POST', {clientRequestId:createRequestId,purchaseOrderId:chosenPo.id,
    locationId:Number($('grLocation').value),reason:$('grReason').value.trim(),lines});
   close(createDialog); alert('Draft saved. Stock unchanged.',true); await load(); await showDetail(saved.reference);
  } catch (error) { alert(error.message); }
  finally { pending = false; $('grCreateSave').disabled = false; }
 });

 async function showDetail(reference) {
  try {
   receipt = await api(base + '/' + reference); submitRequestId = null; reviewRequestId = null;
   const own = Number(receipt.createdByUserId) === userId;
   $('grDetailSummary').textContent = `${receipt.orderNumber} · Store ${receipt.locationId} · ${receipt.stateCode} · Maker ${receipt.createdByUserId ?? '—'} · Checker ${receipt.reviewedByUserId ?? '—'} · ${receipt.reason}`;
   $('grDetailLines').replaceChildren();
   for (const line of receipt.lines) {
    const tr = el('tr'); for (const value of [line.inventoryItemId,line.lotOrBatchNo,num(line.quantity),
     num(line.approvedPoQuantity),num(line.approvedPoUnitPrice),line.postedUnitCost == null ? '—' : num(line.postedUnitCost)]) tr.append(el('td',value));
    $('grDetailLines').append(tr);
   }
   const actions = $('grDetailActions'); actions.replaceChildren();
   if (maker && own && receipt.stateCode === 'Draft') actions.append(btn('Submit / জমা দিন', submit, 'btn-primary'));
   if (checker && !own && receipt.stateCode === 'Submitted') actions.append(btn('Review / যাচাই', openReview, 'btn-primary'));
   if (receipt.postedMovementReference) actions.append(el('span','Posted stock movement: ' + receipt.postedMovementReference));
   detailDialog.showModal();
  } catch (e) { alert(e.message); }
 }
 async function submit() {
  if (pending || !receipt || receipt.stateCode !== 'Draft') return;
  submitRequestId ||= crypto.randomUUID(); pending = true;
  try {
   const updated = await api(base + '/' + receipt.reference + '/submit','POST',
    {clientRequestId:submitRequestId,rowVersion:receipt.rowVersion});
   close(detailDialog); alert('Submitted for independent review.',true); await load(); await showDetail(updated.reference);
  } catch (e) { alert(e.message); } finally { pending = false; }
 }
 function openReview() {
  if (!receipt || receipt.stateCode !== 'Submitted' || Number(receipt.createdByUserId) === userId) return;
  reviewRequestId = crypto.randomUUID(); $('grReviewForm').reset(); renderValuations(); reviewDialog.showModal();
 }
 function renderValuations() {
  const approve = $('grDecision').value === 'Approve', mode = $('grMode').value;
  $('grMode').disabled = !approve; $('grValuationPanel').classList.toggle('d-none', !approve || mode === 'Automatic');
  $('grValuationLines').replaceChildren();
  if (!receipt || !approve || mode === 'Automatic') return;
  $('grCostTitle').textContent = mode === 'ManualFinal' ? 'Final unit cost' : 'Signed unit price correction (+/-)';
  for (const line of receipt.lines) {
   const tr = el('tr'); tr.dataset.line = String(line.lineNo);
   tr.append(el('td', `${line.inventoryItemId} / ${line.lineNo}`),el('td',num(line.approvedPoUnitPrice)));
   const amountCell = el('td'), amount = el('input'); amount.type = 'number'; amount.step = '0.0001';
   amount.value = mode === 'ManualFinal' ? String(line.approvedPoUnitPrice) : '0';
   amount.className = 'form-control form-control-sm'; amount.setAttribute('aria-label','Cost for line '+line.lineNo);
   amount.required = true; amountCell.append(amount); tr.append(amountCell);
   const reasonCell = el('td'), reason = el('input'); reason.type = 'text'; reason.maxLength = 800;
   reason.className = 'form-control form-control-sm'; reason.placeholder = 'Reason if cost differs';
   reason.setAttribute('aria-label','Valuation reason for line '+line.lineNo);
   reasonCell.append(reason); tr.append(reasonCell); $('grValuationLines').append(tr);
  }
 }
 $('grDecision').addEventListener('change',renderValuations);
 $('grMode').addEventListener('change',renderValuations);
 $('grReviewForm').addEventListener('submit', async e => {
  e.preventDefault(); if (pending || !receipt) return;
  const decision = $('grDecision').value, valuationMode = $('grMode').value;
  const valuations = [];
  if (decision === 'Approve' && valuationMode !== 'Automatic') {
   for (const tr of $('grValuationLines').children) {
    const [input,reason] = tr.querySelectorAll('input'); const amount = Number(input.value);
    if (!input.value.trim() || !Number.isFinite(amount)) { alert('Valid unit valuation required.'); return; }
    if (valuationMode === 'ManualFinal' || amount !== 0)
     valuations.push({lineNo:Number(tr.dataset.line),amount,reason:reason.value.trim() || null});
   }
  }
  if (decision === 'Approve' && !window.confirm('Accept delivery and permanently increase stock?')) return;
  pending = true; $('grReviewSave').disabled = true;
  try {
   const updated = await api(base + '/' + receipt.reference + '/review','POST',
    {clientRequestId:reviewRequestId,rowVersion:receipt.rowVersion,decision,valuationMode,
     reason:$('grReviewReason').value.trim(),valuations});
   close(reviewDialog); close(detailDialog);
   alert(updated.stateCode === 'Posted' ? 'Delivery approved: Inventory ledger posted.' : 'Delivery rejected; stock unchanged.',true);
   await load(); await showDetail(updated.reference);
  } catch (error) { alert(error.message); } finally { pending = false; $('grReviewSave').disabled = false; }
 });
 load();
})();
