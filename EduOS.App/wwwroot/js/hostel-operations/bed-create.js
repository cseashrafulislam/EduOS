(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('hostelBedCreatePanel')) return;
    const select = el('hostelBedCreateRoom'), search = el('hostelBedCreateSearch');
    const next = el('hostelBedCreateMore'), save = el('hostelBedCreateButton');
    const status = el('hostelBedCreateStatus');
    let page = 1, totalPages = 1, generation = 0, loading = false, saving = false;
    const notify = (message, error = false) => {
        status.textContent = message;
        status.className = error ? 'small text-danger' : 'small text-muted';
    };
    async function request(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload the page.');
            headers['Content-Type'] = 'application/json';
            headers.RequestVerificationToken = token;
        }
        const response = await fetch(url, { method, headers, credentials: 'same-origin', cache: 'no-store',
            ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Bed creation failed.');
        return result.data;
    }
    async function loadRooms(reset = false) {
        if (reset) { page = 1; select.replaceChildren(new Option('Choose an active room', '')); }
        const current = ++generation;
        loading = true; next.disabled = true; notify('Loading rooms…');
        try {
            const params = new URLSearchParams({ page: String(page), pageSize: '25' });
            if (search.value.trim()) params.set('search', search.value.trim());
            const data = await request('/api/hostel/room-inventory?' + params);
            if (current !== generation) return;
            totalPages = Math.max(1, Number(data.totalPages) || 1);
            let count = 0;
            for (const room of data.items || []) {
                if (!room.isActive || !room.hostelActive || room.activeBeds >= room.capacity) continue;
                if ([...select.options].some(x => x.value === String(room.id))) continue;
                const option = new Option(room.hostelName + ' / ' + room.roomNumber +
                    ' (' + room.activeBeds + '/' + room.capacity + ' active)', String(room.id));
                option.dataset.version = room.rowVersion;
                select.add(option); count++;
            }
            notify(count ? 'Choose a room and enter the bed number.' : 'No additional rooms with available capacity.');
        } catch (error) { if (current === generation) notify(error.message, true); }
        finally { if (current === generation) { loading = false; next.disabled = page >= totalPages; } }
    }
    el('hostelBedCreateSearchForm').addEventListener('submit', event => {
        event.preventDefault(); loadRooms(true);
    });
    search.addEventListener('input', () => {
        generation++; page = 1; totalPages = 1;
        select.replaceChildren(new Option('Select Search to load rooms', ''));
        next.disabled = true; notify('Search changed. Select Search to load matching rooms.');
    });
    next.addEventListener('click', () => { if (!loading && page < totalPages) { page++; loadRooms(); } });
    el('hostelBedCreateForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (saving) return;
        const room = select.selectedOptions[0], hostelRoomId = Number(select.value);
        const bedNumber = el('hostelBedNewNumber').value.trim();
        if (!Number.isSafeInteger(hostelRoomId) || hostelRoomId <= 0 ||
            !room?.dataset.version || !bedNumber || bedNumber.length > 50) {
            notify('Select an active room and enter a bed number (up to 50 characters).', true); return;
        }
        saving = true; save.disabled = true;
        try {
            await request('/api/hostel/beds', 'POST',
                { hostelRoomId, bedNumber, roomRowVersion: room.dataset.version });
            el('hostelBedNewNumber').value = '';
            notify('Bed saved. Refreshing inventories.');
            const inventory = el('hostelBedInventoryFilter');
            if (inventory) inventory.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
            const rooms = el('hostelRoomInventoryFilter');
            if (rooms) rooms.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
            await loadRooms(true);
        } catch (error) { notify(error.message, true); }
        finally { saving = false; save.disabled = false; }
    });
    loadRooms(true);
})();
