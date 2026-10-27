(() => {
'use strict';
const root = document.getElementById('feeDocument');
if (!root) return;
const $ = id => document.getElementById(id);
const kind = root.dataset.kind;
const query = new URLSearchParams(window.location.search);
const student = query.get('studentReference') || '';
const reference = query.get('documentReference') || '';
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const moneyKeys = /("(?:amount|subtotal|discountAmount|fineAmount|totalAmount|paidAmount|dueAmount|netAmount)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
const set = (id, value) => { $(id).textContent = String(value ?? ''); };
function money(value) {
    const s = String(value ?? '');
    if (!/^-?\d+(?:\.\d{1,2})?$/.test(s)) throw new Error('Invalid financial amount in document.');
    const negative = s.startsWith('-'), parts = (negative ? s.slice(1) : s).split('.');
    const cents = BigInt(parts[0]) * 100n + BigInt((parts[1] || '').padEnd(2, '0'));
    const whole = (cents / 100n).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',');
    return (negative ? '-' : '') + whole + '.' + String(cents % 100n).padStart(2, '0');
}
function row(values) {
    const tr = document.createElement('tr');
    for (const value of values) {
        const td = document.createElement('td');
        td.textContent = String(value ?? '');
        tr.appendChild(td);
    }
    $('docRows').appendChild(tr);
}
function columns(labels) {
    $('docColumns').replaceChildren();
    $('docRows').replaceChildren();
    for (const label of labels) {
        const th = document.createElement('th');
        th.textContent = label;
        $('docColumns').appendChild(th);
    }
}
const invoiceStates = {1:'Draft',2:'Issued',3:'Partially paid',4:'Paid',5:'Cancelled',6:'Refunded'};
const paymentStates = {1:'Initiated',2:'Awaiting verification',3:'Successful',4:'Failed',5:'Cancelled',6:'Refunded'};
const methods = {1:'Cash',2:'Bank transfer',3:'Card',4:'Mobile financial service',5:'Gateway',6:'Cheque',99:'Other'};
function invoice(d) {
    set('docTitle','Student fee invoice');
    set('docStudent',d.studentName+' ('+d.studentCode+')');
    set('docNumber',d.invoiceNumber);
    set('docDate',d.invoiceDate);
    set('docState',invoiceStates[d.state] || 'Unknown');
    set('docExtraLabel','Due date');
    set('docExtra',d.dueDate);
    columns(['Fee head','Description','Amount','Discount','Fine','Net']);
    for (const l of d.lines || [])
        row([l.feeHeadName,l.description,money(l.amount),money(l.discountAmount),money(l.fineAmount),money(l.netAmount)]);
    set('docTotal','Total '+money(d.totalAmount)+' '+d.currencyCode+
        ' · Paid '+money(d.paidAmount)+' · Due '+money(d.dueAmount));
    set('docNote','Document reflects the current authorized invoice record, including any posted corrections. A cancelled or refunded invoice is not a demand for payment.');
}
function receipt(d) {
    const p = d.payment;
    set('docTitle',p.state === 3 ? 'Student payment receipt' : 'Student payment record');
    set('docStudent',d.studentName+' ('+d.studentCode+')');
    set('docNumber',p.receiptNumber);
    set('docDate',p.paymentDate);
    set('docState',paymentStates[p.state] || 'Unknown');
    set('docExtraLabel','Payment method');
    set('docExtra',methods[p.paymentMethod] || 'Other');
    columns(['Allocated invoice','Amount ('+p.currencyCode+')']);
    let allocated = 0n;
    for (const a of p.allocations || []) {
        row([a.invoiceNumber,money(a.amount)]);
        const raw = String(a.amount);
        if (!/^\d+(?:\.\d{1,2})?$/.test(raw)) throw new Error('Invalid allocation amount.');
        const parts = raw.split('.');
        allocated += BigInt(parts[0]) * 100n + BigInt((parts[1] || '').padEnd(2,'0'));
    }
    set('docTotal','Payment '+money(p.amount)+' '+p.currencyCode+' · Allocated '+money(allocated / 100n + '.' + String(allocated % 100n).padStart(2,'0')));
    set('docNote',p.state === 3
        ? 'This acknowledges the recorded payment. A linked accounting journal, if any, is managed separately.'
        : 'This payment is not successful. Do not treat this record as proof of collection.');
}
async function load() {
    if (!['invoice','receipt'].includes(kind) || !guid.test(student) || !guid.test(reference))
        throw new Error('Invalid document link.');
    const collection = kind === 'invoice' ? 'invoices' : 'payments';
    const url = '/api/finance/fees/students/'+encodeURIComponent(student)+'/'+collection+'/'+encodeURIComponent(reference);
    const response = await fetch(url, {credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
    const raw = await response.text();
    let result;
    try { result = JSON.parse(raw.replace(moneyKeys,'$1"$2"')); }
    catch { throw new Error('Invalid finance document response.'); }
    if (!response.ok || !result.success || !result.data)
        throw new Error(result.message || 'Document unavailable or access denied.');
    if (kind === 'invoice') invoice(result.data); else receipt(result.data);
    $('docContent').classList.remove('d-none');
    $('docNotice').classList.add('d-none');
    $('docPrint').disabled = false;
}
$('docPrint').addEventListener('click', () => { if (!$('docPrint').disabled) window.print(); });
load().catch(error => {
    set('docNotice',error.message || 'Unable to load document.');
    $('docNotice').className = 'alert alert-danger no-print';
    $('docContent').classList.add('d-none');
    $('docPrint').disabled = true;
});
})();
