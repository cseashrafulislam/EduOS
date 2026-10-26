import { el, api, notice, fill, catalogs } from './movements-api.js';
import { createLineEditor } from './movements-form-lines.js';
export async function initForm(refresh) {
    const editor = createLineEditor();
    let requestId = crypto.randomUUID();
    function mode() {
        const type = el('movementType').value;
        el('movementFrom').disabled = type === 'Receipt';
        el('movementTo').disabled = type === 'Issue';
        el('movementCost').disabled = type !== 'Receipt';
        if (type !== 'Receipt') el('movementCost').value = '0';
    }
    const data = await catalogs();
    fill(el('movementFrom'), data.locations, 'Select origin');
    fill(el('movementTo'), data.locations, 'Select destination');
    fill(el('movementItem'), data.items, 'Select stock item');
    let timer;
    el('movementItemSearch').addEventListener('input', () => {
        clearTimeout(timer); timer = setTimeout(async () => {
            try { const result = await catalogs(el('movementItemSearch').value.trim(), data.locations);
                fill(el('movementItem'), result.items, 'Select stock item'); }
            catch (e) { notice(e.message); }
        }, 350);
    });
    el('movementType').addEventListener('change', mode); mode();
    el('movementForm').addEventListener('submit', async event => {
        event.preventDefault();
        const type = el('movementType').value, from = Number(el('movementFrom').value) || null;
        const to = Number(el('movementTo').value) || null;
        if (!editor.lines.length || (type !== 'Receipt' && !from) || (type !== 'Issue' && !to) ||
            (type === 'Transfer' && from === to)) return notice('Check locations and add at least one line.');
        const body = { clientRequestId: requestId, movementTypeCode: type,
            fromLocationId: type === 'Receipt' ? null : from, toLocationId: type === 'Issue' ? null : to,
            sourceType: type === 'Receipt' ? 'ManualReceipt' : type === 'Issue' ? 'ManualIssue' : 'ManualTransfer',
            remarks: el('movementReason').value.trim(),
            lines: editor.lines.map(({ label, ...rest }) => rest) };
        try { await api('movements', 'POST', body); requestId = crypto.randomUUID(); editor.clear();
            el('movementForm').reset(); mode(); notice('Draft created; an approver must post it.', true); await refresh(); }
        catch (e) { notice(e.message); }
    });
}
