(() => {
    'use strict';
    const byId = id => document.getElementById(id);
    if (!byId('hostelRoomInventoryPanel')) return;
    const rows = byId('hostelRoomInventoryRows'), status = byId('hostelRoomInventoryStatus');
    const prev = byId('hostelRoomInventoryPrev'), next = byId('hostelRoomInventoryNext');
    const search = byId('hostelRoomInventorySearch');
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
        prev.disabled = busy || page <= 1; next.disabled = busy || page >= totalPages;
        byId('hostelRoomInventoryPage').textContent = 'Page ' + page + ' of ' + totalPages;
    }
    async function load() {
        const current = ++generation;
        busy = true; rows.replaceChildren(); controls(); notify('Loading room inventory…');
        try {
            const params = new URLSearchParams({ page: String(page), pageSize: '25' });
            if (search.value.trim()) params.set('search', search.value.trim());
            const data = await request('/api/hostel/room-inventory?' + params);
            if (current !== generation) return;
            totalPages = Math.max(1, Number(data.totalPages) || 1);
            for (const item of data.items || []) {
                const tr = document.createElement('tr');
                [item.hostelName, item.roomNumber, item.capacity, item.activeBeds, item.occupiedBeds,
                    item.isActive ? 'Active' : 'Inactive'].forEach(value => tr.append(td(value)));
                const action = document.createElement('td'), button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-outline-secondary btn-sm';
                button.textContent = item.isActive ? 'Deactivate' : 'Activate';
                button.disabled = (item.isActive && item.occupiedBeds > 0) || (!item.isActive && !item.hostelActive);
                button.addEventListener('click', async () => {
                    if (!confirm((item.isActive ? 'Deactivate' : 'Activate') + ' room ' + item.roomNumber + '?')) return;
                    button.disabled = true;
                    try {
                        await request('/api/hostel/rooms/' + encodeURIComponent(item.id) + '/state', 'PUT',
                            { isActive: !item.isActive, rowVersion: item.rowVersion });
                        notify('Room state saved.'); await load();
                    } catch (error) { notify(error.message, true); button.disabled = false; }
                });
                action.append(button); tr.append(action); rows.append(tr);
            }
            if (!data.items?.length) {
                const tr = document.createElement('tr'), cell = td('No rooms match the filter.');
                cell.colSpan = 7; tr.append(cell); rows.append(tr);
            }
            notify(String(data.totalCount || 0) + ' matching rooms.');
        } catch (error) { if (current === generation) notify(error.message, true); }
        finally { if (current === generation) { busy = false; controls(); } }
    }
    byId('hostelRoomInventoryFilter').addEventListener('submit', event => { event.preventDefault(); page = 1; load(); });
    search.addEventListener('input', () => {
        generation++; rows.replaceChildren(); totalPages = 1; page = 1;
        notify('Filter changed. Select Search to load updated results.'); controls();
    });
    prev.addEventListener('click', () => { if (!busy && page > 1) { page--; load(); } });
    next.addEventListener('click', () => { if (!busy && page < totalPages) { page++; load(); } });
    load();
})();
