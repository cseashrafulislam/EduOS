import { api, el, canManage, notice, requestKey, clearKey } from './assets-api.js';
import { selected, loadHistory } from './assets-custody.js';
import { transferGuard } from './assets-form-guards.js';

export function bindTransferForm(reload) {
    if (!canManage) return;
    const form = el('assetTransferForm');
    const submit = el('assetTransferSubmit');
    let saving = false;
    el('assetTransferCancel').addEventListener('click', () => {
        if (saving || !transferGuard.confirmDiscard()) return;
        form.classList.add('d-none');
        clearKey('transfer');
        form.reset(); transferGuard.markClean();
    });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (saving || !selected || form.classList.contains('d-none') || submit.disabled) return;
        const id = Number(el('assetTransferAssignmentId').value);
        const raw = el('assetTransferTarget').value.trim();
        const type = el('assetTransferType').value;
        if (!Number.isSafeInteger(id) || id <= 0 || !raw) {
            notice('Select a valid active custody and new custodian.');
            return;
        }
        const targetId = type === 'unit' ? Number(raw) : null;
        if (type === 'unit' && (!Number.isSafeInteger(targetId) || targetId <= 0)) {
            notice('Enter a valid organization unit ID.');
            return;
        }
        const body = {
            rowVersion: el('assetTransferVersion').value,
            transferredOn: el('assetTransferredOn').value,
            employeeReference: type === 'employee' ? raw : null,
            studentReference: type === 'student' ? raw : null,
            organizationUnitId: targetId,
            reason: el('assetTransferReason').value.trim()
        };
        body.clientRequestId = requestKey('transfer', { assignmentId: id, ...body });
        saving = true; submit.disabled = true; form.inert = true;
        try {
            await api('/assignments/' + encodeURIComponent(id) + '/transfer', 'POST', body);
            clearKey('transfer');
            form.classList.add('d-none');
            form.reset(); transferGuard.markClean();
            notice('Asset custody transferred.', true);
            try { await loadHistory(); await reload(); }
            catch (_) { notice('Transfer saved; register refresh failed. Reload before the next action.'); }
        } catch (error) {
            notice(error.message);
        } finally {
            saving = false; submit.disabled = false; form.inert = false;
        }
    });
}
