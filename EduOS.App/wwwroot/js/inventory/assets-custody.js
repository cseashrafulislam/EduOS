import { api, el, td, empty, canManage, notice, addButton, dateToday, clearKey } from './assets-api.js';
import { loadMaintenanceHistory } from './assets-maintenance-history.js';
import { assignmentGuard, maintenanceGuard, transferGuard, confirmDraftDiscard } from './assets-form-guards.js';
export let selected = null;
let returning = false;
let historyGeneration = 0;
export async function selectAsset(row) {
    if (returning || (canManage && ['assetAssignForm', 'assetTransferForm', 'assetMaintenanceForm']
        .some(id => el(id).inert))) { notice('Finish the current custody operation before switching assets.'); return; }
    if (selected?.reference !== row.reference && canManage) {
        if (!confirmDraftDiscard([assignmentGuard, transferGuard, maintenanceGuard])) return;
        for (const [id, guard] of [['assetAssignForm', assignmentGuard],
            ['assetTransferForm', transferGuard], ['assetMaintenanceForm', maintenanceGuard]]) {
            el(id).reset(); guard.markClean();
        }
        el('assetAssignedOn').value = dateToday(); assignmentGuard.markClean();
        el('assetMaintenanceDate').value = dateToday(); maintenanceGuard.markClean();
        clearKey('assign'); clearKey('transfer'); clearKey('maintenance');
    }
    selected = row;
    if (canManage) el('assetTransferForm').classList.add('d-none');
    el('assetSelected').textContent = row.assetTag + ' — ' + row.name + ' (' + row.stateCode + ')';
    await Promise.all([loadHistory(), loadMaintenanceHistory(row.reference, true)]);
}
export async function loadHistory() {
    if (!selected) return;
    const reference = selected.reference;
    const generation = ++historyGeneration;
    try {
        const rows = await api('/' + encodeURIComponent(reference) + '/assignments');
        if (generation !== historyGeneration || selected?.reference !== reference) return;
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
                        if (form.inert || !transferGuard.confirmDiscard()) return;
                        clearKey('transfer');
                        form.reset();
                        el('assetTransferAssignmentId').value = row.id;
                        el('assetTransferVersion').value = row.rowVersion;
                        el('assetTransferredOn').value = dateToday();
                        el('assetTransferredOn').min = row.assignedOn;
                        el('assetTransferredOn').max = dateToday();
                        transferGuard.markClean();
                        form.classList.remove('d-none');
                        el('assetTransferTarget').focus();
                    });
                    addButton(cell, 'Return', async () => {
                        if (returning) return;
                        const returnedOn = prompt('Return date (YYYY-MM-DD)', dateToday());
                        if (!returnedOn) return;
                        returning = true;
                        try {
                            await api('/assignments/' + row.id + '/return', 'POST', { returnedOn, rowVersion: row.rowVersion });
                            notice('Asset returned.', true);
                            try { await loadHistory(); } catch (_) { notice('Return saved; refresh custody history.'); }
                        } catch (error) { notice(error.message); }
                        finally { returning = false; }
                    });
                }
                tr.append(cell);
            }
            target.append(tr);
        }
    } catch (error) {
        if (generation !== historyGeneration || selected?.reference !== reference) return;
        notice(error.message); empty('assetHistoryRows', canManage ? 5 : 4, 'Unable to load history.');
    }
}
