import { el, api, activeLocations, fill, td } from './movements-api.js';

export async function initItemHistory() {
    const form = el('itemHistoryForm');
    if (!form) return;
    let page = 1, totalPages = 1, loaded = false;
    const alert = el('itemHistoryAlert');
    const show = (message, error = false) => {
        alert.textContent = message;
        alert.className = 'alert ' + (error ? 'alert-danger' : 'alert-info');
    };
    const locations = await activeLocations();
    fill(el('itemHistoryLocation'), locations, 'Select authorized location');

    async function findItems() {
        const query = new URLSearchParams({
            page: '1', pageSize: '100', search: el('itemHistorySearch').value.trim()
        });
        const result = await api('catalog/items?' + query);
        fill(el('itemHistoryItem'), (result.items || []).filter(x => x.isStockTracked), 'Select stock item');
        if (result.totalCount > 100)
            show('Showing the first 100 matching items. Narrow the item search.');
    }
    el('itemHistoryFind').addEventListener('click', () => findItems().catch(e => show(e.message, true)));
    await findItems();

    const number = value => Number(value || 0).toLocaleString(undefined, { maximumFractionDigits: 6 });
    async function load() {
        const itemId = Number(el('itemHistoryItem').value);
        const locationId = Number(el('itemHistoryLocation').value);
        if (!Number.isSafeInteger(itemId) || itemId <= 0 ||
            !Number.isSafeInteger(locationId) || locationId <= 0) {
            show('Choose an item and an authorized location.', true);
            return;
        }
        const query = new URLSearchParams({
            itemId: String(itemId), locationId: String(locationId), page: String(page), pageSize: '50'
        });
        const from = el('itemHistoryFrom').value, to = el('itemHistoryTo').value;
        if (from) query.set('fromUtc', from + 'T00:00:00Z');
        if (to) query.set('toUtc', to + 'T23:59:59.999Z');
        if (el('itemHistoryLot').value.trim()) query.set('lot', el('itemHistoryLot').value.trim());
        const data = await api('movements/item-history?' + query);
        loaded = true;
        totalPages = Math.max(1, Math.ceil(data.totalCount / data.pageSize));
        el('itemHistoryPage').textContent = 'Page ' + data.page + ' of ' + totalPages +
            ' · ' + data.totalCount + ' posted line effects';
        el('itemHistoryPrev').disabled = page <= 1;
        el('itemHistoryNext').disabled = page >= totalPages;
        el('itemHistorySummary').textContent =
            data.itemCode + ' · ' + data.locationName + ' · ' + data.unitCode +
            ' | Opening: ' + number(data.openingQuantity) +
            ' | In: ' + number(data.quantityIn) +
            ' | Out: ' + number(data.quantityOut) +
            ' | Closing: ' + number(data.closingQuantity) +
            ' | Recorded cost opening/in/out/closing: ' +
            [data.openingRecordedCost, data.recordedCostIn, data.recordedCostOut, data.closingRecordedCost]
                .map(number).join(' / ');
        const tbody = el('itemHistoryRows');
        tbody.replaceChildren();
        for (const row of data.items || []) {
            const tr = document.createElement('tr');
            const provenance = (row.sourceType || 'Unspecified') +
                (row.sourceId == null ? '' : ' #' + row.sourceId) +
                (row.isReversal ? ' (compensating reversal)' : '');
            [new Date(row.postedAtUtc).toISOString(), row.movementNumber,
                row.movementTypeCode, row.lotOrBatchNo, number(row.quantityIn),
                number(row.quantityOut), number(row.recordedCostIn),
                number(row.recordedCostOut), provenance].forEach(value => tr.append(td(value)));
            tbody.append(tr);
        }
        if (!data.items?.length) show('No posted movements in the selected period.');
        else alert.className = 'd-none';
    }
    form.addEventListener('submit', event => {
        event.preventDefault(); page = 1; load().catch(e => show(e.message, true));
    });
    el('itemHistoryPrev').addEventListener('click', () => {
        if (loaded && page > 1) { page--; load().catch(e => show(e.message, true)); }
    });
    el('itemHistoryNext').addEventListener('click', () => {
        if (loaded && page < totalPages) { page++; load().catch(e => show(e.message, true)); }
    });
}
