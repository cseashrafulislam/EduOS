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
const selection = document.getElementById('reservationFinanceChangeReviewSelection');
const manualAmount = document.getElementById('reservationFinanceChangeReviewManualAmount');
const manualReason = document.getElementById('reservationFinanceChangeReviewManualReason');
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
    selection.value = 'auto';
    manualAmount.value = '';
    manualReason.value = '';
    manualAmount.disabled = true;
    manualReason.disabled = true;
});
selection.addEventListener('change', () => {
    const manual = selection.value !== 'auto';
    manualAmount.disabled = !manual;
    manualReason.disabled = !manual;
    manualAmount.required = manual;
    manualReason.required = manual;
    if (!manual) { manualAmount.value = ''; manualReason.value = ''; }
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
    const proposal = {kind:scope.kind, reservationId:scope.id, change:action.value};
    proposal[isStart ? 'proposedStartDate' : 'proposedEndDate'] = proposed;
    if (selection.value !== 'auto') {
        if (!/^-?\d+(\.\d{1,2})?$/.test(manualAmount.value) ||
            manualReason.value.trim().length < 20) {
            message('Enter a signed amount (up to 2 decimal places) and a reason of at least 20 characters.', true);
            return;
        }
        proposal[selection.value === 'hybrid' ? 'manualAdjustmentDelta' : 'manualNetAdjustment'] = manualAmount.value;
        proposal.manualOverrideReason = manualReason.value.trim();
    }
    const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value;
    if (!token) { message('Security token missing. Reload the page.', true); return; }
    button.disabled = true;
    details.replaceChildren();
    try {
        const response = await fetch('/api/finance/adjustments/reservations/change-review', {
            method:'POST', credentials:'same-origin', cache:'no-store',
            headers:{Accept:'application/json', 'Content-Type':'application/json', RequestVerificationToken:token},
            body:JSON.stringify(proposal)
        });
        const body = await response.json().catch(() => null);
        if (!response.ok || !body?.success || !body.data)
            throw Error(body?.message || 'Finance proposal review failed.');
        const data = body.data;
        cell('Proposed change', data.change);
        cell('Current dates', data.currentStartDate + ' to ' + (data.currentEndDate || 'Open-ended'));
        cell('Proposed dates', data.proposedStartDate + ' to ' + (data.proposedEndDate || 'Open-ended'));
        cell('Billing policy', data.calculationMode || 'Manual review');
        cell('Manual override permitted', data.manualOverrideAllowed ? 'Yes' : 'No');
        if (data.suggestedNetAdjustment !== null && data.suggestedNetAdjustment !== undefined) {
            const net = Number(data.suggestedNetAdjustment);
            cell('Estimated period change', Math.abs(net).toFixed(2) +
                (net > 0 ? ' additional charge' : net < 0 ? ' potential credit' : ' no period difference'));
        } else cell('Estimated period change', 'Requires Finance calculation');
        cell('Quotation selection', data.adjustmentSelectionMode || 'Requires review');
        if (data.selectedNetAdjustment !== null && data.selectedNetAdjustment !== undefined)
            cell('Selected adjustment (BDT)', Number(data.selectedNetAdjustment).toFixed(2));
        cell('Override applied', data.manualOverrideApplied ? 'Yes (review only)' : 'No');
        cell('Quotation basis', data.quoteBasis || 'Not available');
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
