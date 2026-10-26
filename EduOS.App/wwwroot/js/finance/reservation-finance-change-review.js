(() => {
'use strict';
const root = document.getElementById('reservationFinanceRoot');
const form = document.getElementById('reservationFinanceChangeReviewForm');
if (!root || !form) return;
const alert = document.getElementById('reservationFinanceChangeReviewAlert');
const details = document.getElementById('reservationFinanceChangeReviewDetails');
const action = document.getElementById('reservationFinanceChangeReviewAction');
const start = document.getElementById('reservationFinanceChangeReviewStart');
const end = document.getElementById('reservationFinanceChangeReviewEnd');
const button = form.querySelector('button[type="submit"]');
let scope = null;
function message(value, error) {
    alert.className = 'alert alert-' + (error ? 'danger' : 'warning');
    alert.textContent = value;
}
function cell(name, value) {
    const dt = document.createElement('dt');
    dt.className = 'col-sm-5';
    dt.textContent = name;
    const dd = document.createElement('dd');
    dd.className = 'col-sm-7';
    dd.textContent = String(value ?? '');
    details.append(dt, dd);
}
function reset() {
    scope = null;
    button.disabled = true;
    details.replaceChildren();
    alert.className = 'alert d-none';
}
root.addEventListener('reservationFinancePreflightReset', reset);
root.addEventListener('reservationFinancePreflightLoaded', event => {
    scope = {kind:event.detail.kind.toLowerCase(), id:event.detail.reservationId};
    button.disabled = false;
    details.replaceChildren();
    alert.className = 'alert d-none';
    start.value = '';
    end.value = '';
});
function toggle() {
    const isStart = action.value === 'reschedule-start';
    start.disabled = !isStart;
    end.disabled = isStart;
    if (isStart) end.value = '';
    else start.value = '';
}
action.addEventListener('change', toggle);
toggle();
form.addEventListener('submit', async event => {
    event.preventDefault();
    if (!scope) { message('Inspect a reservation first.', true); return; }
    const isStart = action.value === 'reschedule-start';
    const proposed = isStart ? start.value : end.value;
    if (!proposed) { message('Choose the proposed date.', true); return; }
    const params = new URLSearchParams({
        kind:scope.kind, reservationId:String(scope.id), change:action.value
    });
    params.set(isStart ? 'proposedStartDate' : 'proposedEndDate', proposed);
    button.disabled = true;
    details.replaceChildren();
    try {
        const response = await fetch('/api/finance/adjustments/reservations/change-review?' + params,
            {credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
        const body = await response.json().catch(() => null);
        if (!response.ok || !body?.success || !body.data)
            throw Error(body?.message || 'Finance proposal review failed.');
        const data = body.data;
        cell('Proposed change', data.change);
        cell('Current dates', data.currentStartDate + ' to ' + (data.currentEndDate || 'Open-ended'));
        cell('Proposed dates', data.proposedStartDate + ' to ' + (data.proposedEndDate || 'Open-ended'));
        cell('Finance status', data.status);
        cell('Posted invoice', data.hasPostedInvoiceActivity ? 'YES' : 'No');
        cell('Posted payment', data.hasPostedPaymentActivity ? 'YES' : 'No');
        cell('Issued invoice', data.hasUnpostedIssuedInvoice ? 'YES' : 'No');
        cell('Finance approval', 'NOT GRANTED');
        message(data.decision + ' ' + data.notice, data.financeExposureBlocksChange);
    } catch (error) { message(error.message || 'Finance proposal review failed.', true); }
    finally { button.disabled = !scope; }
});
})();
