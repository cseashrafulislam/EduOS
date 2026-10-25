import { api, el, td, empty, notice } from './assets-api.js';

let selectedReference = null, page = 1, totalPages = 1;
export function bindMaintenanceHistory() {
    el('assetMaintenancePrev').addEventListener('click', () => {
        if (page > 1) { page--; loadMaintenanceHistory(selectedReference); }
    });
    el('assetMaintenanceNext').addEventListener('click', () => {
        if (page < totalPages) { page++; loadMaintenanceHistory(selectedReference); }
    });
}
export async function loadMaintenanceHistory(reference, reset = false) {
    if (reset) { selectedReference = reference; page = 1; totalPages = 1; }
    if (!reference || reference !== selectedReference) return;
    const requestedReference = reference, requestedPage = page;
    try {
        const query = new URLSearchParams({ page: String(page), pageSize: '10' });
        const result = await api('/' + encodeURIComponent(reference) + '/maintenance?' + query);
        if (selectedReference !== requestedReference || page !== requestedPage) return;
        totalPages = Math.max(1, result.totalPages || 1);
        el('assetMaintenancePage').textContent = 'Page ' + page + ' of ' + totalPages +
            ' · ' + result.totalCount + ' records';
        el('assetMaintenancePrev').disabled = page <= 1;
        el('assetMaintenanceNext').disabled = page >= totalPages;
        const target = el('assetMaintenanceHistoryRows'); target.replaceChildren();
        for (const row of result.items || []) {
            const tr = document.createElement('tr');
            [row.maintenanceDate, row.workDescription, row.serviceProvider,
                Number(row.cost).toFixed(2), row.nextMaintenanceDate].forEach(value => tr.append(td(value)));
            target.append(tr);
        }
        if (!result.items?.length) empty('assetMaintenanceHistoryRows', 5, 'No maintenance recorded.');
    } catch (error) {
        if (selectedReference !== requestedReference) return;
        notice(error.message);
        empty('assetMaintenanceHistoryRows', 5, 'Unable to load maintenance history.');
        el('assetMaintenancePage').textContent = 'Unavailable';
        el('assetMaintenancePrev').disabled = true;
        el('assetMaintenanceNext').disabled = true;
    }
}
