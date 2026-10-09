(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('transportTitle')) return;
    const canManage = !!el('transportAssignmentForm');
    let routes = [], vehicles = [], clientRequestId = newRequestId(), studentPage = 1, activePage = 1, activePages = 1;
    function newRequestId() { return crypto.randomUUID(); }
    function localDate() { const d = new Date(); return [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'), String(d.getDate()).padStart(2, '0')].join('-'); }
    function notify(message, success = false) {
        const alert = el('transportAlert'); alert.className = 'alert alert-' + (success ? 'success' : 'danger'); alert.textContent = message; alert.focus();
    }
    async function api(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' }; if (body !== undefined) headers['Content-Type'] = 'application/json';
        const response = await fetch(url, { method, headers, credentials: 'same-origin', cache: 'no-store', ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Transport request failed.');
        return result.data;
    }
    function option(select, value, label) { const x = document.createElement('option'); x.value = value; x.textContent = label; select.append(x); }
    function textCell(value) { const td = document.createElement('td'); td.textContent = value === null || value === undefined || value === '' ? '—' : String(value); return td; }
    function renderList(id, data, describe) {
        const list = el(id); list.replaceChildren();
        if (!data.length) { const item = document.createElement('li'); item.className = 'list-group-item text-muted'; item.textContent = 'No active records'; list.append(item); return; }
        data.forEach(x => { const item = document.createElement('li'); item.className = 'list-group-item'; item.textContent = describe(x); list.append(item); });
    }
    function fillSelect(id, items, label, name) {
        const select = el(id); select.replaceChildren(); option(select, '', label);
        items.forEach(x => option(select, x.reference, name(x)));
    }
    function updateStops() {
        const route = routes.find(x => x.reference === el('transportRoute').value);
        for (const id of ['transportPickup', 'transportDrop']) {
            const select = el(id); select.replaceChildren(); option(select, '', 'No assigned stop');
            (route?.stops || []).filter(x => x.isActive).forEach(x => option(select, String(x.id), x.name));
        }
    }
    async function loadStatic() {
        try {
            const [routeData, vehicleData] = await Promise.all([api('/api/transport/routes'), api('/api/transport/vehicles')]);
            routes = routeData || []; vehicles = vehicleData || [];
            renderList('transportRouteList', routes, x => x.name + ' · Default fare ' + x.defaultFare + ' · ' + (x.stops?.length || 0) + ' stops');
            renderList('transportVehicleList', vehicles, x => x.vehicleNumber + ' · Capacity ' + x.capacity + ' · Assigned students ' + x.activeAssignments);
            if (canManage) {
                fillSelect('transportRoute', routes, 'Select route', x => x.name);
                fillSelect('transportVehicle', vehicles, 'Select vehicle', x => x.vehicleNumber + ' (' + x.activeAssignments + '/' + x.capacity + ')');
                updateStops();
            }
        } catch (err) { notify(err.message); }
    }
    async function loadMine() {
        try {
            const data = await api('/api/transport/my-assignment');
            el('transportMine').textContent = data ? [data.routeName, data.vehicleNumber, 'From ' + data.startDate, data.pickupStopName || ''].filter(Boolean).join(' · ') : 'No active transport assignment.';
        } catch (err) { notify(err.message); }
    }
    async function loadStudents() {
        try {
            const term = el('transportStudentSearch').value.trim();
            const q = new URLSearchParams({ page: String(studentPage), pageSize: '25' });
            if (term) q.set('search', term);
            const data = await api('/api/transport/eligible-students?' + q);
            const select = el('transportStudent'); select.replaceChildren(); option(select, '', 'Select eligible student');
            (data.items || []).forEach(x => option(select, x.enrollmentReference, x.studentName + ' (' + x.studentCode + ', Roll ' + x.roll + ')'));
            el('transportMoreStudents').disabled = studentPage >= data.totalPages;
            if (!data.items?.length) notify('No eligible students on this page.');
        } catch (err) { notify(err.message); }
    }
    async function assign(event) {
        event.preventDefault(); const form = el('transportAssignmentForm');
        if (!form.reportValidity()) return;
        const save = el('transportSave'); save.disabled = true;
        try {
            const amount = el('transportFare').value.trim();
            const body = { clientRequestId, studentEnrollmentReference: el('transportStudent').value,
                routeReference: el('transportRoute').value, vehicleReference: el('transportVehicle').value,
                pickupStopId: el('transportPickup').value ? Number(el('transportPickup').value) : null,
                dropStopId: el('transportDrop').value ? Number(el('transportDrop').value) : null,
                startDate: el('transportStart').value, monthlyFare: amount ? Number(amount) : null };
            if (amount && (!Number.isFinite(body.monthlyFare) || body.monthlyFare < 0)) throw new Error('Monthly fare must be nonnegative.');
            await api('/api/transport/assignments', 'POST', body);
            clientRequestId = newRequestId(); form.reset(); el('transportStart').value = localDate(); updateStops();
            notify('Transport assignment saved.', true);
            await Promise.all([loadStudents(), loadActive(), loadStatic()]);
        } catch (err) { notify(err.message); } finally { save.disabled = false; }
    }
    async function loadActive() {
        try {
            const q = new URLSearchParams({ page: String(activePage), pageSize: '25' });
            const term = el('transportActiveTerm').value.trim(); if (term) q.set('search', term);
            const data = await api('/api/transport/active-assignments?' + q);
            const body = el('transportActiveRows'); body.replaceChildren();
            for (const item of data.items || []) {
                const tr = document.createElement('tr');
                [item.studentName, item.routeName, item.vehicleNumber, item.startDate].forEach(x => tr.append(textCell(x)));
                const actions = document.createElement('td'), close = document.createElement('button');
                close.type = 'button'; close.className = 'btn btn-outline-danger btn-sm'; close.textContent = 'Close';
                close.addEventListener('click', () => closeAssignment(item, close));
                actions.append(close); tr.append(actions); body.append(tr);
            }
            if (!data.items?.length) {
                const tr = document.createElement('tr'), td = textCell('No active assignments');
                td.colSpan = 5; tr.append(td); body.append(tr);
            }
            activePages = Math.max(1, Number(data.totalPages) || 1);
            el('transportPageInfo').textContent = 'Page ' + activePage + ' of ' + activePages + ' · ' + data.totalCount + ' total';
            el('transportPrev').disabled = activePage <= 1;
            el('transportNext').disabled = activePage >= activePages;
        } catch (err) { notify(err.message); }
    }
    async function closeAssignment(item, button) {
        if (!confirm('Close the transport assignment for ' + item.studentName + '?')) return;
        const date = prompt('End date (YYYY-MM-DD)', localDate());
        if (!date) return;
        if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || date < item.startDate) { notify('Choose a valid end date on or after the start.'); return; }
        button.disabled = true;
        try {
            await api('/api/transport/assignments/' + encodeURIComponent(item.reference) + '/close', 'POST',
                { endDate: date, rowVersion: item.rowVersion });
            notify('Transport assignment closed.', true);
            await Promise.all([loadActive(), loadStudents(), loadStatic()]);
        } catch (err) { notify(err.message); } finally { button.disabled = false; }
    }
    loadMine(); loadStatic();
    if (canManage) {
        el('transportStart').value = localDate();
        el('transportRoute').addEventListener('change', updateStops);
        el('transportFindStudents').addEventListener('click', () => { studentPage = 1; loadStudents(); });
        el('transportMoreStudents').addEventListener('click', () => { studentPage++; loadStudents(); });
        el('transportAssignmentForm').addEventListener('submit', assign);
        el('transportActiveSearch').addEventListener('submit', event => { event.preventDefault(); activePage = 1; loadActive(); });
        el('transportPrev').addEventListener('click', () => { activePage = Math.max(1, activePage - 1); loadActive(); });
        el('transportNext').addEventListener('click', () => { activePage = Math.min(activePages, activePage + 1); loadActive(); });
        loadStudents(); loadActive();
    }
})();
