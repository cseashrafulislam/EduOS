import { el, notice } from './movements-api.js';

const decimalFields = /("(?:quantityOnHand|recordedCostBalance|reorderLevel)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
const csvCell = value => {
    let s = String(value ?? '');
    if (/^[\s\u0000-\u001f]*[=+\-@]/.test(s)) s = "'" + s;
    return '"' + s.replace(/"/g, '""') + '"';
};
async function get(path) {
    const response = await fetch('/api/inventory/' + path, {
        credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
    });
    const raw = await response.text();
    let body;
    try { body = JSON.parse(raw.replace(decimalFields, '$1"$2"')); }
    catch { throw new Error('Unexpected inventory stock report response.'); }
    if (!response.ok || !body?.success) throw new Error(body?.message || 'Stock report request failed.');
    return body.data;
}
function exportCsv(name, rows) {
    const csv = '\uFEFF' + rows.map(row => row.map(csvCell).join(',')).join('\r\n') + '\r\n';
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
    const a = document.createElement('a');
    a.href = url; a.download = name; document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
export async function initStockPosition() {
    const form = el('stockPositionForm');
    if (!form) return;
    const location = el('stockPositionLocation'), run = el('stockPositionRun'), csv = el('stockPositionCsv');
    const locations = await get('catalog/locations');
    const allowed = Array.isArray(locations) ? locations : locations.items || [];
    location.replaceChildren(new Option('Select location', ''));
    allowed.filter(x => x.isActive).forEach(x =>
        location.add(new Option(x.code + ' — ' + x.name, String(x.id))));
    let page = 1, report = null, busy = false;
    function invalidate() {
        report = null; csv.disabled = true;
        el('stockPositionPrev').disabled = true; el('stockPositionNext').disabled = true;
    }
    async function load() {
        if (busy) return;
        if (!/^[1-9]\d*$/.test(location.value)) return notice('Select an accessible stock location.');
        const local = el('stockPositionAsOf').value;
        const asOf = local ? new Date(local) : null;
        if (asOf && Number.isNaN(asOf.getTime())) return notice('Invalid as-of date.');
        const params = new URLSearchParams({
            locationId: location.value, search: el('stockPositionSearch').value.trim(),
            includeZeroStock: String(el('stockPositionIncludeZero').checked),
            page: String(page), pageSize: '50'
        });
        if (asOf) params.set('asOfUtc', asOf.toISOString());
        busy = true; run.disabled = true; invalidate();
        try {
            const data = await get('movements/stock-position?' + params);
            const tbody = el('stockPositionRows');
            tbody.replaceChildren();
            for (const row of data.items || []) {
                const tr = document.createElement('tr');
                const cells = [row.itemCode, row.itemName, row.unitCode, row.quantityOnHand,
                    row.reorderLevel, row.recordedCostBalance, row.activeLotCount,
                    !row.isActive ? 'Inactive catalog item' : row.isBelowReorder ? 'At/below reorder' : 'Active'];
                cells.forEach((value, index) => {
                    const td = document.createElement('td');
                    td.textContent = String(value ?? '');
                    if (index >= 3 && index <= 6) td.className = 'text-end';
                    tr.appendChild(td);
                });
                tbody.appendChild(tr);
            }
            el('stockPositionSummary').textContent = data.locationName + ' | ' + data.totalCount +
                ' matching item(s) | ' + data.catalogItemCount + ' tracked catalog item(s) | Snapshot UTC: ' + data.asOfUtc;
            el('stockPositionPage').textContent = 'Page ' + data.page + ' of ' +
                Math.max(1, Math.ceil(data.totalCount / data.pageSize));
            el('stockPositionPrev').disabled = page <= 1;
            el('stockPositionNext').disabled = page * data.pageSize >= data.totalCount;
            report = data; csv.disabled = false;
            notice('Stock position loaded.', true);
        } catch (error) { notice(error.message); }
        finally { busy = false; run.disabled = false; }
    }
    form.addEventListener('submit', event => { event.preventDefault(); page = 1; load(); });
    for (const id of ['stockPositionLocation', 'stockPositionSearch', 'stockPositionAsOf', 'stockPositionIncludeZero'])
        el(id).addEventListener('change', invalidate);
    el('stockPositionPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
    el('stockPositionNext').addEventListener('click', () => { if (report && page * report.pageSize < report.totalCount) { page++; load(); } });
    csv.addEventListener('click', () => {
        if (!report) return;
        exportCsv('inventory-stock-position-' + report.inventoryLocationId + '-page-' + report.page + '.csv', [
            ['Location', report.locationName, 'As of UTC', report.asOfUtc],
            ['Code', 'Item', 'Unit', 'On hand', 'Reorder level', 'Recorded cost balance', 'Active lots', 'Active', 'Below reorder'],
            ...(report.items || []).map(x => [x.itemCode, x.itemName, x.unitCode, x.quantityOnHand,
                x.reorderLevel, x.recordedCostBalance, x.activeLotCount, x.isActive, x.isBelowReorder])
        ]);
    });
}
