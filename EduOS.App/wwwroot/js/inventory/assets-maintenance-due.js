import { api, el, td, empty, notice, canManage, dateToday, addButton } from './assets-api.js';
import { selectAsset } from './assets-custody.js';
let page = 1, pages = 1, requestSerial = 0;
export async function loadMaintenanceDue() {
    const serial = ++requestSerial, dueBy = el('assetDueBy').value;
    if (!/^\d{4}-\d{2}-\d{2}$/.test(dueBy)) { notice('Select a valid due-by date.'); return; }
    try {
        const query = new URLSearchParams({ dueBy, page: String(page), pageSize: '25' });
        const result = await api('/maintenance/due?' + query);
        if (serial !== requestSerial) return;
        pages = Math.max(1, result.totalPages || 1);
        el('assetDuePage').textContent = 'Page ' + page + ' of ' + pages + ' · ' + result.totalCount + ' due';
        el('assetDuePrev').disabled = page <= 1;
        el('assetDueNext').disabled = page >= pages;
        const target = el('assetDueRows'); target.replaceChildren();
        for (const row of result.items || []) {
            const tr = document.createElement('tr');
            [row.assetTag + ' — ' + row.assetName, row.campusId, row.stateCode,
                row.lastMaintenanceDate, row.nextMaintenanceDate, row.daysOverdue].forEach(x => tr.append(td(x)));
            const actions = document.createElement('td');
            addButton(actions, canManage ? 'Open maintenance' : 'View history', async () => {
                try {
                    await selectAsset({ reference: row.assetReference, assetTag: row.assetTag,
                        name: row.assetName, stateCode: row.stateCode });
                    el('assetSelected').scrollIntoView({ behavior: 'smooth', block: 'center' });
                } catch (error) { notice(error.message); }
            });
            tr.append(actions); target.append(tr);
        }
        if (!result.items?.length) empty('assetDueRows', 7, 'No maintenance due by the selected date.');
    } catch (error) {
        if (serial !== requestSerial) return;
        notice(error.message); empty('assetDueRows', 7, 'Unable to load due maintenance.');
        el('assetDuePage').textContent = 'Unavailable';
        el('assetDuePrev').disabled = true; el('assetDueNext').disabled = true;
    }
}
export function bindMaintenanceDue() {
    el('assetDueBy').value = dateToday();
    el('assetDueFilter').addEventListener('submit', event => {
        event.preventDefault(); page = 1; loadMaintenanceDue();
    });
    el('assetDuePrev').addEventListener('click', () => { if (page > 1) { page--; loadMaintenanceDue(); } });
    el('assetDueNext').addEventListener('click', () => { if (page < pages) { page++; loadMaintenanceDue(); } });
    loadMaintenanceDue();
}
