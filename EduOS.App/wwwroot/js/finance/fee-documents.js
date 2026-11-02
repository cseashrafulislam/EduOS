(() => {
'use strict';
const root = document.getElementById('feeDocument');
if (!root) return;
// Localize only fixed chrome/status; never translate tenant records or amounts.
const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
const t = (en, bd) => bn ? bd : en;
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
    if (!/^-?\d+(?:\.\d{1,2})?$/.test(s)) throw new Error(t('Invalid financial amount in document.','নথিতে অর্থের পরিমাণ সঠিক নয়।'));
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
const invoiceStates = {1:t('Draft','খসড়া'),2:t('Issued','ইস্যুকৃত'),3:t('Partially paid','আংশিক পরিশোধিত'),4:t('Paid','পরিশোধিত'),5:t('Cancelled','বাতিল'),6:t('Refunded','ফেরত দেওয়া')};
const paymentStates = {1:t('Initiated','শুরু হয়েছে'),2:t('Awaiting verification','যাচাইয়ের অপেক্ষায়'),3:t('Successful','সফল'),4:t('Failed','ব্যর্থ'),5:t('Cancelled','বাতিল'),6:t('Refunded','ফেরত দেওয়া')};
const methods = {1:t('Cash','নগদ'),2:t('Bank transfer','ব্যাংক স্থানান্তর'),3:t('Card','কার্ড'),4:t('Mobile financial service','মোবাইল আর্থিক সেবা'),5:t('Gateway','পেমেন্ট গেটওয়ে'),6:t('Cheque','চেক'),99:t('Other','অন্যান্য')};
function invoice(d) {
    set('docTitle',t('Student fee invoice','শিক্ষার্থীর ফি ইনভয়েস'));
    set('docStudent',d.studentName+' ('+d.studentCode+')');
    set('docNumber',d.invoiceNumber);
    set('docDate',d.invoiceDate);
    set('docState',invoiceStates[d.state] || t('Unknown','অজানা'));
    set('docExtraLabel',t('Due date','পরিশোধের শেষ তারিখ'));
    set('docExtra',d.dueDate);
    columns([t('Fee head','ফি খাত'),t('Description','বিবরণ'),t('Amount','পরিমাণ'),t('Discount','ছাড়'),t('Fine','জরিমানা'),t('Net','নিট')]);
    for (const l of d.lines || [])
        row([l.feeHeadName,l.description,money(l.amount),money(l.discountAmount),money(l.fineAmount),money(l.netAmount)]);
    set('docTotal',t('Total ','মোট ')+money(d.totalAmount)+' '+d.currencyCode+
        t(' · Paid ',' · পরিশোধিত ')+money(d.paidAmount)+t(' · Due ',' · বকেয়া ')+money(d.dueAmount));
    set('docNote',t('Document reflects the current authorized invoice record, including any posted corrections. A cancelled or refunded invoice is not a demand for payment.','এই নথিতে অনুমোদিত বর্তমান ইনভয়েস ও পোস্ট করা সংশোধন দেখানো হয়েছে। বাতিল বা ফেরত দেওয়া ইনভয়েস অর্থ পরিশোধের দাবি নয়।'));
}
function receipt(d) {
    const p = d.payment;
    set('docTitle',p.state === 3 ? t('Student payment receipt','শিক্ষার্থীর পরিশোধ রসিদ') : t('Student payment record','শিক্ষার্থীর পরিশোধের নথি'));
    set('docStudent',d.studentName+' ('+d.studentCode+')');
    set('docNumber',p.receiptNumber);
    set('docDate',p.paymentDate);
    set('docState',paymentStates[p.state] || t('Unknown','অজানা'));
    set('docExtraLabel',t('Payment method','পরিশোধের মাধ্যম'));
    set('docExtra',methods[p.paymentMethod] || t('Other','অন্যান্য'));
    columns([t('Allocated invoice','বণ্টিত ইনভয়েস'),t('Amount (','পরিমাণ (')+p.currencyCode+')']);
    let allocated = 0n;
    for (const a of p.allocations || []) {
        row([a.invoiceNumber,money(a.amount)]);
        const raw = String(a.amount);
        if (!/^\d+(?:\.\d{1,2})?$/.test(raw)) throw new Error(t('Invalid allocation amount.','বণ্টনের পরিমাণ সঠিক নয়।'));
        const parts = raw.split('.');
        allocated += BigInt(parts[0]) * 100n + BigInt((parts[1] || '').padEnd(2,'0'));
    }
    set('docTotal',t('Payment ','পরিশোধ ')+money(p.amount)+' '+p.currencyCode+t(' · Allocated ',' · বণ্টিত ')+money(allocated / 100n + '.' + String(allocated % 100n).padStart(2,'0')));
    set('docNote',p.state === 3
        ? t('This acknowledges the recorded payment. A linked accounting journal, if any, is managed separately.','এটি নথিভুক্ত পরিশোধের স্বীকৃতি। সংশ্লিষ্ট হিসাব জার্নাল থাকলে তা আলাদাভাবে পরিচালিত হয়।')
        : t('This payment is not successful. Do not treat this record as proof of collection.','এই পরিশোধ সফল নয়। একে অর্থ সংগ্রহের প্রমাণ হিসেবে ব্যবহার করবেন না।'));
}
async function load() {
    if (!['invoice','receipt'].includes(kind) || !guid.test(student) || !guid.test(reference))
        throw new Error(t('Invalid document link.','নথির লিংক সঠিক নয়।'));
    const collection = kind === 'invoice' ? 'invoices' : 'payments';
    const url = '/api/finance/fees/students/'+encodeURIComponent(student)+'/'+collection+'/'+encodeURIComponent(reference);
    const response = await fetch(url, {credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
    const raw = await response.text();
    let result;
    try { result = JSON.parse(raw.replace(moneyKeys,'$1"$2"')); }
    catch { throw new Error(t('Invalid finance document response.','ফাইন্যান্স নথির উত্তর সঠিক নয়।')); }
    if (!response.ok || !result.success || !result.data)
        // Never expose arbitrary backend exception details on a shared or printable page.
        throw new Error(t('Document unavailable or access denied.','নথি পাওয়া যায়নি বা দেখার অনুমতি নেই।'));
    if (kind === 'invoice') invoice(result.data); else receipt(result.data);
    $('docContent').classList.remove('d-none');
    $('docNotice').classList.add('d-none');
    $('docPrint').disabled = false;
}
$('docPrint').addEventListener('click', () => { if (!$('docPrint').disabled) window.print(); });
load().catch(error => {
    set('docNotice',error.message || t('Unable to load document.','নথি লোড করা যায়নি।'));
    $('docNotice').className = 'alert alert-danger no-print';
    $('docContent').classList.add('d-none');
    $('docPrint').disabled = true;
});
})();
