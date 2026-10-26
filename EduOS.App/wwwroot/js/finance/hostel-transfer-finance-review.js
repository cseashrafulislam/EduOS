(() => {
'use strict';
const root = document.getElementById('reservationFinanceRoot');
const form = document.getElementById('hostelTransferFinanceReviewForm');
if (!root || !form) return;
const alert = document.getElementById('hostelTransferFinanceAlert');
const details = document.getElementById('hostelTransferFinanceDetails');
const destination = document.getElementById('hostelTransferFinanceDestination');
const effective = document.getElementById('hostelTransferFinanceEffective');
const button = form.querySelector('button[type="submit"]');
let scope = null;
function message(value, error) {
    alert.className = 'alert alert-' + (error ? 'danger' : 'warning');
    alert.textContent = value;
}
function cell(label, value) {
    const dt = document.createElement('dt');
    dt.className = 'col-sm-5';
    dt.textContent = label;
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
    reset();
    if (event.detail.kind !== 'Hostel') {
        message('Occupied transfer review is available for Hostel reservations only.', true);
        return;
    }
    scope = {reservationId:event.detail.reservationId};
    destination.value = '';
    effective.value = '';
    button.disabled = false;
});
form.addEventListener('submit', async event => {
    event.preventDefault();
    if (!scope) { message('Inspect an active Hostel reservation first.', true); return; }
    const bed = destination.value;
    const date = effective.value;
    if (!/^\d+$/.test(bed) || !Number.isSafeInteger(Number(bed)) ||
        Number(bed) <= 0 || !/^\d{4}-\d{2}-\d{2}$/.test(date)) {
        message('Enter a valid destination bed and effective date.', true); return;
    }
    button.disabled = true;
    details.replaceChildren();
    try {
        const params = new URLSearchParams({
            reservationId:String(scope.reservationId), destinationBedId:bed, effectiveDate:date
        });
        const response = await fetch('/api/finance/adjustments/reservations/hostel-transfer-review?' + params,
            {credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
        const body = await response.json().catch(() => null);
        if (!response.ok || !body?.success || !body.data)
            throw Error(body?.message || 'Occupied transfer review failed.');
        const data = body.data;
        cell('Current bed', data.sourceBedId);
        cell('Destination bed', data.destinationBedId);
        cell('Effective date', data.effectiveDate);
        cell('Original monthly rent (BDT)', data.monthlyRent);
        cell('Original security deposit (BDT)', data.securityDeposit);
        cell('Posted invoice', data.hasPostedInvoiceActivity ? 'YES' : 'No');
        cell('Posted payment', data.hasPostedPaymentActivity ? 'YES' : 'No');
        cell('Issued invoice', data.hasUnpostedIssuedInvoice ? 'YES' : 'No');
        cell('Finance status', data.status);
        cell('Finance approval', 'NOT GRANTED');
        message(data.decision + ' ' + data.notice, data.requiresApprovedFinanceAdjustment || data.requiresBillingPolicy);
    } catch (error) { message(error.message || 'Occupied transfer review failed.', true); }
    finally { button.disabled = !scope; }
});
})();
