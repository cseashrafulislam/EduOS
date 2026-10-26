(() => {
'use strict';
const root = document.getElementById('reservationFinanceRoot');
const form = document.getElementById('reservationFinanceWorklistForm');
if (!root || !form) return;
const alert = document.getElementById('reservationFinanceWorklistAlert');
const rows = document.getElementById('reservationFinanceWorklistRows');
const count = document.getElementById('reservationFinanceWorklistPage');
const prev = document.getElementById('reservationFinanceWorklistPrev');
const next = document.getElementById('reservationFinanceWorklistNext');
const button = form.querySelector('button[type="submit"]');
let page = 1;
let total = 0;
const pageSize = 25;
function message(text, error) {
    alert.className = 'alert alert-' + (error ? 'danger' : 'info');
    alert.textContent = text;
}
function cell(row, value) {
    const td = document.createElement('td');
    td.textContent = String(value ?? '');
    row.appendChild(td);
}
function render(data) {
    rows.replaceChildren();
    const list = data.reservations;
    total = list.totalCount;
    for (const item of list.items) {
        const tr = document.createElement('tr');
        cell(tr, item.reservationId);
        cell(tr, item.campusId);
        cell(tr, item.studentId);
        cell(tr, item.startDate);
        cell(tr, item.endDate || 'Open-ended');
        cell(tr, item.monthlyCharge);
        cell(tr, item.securityDeposit);
        const exposure = [
            item.hasPostedInvoiceActivity ? 'Posted invoice' : '',
            item.hasPostedPaymentActivity ? 'Posted payment' : '',
            item.hasUnpostedIssuedInvoice ? 'Issued invoice' : '',
            item.monthlyCharge !== 0 || item.securityDeposit !== 0 || item.hasFeeHead ? 'Chargeable' : ''
        ].filter(Boolean).join('; ');
        cell(tr, exposure || 'No known exposure');
        const td = document.createElement('td');
        const inspect = document.createElement('button');
        inspect.type = 'button';
        inspect.className = 'btn btn-sm btn-outline-primary';
        inspect.textContent = 'Inspect preflight';
        inspect.addEventListener('click', () => {
            document.getElementById('reservationFinanceKind').value = item.kind.toLowerCase();
            document.getElementById('reservationFinanceId').value = String(item.reservationId);
            document.getElementById('reservationFinanceForm').requestSubmit();
        });
        td.appendChild(inspect);
        tr.appendChild(td);
        rows.appendChild(tr);
    }
    if (!list.items.length) {
        const tr = document.createElement('tr');
        const td = document.createElement('td');
        td.colSpan = 9;
        td.textContent = 'No reservations match these filters.';
        tr.appendChild(td);
        rows.appendChild(tr);
    }
    count.textContent = 'Page ' + page + ' · ' + total + ' matching reservations';
    prev.disabled = page <= 1;
    next.disabled = page * pageSize >= total;
}
async function load() {
    const kind = document.getElementById('reservationFinanceWorklistKind').value;
    const campus = document.getElementById('reservationFinanceWorklistCampus').value.trim();
    const from = document.getElementById('reservationFinanceWorklistFrom').value;
    const to = document.getElementById('reservationFinanceWorklistTo').value;
    if (campus && (!/^\d+$/.test(campus) || Number(campus) < 1 || !Number.isSafeInteger(Number(campus)))) {
        message('Campus ID must be a positive integer.', true); return;
    }
    if (from && to && from > to) {
        message('Start date From cannot be later than To.', true); return;
    }
    const params = new URLSearchParams({kind, page:String(page), pageSize:String(pageSize),
        onlyReviewRequired: String(document.getElementById('reservationFinanceWorklistReview').checked)});
    if (campus) params.set('campusId', campus);
    if (from) params.set('startFrom', from);
    if (to) params.set('startTo', to);
    button.disabled = true;
    try {
        const response = await fetch('/api/finance/adjustments/reservations/worklist?' + params.toString(),
            {credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
        const body = await response.json().catch(() => null);
        if (!response.ok || !body?.success || !body.data?.reservations)
            throw Error(body?.message || 'Finance review queue failed.');
        render(body.data);
        message('Read-only review queue loaded. No reservation or financial mutation was approved.', false);
    } catch (error) {
        rows.replaceChildren();
        total = 0;
        prev.disabled = true;
        next.disabled = true;
        count.textContent = 'Unavailable';
        message(error.message || 'Unable to load Finance review queue.', true);
    } finally { button.disabled = false; }
}
form.addEventListener('submit', event => { event.preventDefault(); page = 1; load(); });
prev.addEventListener('click', () => { if (page > 1) { page--; load(); } });
next.addEventListener('click', () => { if (page * pageSize < total) { page++; load(); } });
})();
