import { api, el, canManage, notice, requestKey, clearKey } from './assets-api.js';
import { selected, loadHistory } from './assets-custody.js';

export function bindTransferForm(reload) {
    if (!canManage) return;
    const form = el('assetTransferForm');
    const submit = el('assetTransferSubmit');
    form.addEventListener('input', () => clearKey('transfer'));
    el('assetTransferCancel').addEventListener('click', () => {
        form.classList.add('d-none');
        clearKey('transfer');
    });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (!selected || form.classList.contains('d-none') || submit.disabled) return;
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
            clientRequestId: requestKey('transfer'),
            rowVersion: el('assetTransferVersion').value,
            transferredOn: el('assetTransferredOn').value,
            employeeReference: type === 'employee' ? raw : null,
            studentReference: type === 'student' ? raw : null,
            organizationUnitId: targetId,
            reason: el('assetTransferReason').value.trim()
        };
        submit.disabled = true;
        try {
            await api('/assignments/' + encodeURIComponent(id) + '/transfer', 'POST', body);
            clearKey('transfer');
            form.classList.add('d-none');
            await loadHistory();
            await reload();
            notice('Asset custody transferred.', true);
        } catch (error) {
            notice(error.message);
        } finally {
            submit.disabled = false;
        }
    });
}
