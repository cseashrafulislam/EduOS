import { el, api, notice, td, activeLocations } from './movements-api.js';

export async function initLotAging() {
    const form = el('inventoryLotAgingFilter');
    if (!form) return;
    const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const location = el('inventoryLotAgingLocation'), search = el('inventoryLotAgingSearch');
    const days = el('inventoryLotAgingDays'), rows = el('inventoryLotAgingRows');
    const previous = el('inventoryLotAgingPrev'), next = el('inventoryLotAgingNext');
    const label = el('inventoryLotAgingPage');
    const exportButton = el('inventoryLotAgingExport'), printButton = el('inventoryLotAgingPrint');
    let page = 1, last = 1, generation = 0;
    const params = () => {
        const q = new URLSearchParams({ locationId: location.value, minimumDays: days.value,
            page: String(page), pageSize: '25' });
        if (search.value.trim()) q.set('search', search.value.trim());
        return q;
    };
    async function load() {
        const token = ++generation;
        rows.replaceChildren();
        if (!location.value) {
            label.textContent = bn ? 'স্টোর নির্বাচন করুন' : 'Select a store';
            previous.disabled = next.disabled = true;
            return;
        }
        try {
            const result = await api('movements/lots/aging?' + params());
            if (token !== generation || !form.isConnected) return;
            last = Math.max(1, result.totalPages || 1);
            previous.disabled = page <= 1; next.disabled = page >= last;
            label.textContent = (bn ? 'পৃষ্ঠা ' : 'Page ') + page + ' / ' + last + ' · ' + result.totalCount;
            for (const item of result.items || []) {
                const tr = document.createElement('tr');
                [item.itemCode + ' — ' + item.itemName, item.lotOrBatchNo || '—',
                    item.unitCode, item.quantityOnHand, item.reservedQuantity,
                    item.availableQuantity, item.recordedCostBalance,
                    item.latestInboundUtc ? new Date(item.latestInboundUtc).toISOString().replace('T', ' ').slice(0, 16) + ' UTC' : '—',
                    item.daysSinceLatestInbound, item.statusCode].forEach(value => tr.append(td(value)));
                if (item.hasIntegrityException) tr.classList.add('table-danger');
                else if (item.statusCode === 'Dormant') tr.classList.add('table-warning');
                rows.append(tr);
            }
            if (!result.items?.length) {
                const tr = document.createElement('tr'), cell = document.createElement('td');
                cell.colSpan = 10;
                cell.textContent = bn ? 'এই ফিল্টারে লট পাওয়া যায়নি।' : 'No lots match these filters.';
                tr.append(cell); rows.append(tr);
            }
        } catch (error) {
            if (token === generation) { rows.replaceChildren(); label.textContent = ''; notice(error.message); }
        }
    }
    form.addEventListener('submit', event => { event.preventDefault(); page = 1; load(); });
    location.addEventListener('change', () => { page = 1; load(); });
    days.addEventListener('change', () => { page = 1; load(); });
    previous.addEventListener('click', () => { if (page > 1) { page--; load(); } });
    next.addEventListener('click', () => { if (page < last) { page++; load(); } });
    printButton.addEventListener('click', () => {
        if (!location.value) return;
        const q = params(); q.delete('page'); q.delete('pageSize');
        window.open('/InventoryMovements/PrintLotAging?' + q, '_blank', 'noopener,noreferrer');
    });
    exportButton.addEventListener('click', async () => {
        if (!location.value) return;
        exportButton.disabled = true;
        try {
            const q = params(); q.delete('page'); q.delete('pageSize');
            const response = await fetch('/api/inventory/movements/lots/aging/export?' + q, {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/csv' }
            });
            if (!response.ok) {
                const error = await response.json().catch(() => null);
                throw new Error(error?.message || (bn ? 'এক্সপোর্ট ব্যর্থ।' : 'Export failed.'));
            }
            const blob = await response.blob(), url = URL.createObjectURL(blob);
            try {
                const link = document.createElement('a');
                link.href = url; link.download = 'inventory-lot-inbound-aging.csv';
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
