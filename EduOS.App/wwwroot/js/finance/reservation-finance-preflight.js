(() => {
'use strict';
const root = document.getElementById('reservationFinanceRoot');
if (!root) return;
const form = document.getElementById('reservationFinanceForm');
const alert = document.getElementById('reservationFinanceAlert');
const result = document.getElementById('reservationFinanceResult');
const details = document.getElementById('reservationFinanceDetails');
const decision = document.getElementById('reservationFinanceDecision');
function show(message, error) {
    alert.className = 'alert alert-' + (error ? 'danger' : 'info');
    alert.textContent = message;
}
function render(data) {
    details.replaceChildren();
    const fields = [
        ['Type', data.kind], ['Reservation ID', data.reservationId],
        ['Campus ID', data.campusId],
        ...(data.kind === 'Transport' ? [['Route ID', data.transportRouteId], ['Vehicle ID', data.transportVehicleId]] : []),
        ['Student ID', data.studentId],
        ['Enrollment ID', data.studentEnrollmentId], ['Start date', data.startDate],
        ['Inclusive end date', data.endDate || 'Open-ended'],
        ['Monthly charge (BDT)', data.monthlyCharge],
        ['Security deposit (BDT)', data.securityDeposit],
        ['Fee head linked', data.hasFeeHead ? 'Yes' : 'No'],
        ['Posted invoice activity', data.hasPostedInvoiceActivity ? 'YES' : 'No'],
        ['Posted payment activity', data.hasPostedPaymentActivity ? 'YES' : 'No'],
        ['Issued invoice without posting', data.hasUnpostedIssuedInvoice ? 'YES' : 'No'],
        ['Approved financial mutation', 'NO — approval contract unavailable']
    ];
    for (const [name, value] of fields) {
        const dt = document.createElement('dt');
        dt.className = 'col-sm-5';
        dt.textContent = name;
        const dd = document.createElement('dd');
        dd.className = 'col-sm-7';
        dd.textContent = String(value ?? '');
        details.append(dt, dd);
    }
    decision.textContent = data.decision || 'No Finance decision available.';
    decision.className = 'alert ' + (data.requiresApprovedFinanceAdjustment || data.requiresBillingPolicy ? 'alert-danger' : 'alert-warning');
    result.classList.remove('d-none');
}
form.addEventListener('submit', async event => {
    event.preventDefault();
    result.classList.add('d-none');
    alert.className = 'alert d-none';
    const kind = document.getElementById('reservationFinanceKind').value;
    const id = document.getElementById('reservationFinanceId').value;
    if (!/^\d+$/.test(id) || Number(id) < 1 || !Number.isSafeInteger(Number(id))) {
        show('Enter a valid positive reservation ID.', true); return;
    }
    const button = form.querySelector('button[type="submit"]');
    button.disabled = true;
    try {
        const url = '/api/finance/adjustments/reservations/preflight?kind=' + encodeURIComponent(kind) + '&id=' + encodeURIComponent(id);
        const response = await fetch(url, {credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
        const body = await response.json().catch(() => null);
        if (!response.ok || !body?.success || !body.data) throw Error(body?.message || 'Finance preflight failed.');
        render(body.data);
        show('Finance exposure loaded. No reservation mutation or posting was performed.', false);
    } catch (error) { show(error.message || 'Finance preflight failed.', true); }
    finally { button.disabled = false; }
});
})();
