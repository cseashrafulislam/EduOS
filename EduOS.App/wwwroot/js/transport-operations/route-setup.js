(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('transportRouteSetupForm')) return;
    const form = el('transportRouteSetupForm');
    const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    let page = 1, totalPages = 1, loadSequence = 0;
    function notify(message, ok = false) {
        const box = el('transportRouteSetupAlert');
        box.className = 'alert alert-' + (ok ? 'success' : 'danger');
        box.textContent = message; box.focus();
    }
    async function api(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) { headers['Content-Type'] = 'application/json'; headers['RequestVerificationToken'] = token; }
        const response = await fetch(url, { method, headers, credentials: 'same-origin', cache: 'no-store',
            ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
        const json = await response.json().catch(() => null);
        if (!response.ok || !json?.success) throw new Error(json?.message || 'Transport route request failed.');
        return json.data;
    }
    function cell(value) { const td = document.createElement('td'); td.textContent = value == null ? '' : String(value); return td; }
    function option(select, value, label) {
        const node = document.createElement('option'); node.value = String(value); node.textContent = label; select.append(node);
    }
    async function loadCampuses() {
        const campuses = await api('/api/transport/route-setup/campuses');
        const select = el('transportRouteSetupCampus');
        select.replaceChildren(); option(select, '', 'Select campus');
        (campuses || []).forEach(c => option(select, c.id, c.name));
    }
    async function loadRoutes() {
        const sequence = ++loadSequence;
        const q = new URLSearchParams({ page: String(page), pageSize: '15' });
        const campus = el('transportRouteSetupFilterCampus').value;
        const term = el('transportRouteSetupSearch').value.trim();
        if (campus) q.set('campusId', campus);
        if (term) q.set('search', term);
        const data = await api('/api/transport/route-setup?' + q.toString());
        if (sequence !== loadSequence) return;
        const body = el('transportRouteSetupRows'); body.replaceChildren();
        (data.items || []).forEach(item => {
            const tr = document.createElement('tr');
            [item.campusName, item.code, item.name, item.stopCount, item.isActive ? 'Active' : 'Inactive']
                .forEach(value => tr.append(cell(value)));
            body.append(tr);
        });
        if (!data.items?.length) { const tr = document.createElement('tr'); const td = cell('No routes found'); td.colSpan = 5; tr.append(td); body.append(tr); }
        totalPages = Math.max(1, Number(data.totalPages) || 1);
        el('transportRouteSetupPageInfo').textContent = 'Page ' + page + ' of ' + totalPages + ' · ' + data.totalCount + ' routes';
        el('transportRouteSetupPrev').disabled = page <= 1;
        el('transportRouteSetupNext').disabled = page >= totalPages;
    }
    async function save(event) {
        event.preventDefault();
        if (!form.reportValidity()) return;
        const stops = el('transportRouteSetupStops').value.split(/\r?\n/).map(x => x.trim()).filter(Boolean);
        if (stops.length < 1 || stops.length > 50 || stops.some(x => x.length > 150) ||
            new Set(stops.map(x => x.toLocaleLowerCase())).size !== stops.length) {
            notify('Enter 1–50 unique stop names, one per line (maximum 150 characters).'); return;
        }
        const button = el('transportRouteSetupSave'); button.disabled = true;
        try {
            const request = { campusId: Number(el('transportRouteSetupCampus').value),
                name: el('transportRouteSetupName').value.trim(),
                code: el('transportRouteSetupCode').value.trim(),
                stops: stops.map(name => ({ name })) };
            await api('/api/transport/route-setup', 'POST', request);
            notify('Route and stops saved.', true);
            window.dispatchEvent(new CustomEvent('transport:routes-changed'));
            form.reset(); page = 1;
            await loadRoutes();
        } catch (err) { notify(err.message); } finally { button.disabled = false; }
    }
    form.addEventListener('submit', save);
    el('transportRouteSetupSearchForm').addEventListener('submit', event => { event.preventDefault(); page = 1; loadRoutes().catch(e => notify(e.message)); });
    el('transportRouteSetupPrev').addEventListener('click', () => { page = Math.max(1, page - 1); loadRoutes().catch(e => notify(e.message)); });
    el('transportRouteSetupNext').addEventListener('click', () => { page = Math.min(totalPages, page + 1); loadRoutes().catch(e => notify(e.message)); });
    Promise.all([loadCampuses(), loadRoutes()]).then(() => {
        const filter = el('transportRouteSetupFilterCampus');
        const create = el('transportRouteSetupCampus');
        Array.from(create.options).slice(1).forEach(o => option(filter, o.value, o.textContent));
    }).catch(e => notify(e.message));
})();
