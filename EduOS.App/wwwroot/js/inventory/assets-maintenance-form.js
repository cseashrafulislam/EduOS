import { api, el, canManage, notice, requestKey, clearKey, dateToday, money } from './assets-api.js';
import { selected } from './assets-custody.js';
import { loadMaintenanceHistory } from './assets-maintenance-history.js';
import { loadMaintenanceDue } from './assets-maintenance-due.js';
import { maintenanceGuard } from './assets-form-guards.js';
export function bindMaintenanceForm() {
    if (!canManage) return;
    const form = el('assetMaintenanceForm');
    let saving = false;
    el('assetMaintenanceDate').value = dateToday();
    maintenanceGuard.markClean();
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (saving) return;
        if (!selected) { notice('Select an asset first.'); return; }
        try {
            const body = { assetReference: selected.reference,
                maintenanceDate: el('assetMaintenanceDate').value,
                workDescription: el('assetWork').value.trim(),
                cost: money(el('assetMaintenanceCost').value),
                serviceProvider: el('assetProvider').value.trim() || null,
                nextMaintenanceDate: el('assetNextMaintenance').value || null,
                remarks: el('assetRemarks').value.trim() || null };
            body.clientRequestId = requestKey('maintenance', body);
            saving = true; form.inert = true;
            await api('/maintenance', 'POST', body);
            clearKey('maintenance'); form.reset();
            el('assetMaintenanceDate').value = dateToday(); maintenanceGuard.markClean();
            notice('Maintenance recorded.', true);
            try { await Promise.all([loadMaintenanceHistory(body.assetReference, true), loadMaintenanceDue()]); }
            catch (_) { notice('Maintenance saved; history refresh failed. Reload to verify.'); }
        } catch (error) { notice(error.message); }
        finally { saving = false; form.inert = false; }
    });
}
