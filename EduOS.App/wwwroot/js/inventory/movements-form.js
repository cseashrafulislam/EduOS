import { el, api, notice, fill, catalogs } from './movements-api.js';
import { createLineEditor } from './movements-form-lines.js';
import { commandKey, refreshAfterCommit } from './movements-form-safety.js';
export async function initForm(refresh) {
    const form = el('movementForm'), keys = commandKey();
    const editor = createLineEditor(lines => { el('movementDraftState').value = JSON.stringify(lines); });
    let saving = false, itemSearchSequence = 0;
    function mode() {
        const type = el('movementType').value;
        const facility = type === 'FacilityWorkOrder';
        el('movementFrom').disabled = type === 'Receipt';
        el('movementTo').disabled = type === 'Issue' || facility;
        el('movementCost').disabled = type !== 'Receipt';
        el('movementFacilityGroup').classList.toggle('d-none', !facility);
        el('movementFacilityRequest').required = facility;
        if (type !== 'Receipt') el('movementCost').value = '0';
    }
    const data = await catalogs();
    fill(el('movementFrom'), data.locations, 'Select origin');
    fill(el('movementTo'), data.locations, 'Select destination');
    fill(el('movementItem'), data.items, 'Select stock item');
    let timer;
    el('movementItemSearch').addEventListener('input', () => {
        const generation = ++itemSearchSequence;
        clearTimeout(timer); timer = setTimeout(async () => {
            try { const result = await catalogs(el('movementItemSearch').value.trim(), data.locations);
                if (generation !== itemSearchSequence) return;
                fill(el('movementItem'), result.items, 'Select stock item'); }
            catch (e) { if (generation === itemSearchSequence) notice(e.message); }
        }, 350);
    });
    mode();
    const guard = window.EduOSFormGuard?.create(form);
    let previousType = el('movementType').value;
    el('movementType').addEventListener('change', () => {
        if (saving) { el('movementType').value = previousType; return; }
        if (editor.lines.length && !window.confirm('Changing movement type will discard the draft lines. Continue?')) {
            el('movementType').value = previousType; return;
        }
        if (editor.lines.length) editor.clear();
        previousType = el('movementType').value; mode();
    });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (saving || !form.reportValidity()) return;
        const type = el('movementType').value, from = Number(el('movementFrom').value) || null;
        const facility = type === 'FacilityWorkOrder';
        const to = Number(el('movementTo').value) || null;
        const facilityRequestId = Number(el('movementFacilityRequest').value);
        if ((facility && (!Number.isSafeInteger(facilityRequestId) || facilityRequestId <= 0)) ||
            !editor.lines.length || (type !== 'Receipt' && !from) ||
            (type !== 'Issue' && !facility && !to) ||
            ((type === 'Transfer' || type === 'TransferDispatch') && from === to)) return notice('Check locations and add at least one line.');
        const body = facility ? {
            fromLocationId: from, reason: el('movementReason').value.trim(),
            lines: editor.lines.map(({ inventoryItemId, quantity, lotOrBatchNo }) => ({ inventoryItemId, quantity, lotOrBatchNo }))
        } : { movementTypeCode: type === 'TransferDispatch' ? 'Issue' : type,
            fromLocationId: type === 'Receipt' ? null : from, toLocationId: type === 'Issue' ? null : to,
            sourceType: type === 'Receipt' ? 'ManualReceipt' : type === 'Issue' ? 'ManualIssue' :
                type === 'TransferDispatch' ? 'TransferDispatch' : 'ManualTransfer',
            remarks: el('movementReason').value.trim(),
            lines: editor.lines.map(({ label, ...rest }) => rest) };
        const path = facility ? 'movements/facility-issues/' + facilityRequestId : 'movements';
        saving = true; form.inert = true;
        try { await api(path, 'POST', { clientRequestId: keys.for(path, body), ...body });
            keys.clear(); editor.clear(); form.reset(); previousType = el('movementType').value;
            mode(); guard?.markClean();
            await refreshAfterCommit(refresh, 'Draft created; an approver must post it.', notice); }
        catch (e) { notice(e.message); }
        finally { saving = false; form.inert = false; }
    });
}
