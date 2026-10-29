(() => {
'use strict';
const root = document.getElementById('feeReconciliation');
if (!root) return;
const el = id => document.getElementById(id);
let page = 1, pages = 1, active = null;
const moneyKeys = /("(?:totalAmount|paidAmount|dueAmount|netAllocatedAmount|creditNoteAmount)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
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
    const button = el('reconcileRun'); button.disabled = true;
    try {
        const response = await fetch('/api/finance/receivables-aging/reconciliation?' + params,
            {credentials:'same-origin', cache:'no-store', headers:{Accept:'application/json'}});
        const raw = await response.text();
        const json = JSON.parse(raw.replace(moneyKeys, '$1"$2"'));
        if (!response.ok || !json.success) throw Error(json.message || 'Reconciliation failed.');
        const data = json.data;
        el('reconcileRows').replaceChildren();
        for (const x of data.rows || []) {
            const tr = document.createElement('tr');
            for (const value of [x.invoiceNumber, x.studentCode + ' — ' + x.studentName,
                x.campusName, money(x.totalAmount), money(x.paidAmount),
                money(x.netAllocatedAmount), money(x.creditNoteAmount),
                money(x.dueAmount), x.status, x.issues || '—'])
                tr.append(cell(value));
            el('reconcileRows').append(tr);
        }
        if (!el('reconcileRows').children.length) {
            const tr = document.createElement('tr'), td = cell('No matching invoices.');
            td.colSpan = 10; tr.append(td); el('reconcileRows').append(tr);
        }
        page = data.page; pages = Math.max(1, Math.ceil(data.totalCount / data.pageSize));
        el('reconcilePage').textContent = 'Page ' + page + ' of ' + pages + ' · ' +
            data.pageExceptions + ' exception(s) on this page';
        el('reconcileNotice').textContent = 'Read-only. ' + data.totalCount +
            ' matching invoices; exceptions count covers this page only.';
        el('reconcilePrev').disabled = page <= 1;
        el('reconcileNext').disabled = page >= pages;
    } catch (error) {
        el('reconcileNotice').textContent = error.message || 'Reconciliation unavailable.';
    } finally { button.disabled = false; }
}
el('reconcileRun').addEventListener('click', () => {
    active = {campusId:el('agingCampus').value, search:el('agingSearch').value.trim()};
    load(1);
});
el('reconcilePrev').addEventListener('click', () => { if (page > 1) load(page - 1); });
el('reconcileNext').addEventListener('click', () => { if (page < pages) load(page + 1); });
})();
