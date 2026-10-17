import { el, api, alert, action, cell, dateText } from './workspace-core.js?v=1';
const size = 20;
let page = 1, pages = 1, generation = 0, busy = false;
function lock() {
    el('cwDeviceRefresh').disabled = busy;
    el('cwDevicePrev').disabled = busy || page <= 1;
    el('cwDeviceNext').disabled = busy || page >= pages;
    el('cwDeviceRows').querySelectorAll('button').forEach(button => { button.disabled = busy; });
}
function render(items, total) {
    pages = Math.max(1, Math.ceil(total / size));
    el('cwDevicePage').textContent = `Page ${page} of ${pages}`;
    const body = el('cwDeviceRows'); body.replaceChildren();
    for (const row of items) {
        const tr = document.createElement('tr');
        tr.append(cell(row.platform), cell(row.deviceId), cell(dateText(row.lastSeenAt)),
            cell(row.isActive ? 'Active' : 'Disabled'));
        const actions = cell('');
        if (row.isActive) actions.append(action('Disable', () => revoke(row.id), busy));
        tr.append(actions); body.append(tr);
    }
    if (!items.length) {
        const tr = document.createElement('tr'), td = cell('No registered devices on this page.');
        td.colSpan = 5; tr.append(td); body.append(tr);
    }
    lock();
}
async function load() {
    const request = ++generation;
    try {
        const data = await api(`/api/communication/devices?page=${page}&pageSize=${size}`);
        if (request !== generation) return;
        render(data.items || [], data.totalCount || 0);
    } catch (error) { if (request === generation) alert(error.message); }
}
async function revoke(id) {
    if (busy || !window.confirm('Disable push registration for this device?')) return;
    busy = true; lock();
    try {
        await api(`/api/communication/devices/${encodeURIComponent(id)}/deactivate`, 'POST');
        await load(); alert('Device registration disabled.', true);
    } catch (error) { alert(error.status === 409 ? 'Device changed elsewhere. Refresh and retry.' : error.message); }
    finally { busy = false; lock(); }
}
export function initDevices() {
    if (!el('cwDeviceRows')) return;
    el('cwDeviceRefresh').addEventListener('click', () => { if (!busy) load(); });
    el('cwDevicePrev').addEventListener('click', () => { if (!busy && page > 1) { page--; load(); } });
    el('cwDeviceNext').addEventListener('click', () => { if (!busy && page < pages) { page++; load(); } });
    load();
}
