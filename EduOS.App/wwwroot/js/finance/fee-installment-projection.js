(() => {
'use strict';
const root = document.getElementById('feeProjectionApp');
if (!root) return;
const el = id => document.getElementById(id);
const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
const t = (en, bd) => bn ? bd : en;
const context = () => [document.body?.dataset?.authTenantId || '', document.body?.dataset?.authSessionReference || ''].join('|');
let activeContext = context();
let options = null, report = null, generation = 0, controller;
const monetaryKeys = /("(?:grossPerStudent|grossForCurrentCohort|observedInvoiceTotal)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
const now = new Date();
el('feeProjectionStart').value = String(now.getFullYear()) + '-' + String(now.getMonth() + 1).padStart(2, '0');
function cents(value) {
    const raw = String(value ?? '0');
    if (!/^-?\d+(?:\.\d{1,2})?$/.test(raw)) throw Error(t('Invalid monetary precision.','আর্থিক অঙ্কের দশমিক সঠিক নয়।'));
    const negative = raw.startsWith('-'), parts = (negative ? raw.slice(1) : raw).split('.');
    const n = BigInt(parts[0]) * 100n + BigInt((parts[1] || '').padEnd(2, '0'));
    return negative ? -n : n;
}
function money(value) {
    const n = cents(value), abs = n < 0n ? -n : n;
    return (n < 0n ? '-' : '') + String(abs / 100n) + '.' + String(abs % 100n).padStart(2, '0');
}
function cell(value) {
    const td = document.createElement('td');
    td.textContent = String(value ?? '');
    return td;
}
function message(text, error) {
    const n = el('feeProjectionNotice');
    n.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
    n.textContent = text;
}
function invalidate() {
    report = null;
    el('feeProjectionCsv').disabled = true;
    el('feeProjectionPrint').disabled = true;
    el('feeProjectionScope').textContent = t('No verified projection loaded','কোনো যাচাইকৃত প্রক্ষেপণ লোড হয়নি');
    el('feeProjectionCohort').textContent = '';
    el('feeProjectionBasis').textContent = '';
    const tr = document.createElement('tr'), td = cell(t('No verified projection loaded.','কোনো যাচাইকৃত প্রক্ষেপণ লোড হয়নি।'));
    td.colSpan = 7; td.className = 'text-center text-muted p-3';
    tr.append(td); el('feeProjectionRows').replaceChildren(tr);
}
function cancelStale() {
    ++generation;
    if (controller) controller.abort();
    controller = null;
    el('feeProjectionRun').disabled = false;
    invalidate();
}
function option(id, name) {
    const item = document.createElement('option');
    item.value = String(id); item.textContent = name;
    return item;
}
function updateStructures() {
    cancelStale();
    const batch = options?.academicBatches?.find(x => String(x.id) === el('feeProjectionBatch').value);
    const select = el('feeProjectionStructure');
    select.replaceChildren(option('', t('Select fee structure','ফি কাঠামো বাছুন')));
    if (!batch) return;
    for (const s of options.feeStructures || []) {
        if (s.campusId !== batch.campusId || s.academicYearId !== batch.academicYearId ||
            (s.academicBatchId != null && s.academicBatchId !== batch.id) ||
            (s.academicLevelId != null && s.academicLevelId !== batch.academicLevelId) ||
            (s.academicProgramId != null && s.academicProgramId !== batch.academicProgramId)) continue;
        select.append(option(s.id, s.name));
    }
}
async function api(url, signal) {
    const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store',
        headers: { Accept: 'application/json' }, signal });
    const raw = await response.text();
    let json;
    try { json = JSON.parse(raw.replace(monetaryKeys, '$1"$2"')); }
    catch { throw Error(t('Invalid projection response.','প্রক্ষেপণের উত্তর সঠিক নয়।')); }
    if (response.status === 401 || response.status === 403)
        throw Error(t('Finance access denied. Check session and campus access.','Finance অনুমতি নেই। সেশন ও ক্যাম্পাসের অনুমতি যাচাই করুন।'));
    if (!response.ok || !json?.success)
        throw Error(t('Projection unavailable. Review access and retry.','প্রক্ষেপণ পাওয়া যায়নি। অনুমতি যাচাই করে আবার চেষ্টা করুন।'));
    return json.data;
}
function params() {
    const academicBatchId = Number(el('feeProjectionBatch').value);
    const feeStructureId = Number(el('feeProjectionStructure').value);
    const start = el('feeProjectionStart').value;
    const months = Number(el('feeProjectionMonths').value);
    if (!Number.isSafeInteger(academicBatchId) || academicBatchId <= 0 ||
        !Number.isSafeInteger(feeStructureId) || feeStructureId <= 0 ||
        !/^\d{4}-(0[1-9]|1[0-2])$/.test(start) || ![3,6,9,12].includes(months))
        throw Error(t('Choose valid batch, structure, month and duration.','সঠিক ব্যাচ, কাঠামো, মাস ও সময়সীমা নির্বাচন করুন।'));
    return new URLSearchParams({ academicBatchId: String(academicBatchId),
        feeStructureId: String(feeStructureId), startMonth: start + '-01', months: String(months) });
}
function render(data, query) {
    const expected = Number(query.get('months'));
    if (!data || data.academicBatchId !== Number(query.get('academicBatchId')) ||
        data.feeStructureId !== Number(query.get('feeStructureId')) ||
        !Number.isSafeInteger(data.currentEligibleEnrollments) || data.currentEligibleEnrollments < 0 ||
        !Array.isArray(data.months) || data.months.length !== expected)
        throw Error(t('Projection scope mismatch. Export blocked.','প্রক্ষেপণের পরিসর মিলছে না। এক্সপোর্ট বন্ধ।'));
    const first = query.get('startMonth');
    const firstIndex = Number(first.slice(0,4)) * 12 + Number(first.slice(5,7)) - 1;
    let index = 0;
    const body = document.createDocumentFragment();
    for (const m of data.months) {
        const monthIndex = firstIndex + index++;
        if (m.year !== Math.floor(monthIndex/12) || m.month !== monthIndex%12+1 ||
            !Number.isSafeInteger(m.feeHeadCount) || m.feeHeadCount < 0 ||
            !Number.isSafeInteger(m.observedInvoiceCount) || m.observedInvoiceCount < 0 ||
            typeof m.hasUnspecifiedDueDays !== 'boolean' || cents(m.observedInvoiceTotal) < 0n ||
            cents(m.grossPerStudent) < 0n || cents(m.grossForCurrentCohort) !==
            cents(m.grossPerStudent) * BigInt(data.currentEligibleEnrollments))
            throw Error(t('Projection totals do not reconcile. Export blocked.','প্রক্ষেপণের মোট মিলছে না। এক্সপোর্ট বন্ধ।'));
        for (const date of [m.earliestConfiguredDueDate,m.latestConfiguredDueDate]) {
            if (date != null && !new RegExp('^' + m.year + '-' + String(m.month).padStart(2,'0') + '-\\d{2}$').test(String(date)))
                throw Error(t('Invalid due date.','শেষ তারিখ সঠিক নয়।'));
        }
        const due = m.earliestConfiguredDueDate
            ? m.earliestConfiguredDueDate + (m.latestConfiguredDueDate !== m.earliestConfiguredDueDate
                ? ' – ' + m.latestConfiguredDueDate : '') : t('Not configured','নির্ধারিত নয়');
        const tr = document.createElement('tr');
        for (const value of [m.year + '-' + String(m.month).padStart(2, '0'),
            due + (m.hasUnspecifiedDueDays ? t(' (some unspecified)',' (কিছু অনির্ধারিত)') : ''), m.feeHeadCount,
            money(m.grossPerStudent), money(m.grossForCurrentCohort),
            m.observedInvoiceCount, money(m.observedInvoiceTotal)]) tr.append(cell(value));
        body.append(tr);
    }
    el('feeProjectionRows').replaceChildren(body);
    el('feeProjectionScope').textContent = data.campusName + ' / ' + data.academicBatchName +
        ' / ' + data.feeStructureName + ' (' + data.currencyCode + ')';
    el('feeProjectionCohort').textContent = String(data.currentEligibleEnrollments) + t(' active enrolments','টি সক্রিয় ভর্তি');
    el('feeProjectionBasis').textContent = t(
        'Non-binding gross estimate for current active enrolments; discounts, fines, transfers and future changes excluded. Observed invoices are independent batch totals. No invoice or GL posting occurs.',
        'বর্তমান সক্রিয় ভর্তির আনুমানিক মোট হিসাব; ছাড়, জরিমানা, স্থানান্তর ও ভবিষ্যৎ পরিবর্তন বাদ। পর্যবেক্ষিত ইনভয়েস আলাদা ব্যাচের মোট। এখানে ইনভয়েস বা GL পোস্ট হয় না।');
    report = data;
    el('feeProjectionCsv').disabled = false;
    el('feeProjectionPrint').disabled = false;
}
async function load() {
    if (controller) controller.abort();
    controller = new AbortController();
    const current = ++generation, identity = context();
    let query;
    invalidate();
    el('feeProjectionRun').disabled = true;
    try {
        query = params();
        const data = await api('/api/finance/fees/installment-projection?' + query, controller.signal);
        if (current !== generation || controller.signal.aborted || identity !== context() ||
            query.toString() !== params().toString()) return;
        render(data,query);
        message(t('Projection loaded.','প্রক্ষেপণ লোড হয়েছে।'), false);
    } catch (error) {
        if (current === generation && error.name !== 'AbortError') { invalidate(); message(error.message, true); }
    } finally {
        if (current === generation) el('feeProjectionRun').disabled = false;
    }
}
function csvCell(value) {
    let v = String(value ?? '');
    if (/^[\s\u0000-\u001f]*[=+\-@]/.test(v)) v = "'" + v;
    return '"' + v.replace(/"/g, '""') + '"';
}
function exportCsv() {
    if (!report || context() !== activeContext) { cancelStale(); return; }
    const rows = [[t('Month','মাস'),t('Earliest due','প্রথম শেষ তারিখ'),
        t('Latest due','শেষ তারিখ'),t('Unspecified due day','অনির্ধারিত শেষ দিন'),t('Fee heads','ফি খাত'),
        t('Gross per student','শিক্ষার্থীপ্রতি মোট'),t('Current cohort gross','বর্তমান ব্যাচের মোট'),
        t('Observed invoices','পর্যবেক্ষিত ইনভয়েস'),t('Observed invoice total','পর্যবেক্ষিত ইনভয়েস মোট')]];
    for (const m of report.months) rows.push([m.year + '-' + String(m.month).padStart(2,'0'),
        m.earliestConfiguredDueDate || '', m.latestConfiguredDueDate || '',
        m.hasUnspecifiedDueDays ? t('Yes','হ্যাঁ') : t('No','না'), m.feeHeadCount,
        money(m.grossPerStudent), money(m.grossForCurrentCohort),
        m.observedInvoiceCount, money(m.observedInvoiceTotal)]);
    const csv = '\uFEFF' + rows.map(row => row.map(csvCell).join(',')).join('\r\n') + '\r\n';
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
    const link = document.createElement('a'); link.href = url; link.download = 'future-fee-projection.csv';
    document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
el('feeProjectionFilters').addEventListener('input', cancelStale);
el('feeProjectionFilters').addEventListener('change', cancelStale);
el('feeProjectionFilters').addEventListener('submit', event => { event.preventDefault(); activeContext = context(); load(); });
el('feeProjectionBatch').addEventListener('change', updateStructures);

el('feeProjectionCsv').addEventListener('click', exportCsv);
el('feeProjectionPrint').addEventListener('click', () => { if (report && context() === activeContext) window.print(); });
window.addEventListener('pageshow', event => { if (event.persisted) cancelStale(); });
const initialContext = context();
api('/api/finance/fees/options').then(data => {
    if (initialContext !== context()) { cancelStale(); return; }
    options = data;
    const select = el('feeProjectionBatch');
    select.replaceChildren(option('', t('Select academic batch','একাডেমিক ব্যাচ বাছুন')));
    for (const batch of data.academicBatches || []) select.append(option(batch.id, batch.name));
    updateStructures();
}).catch(error => message(error.message, true));
})()