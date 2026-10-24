(() => {
'use strict';
const root = document.getElementById('studentPaymentPosting');
if (!root) return;
const canPost = root.dataset.post === 'true', api = '/api/finance/payment-posting';
const el = id => document.getElementById(id);
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value;
let page = 1, pages = 1, periods = [];
function notice(message, error = false) {
    const box = el('paymentPostingNotice'); box.textContent = message;
    box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
}
async function request(url, method = 'GET', body) {
    const headers = {Accept:'application/json'};
    if (method !== 'GET') {
        if (!token) throw new Error('Missing antiforgery token. Reload.');
        headers.RequestVerificationToken = token;
    }
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const res = await fetch(url, {method, headers, credentials:'same-origin', cache:'no-store',
        ...(body === undefined ? {} : {body:JSON.stringify(body)})});
    const result = await res.json().catch(() => null);
    if (!res.ok || !result?.success) throw new Error(result?.message || 'Payment posting failed.');
    return result.data;
}
function cell(value) {
    const td = document.createElement('td'); td.textContent = value == null ? '' : String(value); return td;
}
function option(id, label) {
    const o = document.createElement('option'); o.value = String(id); o.textContent = label; return o;
}
function renderSelect(id, entries, label, display) {
    const select = el(id), selected = select.value; select.replaceChildren(option('', label));
    for (const row of entries) select.append(option(row.id, display ? display(row) : row.name));
    if ([...select.options].some(x => x.value === selected)) select.value = selected;
}
function amount(value, currency) {
    return Number(value).toLocaleString('en-BD', {minimumFractionDigits:2, maximumFractionDigits:2}) + ' ' + currency;
}
function periodCovers(row, id) {
    const p = periods.find(x => String(x.id) === String(id));
    return p && row.paymentDate >= p.startDate && row.paymentDate <= p.endDate;
}
function render(data) {
    periods = data.openPeriods || [];
    renderSelect('paymentPostingCampus', data.campuses || [], 'All authorized campuses');
    renderSelect('paymentPostingPeriod', periods, 'Choose a period');
    renderSelect('paymentPostingCash', data.cashAccounts || [], 'Choose cash ledger',
        x => x.code + ' — ' + x.name);
    const body = el('paymentPostingRows'); body.replaceChildren();
    for (const row of data.payments || []) {
        const tr = document.createElement('tr');
        for (const value of [row.receiptNumber, row.studentCode + ' — ' + row.studentName,
            row.campusId, row.paymentDate, amount(row.amount, row.currencyCode),
            row.posted ? 'Posted' : row.journalReference ? 'Reconcile link' : 'Unposted',
            row.journalReference || '—']) tr.append(cell(value));
        if (canPost) {
            const action = document.createElement('td'), button = document.createElement('button');
            button.type = 'button'; button.className = 'btn btn-sm btn-primary'; button.textContent = 'Post GL';
            button.disabled = Boolean(row.journalReference) || row.currencyCode !== 'BDT' || row.amount <= 0;
            button.addEventListener('click', async () => {
                const periodId = Number(el('paymentPostingPeriod').value);
                const cashId = Number(el('paymentPostingCash').value);
                if (!periodId || !periodCovers(row, periodId) || !cashId) {
                    notice('Choose a cash ledger and an open period covering this payment date.', true); return;
                }
                if (!confirm('Debit selected cash ledger and credit invoice receivables for this receipt?')) return;
                button.disabled = true;
                try {
                    const result = await request(api + '/' + encodeURIComponent(row.paymentReference) + '/post',
                        'POST', {accountingPeriodId:periodId, cashAccountId:cashId, rowVersion:row.rowVersion});
                    notice(result.alreadyPosted ? 'Already posted: ' + result.journalNumber :
                        'Posted: ' + result.journalNumber);
                    await load();
                } catch (e) { notice(e.message, true); button.disabled = false; }
            });
            action.append(button); tr.append(action);
        }
        body.append(tr);
    }
    if (!(data.payments || []).length) {
        const tr = document.createElement('tr'), td = cell('No matching single-campus receipts.');
        td.colSpan = canPost ? 8 : 7; tr.append(td); body.append(tr);
    }
    pages = Math.max(1, Math.ceil(data.totalCount / data.pageSize));
    el('paymentPostingPage').textContent = 'Page ' + data.page + ' of ' + pages + ' · ' + data.totalCount + ' receipts';
    el('paymentPostingPrev').disabled = page <= 1;
    el('paymentPostingNext').disabled = page >= pages;
}
async function load() {
    const qs = new URLSearchParams({page:String(page), pageSize:'25',
        unpostedOnly:String(el('paymentPostingUnposted').checked)});
    if (el('paymentPostingCampus').value) qs.set('campusId', el('paymentPostingCampus').value);
    if (el('paymentPostingSearch').value.trim()) qs.set('search', el('paymentPostingSearch').value.trim());
    render(await request(api + '?' + qs));
}
async function run(fn) { try { await fn(); } catch (e) { notice(e.message, true); } }
el('paymentPostingFilters').addEventListener('submit', e => { e.preventDefault(); page = 1; run(load); });
el('paymentPostingPrev').addEventListener('click', () => { if (page > 1) { page--; run(load); } });
el('paymentPostingNext').addEventListener('click', () => { if (page < pages) { page++; run(load); } });
run(load);
})();
