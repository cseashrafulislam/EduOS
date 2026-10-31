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
  if (!response.ok || !data?.success) throw new Error(data?.message || 'Request failed / অনুরোধ ব্যর্থ');
  return data.data;
 }
 $('simHelp').addEventListener('click', () => { const x = $('simGuidance');
  x.classList.toggle('d-none'); $('simHelp').setAttribute('aria-expanded', String(!x.classList.contains('d-none'))); });
 let selected = null, searchSeq = 0, pending = false;
 $('simSearchForm').addEventListener('submit', async e => {
  e.preventDefault(); const seq = ++searchSeq;
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
  const seq = ++searchSeq;
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
   selected = po; $('simOrderTitle').textContent = `${po.orderNumber} · Campus ${po.campusId}`;
   for (const receipt of receipts) for (const line of receipt.lines || []) addLine(receipt,line);
   $('simMode').value = 'Automatic'; adjustMode();
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
 $('simMode').addEventListener('change',adjustMode);
 $('simPreviewForm').addEventListener('submit', async e => {
  e.preventDefault(); if (pending || !selected) return;
  const priceMode = $('simMode').value, lines = [];
  for (const tr of $('simRows').children) {
   const [quantityInput] = tr.querySelectorAll('input');
   if (!quantityInput.value.trim()) continue;
   const quantity = Number(quantityInput.value);
   if (!Number.isFinite(quantity) || quantity < 0) { status('Invalid invoice quantity.'); return; }
   if (quantity === 0) continue;
   const price = tr.querySelector('.sim-price'), reason = tr.querySelector('.sim-reason');
   const amount = priceMode === 'Automatic' ? 0 : Number(price.value);
   if (!Number.isFinite(amount) || (!price.disabled && !price.value.trim())) {
    status('Valid final unit price or correction required.'); return;
   }
   lines.push({goodsReceiptReference:tr.dataset.reference,receiptLineNo:Number(tr.dataset.lineNo),
    quantity,amount,varianceReason:reason.disabled ? null : reason.value.trim() || null});
  }
  if (!lines.length) { status('Enter a positive invoice quantity / ইনভয়েস পরিমাণ দিন'); return; }
  try {
   pending = true; $('simRun').disabled = true;
   const preview = await api('/api/procurement/supplier-invoices/preview','POST',{
    purchaseOrderId:selected.id,priceMode,lines});
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
  finally { pending = false; $('simRun').disabled = false; }
 });
})();
