(() => {
'use strict';
const root = document.getElementById('feeProjectionApp');
if (!root) return;
const el = id => document.getElementById(id);
let options = null, report = null, generation = 0, controller;
const monetaryKeys = /("(?:grossPerStudent|grossForCurrentCohort|observedInvoiceTotal)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
const now = new Date();
el('feeProjectionStart').value = String(now.getFullYear()) + '-' + String(now.getMonth() + 1).padStart(2, '0');
function cents(value) {
    const raw = String(value ?? '0');
    if (!/^-?\d+(?:\.\d{1,2})?$/.test(raw)) throw Error('Invalid monetary precision.');
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
    el('feeProjectionScope').textContent = 'No verified projection loaded';
    el('feeProjectionCohort').textContent = '';
    el('feeProjectionBasis').textContent = '';
    const tr = document.createElement('tr'), td = cell('No verified projection loaded.');
    td.colSpan = 7; td.className = 'text-center text-muted p-3';
    tr.append(td); el('feeProjectionRows').replaceChildren(tr);
}
function option(id, name) {
    const item = document.createElement('option');
    item.value = String(id); item.textContent = name;
    return item;
}
function updateStructures() {
    invalidate();
    const batch = options?.academicBatches?.find(x => String(x.id) === el('feeProjectionBatch').value);
    const select = el('feeProjectionStructure');
    select.replaceChildren(option('', 'Select fee structure'));
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
    catch { throw Error('Invalid server response.'); }
    if (!response.ok || !json.success) throw Error(json.message || 'Report unavailable.');
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
        throw Error('Choose a valid batch, fee structure, first month and duration.');
    return new URLSearchParams({ academicBatchId: String(academicBatchId),
        feeStructureId: String(feeStructureId), startMonth: start + '-01', months: String(months) });
}
function render(data) {
    if (!data || !Array.isArray(data.months) || data.months.length < 1 || data.months.length > 12)
        throw Error('Unexpected projection response.');
    const body = document.createDocumentFragment();
    for (const m of data.months) {
        if (cents(m.grossPerStudent) < 0n || cents(m.grossForCurrentCohort) !==
            cents(m.grossPerStudent) * BigInt(data.currentEligibleEnrollments))
            throw Error('Gross projection does not reconcile.');
        const due = m.earliestConfiguredDueDate
            ? m.earliestConfiguredDueDate + (m.latestConfiguredDueDate !== m.earliestConfiguredDueDate
                ? ' – ' + m.latestConfiguredDueDate : '') : 'Not configured';
        const tr = document.createElement('tr');
        for (const value of [m.year + '-' + String(m.month).padStart(2, '0'),
            due + (m.hasUnspecifiedDueDays ? ' (some unspecified)' : ''), m.feeHeadCount,
            money(m.grossPerStudent), money(m.grossForCurrentCohort),
            m.observedInvoiceCount, money(m.observedInvoiceTotal)]) tr.append(cell(value));
        body.append(tr);
    }
    el('feeProjectionRows').replaceChildren(body);
    el('feeProjectionScope').textContent = data.campusName + ' / ' + data.academicBatchName +
        ' / ' + data.feeStructureName + ' (' + data.currencyCode + ')';
    el('feeProjectionCohort').textContent = String(data.currentEligibleEnrollments) + ' active enrolments';
    el('feeProjectionBasis').textContent = data.basis;
    report = data;
    el('feeProjectionCsv').disabled = false;
    el('feeProjectionPrint').disabled = false;
}
async function load() {
    if (controller) controller.abort();
    controller = new AbortController();
    const current = ++generation;
    invalidate();
    el('feeProjectionRun').disabled = true;
    try {
        const query = params();
        const data = await api('/api/finance/fees/installment-projection?' + query, controller.signal);
        if (current !== generation) return;
        render(data);
        message('Projection loaded.', false);
    } catch (error) {
        if (current === generation && error.name !== 'AbortError') message(error.message, true);
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
    if (!report) return;
    const rows = [['Month','Earliest due','Latest due','Unspecified due day','Fee heads',
        'Gross per student','Current cohort gross','Observed invoices','Observed invoice total']];
    for (const m of report.months) rows.push([m.year + '-' + String(m.month).padStart(2,'0'),
        m.earliestConfiguredDueDate || '', m.latestConfiguredDueDate || '',
        m.hasUnspecifiedDueDays ? 'Yes' : 'No', m.feeHeadCount,
        money(m.grossPerStudent), money(m.grossForCurrentCohort),
        m.observedInvoiceCount, money(m.observedInvoiceTotal)]);
    const csv = '\uFEFF' + rows.map(row => row.map(csvCell).join(',')).join('\r\n') + '\r\n';
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
    const link = document.createElement('a'); link.href = url; link.download = 'future-fee-projection.csv';
    document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
el('feeProjectionFilters').addEventListener('submit', event => { event.preventDefault(); load(); });
el('feeProjectionBatch').addEventListener('change', updateStructures);
for (const id of ['feeProjectionStructure','feeProjectionStart','feeProjectionMonths'])
    el(id).addEventListener('change', invalidate);
el('feeProjectionCsv').addEventListener('click', exportCsv);
el('feeProjectionPrint').addEventListener('click', () => { if (report) window.print(); });
api('/api/finance/fees/options').then(data => {
    options = data;
    const select = el('feeProjectionBatch');
    select.replaceChildren(option('', 'Select academic batch'));
    for (const batch of data.academicBatches || []) select.append(option(batch.id, batch.name));
    updateStructures();
}).catch(error => message(error.message, true));
})();
