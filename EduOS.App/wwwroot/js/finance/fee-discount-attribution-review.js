(() => {
'use strict';
const root = document.getElementById('feeAttributionApp');
if (!root) return;
const el = id => document.getElementById(id);
const moneyFields = /("(?:subtotal|discountAmount|totalAmount|potentiallyMisattributedDiscount|potentialAmountOnPage)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
let page = 1, report = null, pending = null, generation = 0;
const today = new Date();
function dateLocal(date) { return date.getFullYear() + '-' + String(date.getMonth() + 1).padStart(2,'0') + '-' + String(date.getDate()).padStart(2,'0'); }
const first = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 29);
el('feeAttributionFrom').value = dateLocal(first);
el('feeAttributionTo').value = dateLocal(today);
function td(value) { const node = document.createElement('td'); node.textContent = String(value ?? ''); return node; }
function notify(message, error) {
    const node = el('feeAttributionNotice');
    node.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
    node.textContent = message;
}
function money(value) {
    const raw = String(value ?? '0');
    if (!/^-?\d+(?:\.\d{1,2})?$/.test(raw)) throw Error('Invalid financial precision.');
    const neg = raw.startsWith('-'), parts = (neg ? raw.slice(1) : raw).split('.');
    const cents = BigInt(parts[0]) * 100n + BigInt((parts[1] || '').padEnd(2,'0'));
    return (neg ? '-' : '') + String(cents / 100n) + '.' + String(cents % 100n).padStart(2,'0');
}
async function api(url, signal) {
    const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store',
        headers: { Accept: 'application/json' }, signal });
    const raw = await response.text();
    let json;
    try { json = JSON.parse(raw.replace(moneyFields, '$1"$2"')); }
    catch { throw Error('Invalid server response.'); }
    if (!response.ok || !json.success) throw Error(json.message || 'Review unavailable.');
    return json.data;
}
function filters() {
    const campusId = Number(el('feeAttributionCampus').value);
    const fromDate = el('feeAttributionFrom').value, toDate = el('feeAttributionTo').value;
    const pageSize = Number(el('feeAttributionSize').value);
    if (!Number.isSafeInteger(campusId) || campusId <= 0 ||
        !/^\d{4}-\d{2}-\d{2}$/.test(fromDate) || !/^\d{4}-\d{2}-\d{2}$/.test(toDate) ||
        ![25,50,100].includes(pageSize)) throw Error('Choose valid campus, dates and rows.');
    const start = new Date(fromDate + 'T00:00:00'), end = new Date(toDate + 'T00:00:00');
    if (!Number.isFinite(start.getTime()) || !Number.isFinite(end.getTime()) ||
        end < start || (end - start) / 86400000 >= 90)
        throw Error('Date range must be 1–90 days.');
    return new URLSearchParams({ campusId: String(campusId), fromDate, toDate,
        page: String(page), pageSize: String(pageSize) });
}
function invalidate() {
    report = null;
    el('feeAttributionCsv').disabled = true;
    el('feeAttributionPrint').disabled = true;
    el('feeAttributionPrev').disabled = true;
    el('feeAttributionNext').disabled = true;
    el('feeAttributionScope').textContent = 'No report loaded';
    el('feeAttributionCount').textContent = '';
    el('feeAttributionPage').textContent = '';
    const row = document.createElement('tr'), cell = td('Run the review.');
    cell.colSpan = 10; cell.className = 'text-center text-muted p-3';
    row.append(cell); el('feeAttributionRows').replaceChildren(row);
}
function render(data) {
    if (!data?.invoices || !Array.isArray(data.invoices.items) || data.invoices.items.length > 100)
        throw Error('Unexpected report response.');
    const body = document.createDocumentFragment();
    for (const item of data.invoices.items) {
        const row = document.createElement('tr');
        const values = [item.invoiceNumber, item.studentCode, item.invoiceDate, item.currencyCode,
            money(item.subtotal), money(item.discountAmount), money(item.totalAmount),
            money(item.potentiallyMisattributedDiscount), item.hasJournalLink ? 'Yes' : 'No',
            item.evidenceStatus];
        values.forEach((value, index) => {
            const cell = td(value);
            if (index >= 4 && index <= 7) cell.className = 'text-end';
            row.append(cell);
        });
        body.append(row);
    }
    if (!data.invoices.items.length) {
        const row = document.createElement('tr'), cell = td('No invoices in this scope.');
        cell.colSpan = 10; cell.className = 'text-center text-muted p-3'; row.append(cell); body.append(row);
    }
    el('feeAttributionRows').replaceChildren(body);
    el('feeAttributionScope').textContent = data.campusName + ' · ' + data.fromDate + ' to ' + data.toDate;
    el('feeAttributionCount').textContent = data.reviewRowsOnPage + ' flagged on page · ' +
        data.invoices.totalCount + ' invoices in scope';
    el('feeAttributionPage').textContent = 'Page ' + data.invoices.page;
    el('feeAttributionPrev').disabled = page <= 1;
    el('feeAttributionNext').disabled = page * data.invoices.pageSize >= data.invoices.totalCount;
    el('feeAttributionCsv').disabled = false;
    el('feeAttributionPrint').disabled = false;
    report = data;
}
async function load() {
    if (pending) pending.abort();
    pending = new AbortController();
    const current = ++generation;
    invalidate(); el('feeAttributionRun').disabled = true;
    try {
        const data = await api('/api/finance/fees/discount-attribution-review?' + filters(), pending.signal);
        if (current === generation) { render(data); notify('Historical evidence loaded.', false); }
    } catch (error) {
        if (current === generation && error.name !== 'AbortError') notify(error.message, true);
    } finally { if (current === generation) el('feeAttributionRun').disabled = false; }
}
function csvCell(value) {
    let text = String(value ?? '');
    if (/^[\s\u0000-\u001f]*[=+\-@]/.test(text)) text = "'" + text;
    return '"' + text.replace(/"/g, '""') + '"';
}
function exportCsv() {
    if (!report) return;
    const rows = [['Invoice','Student code','Invoice date','Currency','Subtotal','Discount',
        'Total','Candidate amount (unverified)','GL linked','Needs review','Evidence status']];
    for (const r of report.invoices.items) rows.push([r.invoiceNumber,r.studentCode,r.invoiceDate,
        r.currencyCode,money(r.subtotal),money(r.discountAmount),money(r.totalAmount),
        money(r.potentiallyMisattributedDiscount),r.hasJournalLink ? 'Yes' : 'No',
        r.needsReview ? 'Yes' : 'No',r.evidenceStatus]);
    const csv = '\uFEFF' + rows.map(r => r.map(csvCell).join(',')).join('\r\n') + '\r\n';
    const url = URL.createObjectURL(new Blob([csv], { type:'text/csv;charset=utf-8' }));
    const a = document.createElement('a'); a.href = url; a.download = 'fee-discount-evidence-page.csv';
    document.body.append(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
el('feeAttributionFilters').addEventListener('submit', event => { event.preventDefault(); page = 1; load(); });
for (const id of ['feeAttributionCampus','feeAttributionFrom','feeAttributionTo','feeAttributionSize'])
    el(id).addEventListener('change', () => { page = 1; invalidate(); });
el('feeAttributionPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
el('feeAttributionNext').addEventListener('click', () => { if (report && page * report.invoices.pageSize < report.invoices.totalCount) { page++; load(); } });
el('feeAttributionCsv').addEventListener('click', exportCsv);
el('feeAttributionPrint').addEventListener('click', () => { if (report) window.print(); });
api('/api/finance/fees/options').then(options => {
    const select = el('feeAttributionCampus');
    select.replaceChildren();
    const blank = document.createElement('option'); blank.value = ''; blank.textContent = 'Select campus';
    select.append(blank);
    for (const campus of options.campuses || []) {
        const option = document.createElement('option');
        option.value = String(campus.id); option.textContent = campus.name; select.append(option);
    }
}).catch(error => notify(error.message, true));
})();
