import { el, notice } from './movements-api.js';

const decimalFields = /("(?:quantityOnHand|reorderLevel|shortfall)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
const csvCell = value => {
    let s = String(value ?? '');
    if (/^[\s\u0000-\u001f]*[=+\-@]/.test(s)) s = "'" + s;
    return '"' + s.replace(/"/g, '""') + '"';
};
function exportCsv(name, rows) {
    const csv = '\uFEFF' + rows.map(row => row.map(csvCell).join(',')).join('\r\n') + '\r\n';
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
    const a = document.createElement('a');
    a.href = url; a.download = name; document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
async function get(path) {
    const response = await fetch('/api/inventory/' + path, {
        credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
    });
    const raw = await response.text();
    let body;
    try { body = JSON.parse(raw.replace(decimalFields, '$1"$2"')); }
    catch { throw new Error('Unexpected inventory report response.'); }
    if (!response.ok || !body?.success) throw new Error(body?.message || 'Inventory request failed.');
    return body.data;
}
export async function initReorder() {
    const select = el('reorderLocation');
    if (!select) return;
    const locations = await get('catalog/locations');
    const rows = Array.isArray(locations) ? locations : locations.items || [];
    select.replaceChildren(new Option('Select location', ''));
    rows.filter(x => x.isActive).forEach(x => select.add(new Option(x.code + ' — ' + x.name, String(x.id))));
    let report = null;
    el('reorderForm').addEventListener('submit', async event => {
        event.preventDefault();
        const id = select.value;
        if (!/^[1-9]\d*$/.test(id)) return notice('Select an accessible stock location.');
        const run = el('reorderRun'), csv = el('reorderCsv');
        run.disabled = true; csv.disabled = true; report = null;
        try {
            const data = await get('movements/reorder-alerts?' + new URLSearchParams({ locationId: id }));
            const tbody = el('reorderRows');
            tbody.replaceChildren();
            for (const row of data.alerts || []) {
                const tr = document.createElement('tr');
                for (const value of [row.itemCode, row.itemName, row.unitCode, row.quantityOnHand, row.reorderLevel, row.shortfall]) {
                    const td = document.createElement('td');
                    td.textContent = String(value ?? '');
                    if (tr.children.length >= 3) td.className = 'text-end';
                    tr.appendChild(td);
                }
                tbody.appendChild(tr);
            }
            el('reorderSummary').textContent = data.locationName + ' | ' +
                (data.alerts || []).length + ' alert(s) across ' + data.monitoredItemCount +
                ' reorder-tracked item(s) | Snapshot ' + data.asOfUtc;
            report = data; csv.disabled = false; notice('Reorder alert report loaded.', true);
        } catch (error) { notice(error.message); }
        finally { run.disabled = false; }
    });
    el('reorderCsv').addEventListener('click', () => {
        if (!report) return;
        exportCsv('inventory-reorder-' + report.inventoryLocationId + '.csv', [
            ['Location', report.locationName, 'As of UTC', report.asOfUtc],
            ['Item code', 'Item', 'Unit', 'On hand', 'Reorder level', 'Shortfall to level'],
            ...(report.alerts || []).map(x => [x.itemCode, x.itemName, x.unitCode,
                x.quantityOnHand, x.reorderLevel, x.shortfall])
        ]);
    });
}
