import { api, el, td, empty, canManage, notice, addButton, dateToday, clearKey } from './assets-api.js';
import { loadMaintenanceHistory } from './assets-maintenance-history.js';
export let selected = null;
export async function selectAsset(row) {
    selected = row;
    if (canManage) el('assetTransferForm').classList.add('d-none');
    el('assetSelected').textContent = row.assetTag + ' — ' + row.name + ' (' + row.stateCode + ')';
    await Promise.all([loadHistory(), loadMaintenanceHistory(row.reference, true)]);
}
export async function loadHistory() {
    if (!selected) return;
    try {
        const rows = await api('/' + encodeURIComponent(selected.reference) + '/assignments');
        const target = el('assetHistoryRows'); target.replaceChildren();
        if (!rows.length) { empty('assetHistoryRows', canManage ? 5 : 4, 'No assignment history.'); return; }
        for (const row of rows) {
            const tr = document.createElement('tr');
            const assignee = row.employeeReference || row.studentReference || row.organizationUnitId;
            [row.assignedOn, assignee, row.returnedOn, row.stateCode].forEach(x => tr.append(td(x)));
            if (canManage) {
                const cell = document.createElement('td');
                if (row.stateCode === 'Assigned' && !row.returnedOn) {
                    addButton(cell, 'Transfer', () => {
                        const form = el('assetTransferForm');
                        clearKey('transfer');
                        form.reset();
                        el('assetTransferAssignmentId').value = row.id;
                        el('assetTransferVersion').value = row.rowVersion;
                        el('assetTransferredOn').value = dateToday();
                        el('assetTransferredOn').min = row.assignedOn;
                        el('assetTransferredOn').max = dateToday();
                        form.classList.remove('d-none');
                        el('assetTransferTarget').focus();
                    });
                    addButton(cell, 'Return', async () => {
                        const returnedOn = prompt('Return date (YYYY-MM-DD)', dateToday());
                        if (!returnedOn) return;
                        try {
                            await api('/assignments/' + row.id + '/return', 'POST', { returnedOn, rowVersion: row.rowVersion });
                            notice('Asset returned.', true); await loadHistory();
                        } catch (error) { notice(error.message); }
                    });
                }
                tr.append(cell);
            }
            target.append(tr);
        }
    } catch (error) { notice(error.message); empty('assetHistoryRows', canManage ? 5 : 4, 'Unable to load history.'); }
}
