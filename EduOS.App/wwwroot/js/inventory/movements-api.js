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
export async function activeLocations() {
    const pageSize = 100, rows = [];
    for (let page = 1; page <= 100; page++) {
        const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
        const data = await api('catalog/locations?' + query);
        if (!Array.isArray(data?.items) || !Number.isSafeInteger(data.totalCount) || data.totalCount < 0 ||
            data.page !== page || data.pageSize !== pageSize)
            throw new Error('Invalid location page received. Please retry.');
        rows.push(...data.items);
        if (!data.hasNextPage) {
            if (rows.length !== data.totalCount) throw new Error('Location list changed. Please retry.');
            return rows.filter(x => x.isActive);
        }
        if (!data.items.length) throw new Error('Incomplete location page received. Please retry.');
    }
    throw new Error('Over 10,000 accessible locations; narrow the catalog before loading dropdowns.');
}
export async function catalogs(search = '', knownLocations = null) {
    const query = new URLSearchParams({ page: '1', pageSize: '100', search });
    const [items, locations] = await Promise.all([api('catalog/items?' + query),
        knownLocations ?? activeLocations()]);
    return { items: (items.items || []).filter(x => x.isActive && x.isStockTracked),
        locations };
}
