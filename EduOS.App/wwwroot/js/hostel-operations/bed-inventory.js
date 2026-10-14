(() => {
    'use strict';
    const byId = id => document.getElementById(id);
    const panel = byId('hostelBedInventoryPanel');
    if (!panel) return;
    const tbody = byId('hostelBedInventoryRows'), status = byId('hostelBedInventoryStatus');
    const prev = byId('hostelBedInventoryPrev'), next = byId('hostelBedInventoryNext');
    const search = byId('hostelBedInventorySearch');
    let page = 1, totalPages = 1, generation = 0, busy = false;
    const td = value => { const cell = document.createElement('td'); cell.textContent = String(value ?? ''); return cell; };
    const notify = (message, error = false) => { status.textContent = message; status.className = error ? 'small text-danger' : 'small text-muted'; };
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
    function controls() {
        prev.disabled = busy || page <= 1;
        next.disabled = busy || page >= totalPages;
        byId('hostelBedInventoryPage').textContent = 'Page ' + page + ' of ' + totalPages;
    }
    async function load() {
        const current = ++generation;
        busy = true; tbody.replaceChildren(); controls(); notify('Loading bed inventory…');
        try {
            const params = new URLSearchParams({ page: String(page), pageSize: '25' });
            if (search.value.trim()) params.set('search', search.value.trim());
            const data = await request('/api/hostel/bed-inventory?' + params);
            if (current !== generation) return;
            totalPages = Math.max(1, Number(data.totalPages) || 1);
            for (const item of data.items || []) {
                const tr = document.createElement('tr');
                [item.hostelName, item.roomNumber, item.bedNumber,
                    item.isActive ? 'Active' : 'Inactive', item.isOccupied ? 'Occupied' : 'Free']
                    .forEach(value => tr.append(td(value)));
                const action = document.createElement('td'), button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-outline-secondary btn-sm';
                button.textContent = item.isActive ? 'Deactivate' : 'Activate';
                button.disabled = item.isActive && item.isOccupied;
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
                cell.colSpan = 6; tr.append(cell); tbody.append(tr);
            }
            notify(String(data.totalCount || 0) + ' matching beds.');
        } catch (error) { if (current === generation) notify(error.message, true); }
        finally { if (current === generation) { busy = false; controls(); } }
    }
    byId('hostelBedInventoryFilter').addEventListener('submit', event => {
        event.preventDefault(); page = 1; load();
    });
    search.addEventListener('input', () => {
        generation++; tbody.replaceChildren(); totalPages = 1; page = 1;
        notify('Filter changed. Select Search to load updated results.'); controls();
    });
    prev.addEventListener('click', () => { if (!busy && page > 1) { page--; load(); } });
    next.addEventListener('click', () => { if (!busy && page < totalPages) { page++; load(); } });
    load();
})();
