import { api, el, td, empty, canManage, notice, addButton, dateToday } from './assets-api.js';
export let selected = null;
export async function selectAsset(row) {
    selected = row;
    el('assetSelected').textContent = row.assetTag + ' — ' + row.name + ' (' + row.stateCode + ')';
    await loadHistory();
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
                if (row.stateCode === 'Assigned' && !row.returnedOn)
                    addButton(cell, 'Return', async () => {
                        const returnedOn = prompt('Return date (YYYY-MM-DD)', dateToday());
                        if (!returnedOn) return;
                        try {
                            await api('/assignments/' + row.id + '/return', 'POST', { returnedOn, rowVersion: row.rowVersion });
                            notice('Asset returned.', true); await loadHistory();
                        } catch (error) { notice(error.message); }
                    });
                tr.append(cell);
            }
            target.append(tr);
        }
    } catch (error) { notice(error.message); empty('assetHistoryRows', canManage ? 5 : 4, 'Unable to load history.'); }
}
