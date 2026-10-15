(() => {
    'use strict';
    const el = id => document.getElementById(id), form = el('transportCapacityFilter');
    if (!form) return;
    let page = 1, pages = 1, sequence = 0;
    const alertUser = message => { const box = el('transportCapacityAlert'); box.className = 'alert alert-danger'; box.textContent = message; box.focus(); };
    async function api(url) {
        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw Error(result?.message || 'Capacity report unavailable.');
        return result.data;
    }
    function query(includePage = true) {
        const q = new URLSearchParams();
        if (includePage) { q.set('page', String(page)); q.set('pageSize', '15'); }
        const campus = el('transportCapacityCampus').value, search = el('transportCapacitySearch').value.trim();
        if (campus) q.set('campusId', campus);
        if (search) q.set('search', search);
        return q;
    }
    function cell(value) { const td = document.createElement('td'); td.textContent = value == null ? '—' : String(value); return td; }
    async function campuses() {
        const data = await api('/api/transport/route-setup/campuses');
        const select = el('transportCapacityCampus');
        for (const item of data || []) { const opt = document.createElement('option'); opt.value = item.id; opt.textContent = item.name; select.append(opt); }
    }
    async function load() {
        const current = ++sequence, data = await api('/api/transport/capacity-report?' + query());
        if (current !== sequence) return;
        const body = el('transportCapacityRows'); body.replaceChildren();
        for (const item of data.items || []) {
            const tr = document.createElement('tr');
            [item.campusName, item.vehicleNumber, item.isActive ? 'Active' : 'Inactive',
                item.capacity, item.activeRiders, item.availableSeats, item.currentRouteLinks,
                item.utilizationPercent + '%'].forEach(x => tr.append(cell(x)));
            if (item.overCapacity) tr.classList.add('table-danger');
            body.append(tr);
        }
        if (!data.items?.length) { const tr = document.createElement('tr'), td = cell('No vehicles match this filter.'); td.colSpan = 8; tr.append(td); body.append(tr); }
        pages = Math.max(1, Number(data.totalPages) || 1);
        el('transportCapacityPageInfo').textContent = 'Page ' + page + ' of ' + pages + ' · ' + data.totalCount + ' vehicles';
        el('transportCapacityPrev').disabled = page <= 1;
        el('transportCapacityNext').disabled = page >= pages;
    }
    form.addEventListener('submit', event => { event.preventDefault(); page = 1; load().catch(e => alertUser(e.message)); });
    el('transportCapacityPrev').addEventListener('click', () => { page = Math.max(1, page - 1); load().catch(e => alertUser(e.message)); });
    el('transportCapacityNext').addEventListener('click', () => { page = Math.min(pages, page + 1); load().catch(e => alertUser(e.message)); });
    el('transportCapacityExport').addEventListener('click', async () => {
        const button = el('transportCapacityExport'); button.disabled = true;
        try {
            const response = await fetch('/api/transport/capacity-report/export?' + query(false), { credentials: 'same-origin', cache: 'no-store' });
            if (!response.ok) { const error = await response.json().catch(() => null); throw Error(error?.message || 'CSV export failed.'); }
            const blob = await response.blob(), url = URL.createObjectURL(blob), link = document.createElement('a');
            link.href = url; link.download = 'transport-capacity.csv'; document.body.append(link); link.click(); link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch (e) { alertUser(e.message); } finally { button.disabled = false; }
    });
    window.addEventListener('transport:vehicles-changed', () => load().catch(e => alertUser(e.message)));
    Promise.all([campuses(), load()]).catch(e => alertUser(e.message));
})();
