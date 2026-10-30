import { el, api, notice, td, activeLocations } from './movements-api.js';

export async function initReservationCoverage() {
    const form = el('inventoryCoverageFilter');
    if (!form) return;
    const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const location = el('inventoryCoverageLocation');
    const search = el('inventoryCoverageSearch');
    const overdue = el('inventoryCoverageOverdue');
    const rows = el('inventoryCoverageRows');
    const previous = el('inventoryCoveragePrev');
    const next = el('inventoryCoverageNext');
    const label = el('inventoryCoveragePage');
    const exportButton = el('inventoryCoverageExport');
    const printButton = el('inventoryCoveragePrint');
    let page = 1, last = 1, generation = 0;
    const params = () => {
        const q = new URLSearchParams({ locationId: location.value, page: String(page), pageSize: '25' });
        if (search.value.trim()) q.set('search', search.value.trim());
        if (overdue.checked) q.set('overdueOnly', 'true');
        return q;
    };
    async function load() {
        const token = ++generation;
        if (!location.value) {
            rows.replaceChildren();
            label.textContent = bn ? 'স্টোর নির্বাচন করুন' : 'Select a store';
            previous.disabled = true;
            next.disabled = true;
            return;
        }
        try {
            const result = await api('movements/reservations/coverage?' + params());
            if (token !== generation || !form.isConnected) return;
            last = Math.max(1, result.totalPages || 1);
            previous.disabled = page <= 1;
            next.disabled = page >= last;
            label.textContent = (bn ? 'পৃষ্ঠা ' : 'Page ') + page + ' / ' + last + ' · ' + result.totalCount;
            rows.replaceChildren();
            for (const item of result.items || []) {
                const tr = document.createElement('tr');
                [
                    item.itemCode + ' — ' + item.itemName,
                    item.lotOrBatchNo || '—', item.unitCode,
                    item.quantityOnHand, item.reservedQuantity,
                    item.availableQuantity, item.overdueReservedQuantity,
                    item.shortfallQuantity, item.activeHoldLines,
                    item.oldestHoldAgeDays,
                    item.statusCode === 'Shortfall' ? (bn ? 'ঘাটতি' : 'Shortfall') :
                        item.statusCode === 'Overdue' ? (bn ? 'মেয়াদ পেরিয়েছে' : 'Overdue') :
                            (bn ? 'পর্যাপ্ত' : 'Covered')
                ].forEach(value => tr.append(td(value)));
                if (item.hasShortfall) tr.classList.add('table-danger');
                else if (item.isOverdue) tr.classList.add('table-warning');
                rows.append(tr);
            }
            if (!result.items?.length) {
                const tr = document.createElement('tr');
                const cell = document.createElement('td');
                cell.colSpan = 11;
                cell.textContent = bn ? 'এই স্টোরে সক্রিয় রিজার্ভেশন নেই।' : 'No matching active holds at this store.';
                tr.append(cell); rows.append(tr);
            }
        } catch (error) {
            if (token === generation) {
                rows.replaceChildren();
                label.textContent = '';
                notice(error.message);
            }
        }
    }
    form.addEventListener('submit', event => { event.preventDefault(); page = 1; load(); });
    overdue.addEventListener('change', () => { page = 1; load(); });
    location.addEventListener('change', () => { page = 1; load(); });
    previous.addEventListener('click', () => { if (page > 1) { page--; load(); } });
    next.addEventListener('click', () => { if (page < last) { page++; load(); } });
    printButton.addEventListener('click', () => {
        if (!location.value) return;
        const q = params(); q.delete('page'); q.delete('pageSize');
        window.open('/InventoryMovements/PrintReservationCoverage?' + q.toString(),
            '_blank', 'noopener,noreferrer');
    });
    exportButton.addEventListener('click', async () => {
        if (!location.value) return;
        exportButton.disabled = true;
        try {
            const q = params(); q.delete('page'); q.delete('pageSize');
            const response = await fetch('/api/inventory/movements/reservations/coverage/export?' + q, {
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
                link.href = url; link.download = 'inventory-reservation-coverage.csv';
                document.body.append(link); link.click(); link.remove();
            } finally { URL.revokeObjectURL(url); }
        } catch (error) { notice(error.message); }
        finally { exportButton.disabled = false; }
    });
    window.addEventListener('pagehide', () => {
        generation++; rows.replaceChildren(); label.textContent = '';
    }, { once: true });
    try {
        const stores = await activeLocations();
        if (!form.isConnected) return;
        for (const store of stores)
            location.add(new Option(store.code + ' — ' + store.name, String(store.id)));
    } catch (error) { notice(error.message); }
    await load();
}
