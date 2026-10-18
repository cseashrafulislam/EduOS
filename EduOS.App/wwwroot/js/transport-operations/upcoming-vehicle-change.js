(() => {
'use strict';
const el = id => document.getElementById(id);
if (!el('transportVehicleChangePanel')) return;
let pending = null, page = 1, pages = 1, busy = false;
function localDate() { const d = new Date(); return [d.getFullYear(), String(d.getMonth()+1).padStart(2,'0'), String(d.getDate()).padStart(2,'0')].join('-'); }
function notify(message, ok = false) { const a = el('transportAlert'); a.className = 'alert alert-' + (ok ? 'success' : 'danger'); a.textContent = message; a.focus(); }
async function api(url, method = 'GET', body) {
    const headers = { Accept: 'application/json' };
    if (body !== undefined) {
        const token = document.querySelector('meta[name="request-verification-token"]')?.content;
        if (!token) throw new Error('Security token missing. Reload.');
        headers['Content-Type'] = 'application/json'; headers.RequestVerificationToken = token;
    }
    const response = await fetch(url, { method, headers, credentials: 'same-origin', cache: 'no-store',
        ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
    const result = await response.json().catch(() => null);
    if (!response.ok || !result?.success) throw new Error(result?.message || 'Vehicle request failed.');
    return result.data;
}
async function load() {
    if (!pending || busy) return;
    busy = true;
    try {
        const q = new URLSearchParams({ page: String(page), pageSize: '25' });
        const data = await api('/api/transport/assignments/' + encodeURIComponent(pending.reference) + '/vehicle-options?' + q);
        const select = el('transportVehicleChangeChoice'); select.replaceChildren();
        const empty = document.createElement('option'); empty.value = ''; empty.textContent = 'Select a vehicle'; select.append(empty);
        for (const v of data.items || []) {
            const option = document.createElement('option'); option.value = v.vehicleReference;
            option.textContent = v.vehicleNumber + ' (' + v.reservedSeats + '/' + v.capacity + ' reserved)' +
                (v.isCurrent ? ' · current' : v.available ? '' : ' · full');
            option.disabled = !v.available; select.append(option);
        }
        pages = Math.max(1, Number(data.totalPages) || 1);
        el('transportVehicleChangePageInfo').textContent = 'Page ' + page + ' of ' + pages;
        el('transportVehicleChangePrev').disabled = page <= 1;
        el('transportVehicleChangeNext').disabled = page >= pages;
    } finally { busy = false; }
}
window.addEventListener('transport:change-vehicle', async event => {
    const item = event.detail;
    if (!item || item.startDate <= localDate()) { notify('Only future assignments can change vehicles.'); return; }
    pending = item; page = 1;
    el('transportVehicleChangeDetails').textContent = item.studentName + ' · ' + item.routeName +
        ' · starts ' + item.startDate + ' · currently ' + item.vehicleNumber;
    el('transportVehicleChangePanel').hidden = false;
    try { await load(); el('transportVehicleChangeChoice').focus(); }
    catch (error) { notify(error.message); pending = null; el('transportVehicleChangePanel').hidden = true; }
});
el('transportVehicleChangeForm').addEventListener('submit', async event => {
    event.preventDefault();
    if (!pending) return;
    const vehicleReference = el('transportVehicleChangeChoice').value;
    if (!vehicleReference) { notify('Select an available vehicle.'); return; }
    if (!confirm('Change vehicle for ' + pending.studentName + '? Route and monthly fare remain unchanged.')) return;
    const button = el('transportVehicleChangeSave'); button.disabled = true;
    try {
        await api('/api/transport/assignments/' + encodeURIComponent(pending.reference) + '/vehicle', 'PUT',
            { vehicleReference, rowVersion: pending.rowVersion });
        pending = null; el('transportVehicleChangePanel').hidden = true;
        notify('Upcoming vehicle changed.', true);
        window.dispatchEvent(new Event('transport:refresh-assignments'));
    } catch (error) { notify(error.message); } finally { button.disabled = false; }
});
el('transportVehicleChangeCancel').addEventListener('click', () => { pending = null; el('transportVehicleChangePanel').hidden = true; });
el('transportVehicleChangePrev').addEventListener('click', async () => {
    if (page <= 1 || busy) return;
    page--; try { await load(); } catch (error) { notify(error.message); }
});
el('transportVehicleChangeNext').addEventListener('click', async () => {
    if (page >= pages || busy) return;
    page++; try { await load(); } catch (error) { notify(error.message); }
});
})();