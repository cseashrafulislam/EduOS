(() => {
'use strict';
const root = document.getElementById('loanRecovery'); if (!root) return;
const el = id => document.getElementById(id);
const endpoint = '/api/finance/loan-recoveries';
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value;
let page = 1, pages = 1, selected = null, periods = [], cashAccounts = [], requestId = null;
const notice = (message, error = false) => {
    const box = el('recoveryNotice'); box.textContent = message;
    box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
};
async function api(url, method = 'GET', data) {
    const headers = {Accept:'application/json'};
    if (method !== 'GET') {
        if (!token) throw new Error('Antiforgery token missing; reload the page.');
        headers.RequestVerificationToken = token;
    }
    if (data !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(url, {method, credentials:'same-origin', cache:'no-store',
        headers, ...(data === undefined ? {} : {body:JSON.stringify(data)})});
    const result = await response.json().catch(() => null);
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
    selected = row; requestId = crypto.randomUUID();
    el('recoveryConfirmPanel').classList.remove('d-none');
    el('recoverySelected').textContent = row.employeeName + ' · Loan #' + row.loanId +
        ' · Outstanding BDT ' + money(row.outstandingAmount);
    el('recoveryDate').value = new Date().toISOString().slice(0, 10);
    el('recoveryAmount').value = '';
    el('recoveryAmount').max = String(row.outstandingAmount);
    el('recoveryRemarks').value = '';
    fill('recoveryPeriod', periods, 'Select open period');
    fill('recoveryCash', cashAccounts, 'Select cash-in-hand ledger');
    el('recoveryConfirmPanel').scrollIntoView({behavior:'smooth', block:'start'});
}
async function history(row) {
    try {
        const records = await api(endpoint + '/' + encodeURIComponent(row.loanId) + '/history');
        const body = el('recoveryHistoryRows'); body.replaceChildren();
        el('recoveryHistoryLoan').textContent = row.employeeName + ' · Loan #' + row.loanId;
        for (const record of records) {
            const tr = document.createElement('tr');
            for (const value of [record.recoveryDate, money(record.amount), record.journalNumber, record.remarks]) tr.append(cell(value));
            body.append(tr);
        }
        if (!records.length) { const tr = document.createElement('tr'), td = cell('No posted recoveries.'); td.colSpan = 4; tr.append(td); body.append(tr); }
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
    try {
        const query = new URLSearchParams({openOnly:String(el('recoveryOpen').checked), page:String(page), pageSize:'10'});
        render(await api(endpoint + '?' + query));
    } catch (err) { notice(err.message, true); }
}
el('recoveryFilter').addEventListener('submit', e => { e.preventDefault(); page = 1; load(); });
el('recoveryPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
el('recoveryNext').addEventListener('click', () => { if (page < pages) { page++; load(); } });
el('recoveryCancel').addEventListener('click', () => {
    selected = null; requestId = null; el('recoveryConfirmPanel').classList.add('d-none');
});
el('recoveryForm').addEventListener('submit', async e => {
    e.preventDefault(); if (!selected) return;
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
    const button = el('recoveryConfirm'); button.disabled = true;
    try {
        const result = await api(endpoint + '/' + encodeURIComponent(selected.loanId) + '/post', 'POST',
            {clientRequestId:requestId, recoveryDate:date, amount, accountingPeriodId:period,
                cashAccountId:cash, rowVersion:selected.rowVersion, remarks:el('recoveryRemarks').value.trim() || null});
        notice((result.alreadyPosted ? 'Already posted: ' : 'Recovery posted: ') +
            result.journalNumber + ' · Remaining BDT ' + money(result.outstandingAmount));
        selected = null; requestId = null; el('recoveryConfirmPanel').classList.add('d-none'); await load();
    } catch (err) { notice(err.message, true); } finally { button.disabled = false; }
});
load();
})();
