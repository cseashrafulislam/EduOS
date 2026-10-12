export const el = id => document.getElementById(id);
export const root = el('inventoryMovements');
const token = document.querySelector('meta[name="request-verification-token"]')?.content;
export async function api(path, method = 'GET', data) {
    const headers = { Accept: 'application/json' };
    if (data !== undefined) headers['Content-Type'] = 'application/json';
    if (method !== 'GET' && token) headers.RequestVerificationToken = token;
    const response = await fetch('/api/inventory/' + path, { method, credentials: 'same-origin', cache: 'no-store',
        headers, ...(data === undefined ? {} : { body: JSON.stringify(data) }) });
    const body = await response.json().catch(() => null);
    if (!response.ok || !body?.success) throw new Error(body?.message || 'Inventory request failed.');
    return body.data;
}
export function notice(message, success = false) {
    const box = el('movementAlert');
    box.className = 'alert alert-' + (success ? 'success' : 'danger');
    box.textContent = message;
}
export function td(value) {
    const cell = document.createElement('td');
    cell.textContent = value == null || value === '' ? '—' : String(value);
    return cell;
}
export function fill(select, rows, text) {
    select.replaceChildren();
    select.add(new Option(text, ''));
    rows.forEach(row => select.add(new Option(row.code ? row.code + ' — ' + row.name : row.name, String(row.id))));
}
export async function catalogs(search = '') {
    const query = new URLSearchParams({ page: '1', pageSize: '100', search });
    const [items, locations] = await Promise.all([api('catalog/items?' + query), api('catalog/locations')]);
    return { items: (items.items || []).filter(x => x.isActive && x.isStockTracked),
        locations: (Array.isArray(locations) ? locations : locations.items || []).filter(x => x.isActive) };
}