(() => {
'use strict';
const root = document.getElementById('feeAgingApp');
if (!root) return;
const el = id => document.getElementById(id);
const state = { active: null, page: 1, pages: 1, report: null };
const moneyKeys = /("(?:totalAmount|paidAmount|dueAmount|outstanding|current|days1To30|days31To60|days61To90|daysOver90)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
function cents(value) {
    const raw = String(value ?? '0');
    if (!/^-?\d+(?:\.\d{1,2})?$/.test(raw)) throw Error('Invalid currency precision in report.');
    const negative = raw[0] === '-', parts = (negative ? raw.slice(1) : raw).split('.');
    const amount = BigInt(parts[0]) * 100n + BigInt((parts[1] || '').padEnd(2, '0'));
    return negative ? -amount : amount;
}
function money(value) {
    const n = typeof value === 'bigint' ? value : cents(value);
    const abs = n < 0n ? -n : n;
    return (n < 0n ? '-' : '') + (abs / 100n).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',') +
        '.' + String(abs % 100n).padStart(2, '0');
}
function notice(message, error) {
    const node = el('agingAlert');
    node.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
    node.textContent = message;
}
function cell(value) {
    const td = document.createElement('td');
    td.textContent = String(value ?? '');
    return td;
}
function empty(id, span, message) {
    const tr = document.createElement('tr'), td = cell(message);
    td.colSpan = span; td.className = 'text-center text-muted p-3';
    tr.append(td); el(id).replaceChildren(tr);
}
function readFilters() {
    const date = el('agingDate').value, campus = el('agingCampus').value;
    const pageSize = Number(el('agingPageSize').value);
    if (!date || !/^\d{4}-\d{2}-\d{2}$/.test(date) || Number(date.slice(0,4)) < 2000 ||
        Number(date.slice(0,4)) > 2200 || ![25,50,100].includes(pageSize))
        throw Error('Choose a valid reference date and page size.');
    return { agingDate: date, campusId: campus, search: el('agingSearch').value.trim(), pageSize };
}
async function request(page) {
    const params = new URLSearchParams({ agingDate: state.active.agingDate,
        page: String(page), pageSize: String(state.active.pageSize) });
    if (state.active.campusId) params.set('campusId', state.active.campusId);
    if (state.active.search) params.set('search', state.active.search);
    const response = await fetch('/api/finance/receivables-aging?' + params,
        { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
    const raw = await response.text();
    let json;
    try { json = JSON.parse(raw.replace(moneyKeys, '$1"$2"')); }
    catch { throw Error('Unexpected report response.'); }
    if (!response.ok || !json.success) throw Error(json.message || 'Unable to load receivables.');
    return json.data;
}
function render(data) {
    state.report = data;
    const selected = el('agingCampus').value, campuses = data.campuses || [];
    el('agingCampus').replaceChildren();
    const all = document.createElement('option'); all.value = ''; all.textContent = 'All accessible campuses';
    el('agingCampus').append(all);
    for (const c of campuses) {
        const option = document.createElement('option');
        option.value = String(c.id); option.textContent = c.name; el('agingCampus').append(option);
    }
    if (campuses.some(c => String(c.id) === selected)) el('agingCampus').value = selected;
    el('agingTotals').replaceChildren();
    for (const x of data.totals || []) {
        const expected = cents(x.current) + cents(x.days1To30) + cents(x.days31To60) +
            cents(x.days61To90) + cents(x.daysOver90);
        if (expected !== cents(x.outstanding)) throw Error('Aging buckets do not reconcile.');
        const tr = document.createElement('tr');
        for (const v of [x.currencyCode, x.invoiceCount, money(x.current), money(x.days1To30),
            money(x.days31To60), money(x.days61To90), money(x.daysOver90), money(x.outstanding)]) tr.append(cell(v));
        el('agingTotals').append(tr);
    }
    if (!el('agingTotals').children.length) empty('agingTotals', 8, 'No outstanding invoices for these filters.');
    el('agingRows').replaceChildren();
    const page = data.invoices;
    for (const x of page.items || []) {
        const tr = document.createElement('tr');
        for (const v of [x.invoiceNumber, x.studentCode + ' — ' + x.displayName, x.campusName,
            String(x.invoiceDate).slice(0,10), String(x.dueDate).slice(0,10), x.currencyCode,
            money(x.totalAmount), money(x.paidAmount), money(x.dueAmount), x.daysOverdue, x.agingBucket]) tr.append(cell(v));
        el('agingRows').append(tr);
    }
    if (!el('agingRows').children.length) empty('agingRows', 11, 'No invoices on this page.');
    state.page = page.page; state.pages = Math.max(1, page.totalPages);
    el('agingCount').textContent = page.totalCount + ' outstanding invoices';
    el('agingPage').textContent = 'Page ' + state.page + ' of ' + state.pages;
    el('agingPrev').disabled = state.page <= 1;
    el('agingNext').disabled = state.page >= state.pages;
    el('agingExport').disabled = !(page.items || []).length;
}
async function load(page) {
    el('agingRun').disabled = true;
    el('agingExport').disabled = true;
    try {
        const result = await request(page);
        render(result);
        notice('Report loaded. Totals cover all matching invoices; CSV exports the visible page.', false);
    } catch (error) {
        state.report = null; notice(error.message || 'Report failed.', true);
    } finally { el('agingRun').disabled = false; }
}
function csvField(value) {
    let text = String(value ?? '');
    if (/^[\s\u0000-\u001f]*[=+\-@]/.test(text)) text = "'" + text;
    return '"' + text.replace(/"/g, '""') + '"';
}
function exportPage() {
    const report = state.report;
    if (!report) return;
    const rows = [['Invoice','Student code','Student name','Campus','Invoice date','Due date',
        'Currency','Total','Paid','Outstanding','Days overdue','Bucket']];
    for (const x of report.invoices.items || []) rows.push([x.invoiceNumber, x.studentCode,
        x.displayName, x.campusName, x.invoiceDate, x.dueDate, x.currencyCode,
        money(x.totalAmount), money(x.paidAmount), money(x.dueAmount), x.daysOverdue, x.agingBucket]);
    const csv = '\uFEFF' + rows.map(r => r.map(csvField).join(',')).join('\r\n') + '\r\n';
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
    const link = document.createElement('a'); link.href = url;
    link.download = 'fee-receivables-aging-' + state.active.agingDate + '-page-' + state.page + '.csv';
    document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
el('agingFilters').addEventListener('submit', event => {
    event.preventDefault();
    try { state.active = readFilters(); load(1); }
    catch (error) { notice(error.message, true); }
});
el('agingPrev').addEventListener('click', () => { if (state.page > 1) load(state.page - 1); });
el('agingNext').addEventListener('click', () => { if (state.page < state.pages) load(state.page + 1); });
el('agingExport').addEventListener('click', exportPage);
const now = new Date();
el('agingDate').value = [now.getFullYear(), String(now.getMonth()+1).padStart(2,'0'),
    String(now.getDate()).padStart(2,'0')].join('-');
state.active = readFilters();
load(1);
})();
