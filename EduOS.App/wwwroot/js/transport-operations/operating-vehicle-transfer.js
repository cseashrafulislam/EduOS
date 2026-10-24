(() => {
'use strict';
const el = id => document.getElementById(id);
if (!el('transportOperatingTransferPanel')) return;
let pending = null, page = 1, pages = 1, busy = false, requestId = null;
const localDate = (days = 0) => { const d = new Date(); d.setDate(d.getDate() + days); return [d.getFullYear(), String(d.getMonth()+1).padStart(2,'0'), String(d.getDate()).padStart(2,'0')].join('-'); };
function notify(message, ok = false) {
    const alert = el('transportAlert'); alert.className = 'alert alert-' + (ok ? 'success' : 'danger');
    alert.textContent = message; alert.focus();
}
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
    if (!response.ok || !result?.success) throw new Error(result?.message || 'Transfer request failed.');
    return result.data;
}
async function loadOptions() {
    if (!pending || busy) return;
    busy = true;
    try {
        const date = el('transportOperatingTransferDate').value;
        if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) return;
        const q = new URLSearchParams({ effectiveDate: date, page: String(page), pageSize: '25' });
        const data = await api('/api/transport/assignments/' + encodeURIComponent(pending.reference) + '/transfer-vehicles?' + q);
        const select = el('transportOperatingTransferVehicle'); select.replaceChildren();
        const empty = document.createElement('option'); empty.value = ''; empty.textContent = 'Select available vehicle'; select.append(empty);
        for (const v of data.items || []) {
            const option = document.createElement('option'); option.value = v.vehicleReference;
            option.textContent = v.vehicleNumber + ' (' + v.reservedSeats + '/' + v.capacity + ' reserved)' + (v.available ? '' : ' · full');
            option.disabled = !v.available; select.append(option);
        }
        pages = Math.max(1, Number(data.totalPages) || 1);
        el('transportOperatingTransferPageInfo').textContent = 'Page ' + page + ' of ' + pages;
        el('transportOperatingTransferPrev').disabled = page <= 1;
        el('transportOperatingTransferNext').disabled = page >= pages;
    } finally { busy = false; }
}
window.addEventListener('transport:transfer-operating-vehicle', async event => {
    const item = event.detail, today = localDate();
    if (!item || item.startDate > today || (item.endDate && item.endDate < today)) {
        notify('Only currently operating reservations can be transferred.'); return;
    }
    pending = item; requestId = crypto.randomUUID(); page = 1;
    el('transportOperatingTransferDetails').textContent = item.studentName + ' · ' + item.routeName + ' · current ' + item.vehicleNumber;
    const date = el('transportOperatingTransferDate');
    const start = item.startDate >= today ? localDate(1) : today;
    date.min = start; date.max = item.endDate || ''; date.value = start;
    el('transportOperatingTransferPanel').hidden = false;
    try { await loadOptions(); date.focus(); }
    catch (error) { notify(error.message); pending = null; el('transportOperatingTransferPanel').hidden = true; }
});
el('transportOperatingTransferDate').addEventListener('change', async () => {
    page = 1; if (!pending) return;
    try { await loadOptions(); } catch (error) { notify(error.message); }
});
el('transportOperatingTransferForm').addEventListener('submit', async event => {
    event.preventDefault();
    if (!pending) return;
    const effectiveDate = el('transportOperatingTransferDate').value;
    const vehicleReference = el('transportOperatingTransferVehicle').value;
    if (!vehicleReference || effectiveDate < localDate() || effectiveDate <= pending.startDate ||
        (pending.endDate && effectiveDate > pending.endDate)) {
        notify('Choose a valid transfer date and an available destination vehicle.'); return;
    }
    if (!confirm('Transfer ' + pending.studentName + ' from ' + effectiveDate + '? Prior vehicle history is preserved.')) return;
    const save = el('transportOperatingTransferSave'); save.disabled = true;
    try {
        await api('/api/transport/assignments/' + encodeURIComponent(pending.reference) + '/transfer-vehicle',
            'POST', { clientRequestId: requestId, vehicleReference, effectiveDate, rowVersion: pending.rowVersion });
        pending = null; requestId = null; el('transportOperatingTransferPanel').hidden = true;
        notify('Operating vehicle transfer saved.', true);
        window.dispatchEvent(new Event('transport:refresh-assignments'));
    } catch (error) { notify(error.message); } finally { save.disabled = false; }
});
el('transportOperatingTransferCancel').addEventListener('click', () => {
    pending = null; requestId = null; el('transportOperatingTransferPanel').hidden = true;
});
for (const [id, direction] of [['transportOperatingTransferPrev', -1], ['transportOperatingTransferNext', 1]]) {
    el(id).addEventListener('click', async () => {
        if (busy || page + direction < 1 || page + direction > pages) return;
        page += direction; try { await loadOptions(); } catch (error) { notify(error.message); }
    });
}
})();
