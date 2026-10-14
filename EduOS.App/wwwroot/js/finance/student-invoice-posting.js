(() => {
'use strict';
const root = document.getElementById('studentInvoicePosting');
if (!root) return;
const canPost = root.dataset.post === 'true', api = '/api/finance/invoice-posting';
const el = id => document.getElementById(id);
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value;
let page = 1, pages = 1, periods = [];
function notice(message, error = false) {
    const box = el('invoicePostingNotice'); box.textContent = message;
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
    if (!res.ok || !result?.success) throw new Error(result?.message || 'Finance request failed.');
    return result.data;
}
function cell(value) {
    const td = document.createElement('td'); td.textContent = value == null ? '' : String(value); return td;
}
function option(id, label) {
    const o = document.createElement('option'); o.value = String(id); o.textContent = label; return o;
}
function renderSelect(id, entries, label) {
    const select = el(id), selected = select.value; select.replaceChildren(option('', label));
    for (const row of entries) select.append(option(row.id, row.name));
    if ([...select.options].some(x => x.value === selected)) select.value = selected;
}
function amount(value, currency) {
    return Number(value).toLocaleString('en-BD', {minimumFractionDigits:2, maximumFractionDigits:2}) + ' ' + currency;
}
function periodCovers(row, id) {
    const p = periods.find(x => String(x.id) === String(id));
    return p && row.invoiceDate >= p.startDate && row.invoiceDate <= p.endDate;
}
function render(data) {
    periods = data.openPeriods || [];
    renderSelect('invoicePostingCampus', data.campuses || [], 'All authorized campuses');
    renderSelect('invoicePostingPeriod', periods, 'Choose a period');
    const body = el('invoicePostingRows'); body.replaceChildren();
    for (const row of data.invoices || []) {
        const tr = document.createElement('tr');
        for (const value of [row.invoiceNumber, row.studentCode + ' — ' + row.studentName,
            row.campusId, row.invoiceDate, amount(row.totalAmount, row.currencyCode),
            row.posted ? 'Posted' : row.journalReference ? 'Reconcile link' : 'Unposted',
            row.journalReference || '—']) tr.append(cell(value));
        if (canPost) {
            const action = document.createElement('td'), button = document.createElement('button');
            button.type = 'button'; button.className = 'btn btn-sm btn-primary'; button.textContent = 'Post GL';
            button.disabled = Boolean(row.journalReference) || row.currencyCode !== 'BDT' || row.totalAmount <= 0;
            button.addEventListener('click', async () => {
                const periodId = Number(el('invoicePostingPeriod').value);
                if (!periodId || !periodCovers(row, periodId)) {
                    notice('Choose an open period covering this invoice date.', true); return;
                }
                if (!confirm('Post this student invoice to the institutional general ledger?')) return;
                button.disabled = true;
                try {
                    const result = await request(api + '/' + encodeURIComponent(row.invoiceReference) + '/post',
                        'POST', {accountingPeriodId:periodId, rowVersion:row.rowVersion});
                    notice(result.alreadyPosted ? 'Already posted: ' + result.journalNumber :
                        'Posted: ' + result.journalNumber);
                    await load();
                } catch (e) { notice(e.message, true); button.disabled = false; }
            });
            action.append(button); tr.append(action);
        }
        body.append(tr);
    }
    if (!(data.invoices || []).length) {
        const tr = document.createElement('tr'), td = cell('No matching student invoices.');
        td.colSpan = canPost ? 8 : 7; tr.append(td); body.append(tr);
    }
    pages = Math.max(1, Math.ceil(data.totalCount / data.pageSize));
    el('invoicePostingPage').textContent = 'Page ' + data.page + ' of ' + pages + ' · ' + data.totalCount + ' invoices';
    el('invoicePostingPrev').disabled = page <= 1;
    el('invoicePostingNext').disabled = page >= pages;
}
async function load() {
    const qs = new URLSearchParams({page:String(page), pageSize:'25',
        unpostedOnly:String(el('invoicePostingUnposted').checked)});
    if (el('invoicePostingCampus').value) qs.set('campusId', el('invoicePostingCampus').value);
    if (el('invoicePostingSearch').value.trim()) qs.set('search', el('invoicePostingSearch').value.trim());
    render(await request(api + '?' + qs));
}
async function run(fn) { try { await fn(); } catch (e) { notice(e.message, true); } }
el('invoicePostingFilters').addEventListener('submit', e => { e.preventDefault(); page = 1; run(load); });
el('invoicePostingPrev').addEventListener('click', () => { if (page > 1) { page--; run(load); } });
el('invoicePostingNext').addEventListener('click', () => { if (page < pages) { page++; run(load); } });
run(load);
})();
