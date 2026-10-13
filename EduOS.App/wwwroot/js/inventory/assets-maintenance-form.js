import { api, el, canManage, notice, requestKey, clearKey, dateToday, money } from './assets-api.js';
import { selected } from './assets-custody.js';
export function bindMaintenanceForm() {
    if (!canManage) return;
    el('assetMaintenanceDate').value = dateToday();
    el('assetMaintenanceForm').addEventListener('input', () => clearKey('maintenance'));
    el('assetMaintenanceForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (!selected) { notice('Select an asset first.'); return; }
        try {
            const body = { clientRequestId: requestKey('maintenance'), assetReference: selected.reference,
                maintenanceDate: el('assetMaintenanceDate').value,
                workDescription: el('assetWork').value.trim(),
                cost: money(el('assetMaintenanceCost').value),
                serviceProvider: el('assetProvider').value.trim() || null,
                nextMaintenanceDate: el('assetNextMaintenance').value || null,
                remarks: el('assetRemarks').value.trim() || null };
            await api('/maintenance', 'POST', body);
            clearKey('maintenance'); el('assetMaintenanceForm').reset();
            el('assetMaintenanceDate').value = dateToday(); notice('Maintenance recorded.', true);
        } catch (error) { notice(error.message); }
    });
}
