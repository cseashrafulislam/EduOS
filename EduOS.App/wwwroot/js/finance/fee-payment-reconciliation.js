(() => {
'use strict';
if (!document.getElementById('feePaymentReconciliation')) return;
const el = id => document.getElementById(id);
let page = 1, pages = 1, active = null;
const moneyKeys = /("(?:amount|grossAllocated|paidRefunds|netAllocated)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
function money(value) {
    const s = String(value ?? '0');
    if (!/^-?\d+(?:\.\d{1,2})?$/.test(s)) throw Error('Invalid monetary precision.');
    const negative = s.startsWith('-'), parts = (negative ? s.slice(1) : s).split('.');
    const cents = BigInt(parts[0]) * 100n + BigInt((parts[1] || '').padEnd(2, '0'));
    return (negative ? '-' : '') + (cents / 100n).toString() + '.' + String(cents % 100n).padStart(2, '0');
}
function cell(value) { const td = document.createElement('td'); td.textContent = String(value ?? ''); return td; }
async function load(next) {
    if (!active) return;
    const params = new URLSearchParams({page:String(next), pageSize:'25'});
    if (active.campusId) params.set('campusId', active.campusId);
    if (active.search) params.set('search', active.search);
    const button = el('paymentReconcileRun'); button.disabled = true;
    try {
        const response = await fetch('/api/finance/receivables-aging/payment-reconciliation?' + params,
            {credentials:'same-origin', cache:'no-store', headers:{Accept:'application/json'}});
        const raw = await response.text();
        const json = JSON.parse(raw.replace(moneyKeys, '$1"$2"'));
        if (!response.ok || !json.success) throw Error(json.message || 'Receipt reconciliation failed.');
        const data = json.data;
        const body = el('paymentReconcileRows'); body.replaceChildren();
        for (const x of data.rows || []) {
            const tr = document.createElement('tr');
            for (const value of [x.receiptNumber, x.studentCode + ' — ' + x.studentName,
                x.campusName, money(x.amount), money(x.grossAllocated),
                money(x.paidRefunds), money(x.netAllocated), x.status, x.issues || '—'])
                tr.append(cell(value));
            body.append(tr);
        }
        if (!body.children.length) {
            const tr = document.createElement('tr'), td = cell('No matching successful receipts.');
            td.colSpan = 9; tr.append(td); body.append(tr);
        }
        page = data.page; pages = Math.max(1, Math.ceil(data.totalCount / data.pageSize));
        el('paymentReconcilePage').textContent = 'Page ' + page + ' of ' + pages + ' · ' +
            data.pageExceptions + ' exception(s) on this page';
        el('paymentReconcileNotice').textContent = 'Read-only. ' + data.totalCount +
            ' matching receipts; exception count covers this page only.';
        el('paymentReconcilePrev').disabled = page <= 1;
        el('paymentReconcileNext').disabled = page >= pages;
    } catch (error) {
        el('paymentReconcileNotice').textContent = error.message || 'Receipt reconciliation unavailable.';
    } finally { button.disabled = false; }
}
el('paymentReconcileRun').addEventListener('click', () => {
    active = {campusId:el('agingCampus').value, search:el('agingSearch').value.trim()};
    load(1);
});
el('paymentReconcilePrev').addEventListener('click', () => { if (page > 1) load(page - 1); });
el('paymentReconcileNext').addEventListener('click', () => { if (page < pages) load(page + 1); });
})();
