(() => {
'use strict';
const root = document.getElementById('loanDisbursement'); if (!root) return;
const el = id => document.getElementById(id);
const endpoint = '/api/finance/loan-disbursements';
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value;
let page = 1, pages = 1, selected = null, periods = [], accounts = [];
function notice(message, error = false) {
    const box = el('loanNotice'); box.textContent = message;
    box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
}
async function api(url, method = 'GET', body) {
    const headers = {Accept:'application/json'};
    if (method !== 'GET') {
        if (!token) throw new Error('Antiforgery token missing; reload the page.');
        headers.RequestVerificationToken = token;
    }
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(url, {method, credentials:'same-origin', cache:'no-store',
        headers, ...(body === undefined ? {} : {body:JSON.stringify(body)})});
    const result = await response.json().catch(() => null);
    if (!response.ok || !result?.success) throw new Error(result?.message || 'Loan operation failed.');
    return result.data;
}
function option(value, label) {
    const node = document.createElement('option'); node.value = String(value); node.textContent = label; return node;
}
function cell(value) {
    const node = document.createElement('td'); node.textContent = value == null ? '' : String(value); return node;
}
function money(value) { return Number(value).toLocaleString('en-BD', {minimumFractionDigits:2, maximumFractionDigits:2}); }
function fill(id, options, label) {
    const select = el(id); select.replaceChildren(option('', label));
    for (const x of options) select.append(option(x.id, x.code ? x.code + ' — ' + x.name : x.name));
}
function choose(row) {
    selected = row;
    el('loanConfirmPanel').classList.remove('d-none');
    el('loanSelected').textContent = row.employeeName + ' · Loan #' + row.loanId + ' · BDT ' + money(row.principalAmount);
    el('loanDate').value = row.issueDate;
    fill('loanPeriod', periods, 'Select open period');
    fill('loanReceivable', accounts.filter(x => !/cash/i.test(x.code + ' ' + x.name)), 'Select loan receivable');
    fill('loanCash', accounts.filter(x => /cash/i.test(x.code + ' ' + x.name)), 'Select cash-in-hand');
    el('loanConfirmPanel').scrollIntoView({behavior:'smooth', block:'start'});
}
function render(data) {
    periods = data.openPeriods || []; accounts = data.assetAccounts || [];
    const body = el('loanRows'); body.replaceChildren();
    for (const row of data.loans || []) {
        const tr = document.createElement('tr');
        for (const value of [row.loanId, row.employeeName, row.issueDate,
            money(row.principalAmount), money(row.outstandingAmount), money(row.installmentAmount),
            row.journalReference ? 'Posted · ' + row.journalReference : 'Not disbursed']) tr.append(cell(value));
        const action = document.createElement('td'), button = document.createElement('button');
        button.type = 'button'; button.className = 'btn btn-sm btn-primary';
        button.textContent = 'Post disbursement';
        button.disabled = !!row.journalReference || Number(row.outstandingAmount) !== Number(row.principalAmount);
        button.addEventListener('click', () => choose(row)); action.append(button); tr.append(action);
        body.append(tr);
    }
    if (!data.loans?.length) {
        const tr = document.createElement('tr'), td = cell('No matching employee loans.'); td.colSpan = 8;
        tr.append(td); body.append(tr);
    }
    pages = Math.max(1, Math.ceil((data.totalCount || 0) / (data.pageSize || 10)));
    el('loanPage').textContent = 'Page ' + page + ' / ' + pages + ' · ' + (data.totalCount || 0) + ' loans';
    el('loanPrev').disabled = page <= 1; el('loanNext').disabled = page >= pages;
}
async function load() {
    try {
        const query = new URLSearchParams({pendingOnly:String(el('loanPending').checked),
            page:String(page), pageSize:'10'});
        render(await api(endpoint + '?' + query));
    } catch (err) { notice(err.message, true); }
}
el('loanFilter').addEventListener('submit', e => { e.preventDefault(); page = 1; load(); });
el('loanPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
el('loanNext').addEventListener('click', () => { if (page < pages) { page++; load(); } });
el('loanCancel').addEventListener('click', () => {
    selected = null; el('loanConfirmPanel').classList.add('d-none');
});
el('loanConfirm').addEventListener('click', async e => {
    if (!selected) return;
    const period = Number(el('loanPeriod').value), receivable = Number(el('loanReceivable').value),
        cash = Number(el('loanCash').value), date = el('loanDate').value;
    if (![period, receivable, cash].every(x => Number.isSafeInteger(x) && x > 0) ||
        receivable === cash || !/^\d{4}-\d{2}-\d{2}$/.test(date)) {
        notice('Choose a date, open period, loan receivable and a distinct cash ledger.', true); return;
    }
    if (!confirm('Record the cash disbursement and post an immutable balanced institutional journal?')) return;
    e.currentTarget.disabled = true;
    try {
        const result = await api(endpoint + '/' + encodeURIComponent(selected.loanId) + '/post', 'POST',
            {disbursementDate:date, accountingPeriodId:period, loanReceivableAccountId:receivable,
                cashAccountId:cash, rowVersion:selected.rowVersion});
        notice((result.alreadyPosted ? 'Already posted: ' : 'Disbursement posted: ') + result.journalNumber);
        selected = null; el('loanConfirmPanel').classList.add('d-none'); await load();
    } catch (err) { notice(err.message, true); } finally { e.currentTarget.disabled = false; }
});
load();
})();
