(() => {
'use strict';
const root = document.getElementById('payrollPaymentPosting'); if (!root) return;
const el = id => document.getElementById(id);
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value;
const canPost = root.dataset.post === 'true', endpoint = '/api/finance/payroll-payments';
let page = 1, totalPages = 1, selected = null, accounts = [];
function notice(message, error = false) {
    const box = el('payrollPaymentNotice'); box.textContent = message;
    box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
}
async function api(url, method = 'GET', body) {
    const headers = {Accept:'application/json'};
    if (method !== 'GET') {
        if (!token) throw new Error('Missing antiforgery token; reload.');
        headers.RequestVerificationToken = token;
    }
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(url, {method, credentials:'same-origin', cache:'no-store',
        headers, ...(body === undefined ? {} : {body:JSON.stringify(body)})});
    const payload = await response.json().catch(() => null);
    if (!response.ok || !payload?.success) throw new Error(payload?.message || 'Payment settlement failed.');
    return payload.data;
}
function option(value, label) {
    const x = document.createElement('option'); x.value = String(value); x.textContent = label; return x;
}
function cell(value) {
    const td = document.createElement('td'); td.textContent = value == null ? '' : String(value); return td;
}
function money(amount) { return Number(amount).toLocaleString('en-BD', {minimumFractionDigits:2, maximumFractionDigits:2}) + ' BDT'; }
async function bankReview(runReference, button) {
    if (!runReference || !confirm('Download a sensitive REVIEW_ONLY bank transfer worksheet for the entire payroll run? This does not send money.')) return;
    button.disabled = true;
    try {
        const response = await fetch(endpoint + '/bank-review.csv?' + new URLSearchParams({runReference}),
            {credentials:'same-origin', cache:'no-store', headers:{Accept:'text/csv'}});
        if (!response.ok) {
            const payload = await response.json().catch(() => null);
            throw new Error(payload?.message || 'Bank review export failed.');
        }
        if (!response.headers.get('content-type')?.includes('text/csv')) throw new Error('Unexpected bank export format.');
        const content = await response.blob();
        const url = URL.createObjectURL(content), link = document.createElement('a');
        link.href = url; link.download = 'payroll-bank-review-' + runReference.replace(/[^a-f0-9-]/gi, '') + '.csv';
        document.body.append(link); link.click(); link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        notice('Sensitive bank review worksheet downloaded. No payment status was changed.');
    } catch (error) { notice(error.message, true); }
    finally { button.disabled = false; }
}
function choose(row) {
    selected = row;
    el('payrollPaymentConfirmPanel').classList.remove('d-none');
    const method = Number(row.paymentMethod);
    const bankTransfer = method === 2 || row.paymentMethod === 'BankTransfer';
    el('payrollPaymentSelected').textContent = row.employeeName + ' · ' + row.runNumber +
        ' · ' + money(row.amount) + ' · ' + (bankTransfer ? 'Bank transfer' : 'Cash');
    const select = el('payrollPaymentAccount'); select.replaceChildren(option('', 'Select settlement ledger'));
    const available = bankTransfer ? accounts.filter(x => String(x.id) === String(row.bankLedgerId)) :
        accounts.filter(x => /cash/i.test(x.code + ' ' + x.name));
    for (const x of available) select.append(option(x.id, x.code + ' — ' + x.name));
    if (bankTransfer && row.bankLedgerId) select.value = String(row.bankLedgerId);
    select.disabled = bankTransfer;
    if (bankTransfer && !available.length) notice('Bank account is not mapped to an active BDT asset ledger.', true);
    el('payrollPaymentConfirmPanel').scrollIntoView({behavior:'smooth', block:'start'});
}
function render(data) {
    accounts = data.accounts || [];
    const periods = el('payrollPaymentPeriod'), old = periods.value;
    periods.replaceChildren(option('', 'Select open period'));
    for (const p of data.openPeriods || []) periods.append(option(p.id, p.name + ' · ' + p.startDate + '–' + p.endDate));
    if ([...periods.options].some(x => x.value === old)) periods.value = old;
    const tbody = el('payrollPaymentRows'); tbody.replaceChildren();
    for (const row of data.payments || []) {
        const tr = document.createElement('tr');
        const method = Number(row.paymentMethod) === 2 || row.paymentMethod === 'BankTransfer' ? 'Bank transfer' : 'Cash';
        for (const value of [row.runNumber, row.employeeName, row.campusId ?? 'Institution-wide',
            row.paymentDate, method, money(row.amount), money(row.netSalaryAmount),
            money(row.settledAmount), money(row.remainingAmount),
            row.journalReference ? 'Posted · ' + row.journalReference : 'Awaiting verification',
            row.reconciliationIssue || row.reconciliationStatus || 'Not checked']) tr.append(cell(value));
        const action = document.createElement('td'), button = document.createElement('button');
        button.type = 'button'; button.className = 'btn btn-sm btn-primary';
        button.textContent = 'Verify & post';
        const pending = Number(row.state) === 2 || row.state === 'AwaitingVerification';
        button.disabled = !canPost || !pending || !!row.journalReference ||
            row.reconciliationStatus !== 'Pending';
        button.addEventListener('click', () => choose(row)); action.append(button);
        if (canPost && method === 'Bank transfer' && pending && row.runReference) {
            const exportButton = document.createElement('button'); exportButton.type = 'button';
            exportButton.className = 'btn btn-sm btn-outline-secondary ms-1';
            exportButton.textContent = 'Bank review CSV';
            exportButton.addEventListener('click', () => bankReview(row.runReference, exportButton));
            action.append(exportButton);
        }
        tr.append(action);
        tbody.append(tr);
    }
    if (!data.payments?.length) { const tr = document.createElement('tr'), td = cell('No payroll payment requests found.'); td.colSpan = 12; tr.append(td); tbody.append(tr); }
    totalPages = Math.max(1, Math.ceil((data.totalCount || 0) / (data.pageSize || 10)));
    el('payrollPaymentPage').textContent = 'Page ' + page + ' / ' + totalPages + ' · ' + (data.totalCount || 0) + ' requests';
    el('payrollPaymentPrev').disabled = page <= 1;
    el('payrollPaymentNext').disabled = page >= totalPages;
}
async function load() {
    try {
        const query = new URLSearchParams({pendingOnly: String(el('payrollPaymentPending').checked),
            page:String(page), pageSize:'10'});
        render(await api(endpoint + '?' + query));
    } catch (e) { notice(e.message, true); }
}
el('payrollPaymentFilter').addEventListener('submit', e => { e.preventDefault(); page = 1; load(); });
el('payrollPaymentPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
el('payrollPaymentNext').addEventListener('click', () => { if (page < totalPages) { page++; load(); } });
if (canPost) {
    el('payrollPaymentCancel').addEventListener('click', () => {
        selected = null; el('payrollPaymentConfirmPanel').classList.add('d-none');
    });
    el('payrollPaymentConfirm').addEventListener('click', async e => {
        if (!selected) return;
        const period = Number(el('payrollPaymentPeriod').value);
        const method = Number(selected.paymentMethod);
        const bankTransfer = method === 2 || selected.paymentMethod === 'BankTransfer';
        const account = Number(bankTransfer ? selected.bankLedgerId : el('payrollPaymentAccount').value);
        if (!Number.isSafeInteger(period) || period <= 0 || !Number.isSafeInteger(account) || account <= 0) {
            notice('Choose an open accounting period and valid settlement ledger.', true); return;
        }
        if (!confirm('Verify this payroll disbursement and create an immutable balanced journal?')) return;
        e.currentTarget.disabled = true;
        try {
            const result = await api(endpoint + '/' + encodeURIComponent(selected.reference) + '/post',
                'POST', {accountingPeriodId:period, settlementAccountId:account, rowVersion:selected.rowVersion});
            notice((result.alreadyPosted ? 'Already posted: ' : 'Payroll payment posted: ') + result.journalNumber);
            selected = null; el('payrollPaymentConfirmPanel').classList.add('d-none'); await load();
        } catch (err) { notice(err.message, true); } finally { e.currentTarget.disabled = false; }
    });
}
load();
})();
