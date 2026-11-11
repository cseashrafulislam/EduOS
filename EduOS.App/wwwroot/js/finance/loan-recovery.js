(() => {
'use strict';
const root = document.getElementById('loanRecovery'); if (!root) return;
const el = id => document.getElementById(id);
const endpoint = '/api/finance/loan-recoveries';
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value;
let page = 1, pages = 1, selected = null, periods = [], cashAccounts = [], requestId = null;
let fingerprint = '', posting = false, loadSequence = 0, historySequence = 0;
const context = [document.body?.dataset.authTenantId || '', document.body?.dataset.authSessionReference || ''];
const live = () => context[0] === (document.body?.dataset.authTenantId || '') &&
    context[1] === (document.body?.dataset.authSessionReference || '') &&
    window.EduOSSessionTransitionPending?.() !== true;
const notice = (message, error = false) => {
    const box = el('recoveryNotice'); box.textContent = message;
    box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
};
async function api(url, method = 'GET', data) {
    if (!live()) throw new Error('Institution session changed. Reload the page.');
    const headers = {Accept:'application/json'};
    if (method !== 'GET') {
        if (!token) throw new Error('Antiforgery token missing; reload the page.');
        headers.RequestVerificationToken = token;
    }
    if (data !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(url, {method, credentials:'same-origin', cache:'no-store',
        headers, ...(data === undefined ? {} : {body:JSON.stringify(data)})});
    const result = await response.json().catch(() => null);
    if (!live()) throw new Error('Institution session changed. Reload the page.');
    if (!response.ok || !result?.success) throw new Error(result?.message || 'Loan recovery failed.');
    return result.data;
}
const option = (value, label) => {
    const node = document.createElement('option'); node.value = String(value); node.textContent = label; return node;
};
const cell = value => {
    const node = document.createElement('td'); node.textContent = value == null ? '' : String(value); return node;
};
const money = value => Number(value).toLocaleString('en-BD', {minimumFractionDigits:2, maximumFractionDigits:2});
function fill(id, values, label) {
    const select = el(id); select.replaceChildren(option('', label));
    for (const x of values) select.append(option(x.id, (x.code ? x.code + ' — ' : '') + x.name));
}
function choose(row) {
    if (posting || !live()) return;
    selected = row; requestId = null; fingerprint = '';
    el('recoveryConfirmPanel').classList.remove('d-none');
    el('recoverySelected').textContent = row.employeeName + ' · Loan #' + row.loanId +
        ' · Outstanding BDT ' + money(row.outstandingAmount);
    const now = new Date();
    el('recoveryDate').value = new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
    el('recoveryAmount').value = '';
    el('recoveryAmount').max = String(row.outstandingAmount);
    el('recoveryRemarks').value = '';
    fill('recoveryPeriod', periods, 'Select open period');
    fill('recoveryCash', cashAccounts, 'Select cash-in-hand ledger');
    el('recoveryConfirmPanel').scrollIntoView({behavior:'smooth', block:'start'});
}
async function history(row) {
    const sequence = ++historySequence;
    try {
        const records = await api(endpoint + '/' + encodeURIComponent(row.loanId) + '/history');
        if (!live() || sequence !== historySequence) return;
        const body = el('recoveryHistoryRows'); body.replaceChildren();
        el('recoveryHistoryLoan').textContent = row.employeeName + ' · Loan #' + row.loanId;
        for (const record of records) {
            const tr = document.createElement('tr');
            const status = record.isCompensated ? 'Compensated by ' + (record.compensationJournalNumber || 'payroll reversal') : 'Effective';
            for (const value of [record.recoveryDate, money(record.amount), record.journalNumber, status, record.remarks]) tr.append(cell(value));
            body.append(tr);
        }
        if (!records.length) { const tr = document.createElement('tr'), td = cell('No posted recoveries.'); td.colSpan = 5; tr.append(td); body.append(tr); }
        el('recoveryHistoryPanel').classList.remove('d-none');
    } catch (err) { notice(err.message, true); }
}
function render(data) {
    periods = data.openPeriods || []; cashAccounts = data.cashAccounts || [];
    const body = el('recoveryRows'); body.replaceChildren();
    for (const row of data.loans || []) {
        const tr = document.createElement('tr');
        for (const value of [row.loanId, row.employeeName, row.issueDate,
            money(row.principalAmount), money(row.outstandingAmount), row.disbursementJournalReference]) tr.append(cell(value));
        const action = document.createElement('td');
        const post = document.createElement('button'); post.type = 'button';
        post.className = 'btn btn-sm btn-primary me-1'; post.textContent = 'Record cash';
        post.disabled = row.isClosed || Number(row.outstandingAmount) <= 0;
        post.addEventListener('click', () => choose(row)); action.append(post);
        const view = document.createElement('button'); view.type = 'button';
        view.className = 'btn btn-sm btn-outline-secondary'; view.textContent = 'History';
        view.addEventListener('click', () => history(row)); action.append(view); tr.append(action); body.append(tr);
    }
    if (!data.loans?.length) {
        const tr = document.createElement('tr'), td = cell('No disbursed employee loans found.');
        td.colSpan = 7; tr.append(td); body.append(tr);
    }
    pages = Math.max(1, Math.ceil((data.totalCount || 0) / (data.pageSize || 10)));
    el('recoveryPage').textContent = 'Page ' + page + ' / ' + pages + ' · ' + (data.totalCount || 0) + ' loans';
    el('recoveryPrev').disabled = page <= 1; el('recoveryNext').disabled = page >= pages;
}
async function load() {
    const sequence = ++loadSequence;
    try {
        const query = new URLSearchParams({openOnly:String(el('recoveryOpen').checked), page:String(page), pageSize:'10'});
        const result = await api(endpoint + '?' + query);
        if (sequence === loadSequence && live()) render(result);
    } catch (err) { if (sequence === loadSequence) notice(err.message, true); }
}
el('recoveryFilter').addEventListener('submit', e => { e.preventDefault(); page = 1; load(); });
el('recoveryPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
el('recoveryNext').addEventListener('click', () => { if (page < pages) { page++; load(); } });
el('recoveryCancel').addEventListener('click', () => {
    if (posting) return;
    selected = null; requestId = null; el('recoveryConfirmPanel').classList.add('d-none');
});
el('recoveryForm').addEventListener('submit', async e => {
    e.preventDefault(); if (!selected || posting || !live()) return;
    const period = Number(el('recoveryPeriod').value), cash = Number(el('recoveryCash').value),
        amount = Number(el('recoveryAmount').value), date = el('recoveryDate').value;
    if (![period, cash].every(x => Number.isSafeInteger(x) && x > 0) ||
        !Number.isFinite(amount) || amount <= 0 || amount > Number(selected.outstandingAmount) ||
        amount > 999999999999 || !Number.isSafeInteger(Math.round(amount * 100)) ||
        Math.abs(Math.round(amount * 100) / 100 - amount) > 0.00000001 ||
        !/^\d{4}-\d{2}-\d{2}$/.test(date)) {
        notice('Enter a valid date, cash ledger, open period and positive amount not exceeding outstanding.', true); return;
    }
    if (!confirm('Record cash received and post an immutable balanced institutional journal?')) return;
    const loan = selected;
    const command = [loan.loanId, loan.rowVersion, period, cash, amount.toFixed(2), date,
        el('recoveryRemarks').value.trim()].join('|');
    if (command !== fingerprint) { fingerprint = command; requestId = crypto.randomUUID(); }
    const button = el('recoveryConfirm'); posting = true; button.disabled = true;
    try {
        const result = await api(endpoint + '/' + encodeURIComponent(loan.loanId) + '/post', 'POST',
            {clientRequestId:requestId, recoveryDate:date, amount, accountingPeriodId:period,
                cashAccountId:cash, rowVersion:loan.rowVersion, remarks:el('recoveryRemarks').value.trim() || null});
        notice((result.alreadyPosted ? 'Already posted: ' : 'Recovery posted: ') +
            result.journalNumber + ' · Remaining BDT ' + money(result.outstandingAmount));
        selected = null; requestId = null; fingerprint = '';
        el('recoveryConfirmPanel').classList.add('d-none'); await load();
    } catch (err) { notice(err.message, true); } finally { posting = false; button.disabled = false; }
});
const observer = new MutationObserver(() => {
    if (live()) return;
    ++loadSequence; ++historySequence;
    selected = null; periods = []; cashAccounts = []; requestId = null; fingerprint = '';
    el('recoveryRows').replaceChildren(); el('recoveryHistoryRows').replaceChildren();
    el('recoveryHistoryPanel').classList.add('d-none');
    el('recoveryConfirmPanel').classList.add('d-none');
    notice('Institution session changed. Reload the page.', true);
    observer.disconnect();
});
observer.observe(document.body, { attributes: true,
    attributeFilter: ['data-auth-tenant-id', 'data-auth-session-reference'] });
load();
})();
