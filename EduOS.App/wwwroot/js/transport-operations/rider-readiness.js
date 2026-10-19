(() => {
'use strict';
const get = id => document.getElementById(id);
if (!get('transportRiderReadinessPanel')) return;
const campus = get('transportRiderReadinessCampus'), search = get('transportRiderReadinessSearch');
const mode = get('transportRiderReadinessMode'), status = get('transportRiderReadinessStatus');
const body = get('transportRiderReadinessRows'), exportBtn = get('transportRiderReadinessExport');
const prev = get('transportRiderReadinessPrev'), next = get('transportRiderReadinessNext');
let page = 1, pages = 1, sequence = 0, loadedFilter = null;
const filters = () => {
    const p = new URLSearchParams({ attentionOnly: mode.value });
    if (campus.value) p.set('campusId', campus.value);
    if (search.value.trim()) p.set('search', search.value.trim());
    return p.toString();
};
const td = value => {
    const cell = document.createElement('td');
    cell.textContent = value == null ? '—' : String(value);
    return cell;
};
function invalidate() {
    ++sequence; loadedFilter = null; exportBtn.disabled = true;
    status.textContent = 'Filters changed. Apply to refresh.';
}
async function load() {
    const request = ++sequence, snapshot = filters();
    loadedFilter = null; exportBtn.disabled = true;
    const params = new URLSearchParams(snapshot);
    params.set('page', String(page)); params.set('pageSize', '25');
    status.textContent = 'Loading rider readiness…';
    try {
        const response = await fetch('/api/transport/rider-readiness?' + params, { credentials: 'same-origin', cache: 'no-store' });
        const payload = await response.json();
        if (!response.ok || !payload.success || !payload.data) throw Error(payload.message || 'Report unavailable');
        if (request !== sequence || snapshot !== filters()) return;
        const result = payload.data;
        body.replaceChildren();
        for (const item of result.items || []) {
            const row = document.createElement('tr');
            [item.assignmentReference, item.campusName, item.routeName, item.vehicleNumber,
             item.startDate, item.endDate, item.readiness].forEach(value => row.append(td(value)));
            body.append(row);
        }
        if (!result.items?.length) {
            const row = document.createElement('tr'), cell = td('No matching assignments');
            cell.colSpan = 7; row.append(cell); body.append(row);
        }
        pages = Math.max(1, Number(result.totalPages) || 1);
        get('transportRiderReadinessPage').textContent = 'Page ' + page + ' of ' + pages + ' · ' + result.totalCount;
        prev.disabled = page <= 1; next.disabled = page >= pages;
        loadedFilter = snapshot; exportBtn.disabled = !result.totalCount;
        status.textContent = 'Snapshot only. Recheck before dispatch.';
    } catch (error) {
        if (request === sequence) {
            body.replaceChildren(); status.textContent = error.message;
            prev.disabled = true; next.disabled = true;
        }
    }
}
get('transportRiderReadinessFilters').addEventListener('submit', e => { e.preventDefault(); page = 1; load(); });
for (const field of [campus, mode]) field.addEventListener('change', invalidate);
search.addEventListener('input', invalidate);
prev.addEventListener('click', () => { page = Math.max(1, page - 1); load(); });
next.addEventListener('click', () => { page = Math.min(pages, page + 1); load(); });
exportBtn.addEventListener('click', async () => {
    const snapshot = filters(), request = sequence;
    if (loadedFilter===null || loadedFilter !== snapshot) return;
    exportBtn.disabled = true;
    try {
        const response = await fetch('/api/transport/rider-readiness/export?' + snapshot, { credentials: 'same-origin', cache: 'no-store' });
        if (!response.ok) throw Error('Export unavailable');
        const blob = await response.blob();
        if (request !== sequence || loadedFilter !== snapshot || snapshot !== filters()) throw Error('Filters changed; retry.');
        const url = URL.createObjectURL(blob), link = document.createElement('a');
        link.href = url; link.download = 'transport-rider-readiness.csv';
        document.body.append(link); link.click(); link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (error) { status.textContent = error.message; }
    finally { exportBtn.disabled = loadedFilter === null || loadedFilter !== filters(); }
});
(async () => {
    try {
        const response = await fetch('/api/transport/route-setup/campuses', { credentials: 'same-origin', cache: 'no-store' });
        const payload = await response.json();
        if (!response.ok || !payload.success || !Array.isArray(payload.data)) throw Error('Campus list unavailable');
        for (const item of payload.data) {
            const option = document.createElement('option');
            option.value = String(item.id); option.textContent = item.name; campus.append(option);
        }
    } catch (error) { status.textContent = error.message; }
    await load();
})();
})();
