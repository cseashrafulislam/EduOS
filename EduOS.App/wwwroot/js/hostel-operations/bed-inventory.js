(() => {
    'use strict';
    const byId = id => document.getElementById(id);
    const panel = byId('hostelBedInventoryPanel');
    if (!panel) return;
    const tbody = byId('hostelBedInventoryRows'), status = byId('hostelBedInventoryStatus');
    const prev = byId('hostelBedInventoryPrev'), next = byId('hostelBedInventoryNext');
    const search = byId('hostelBedInventorySearch'), form = byId('hostelBedInventoryFilter');
    const campus = byId('hostelBedInventoryCampus') || (() => {
        const select = document.createElement('select');
        select.id = 'hostelBedInventoryCampus'; select.className = 'form-select';
        select.setAttribute('aria-label', 'Filter bed inventory by campus');
        select.add(new Option('All permitted campuses', ''));
        form.prepend(select); return select;
    })();
    const exportButton = byId('hostelBedInventoryCsv') || (() => {
        const button = document.createElement('button');
        button.id = 'hostelBedInventoryCsv'; button.type = 'button';
        button.className = 'btn btn-outline-secondary';
        button.textContent = 'Export filtered beds (CSV)'; button.disabled = true;
        form.append(button); return button;
    })();
    let page = 1, totalPages = 1, generation = 0, busy = false, loadedFilter = null;
    const td = value => { const cell = document.createElement('td'); cell.textContent = String(value ?? ''); return cell; };
    const notify = (message, error = false) => { status.textContent = message; status.className = error ? 'small text-danger' : 'small text-muted'; };
    function filters(includePage = true) {
        const params = new URLSearchParams(includePage ? { page: String(page), pageSize: '25' } : {});
        if (campus.value) params.set('campusId', campus.value);
        if (search.value.trim()) params.set('search', search.value.trim());
        return params;
    }
    function controls() {
        prev.disabled = busy || page <= 1;
        next.disabled = busy || page >= totalPages;
        exportButton.disabled = busy || loadedFilter === null || loadedFilter !== filters(false).toString();
        byId('hostelBedInventoryPage').textContent = 'Page ' + page + ' of ' + totalPages;
    }
    function invalidate() {
        generation++; busy = false; loadedFilter = null; page = 1; totalPages = 1;
        tbody.replaceChildren(); controls();
        notify('Filters changed. Select Search to load updated results.');
    }
    async function request(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) {
            headers['Content-Type'] = 'application/json';
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload the page.');
            headers.RequestVerificationToken = token;
        }
        const response = await fetch(url, { method, headers, credentials: 'same-origin', cache: 'no-store',
            ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Hostel request failed.');
        return result.data;
    }
    async function load() {
        const current = ++generation;
        busy = true; loadedFilter = null; tbody.replaceChildren(); controls(); notify('Loading bed inventory…');
        try {
            const data = await request('/api/hostel/bed-inventory?' + filters());
            if (current !== generation) return;
            totalPages = Math.max(1, Number(data.totalPages) || 1);
            for (const item of data.items || []) {
                const tr = document.createElement('tr');
                [item.hostelName, item.roomNumber, item.bedNumber,
                    item.isActive ? 'Active' : 'Inactive', item.isOccupied ? 'Occupied' : 'Free',
                    item.hasActiveReservations ? 'Yes' : 'No']
                    .forEach(value => tr.append(td(value)));
                const action = document.createElement('td'), button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-outline-secondary btn-sm';
                button.textContent = item.isActive ? 'Deactivate' : 'Activate';
                button.disabled = item.isActive && item.hasActiveReservations;
                button.addEventListener('click', async () => {
                    if (!confirm((item.isActive ? 'Deactivate' : 'Activate') + ' bed ' + item.bedNumber + '?')) return;
                    button.disabled = true;
                    try {
                        await request('/api/hostel/beds/' + encodeURIComponent(item.id) + '/state', 'PUT',
                            { isActive: !item.isActive, rowVersion: item.rowVersion });
                        notify('Bed state saved.'); await load();
                    } catch (error) { notify(error.message, true); button.disabled = false; }
                });
                action.append(button); tr.append(action); tbody.append(tr);
            }
            if (!data.items?.length) {
                const tr = document.createElement('tr'), cell = td('No beds match the filter.');
                cell.colSpan = 7; tr.append(cell); tbody.append(tr);
            }
            loadedFilter = filters(false).toString();
            notify(String(data.totalCount || 0) + ' matching beds. Export is limited to 1,000 records.');
        } catch (error) { if (current === generation) notify(error.message, true); }
        finally { if (current === generation) { busy = false; controls(); } }
    }
    async function loadCampuses() {
        try {
            const data = await request('/api/hostel/facility-campuses');
            if (!Array.isArray(data)) throw Error('Campus options unavailable.');
            campus.replaceChildren(new Option('All permitted campuses', ''));
            for (const item of data) {
                const id = Number(item.id);
                if (Number.isSafeInteger(id) && id > 0)
                    campus.add(new Option(String(item.name || 'Campus'), String(id)));
            }
        } catch (error) { notify(error.message + ' You can still search permitted campuses.', true); }
    }
    byId('hostelBedInventoryFilter').addEventListener('submit', event => {
        event.preventDefault(); page = 1; load();
    });
    search.addEventListener('input', invalidate);
    campus.addEventListener('change', invalidate);
    prev.addEventListener('click', () => { if (!busy && page > 1) { page--; load(); } });
    next.addEventListener('click', () => { if (!busy && page < totalPages) { page++; load(); } });
    exportButton.addEventListener('click', async () => {
        const snapshot = filters(false).toString(), requestGeneration = generation;
        if (busy || loadedFilter === null || snapshot !== loadedFilter) return;
        exportButton.disabled = true;
        try {
            const response = await fetch('/api/hostel/bed-inventory/export?' + snapshot,
                { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/csv' } });
            if (!response.ok) {
                const error = await response.json().catch(() => null);
                throw Error(error?.message || 'Bed inventory export failed.');
            }
            const blob = await response.blob();
            if (requestGeneration !== generation || loadedFilter !== snapshot || filters(false).toString() !== snapshot)
                throw Error('Filters changed during export. Apply filters and retry.');
            const url = URL.createObjectURL(blob), link = document.createElement('a');
            link.href = url; link.download = 'hostel-bed-inventory.csv';
            document.body.append(link); link.click(); link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch (error) { notify(error.message, true); }
        finally { controls(); }
    });
    load(); loadCampuses();
})();
