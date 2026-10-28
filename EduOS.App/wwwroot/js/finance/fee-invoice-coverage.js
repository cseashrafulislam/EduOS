(() => {
'use strict';
const root = document.getElementById('feeCoverageApp');
if (!root) return;
const el = id => document.getElementById(id);
let options = null, report = null, page = 1, serial = 0, pending = null;
const today = new Date();
el('feeCoverageMonth').value = today.getFullYear() + '-' + String(today.getMonth() + 1).padStart(2, '0');
function cell(value) {
    const td = document.createElement('td');
    td.textContent = String(value ?? '');
    return td;
}
function option(value, text) {
    const o = document.createElement('option');
    o.value = String(value); o.textContent = text; return o;
}
function notice(message, failed) {
    const n = el('feeCoverageNotice');
    n.className = 'alert ' + (failed ? 'alert-danger' : 'alert-success');
    n.textContent = message;
}
function clear() {
    report = null;
    el('feeCoverageCsv').disabled = true;
    el('feeCoveragePrint').disabled = true;
    el('feeCoveragePrev').disabled = true;
    el('feeCoverageNext').disabled = true;
    el('feeCoverageScope').textContent = 'No report loaded';
    for (const id of ['Eligible','Billed','Unbilled','Review']) el('feeCoverage' + id).textContent = '—';
    el('feeCoverageCount').textContent = '';
    el('feeCoveragePage').textContent = '';
    const tr = document.createElement('tr'), td = cell('No report loaded');
    td.colSpan = 5; td.className = 'text-muted text-center p-3';
    tr.append(td); el('feeCoverageRows').replaceChildren(tr);
}
async function api(url, signal) {
    const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store',
        headers: { Accept: 'application/json' }, signal });
    const json = await response.json();
    if (!response.ok || !json.success) throw Error(json.message || 'Report unavailable.');
    return json.data;
}
function params() {
    const batch = Number(el('feeCoverageBatch').value);
    const month = el('feeCoverageMonth').value;
    if (!Number.isSafeInteger(batch) || batch <= 0 || !/^\d{4}-(0[1-9]|1[0-2])$/.test(month))
        throw Error('Choose an academic batch and billing month.');
    return new URLSearchParams({ academicBatchId: String(batch), billingMonth: month + '-01',
        status: el('feeCoverageStatus').value, search: el('feeCoverageSearch').value.trim(),
        page: String(page), pageSize: el('feeCoverageSize').value });
}
function render(data) {
    if (!data || !data.students || !Array.isArray(data.students.items) ||
        ![data.eligible, data.billed, data.unbilled, data.review].every(Number.isSafeInteger) ||
        data.eligible !== data.billed + data.unbilled + data.review)
        throw Error('Coverage totals do not reconcile.');
    const body = document.createDocumentFragment();
    for (const item of data.students.items) {
        if (!Number.isSafeInteger(item.invoiceCount) || item.invoiceCount < 0 ||
            !Number.isSafeInteger(item.integrityIssueCount) || item.integrityIssueCount < 0 ||
            !['Billed','Unbilled','Review'].includes(item.status))
            throw Error('Unexpected coverage row.');
        const tr = document.createElement('tr');
        for (const value of [item.studentCode, item.studentName, item.invoiceCount,
            item.integrityIssueCount, item.status]) tr.append(cell(value));
        body.append(tr);
    }
    if (!data.students.items.length) {
        const tr = document.createElement('tr'), td = cell('No matching students');
        td.colSpan = 5; td.className = 'text-center text-muted p-3';
        tr.append(td); body.append(tr);
    }
    el('feeCoverageRows').replaceChildren(body);
    el('feeCoverageScope').textContent = data.campusName + ' / ' + data.academicBatchName + ' / ' + data.billingMonth;
    for (const id of ['Eligible','Billed','Unbilled','Review'])
        el('feeCoverage' + id).textContent = String(data[id.toLowerCase()]);
    el('feeCoverageCount').textContent = String(data.students.totalCount) + ' matching';
    el('feeCoveragePage').textContent = 'Page ' + data.students.page;
    el('feeCoveragePrev').disabled = data.students.page <= 1;
    el('feeCoverageNext').disabled = data.students.page * data.students.pageSize >= data.students.totalCount;
    report = data;
    el('feeCoverageCsv').disabled = false;
    el('feeCoveragePrint').disabled = false;
}
async function load() {
    if (pending) pending.abort();
    pending = new AbortController();
    const id = ++serial;
    clear(); el('feeCoverageRun').disabled = true;
    try {
        const data = await api('/api/finance/fees/invoice-coverage?' + params(), pending.signal);
        if (id !== serial) return;
        render(data); notice('Coverage loaded.', false);
    } catch (error) {
        if (id === serial && error.name !== 'AbortError') notice(error.message, true);
    } finally {
        if (id === serial) el('feeCoverageRun').disabled = false;
    }
}
function csvCell(value) {
    let v = String(value ?? '');
    if (/^[\s\u0000-\u001f]*[=+\-@]/.test(v)) v = "'" + v;
    return '"' + v.replace(/"/g, '""') + '"';
}
function exportCsv() {
    if (!report) return;
    const rows = [['Student code','Student name','Invoices','Integrity issues','Status']];
    for (const x of report.students.items)
        rows.push([x.studentCode, x.studentName, x.invoiceCount, x.integrityIssueCount, x.status]);
    const csv = '\uFEFF' + rows.map(row => row.map(csvCell).join(',')).join('\r\n') + '\r\n';
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
    const a = document.createElement('a');
    a.href = url; a.download = 'fee-invoice-coverage-page.csv';
    document.body.append(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
el('feeCoverageFilters').addEventListener('submit', event => { event.preventDefault(); page = 1; load(); });
for (const id of ['feeCoverageBatch','feeCoverageMonth','feeCoverageStatus','feeCoverageSearch','feeCoverageSize'])
    el(id).addEventListener('change', () => { page = 1; clear(); });
el('feeCoveragePrev').addEventListener('click', () => { if (report && page > 1) { page--; load(); } });
el('feeCoverageNext').addEventListener('click', () => { if (report) { page++; load(); } });
el('feeCoverageCsv').addEventListener('click', exportCsv);
el('feeCoveragePrint').addEventListener('click', () => { if (report) window.print(); });
api('/api/finance/fees/options').then(data => {
    options = data;
    const select = el('feeCoverageBatch');
    select.replaceChildren(option('', 'Select academic batch'));
    for (const batch of options.academicBatches || []) select.append(option(batch.id, batch.name));
}).catch(error => notice(error.message, true));
})();
