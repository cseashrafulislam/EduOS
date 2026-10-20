(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const form = el('transportSeatForecastFilter');
    if (!form) return;
    let page = 1, pages = 1, serial = 0, loadedFilter = null;
    const today = new Date();
    const iso = date => [date.getFullYear(), String(date.getMonth() + 1).padStart(2, '0'), String(date.getDate()).padStart(2, '0')].join('-');
    const end = new Date(today.getFullYear(), today.getMonth(), today.getDate() + 29);
    el('transportSeatForecastFrom').value = iso(today);
    el('transportSeatForecastTo').value = iso(end);
    function alertUser(message) {
        const box = el('transportSeatForecastAlert');
        box.className = 'alert alert-danger'; box.textContent = message; box.focus();
    }
    function clearAlert() { const box = el('transportSeatForecastAlert'); box.className = 'd-none'; box.textContent = ''; }
    async function api(url) {
        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw Error(result?.message || 'Seat forecast unavailable.');
        return result.data;
    }
    function filters(withPage = true) {
        const q = new URLSearchParams();
        q.set('fromDate', el('transportSeatForecastFrom').value);
        q.set('toDate', el('transportSeatForecastTo').value);
        const campus = el('transportSeatForecastCampus').value;
        const search = el('transportSeatForecastSearch').value.trim();
        if (campus) q.set('campusId', campus);
        if (search) q.set('search', search);
        if (withPage) { q.set('page', String(page)); q.set('pageSize', '15'); }
        return q.toString();
    }
    function validate() {
        const from = el('transportSeatForecastFrom').value, to = el('transportSeatForecastTo').value;
        const now = iso(new Date());
        const days = (Date.parse(to + 'T00:00:00') - Date.parse(from + 'T00:00:00')) / 86400000;
        if (!from || !to || from < now || !Number.isFinite(days) || days < 0 || days > 89)
            throw Error('Choose today or later and an interval of at most 90 days.');
    }
    function invalidate() {
        ++serial; page = 1; pages = 1; loadedFilter = null;
        el('transportSeatForecastCsv').disabled = true;
        el('transportSeatForecastRows').replaceChildren();
        el('transportSeatForecastStatus').textContent = 'Filters changed; apply to refresh.';
        el('transportSeatForecastPrev').disabled = true;
        el('transportSeatForecastNext').disabled = true;
    }
    function cell(value) { const td = document.createElement('td'); td.textContent = value == null ? '—' : String(value); return td; }
    async function load() {
        validate(); clearAlert();
        const current = ++serial, snapshot = filters();
        loadedFilter = null; el('transportSeatForecastCsv').disabled = true;
        const data = await api('/api/transport/seat-forecast?' + snapshot);
        if (current !== serial || snapshot !== filters()) return;
        const body = el('transportSeatForecastRows'); body.replaceChildren();
        for (const row of data.items || []) {
            const tr = document.createElement('tr');
            [row.campusName, row.vehicleNumber, row.isActive ? 'Active' : 'Inactive',
                row.capacity, row.peakReservedSeats, row.peakDate,
                row.minimumFreeSeats, row.hasRisk ? 'Needs review' : '—'].forEach(value => tr.append(cell(value)));
            if (row.hasRisk) tr.classList.add(row.overCapacity ? 'table-danger' : 'table-warning');
            body.append(tr);
        }
        if (!data.items?.length) { const tr = document.createElement('tr'), td = cell('No vehicles match this filter.'); td.colSpan = 8; tr.append(td); body.append(tr); }
        pages = Math.max(1, Number(data.totalPages) || 1);
        el('transportSeatForecastPage').textContent = 'Page ' + page + ' of ' + pages + ' · ' + data.totalCount + ' vehicles';
        el('transportSeatForecastStatus').textContent = 'Peak concurrent reservations across the selected interval; values may change before allocation.';
        el('transportSeatForecastPrev').disabled = page <= 1;
        el('transportSeatForecastNext').disabled = page >= pages;
        loadedFilter = filters(false);
        el('transportSeatForecastCsv').disabled = !data.items?.length;
    }
    for (const id of ['transportSeatForecastFrom', 'transportSeatForecastTo', 'transportSeatForecastCampus'])
        el(id).addEventListener('change', invalidate);
    el('transportSeatForecastSearch').addEventListener('input', invalidate);
    form.addEventListener('submit', e => { e.preventDefault(); page = 1; load().catch(err => alertUser(err.message)); });
    el('transportSeatForecastPrev').addEventListener('click', () => { page = Math.max(1, page - 1); load().catch(err => alertUser(err.message)); });
    el('transportSeatForecastNext').addEventListener('click', () => { page = Math.min(pages, page + 1); load().catch(err => alertUser(err.message)); });
    el('transportSeatForecastCsv').addEventListener('click', async () => {
        if (loadedFilter === null || loadedFilter !== filters(false)) return;
        const current = serial, q = filters(false), button = el('transportSeatForecastCsv');
        button.disabled = true;
        try {
            const response = await fetch('/api/transport/seat-forecast/export?' + q, { credentials: 'same-origin', cache: 'no-store' });
            if (!response.ok) {
                const error = await response.json().catch(() => null);
                throw Error(error?.message || 'CSV export failed.');
            }
            const blob = await response.blob();
            if (current !== serial || q !== filters(false)) throw Error('Filters changed during export. Retry.');
            const url = URL.createObjectURL(blob), link = document.createElement('a');
            link.href = url; link.download = 'transport-seat-forecast.csv';
            document.body.append(link); link.click(); link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch (error) { alertUser(error.message); }
        finally { button.disabled = loadedFilter === null || loadedFilter !== filters(false); }
    });
    window.addEventListener('transport:vehicles-changed', () => load().catch(err => alertUser(err.message)));
    api('/api/transport/route-setup/campuses').then(items => {
        for (const item of items || []) {
            const option = document.createElement('option');
            option.value = item.id; option.textContent = item.name;
            el('transportSeatForecastCampus').append(option);
        }
    }).catch(err => alertUser(err.message));
    load().catch(err => alertUser(err.message));
})();
