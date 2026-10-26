(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const form = el('transportStopDemandFilter');
    if (!form) return;
    const endpoint = '/api/transport/stop-demand';
    const campus = el('transportStopDemandCampus'), search = el('transportStopDemandSearch');
    const operatingDate = el('transportStopDemandDate');
    const attention = el('transportStopDemandAttention'), rows = el('transportStopDemandRows');
    const status = el('transportStopDemandAlert'), csvButton = el('transportStopDemandCsv');
    const prev = el('transportStopDemandPrev'), next = el('transportStopDemandNext');
    const pageLabel = el('transportStopDemandPage');
    let page = 1, pages = 1, sequence = 0, loadedFilter = null;
    const localToday = () => { const d = new Date(); return [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'), String(d.getDate()).padStart(2, '0')].join('-'); };
    operatingDate.value = localToday(); operatingDate.min = operatingDate.value;
    function filters() {
        const q = new URLSearchParams({ operatingDate: operatingDate.value });
        if (campus.value) q.set('campusId', campus.value);
        if (search.value.trim()) q.set('search', search.value.trim());
        if (attention.checked) q.set('attentionOnly', 'true');
        return q.toString();
    }
    const cell = value => {
        const td = document.createElement('td');
        td.textContent = value == null ? '—' : String(value);
        return td;
    };
    function showError(message) {
        status.textContent = message;
        status.className = 'alert alert-danger mb-2';
    }
    function invalidate() {
        ++sequence; page = 1; pages = 1; loadedFilter = null;
        csvButton.disabled = true; prev.disabled = true; next.disabled = true;
        rows.replaceChildren(); pageLabel.textContent = '';
        status.className = 'small text-muted mb-2';
        status.textContent = 'Filters changed. Apply to refresh stop demand.';
    }
    async function getJson(url) {
        const response = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json' }
        });
        const body = await response.json().catch(() => null);
        if (!response.ok || !body?.success || !body.data)
            throw Error(body?.message || 'Stop demand unavailable.');
        return body.data;
    }
    async function load() {
        if (!operatingDate.value || operatingDate.value < localToday()) { invalidate(); showError('Choose today or a future operating date.'); return; }
        const current = ++sequence, snapshot = filters();
        loadedFilter = null; csvButton.disabled = true;
        status.className = 'small text-muted mb-2';
        status.textContent = 'Loading route stop demand…';
        const q = new URLSearchParams(snapshot);
        q.set('page', String(page)); q.set('pageSize', '15');
        try {
            const data = await getJson(endpoint + '?' + q.toString());
            if (current !== sequence || snapshot !== filters()) return;
            rows.replaceChildren();
            for (const item of data.items || []) {
                const tr = document.createElement('tr');
                [item.campusName, (item.routeCode || '') + ' · ' + (item.routeName || ''),
                    item.stopName, item.sequenceNo, item.routeActive ? 'Active' : 'Inactive',
                    item.stopActive ? 'Active' : 'Inactive', item.currentPickups, item.currentDropoffs,
                    item.inactiveWithRiders ? 'Inactive with riders' : item.needsAttention ? 'Inactive' : '—']
                    .forEach(value => tr.append(cell(value)));
                if (item.inactiveWithRiders) tr.classList.add('table-danger');
                else if (item.needsAttention) tr.classList.add('table-warning');
                rows.append(tr);
            }
            if (!data.items?.length) {
                const tr = document.createElement('tr'), td = cell('No stops match these filters.');
                td.colSpan = 9; tr.append(td); rows.append(tr);
            }
            pages = Math.max(1, Number(data.totalPages) || 1);
            pageLabel.textContent = 'Page ' + page + ' of ' + pages + ' · ' + data.totalCount + ' stops';
            prev.disabled = page <= 1; next.disabled = page >= pages;
            loadedFilter = snapshot; csvButton.disabled = !data.items?.length;
            status.textContent = 'Reservations effective on ' + operatingDate.value + '; not measured boardings.';
        } catch (error) {
            if (current !== sequence) return;
            rows.replaceChildren(); prev.disabled = true; next.disabled = true;
            showError(error.message);
        }
    }
    form.addEventListener('submit', event => {
        event.preventDefault(); page = 1; load();
    });
    search.addEventListener('input', invalidate);
    operatingDate.addEventListener('change', invalidate);
    campus.addEventListener('change', invalidate);
    attention.addEventListener('change', invalidate);
    prev.addEventListener('click', () => { page = Math.max(1, page - 1); load(); });
    next.addEventListener('click', () => { page = Math.min(pages, page + 1); load(); });
    csvButton.addEventListener('click', async () => {
        const snapshot = filters(), current = sequence;
        if (loadedFilter === null || loadedFilter !== snapshot) return;
        csvButton.disabled = true;
        try {
            const response = await fetch(endpoint + '/export?' + snapshot, {
                credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'text/csv' }
            });
            if (!response.ok) {
                const error = await response.json().catch(() => null);
                throw Error(error?.message || 'Stop demand export failed.');
            }
            const blob = await response.blob();
            if (current !== sequence || snapshot !== filters() || loadedFilter !== snapshot)
                throw Error('Filters changed during export. Apply and retry.');
            const url = URL.createObjectURL(blob), link = document.createElement('a');
            link.href = url; link.download = 'transport-stop-demand.csv';
            document.body.append(link); link.click(); link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch (error) { showError(error.message); }
        finally { csvButton.disabled = loadedFilter === null || loadedFilter !== filters(); }
    });
    window.addEventListener('transport:routes-changed', () => load());
    (async () => {
        try {
            const choices = await getJson('/api/transport/route-setup/campuses');
            if (!Array.isArray(choices)) throw Error('Campus choices unavailable.');
            for (const item of choices) {
                const option = document.createElement('option');
                option.value = String(item.id); option.textContent = item.name;
                campus.append(option);
            }
        } catch (error) { showError(error.message); }
        await load();
    })();
})();
