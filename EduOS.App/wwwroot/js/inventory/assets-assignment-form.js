import { api, el, canManage, notice, requestKey, clearKey, dateToday } from './assets-api.js';
import { selected, loadHistory } from './assets-custody.js';
export function bindAssignmentForm(reload) {
    if (!canManage) return;
    el('assetAssignedOn').value = dateToday();
    el('assetAssignForm').addEventListener('input', () => clearKey('assign'));
    el('assetAssignForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (!selected) { notice('Select an asset first.'); return; }
        const type = el('assetAssigneeType').value, raw = el('assetAssignee').value.trim();
        const body = { clientRequestId: requestKey('assign'), assetReference: selected.reference,
            assignedOn: el('assetAssignedOn').value,
            employeeReference: type === 'employee' ? raw : null,
            studentReference: type === 'student' ? raw : null,
            organizationUnitId: type === 'unit' ? Number(raw) : null };
        try {
            await api('/assignments', 'POST', body);
            clearKey('assign'); el('assetAssignee').value = '';
            await loadHistory(); await reload(); notice('Asset assigned.', true);
        } catch (error) { notice(error.message); }
    });
}
