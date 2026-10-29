(() => {
'use strict';
const root = document.getElementById('payrollJournalPosting'); if (!root) return;
const el = id => document.getElementById(id), canPost = root.dataset.post === 'true';
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value;
const endpoint = '/api/finance/payroll-posting';
let page = 1, totalPages = 1, selected = null, periods = [], accounts = [];
let reversalRun = null, reversalRequest = null;
function requestReversal(row) {
    reversalRun = row;
    el('payrollReversalSelected').textContent = row.runNumber + ' · original GL ' + row.journalReference;
    el('payrollReversalForm').reset();
    el('payrollReversalDate').value = new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10);
    el('payrollReversalPanel').classList.remove('d-none');
    el('payrollReversalReason').focus();
}
async function reviewReversal(row, approve) {
    const note = approve ? null : prompt('Reason for rejecting the contra journal:');
    if (!approve && !note?.trim()) return;
    if (approve && !confirm('Approve the independent payroll contra journal for ' + row.runNumber + '?')) return;
    try {
        await request(endpoint + '/' + encodeURIComponent(row.reference) + '/reverse/review', 'POST',
            {approve, note, rowVersion:row.reversalRowVersion});
        await load();alertBox(approve ? 'Reversal approved; still needs GL posting.' : 'Reversal rejected with audit history.');
    } catch (error) {alertBox(error.message,true);}
}
function alertBox(message, error = false) {
    const box = el('payrollPostingNotice'); box.textContent = message;
    box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
}
async function request(url, method = 'GET', body) {
    const headers = {Accept:'application/json'};
    if (method !== 'GET') {
        if (!token) throw new Error('Missing antiforgery token; reload the page.');
        headers.RequestVerificationToken = token;
    }
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(url, {method, credentials:'same-origin', cache:'no-store',
        headers, ...(body === undefined ? {} : {body:JSON.stringify(body)})});
    const result = await response.json().catch(() => null);
    if (!response.ok || !result?.success) throw new Error(result?.message || 'Payroll posting failed.');
    return result.data;
}
function cell(value) { const x = document.createElement('td'); x.textContent = value == null ? '' : String(value); return x; }
function option(value, text) { const x = document.createElement('option'); x.value = String(value); x.textContent = text; return x; }
function select(id, values, label) {
    const node = el(id), prior = node.value; node.replaceChildren(option('', label));
    for (const x of values) node.append(option(x.id, x.code ? x.code + ' — ' + x.name : x.name));
    if ([...node.options].some(x => x.value === prior)) node.value = prior;
}
function money(value) { return Number(value).toLocaleString('en-BD', {minimumFractionDigits:2, maximumFractionDigits:2}) + ' BDT'; }
function accountSelect(type, field, label) {
    const node = document.createElement('select'); node.className = 'form-select form-select-sm';
    node.dataset.field = field; node.required = true; node.append(option('', label));
    for (const account of accounts.filter(x => type.includes(x.accountType)))
        node.append(option(account.id, account.code + ' — ' + account.name));
    return node;
}
function choose(row) {
    selected = row;
    const panel = el('payrollPostingMappingPanel'); panel.classList.remove('d-none');
    el('payrollPostingSelected').textContent = row.runNumber + ' · ' + row.periodEnd + ' · net ' + money(row.netAmount);
    const table = document.createElement('table'); table.className = 'table table-sm';
    const head = document.createElement('thead'); const hr = document.createElement('tr');
    for (const name of ['Component', 'Amount', 'Posting account', 'Employer liability']) {
        const th = document.createElement('th'); th.textContent = name; hr.append(th);
    }
    head.append(hr); table.append(head);
    const body = document.createElement('tbody');
    for (const component of row.components || []) {
        const tr = document.createElement('tr'); tr.dataset.component = String(component.salaryComponentId);
        const type = typeof component.type === 'string' ? ({Earning:1,Deduction:2,EmployerContribution:3}[component.type] || 0) : Number(component.type);
        tr.append(cell(component.code + ' — ' + component.name), cell(money(component.amount)));
        const primary = document.createElement('td');
        const isLoan = component.isLoanRecovery === true;
        primary.append(accountSelect(isLoan ? ['Asset'] : (type === 2 ? ['Liability'] : ['Expense']), 'accountId',
            isLoan ? 'Original loan receivable asset' : (type === 2 ? 'Withholding liability' : 'Expense ledger')));
        if (isLoan) {
            const note = document.createElement('small'); note.className = 'text-muted d-block';
            note.textContent = 'Each employee loan is reconciled to its disbursement and settled atomically with this journal.';
            primary.append(note);
        }
        tr.append(primary);
        const offset = document.createElement('td');
        if (type === 3) offset.append(accountSelect(['Liability'], 'offsetAccountId', 'Employer liability'));
        tr.append(offset); body.append(tr);
    }
    table.append(body); el('payrollPostingMappings').replaceChildren(table);
    panel.scrollIntoView({behavior:'smooth', block:'start'});
}
function render(data) {
    periods = data.openPeriods || []; accounts = data.accounts || [];
    select('payrollPostingPeriod', periods, 'Select open period');
    select('payrollPostingPayable', accounts.filter(x => x.accountType === 'Liability'), 'Select salary payable liability');
    const body = el('payrollPostingRows'); body.replaceChildren();
    for (const row of data.runs || []) {
        const tr = document.createElement('tr');
        for (const value of [row.runNumber, row.periodEnd, row.campusId || 'Institution-wide',
            money(row.grossAmount), money(row.deductionAmount), money(row.netAmount),
            row.reversalState && [4, 'Posted'].includes(row.reversalState) ? 'Reversed · original preserved' :
            row.reversalState ? 'Reversal ' + ({2:'submitted',3:'approved'}[row.reversalState] || row.reversalState) :
            row.journalReference ? 'Posted · ' + row.journalReference : ([2, 'Calculated'].includes(row.state) ? 'Calculated / approval required' : 'Approved / not posted')]) tr.append(cell(value));
        {
            const action = document.createElement('td'), button = document.createElement('button');
            button.type = 'button'; button.className = 'btn btn-sm btn-primary';
            if (canPost && [2, 'Calculated'].includes(row.state)) {
                button.textContent = 'Approve payroll';
                button.addEventListener('click', async () => {
                    if (!confirm('Approve calculated payroll ' + row.runNumber + '? Maker and approver must differ.')) return;
                    button.disabled = true;
                    try {
                        await request('/api/hr-payroll/runs/' + encodeURIComponent(row.reference) + '/approve',
                            'POST', {rowVersion:row.rowVersion});
                        alertBox('Payroll approved. Select its accounting mappings before GL posting.');
                        await load();
                    } catch (e) { alertBox(e.message, true); } finally { button.disabled = false; }
                });
            } else if (canPost && !row.journalReference && [3, 'Approved'].includes(row.state)) {
                button.textContent = 'Map & post'; button.addEventListener('click', () => choose(row));
            } else if (canPost && row.journalReference && !row.reversalState) {
                button.textContent = 'Request reversal'; button.className = 'btn btn-sm btn-outline-danger';
                button.addEventListener('click', () => requestReversal(row));
            } else if (canPost && row.reversalState && [2, 'Submitted'].includes(row.reversalState)) {
                button.textContent = 'Approve reversal';button.className = 'btn btn-sm btn-outline-warning';
                button.addEventListener('click', () => reviewReversal(row,true));
            } else if (canPost && row.reversalState && [3, 'Approved'].includes(row.reversalState)) {
                button.textContent = 'Post reversal';button.className = 'btn btn-sm btn-outline-danger';
                button.addEventListener('click', async () => {
                    if (!confirm('Post the balanced contra journal? The original payroll remains in history.')) return;
                    button.disabled = true;
                    try {
                        await request(endpoint + '/' + encodeURIComponent(row.reference) + '/reverse/post', 'POST',
                            {rowVersion:row.reversalRowVersion});
                        await load();alertBox('Payroll GL reversed with a separate posted contra journal.');
                    } catch(e) {alertBox(e.message,true);} finally {button.disabled=false;}
                });
            } else {
                button.textContent = '—'; button.disabled = true;
            }
            action.append(button);
            if (canPost && row.reversalState && [2, 3, 'Submitted', 'Approved'].includes(row.reversalState)) {
                const reject = document.createElement('button');reject.type = 'button';
                reject.className = 'btn btn-sm btn-outline-secondary ms-1';reject.textContent = 'Reject';
                reject.addEventListener('click', () => reviewReversal(row,false));action.append(reject);
            }
            tr.append(action);
        }
        body.append(tr);
    }
    if (!data.runs?.length) { const tr = document.createElement('tr'), td = cell('No approved payroll runs found.'); td.colSpan = 8; tr.append(td); body.append(tr); }
    totalPages = Math.max(1, Math.ceil(data.totalCount / data.pageSize));
    el('payrollPostingPage').textContent = 'Page ' + data.page + ' of ' + totalPages + ' · ' + data.totalCount + ' runs';
    el('payrollPostingPrev').disabled = page <= 1;
    el('payrollPostingNext').disabled = page >= totalPages;
}
async function load() {
    try {
        const data = await request(endpoint + '?unpostedOnly=' + el('payrollPostingUnposted').checked +
            '&page=' + page + '&pageSize=5'); render(data);
    } catch (e) { alertBox(e.message, true); }
}
async function post() {
    if (!selected) return;
    const periodId = Number(el('payrollPostingPeriod').value), payable = Number(el('payrollPostingPayable').value);
    const period = periods.find(x => x.id === periodId);
    if (!period || selected.periodEnd < period.startDate || selected.periodEnd > period.endDate || !payable)
        return alertBox('Select an open accounting period covering payroll end date and a net salary payable ledger.', true);
    const mappings = [];
    for (const row of el('payrollPostingMappings').querySelectorAll('tbody tr')) {
        const accountId = Number(row.querySelector('[data-field="accountId"]')?.value);
        const offset = row.querySelector('[data-field="offsetAccountId"]');
        if (!accountId || (offset && !Number(offset.value)))
            return alertBox('Every component must have the required ledger mappings.', true);
        mappings.push({salaryComponentId:Number(row.dataset.component), accountId,
            offsetAccountId:offset ? Number(offset.value) : null});
    }
    if (!mappings.length || !confirm('Post immutable balanced payroll accrual for ' + selected.runNumber + '?'))
        return;
    const button = el('payrollPostingConfirm'); button.disabled = true;
    try {
        const result = await request(endpoint + '/' + encodeURIComponent(selected.reference) + '/post', 'POST',
            {accountingPeriodId:periodId, salaryPayableAccountId:payable,
                rowVersion:selected.rowVersion, mappings});
        alertBox('Journal ' + result.journalNumber + ' posted. Debit ' + money(result.totalDebit) +
            ' / Credit ' + money(result.totalCredit) + '.' +
            (result.loanRecoveriesSettled ? ' Loan installments settled: ' +
                result.loanRecoveriesSettled + ' (' + money(result.loanAmountSettled) + ').' : ''));
        selected = null; el('payrollPostingMappingPanel').classList.add('d-none'); await load();
    } catch (e) { alertBox(e.message, true); } finally { button.disabled = false; }
}
el('payrollPostingFilter').addEventListener('submit', e => {e.preventDefault(); page = 1; load();});
el('payrollPostingPrev').addEventListener('click', () => {if (page > 1) {page--; load();}});
el('payrollPostingNext').addEventListener('click', () => {if (page < totalPages) {page++; load();}});
if (canPost) {
    el('payrollPostingConfirm').addEventListener('click', post);
    el('payrollPostingCancel').addEventListener('click', () => {
        selected = null; el('payrollPostingMappingPanel').classList.add('d-none');
    });
    el('payrollReversalForm').addEventListener('submit',async event => {
        event.preventDefault();
        if (!reversalRun) return;
        const periodId = Number(el('payrollPostingPeriod').value);
        const date = el('payrollReversalDate').value, reason = el('payrollReversalReason').value.trim();
        const period = periods.find(x => x.id === periodId);
        if (!period || !date || date < period.startDate || date > period.endDate || !reason)
            return alertBox('Choose an open accounting period covering reversal date and provide the reason.',true);
        if (!confirm('Request reversal of payroll ' + reversalRun.runNumber + '? Payments and loan recoveries must be cleared first.')) return;
        const payload = {accountingPeriodId:periodId,reversalDate:date,reason,rowVersion:reversalRun.rowVersion};
        const fingerprint = JSON.stringify({reference:reversalRun.reference,...payload});
        if (!reversalRequest || reversalRequest.fingerprint !== fingerprint)
            reversalRequest = {fingerprint,id:crypto.randomUUID()};
        const submit = el('payrollReversalSubmit');submit.disabled = true;
        try {
            await request(endpoint + '/' + encodeURIComponent(reversalRun.reference) + '/reverse/request','POST',
                {...payload,clientRequestId:reversalRequest.id});
            reversalRun=null;reversalRequest=null;
            el('payrollReversalPanel').classList.add('d-none');
            el('payrollPostingUnposted').checked = false;page = 1;
            await load();alertBox('Payroll reversal submitted for independent approval.');
        } catch (error) {alertBox(error.message,true);} finally {submit.disabled=false;}
    });
}
load();
})();
