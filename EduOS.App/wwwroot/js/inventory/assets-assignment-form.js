import { api, el, canManage, notice, requestKey, clearKey, dateToday } from './assets-api.js';
import { selected, loadHistory } from './assets-custody.js';
import { assignmentGuard } from './assets-form-guards.js';
export function bindAssignmentForm(reload) {
    if (!canManage) return;
    const form = el('assetAssignForm');
    let saving = false;
    el('assetAssignedOn').value = dateToday();
    assignmentGuard.markClean();
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (saving) return;
        if (!selected) { notice('Select an asset first.'); return; }
        const type = el('assetAssigneeType').value, raw = el('assetAssignee').value.trim();
        const body = { assetReference: selected.reference,
            assignedOn: el('assetAssignedOn').value,
            employeeReference: type === 'employee' ? raw : null,
            studentReference: type === 'student' ? raw : null,
            organizationUnitId: type === 'unit' ? Number(raw) : null };
        body.clientRequestId = requestKey('assign', body);
        saving = true; form.inert = true;
        try {
            await api('/assignments', 'POST', body);
            clearKey('assign'); el('assetAssignee').value = '';
            assignmentGuard.markClean(); notice('Asset assigned.', true);
            try { await loadHistory(); await reload(); }
            catch (_) { notice('Asset assigned; refresh custody and asset register.'); }
        } catch (error) { notice(error.message); }
        finally { saving = false; form.inert = false; }
    });
}
