(() => {
'use strict';
const root = document.getElementById('feeDueCalendarApp');
if (!root) return;
const el = id => document.getElementById(id);
const state = { filters: null, report: null, page: 1, pages: 1, generation: 0 };
const moneyPattern = /("(?:outstanding|totalAmount|paidAmount|dueAmount)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
const iso = date => [date.getFullYear(), String(date.getMonth() + 1).padStart(2, '0'),
    String(date.getDate()).padStart(2, '0')].join('-');
const start = new Date(); start.setDate(1);
const end = new Date(start.getFullYear(), start.getMonth() + 3, 0);
el('feeDueFrom').value = iso(start);
el('feeDueTo').value = iso(end);
function cents(value) {
    const raw = String(value ?? '0');
    if (!/^-?\d+(?:\.\d{1,2})?$/.test(raw)) throw Error('Invalid financial precision.');
    const negative = raw[0] === '-';
    const parts = (negative ? raw.slice(1) : raw).split('.');
    const n = BigInt(parts[0]) * 100n + BigInt((parts[1] || '').padEnd(2, '0'));
    return negative ? -n : n;
}
function money(value) {
    const n = cents(value), a = n < 0n ? -n : n;
    return (n < 0n ? '-' : '') + (a / 100n).toString() + '.' + String(a % 100n).padStart(2, '0');
}
function cell(value) {
    const td = document.createElement('td');
    td.textContent = String(value ?? '');
    return td;
}
function empty(id, colspan, message) {
    const row = document.createElement('tr'), td = cell(message);
    td.colSpan = colspan; td.className = 'text-center text-muted p-3';
    row.append(td); el(id).replaceChildren(row);
}
function notice(message, error) {
    const node = el('feeDueNotice');
    node.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
    node.textContent = message;
}
function filters() {
    const fromDate = el('feeDueFrom').value, toDate = el('feeDueTo').value;
    const pageSize = Number(el('feeDueSize').value);
    if (!/^\d{4}-\d{2}-\d{2}$/.test(fromDate) || !/^\d{4}-\d{2}-\d{2}$/.test(toDate) ||
        fromDate > toDate || ![25,50,100].includes(pageSize))
        throw Error('Select valid due dates and page size.');
    return { fromDate, toDate, campusId: el('feeDueCampus').value,
        search: el('feeDueSearch').value.trim(), pageSize };
}
async function fetchPage(page, signal) {
    const f = state.filters;
    const p = new URLSearchParams({ fromDate: f.fromDate, toDate: f.toDate,
        page: String(page), pageSize: String(f.pageSize) });
    if (f.campusId) p.set('campusId', f.campusId);
    if (f.search) p.set('search', f.search);
    const response = await fetch('/api/finance/receivables-aging/due-calendar?' + p,
        { credentials: 'same-origin', cache: 'no-store', signal,
            headers: { Accept: 'application/json' } });
    const raw = await response.text();
    let result;
    try { result = JSON.parse(raw.replace(moneyPattern, '$1"$2"')); }
    catch { throw Error('Invalid report response.'); }
    if (!response.ok || !result.success) throw Error(result.message || 'Report unavailable.');
    return result.data;
}
function render(data) {
    const selected = el('feeDueCampus').value;
    el('feeDueCampus').replaceChildren();
    const all = document.createElement('option'); all.value = ''; all.textContent = 'All accessible';
    el('feeDueCampus').append(all);
    for (const c of data.campuses || []) {
        const option = document.createElement('option');
        option.value = String(c.id); option.textContent = c.name;
        el('feeDueCampus').append(option);
    }
    if ((data.campuses || []).some(c => String(c.id) === selected)) el('feeDueCampus').value = selected;
    el('feeDueMonths').replaceChildren();
    for (const m of data.months || []) {
        const tr = document.createElement('tr');
        for (const v of [String(m.year) + '-' + String(m.month).padStart(2, '0'),
            m.currencyCode, m.invoiceCount, money(m.outstanding)]) tr.append(cell(v));
        el('feeDueMonths').append(tr);
    }
    if (!el('feeDueMonths').children.length) empty('feeDueMonths', 4, 'No unpaid issued invoices in this range.');
    el('feeDueRows').replaceChildren();
    const p = data.invoices;
    for (const x of p.items || []) {
        if (cents(x.totalAmount) - cents(x.paidAmount) !== cents(x.dueAmount))
            throw Error('Invoice amount mismatch; export blocked.');
        const tr = document.createElement('tr');
        for (const v of [String(x.dueDate).slice(0,10), x.invoiceNumber,
            x.studentCode + ' — ' + x.studentName, x.campusName, x.currencyCode,
            money(x.totalAmount), money(x.paidAmount), money(x.dueAmount)]) tr.append(cell(v));
        el('feeDueRows').append(tr);
    }
    if (!el('feeDueRows').children.length) empty('feeDueRows', 8, 'No invoices on this page.');
    state.report = data; state.page = p.page; state.pages = Math.max(1, p.totalPages);
    el('feeDueScope').textContent = 'Current unpaid invoices due ' + data.fromDate + ' to ' + data.toDate;
    el('feeDueCount').textContent = String(p.totalCount) + ' invoices';
    el('feeDuePage').textContent = 'Page ' + state.page + ' of ' + state.pages;
    el('feeDuePrev').disabled = state.page <= 1;
    el('feeDueNext').disabled = state.page >= state.pages;
    el('feeDueCsv').disabled = !(p.items || []).length;
    el('feeDuePrint').disabled = !(p.items || []).length;
}
let controller;
async function load(page) {
    if (controller) controller.abort();
    controller = new AbortController();
    const generation = ++state.generation;
    el('feeDueRun').disabled = true;
    el('feeDueCsv').disabled = true;
    el('feeDuePrint').disabled = true;
    state.report = null;
    try {
        const result = await fetchPage(page, controller.signal);
        if (generation !== state.generation) return;
        render(result); notice('Report loaded.', false);
    } catch (error) {
        if (generation !== state.generation) return;
        if (error.name !== 'AbortError') notice(error.message || 'Report failed.', true);
        empty('feeDueMonths', 4, 'No verified report loaded.');
        empty('feeDueRows', 8, 'No verified report loaded.');
    } finally {
        if (generation === state.generation) el('feeDueRun').disabled = false;
    }
}
function csvCell(value) {
    let valueText = String(value ?? '');
    if (/^[\s\u0000-\u001f]*[=+\-@]/.test(valueText)) valueText = "'" + valueText;
    return '"' + valueText.replace(/"/g, '""') + '"';
}
function exportCsv() {
    if (!state.report) return;
    const rows = [['Due date','Invoice','Student code','Student name','Campus','Currency','Total','Paid','Due']];
    for (const x of state.report.invoices.items) rows.push([x.dueDate, x.invoiceNumber,
        x.studentCode, x.studentName, x.campusName, x.currencyCode,
        money(x.totalAmount), money(x.paidAmount), money(x.dueAmount)]);
    const blob = new Blob(['\uFEFF' + rows.map(r => r.map(csvCell).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a'); link.href = url;
    link.download = 'student-fee-due-calendar-page-' + state.page + '.csv';
    document.body.append(link); link.click(); link.remove();
    URL.revokeObjectURL(url);
}
el('feeDueFilters').addEventListener('submit', event => {
    event.preventDefault();
    try { state.filters = filters(); load(1); }
    catch (error) { notice(error.message, true); }
});
el('feeDuePrev').addEventListener('click', () => { if (state.page > 1) load(state.page - 1); });
el('feeDueNext').addEventListener('click', () => { if (state.page < state.pages) load(state.page + 1); });
el('feeDueCsv').addEventListener('click', exportCsv);
el('feeDuePrint').addEventListener('click', () => { if (state.report) window.print(); });
})();
