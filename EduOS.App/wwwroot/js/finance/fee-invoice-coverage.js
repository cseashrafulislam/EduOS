(() => {
'use strict';
const root = document.getElementById('feeCoverageApp');
if (!root) return;
const el = id => document.getElementById(id);
const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
const t = (en, bd) => bn ? bd : en;
const context = () => [document.body?.dataset?.authTenantId || '', document.body?.dataset?.authSessionReference || ''].join('|');
let activeContext = context();
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
    el('feeCoverageScope').textContent = t('No verified report loaded','কোনো যাচাইকৃত প্রতিবেদন লোড হয়নি');
    for (const id of ['Eligible','Billed','Unbilled','Review']) el('feeCoverage' + id).textContent = '—';
    el('feeCoverageCount').textContent = '';
    el('feeCoveragePage').textContent = '';
    const tr = document.createElement('tr'), td = cell(t('No verified report loaded','কোনো যাচাইকৃত প্রতিবেদন লোড হয়নি'));
    td.colSpan = 6; td.className = 'text-muted text-center p-3';
    tr.append(td); el('feeCoverageRows').replaceChildren(tr);
}
function invalidate() {
    ++serial;
    if (pending) pending.abort();
    pending = null; page = 1;
    el('feeCoverageRun').disabled = false;
    clear();
}
async function api(url, signal) {
    const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store',
        headers: { Accept: 'application/json' }, signal });
    if (response.status === 401 || response.status === 403)
        throw Error(t('Finance access denied. Check session and campus access.','Finance অনুমতি নেই। সেশন ও ক্যাম্পাসের অনুমতি যাচাই করুন।'));
    if (!response.ok) throw Error(t('Report unavailable. Check filters and retry.','প্রতিবেদন পাওয়া যায়নি। ফিল্টার দেখে আবার চেষ্টা করুন।'));
    let json;
    try { json = await response.json(); }
    catch { throw Error(t('Invalid report response.','প্রতিবেদনের উত্তর সঠিক নয়।')); }
    if (!json?.success) throw Error(t('Report unavailable. Review access and retry.','প্রতিবেদন পাওয়া যায়নি। অনুমতি যাচাই করে আবার চেষ্টা করুন।'));
    return json.data;
}
function params() {
    const batch = Number(el('feeCoverageBatch').value);
    const month = el('feeCoverageMonth').value;
    if (!Number.isSafeInteger(batch) || batch <= 0 || !/^\d{4}-(0[1-9]|1[0-2])$/.test(month) ||
        !['all','unbilled','billed','review'].includes(el('feeCoverageStatus').value) ||
        el('feeCoverageSearch').value.trim().length > 100 ||
        ![25,50,100].includes(Number(el('feeCoverageSize').value)))
        throw Error(t('Choose valid batch, month and filters.','সঠিক ব্যাচ, মাস ও ফিল্টার নির্বাচন করুন।'));
    return new URLSearchParams({ academicBatchId: String(batch), billingMonth: month + '-01',
        status: el('feeCoverageStatus').value, search: el('feeCoverageSearch').value.trim(),
        page: String(page), pageSize: el('feeCoverageSize').value });
}
function statusLabel(value) {
    return ({ Billed:t('Billed','বিল করা হয়েছে'), Unbilled:t('Unbilled','বিল হয়নি'),
        Review:t('Review','যাচাই প্রয়োজন') })[value] || '';
}
function render(data, requested, query) {
    if (!data || data.academicBatchId !== Number(query.get('academicBatchId')) ||
        String(data.billingMonth).slice(0,10) !== query.get('billingMonth') ||
        !data.students || data.students.page !== requested ||
        data.students.pageSize !== Number(query.get('pageSize')) ||
        !Number.isSafeInteger(data.students.totalCount) || data.students.totalCount < 0 ||
        !Array.isArray(data.students.items) || data.students.items.length > data.students.pageSize ||
        requested > Math.max(1,Math.ceil(data.students.totalCount/data.students.pageSize)) ||
        (data.students.totalCount > 0 && !data.students.items.length) ||
        ![data.eligible, data.billed, data.unbilled, data.review].every(Number.isSafeInteger) ||
        data.eligible !== data.billed + data.unbilled + data.review ||
        [data.eligible,data.billed,data.unbilled,data.review].some(n=>n<0))
        throw Error(t('Coverage scope or totals mismatch. Export blocked.','কভারেজের পরিসর বা মোট মিলছে না। এক্সপোর্ট বন্ধ।'));
    const body = document.createDocumentFragment();
    for (const item of data.students.items) {
        if (!Number.isSafeInteger(item.invoiceCount) || item.invoiceCount < 0 ||
            !Number.isSafeInteger(item.integrityIssueCount) || item.integrityIssueCount < 0 ||
            !Number.isSafeInteger(item.lineIssueCount) || item.lineIssueCount < 0 ||
            item.lineIssueCount > item.integrityIssueCount ||
            !['Billed','Unbilled','Review'].includes(item.status))
            throw Error(t('Invalid coverage row. Export blocked.','কভারেজ সারি সঠিক নয়। এক্সপোর্ট বন্ধ।'));
        const tr = document.createElement('tr');
        for (const value of [item.studentCode, item.studentName, item.invoiceCount,
            item.integrityIssueCount, item.lineIssueCount, statusLabel(item.status)]) tr.append(cell(value));
        body.append(tr);
    }
    if (!data.students.items.length) {
        const tr = document.createElement('tr'), td = cell(t('No matching students','কোনো শিক্ষার্থী মেলেনি'));
        td.colSpan = 6; td.className = 'text-center text-muted p-3';
        tr.append(td); body.append(tr);
    }
    el('feeCoverageRows').replaceChildren(body);
    el('feeCoverageScope').textContent = data.campusName + ' / ' + data.academicBatchName + ' / ' + data.billingMonth;
    for (const id of ['Eligible','Billed','Unbilled','Review'])
        el('feeCoverage' + id).textContent = String(data[id.toLowerCase()]);
    el('feeCoverageCount').textContent = String(data.students.totalCount) + t(' matching','টি মিলেছে');
    el('feeCoveragePage').textContent = t('Page ','পৃষ্ঠা ') + data.students.page + t(' of ',' / ') +
        Math.max(1,Math.ceil(data.students.totalCount/data.students.pageSize));
    el('feeCoveragePrev').disabled = data.students.page <= 1;
    el('feeCoverageNext').disabled = data.students.page * data.students.pageSize >= data.students.totalCount;
    report = data;
    el('feeCoverageCsv').disabled = !data.students.items.length;
    el('feeCoveragePrint').disabled = !data.students.items.length;
}
async function load() {
    if (pending) pending.abort();
    pending = new AbortController();
    const id = ++serial, identity = context();
    let query;
    clear(); el('feeCoverageRun').disabled = true;
    try {
        query = params();
        const data = await api('/api/finance/fees/invoice-coverage?' + query, pending.signal);
        if (id !== serial || pending.signal.aborted || identity !== context() ||
            query.toString() !== params().toString()) return;
        render(data,page,query); notice(t('Coverage loaded.','কভারেজ লোড হয়েছে।'), false);
    } catch (error) {
        if (id === serial && error.name !== 'AbortError') { clear(); notice(error.message, true); }
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
    if (context() !== activeContext) { invalidate(); return; }
    const rows = [[t('Student code','শিক্ষার্থী কোড'),t('Student name','শিক্ষার্থীর নাম'),
        t('Invoices','ইনভয়েস'),t('Integrity issues','অসামঞ্জস্য'),
        t('Line issues','লাইন সমস্যা'),t('Status','অবস্থা')]];
    for (const x of report.students.items)
        rows.push([x.studentCode, x.studentName, x.invoiceCount, x.integrityIssueCount, x.lineIssueCount, statusLabel(x.status)]);
    const csv = '\uFEFF' + rows.map(row => row.map(csvCell).join(',')).join('\r\n') + '\r\n';
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
    const a = document.createElement('a');
    a.href = url; a.download = 'fee-invoice-coverage-page.csv';
    document.body.append(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
el('feeCoverageFilters').addEventListener('input', invalidate);
el('feeCoverageFilters').addEventListener('change', invalidate);
el('feeCoverageFilters').addEventListener('submit', event => {
    event.preventDefault(); page = 1; activeContext = context(); load();
});
el('feeCoveragePrev').addEventListener('click', () => { if (report && page > 1) { page--; load(); } });
el('feeCoverageNext').addEventListener('click', () => { if (report) { page++; load(); } });
el('feeCoverageCsv').addEventListener('click', exportCsv);
el('feeCoveragePrint').addEventListener('click', () => { if (report && context() === activeContext) window.print(); });
window.addEventListener('pageshow', event => { if (event.persisted) invalidate(); });
const initialContext = context();
api('/api/finance/fees/options').then(data => {
    if (initialContext !== context()) { invalidate(); return; }
    options = data;
    const select = el('feeCoverageBatch');
    select.replaceChildren(option('', t('Select academic batch','একাডেমিক ব্যাচ বাছুন')));
    for (const batch of options.academicBatches || []) select.append(option(batch.id, batch.name));
}).catch(error => notice(error.message, true));
})();
