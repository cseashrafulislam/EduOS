import { el, api, notice, td, activeLocations } from './movements-api.js';

export async function initReservations() {
    const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const form = el('inventoryReservationFilter');
    if (!form) return;
    let page = 1, last = 1, generation = 0;
    const rows = el('inventoryReservationRows');
    const prev = el('inventoryReservationPrev');
    const next = el('inventoryReservationNext');
    const pageLabel = el('inventoryReservationPage');
    const exportButton = el('inventoryReservationExport');
    const locationSelect = el('inventoryReservationLocation');
    const searchInput = el('inventoryReservationSearch');

    const params = () => {
        const q = new URLSearchParams({ page: String(page), pageSize: '25' });
        if (locationSelect.value) q.set('locationId', locationSelect.value);
        if (searchInput.value.trim()) q.set('search', searchInput.value.trim());
        return q;
    };
    async function load() {
        const token = ++generation;
        try {
            const data = await api('movements/reservations?' + params());
            if (token !== generation || !form.isConnected) return;
            last = Math.max(1, data.totalPages || 1);
            prev.disabled = page <= 1;
            next.disabled = page >= last;
            pageLabel.textContent = (bn ? 'পৃষ্ঠা ' : 'Page ') + page + ' / ' + last + ' · ' + data.totalCount;
            rows.replaceChildren();
            for (const item of data.items || []) {
                const tr = document.createElement('tr');
                [item.movementNumber, item.movementTypeCode,
                    item.itemCode + ' — ' + item.itemName, item.lotOrBatchNo,
                    item.fromLocationId, item.reservedQuantity + ' ' + item.unitCode,
                    new Date(item.heldSinceUtc).toLocaleString()].forEach(value => tr.append(td(value)));
                rows.append(tr);
            }
            if (!data.items?.length) {
                const tr = document.createElement('tr'), cell = document.createElement('td');
                cell.colSpan = 7;
                cell.textContent = bn ? 'কোনো সক্রিয় রিজার্ভেশন নেই।' : 'No active reservations.';
                tr.append(cell); rows.append(tr);
            }
        } catch (error) {
            if (token === generation) notice(error.message);
        }
    }

    form.addEventListener('submit', event => { event.preventDefault(); page = 1; load(); });
    prev.addEventListener('click', () => { if (page > 1) { page--; load(); } });
    next.addEventListener('click', () => { if (page < last) { page++; load(); } });
    exportButton.addEventListener('click', async () => {
        exportButton.disabled = true;
        try {
            const q = params();
            q.delete('page'); q.delete('pageSize');
            const response = await fetch('/api/inventory/movements/reservations/export?' + q, {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/csv' }
            });
            if (!response.ok) {
                const error = await response.json().catch(() => null);
                throw new Error(error?.message || (bn ? 'এক্সপোর্ট ব্যর্থ।' : 'Export failed.'));
            }
            const blob = await response.blob();
            const url = URL.createObjectURL(blob);
            try {
                const link = document.createElement('a');
                link.href = url; link.download = 'inventory-active-reservations.csv';
                document.body.append(link); link.click(); link.remove();
            } finally { URL.revokeObjectURL(url); }
        } catch (error) { notice(error.message); }
        finally { exportButton.disabled = false; }
    });

    window.addEventListener('pagehide', () => {
        generation++;
        rows.replaceChildren();
        pageLabel.textContent = '';
    }, { once: true });
    try {
        const locations = await activeLocations();
        if (!form.isConnected) return;
        for (const location of locations)
            locationSelect.add(new Option(location.code + ' — ' + location.name, String(location.id)));
    } catch (error) { notice(error.message); }
    await load();
}
