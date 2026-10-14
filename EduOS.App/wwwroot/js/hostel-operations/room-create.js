(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('hostelRoomCreatePanel')) return;
    const facilitySelect = el('hostelRoomFacilitySelect');
    const search = el('hostelRoomFacilitySearch');
    const next = el('hostelRoomFacilityNext');
    const createButton = el('hostelRoomCreateButton');
    const status = el('hostelRoomCreateStatus');
    let page = 1, totalPages = 1, generation = 0, loading = false, creating = false;
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
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Room administration request failed.');
        return result.data;
    }
    async function loadFacilities(reset = false) {
        if (reset) { page = 1; facilitySelect.replaceChildren(new Option('Choose an active facility', '')); }
        const current = ++generation;
        loading = true; next.disabled = true; notify('Loading facilities…');
        try {
            const params = new URLSearchParams({ page: String(page), pageSize: '25' });
            if (search.value.trim()) params.set('search', search.value.trim());
            const data = await request('/api/hostel/facilities?' + params);
            if (current !== generation) return;
            totalPages = Math.max(1, Number(data.totalPages) || 1);
            let added = 0;
            for (const item of data.items || []) {
                if (!item.isActive) continue;
                if ([...facilitySelect.options].some(x => x.value === String(item.reference))) continue;
                facilitySelect.add(new Option(item.campusName + ' — ' + item.name + ' (' + item.code + ')', item.reference));
                added++;
            }
            notify(added ? 'Choose a facility, then enter room details.' : 'No additional active facilities found.');
        } catch (error) { if (current === generation) notify(error.message, true); }
        finally { if (current === generation) { loading = false; next.disabled = page >= totalPages; } }
    }
    el('hostelRoomFacilitySearchForm').addEventListener('submit', event => {
        event.preventDefault(); loadFacilities(true);
    });
    search.addEventListener('input', () => {
        generation++; page = 1; totalPages = 1; facilitySelect.replaceChildren(new Option('Search to choose facility', ''));
        next.disabled = true; notify('Search changed. Select Find to load facilities.');
    });
    next.addEventListener('click', () => { if (!loading && page < totalPages) { page++; loadFacilities(); } });
    el('hostelRoomCreateForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (creating) return;
        const hostelReference = facilitySelect.value;
        const roomNumber = el('hostelRoomNewNumber').value.trim();
        const capacity = Number(el('hostelRoomNewCapacity').value);
        if (!hostelReference || !roomNumber || roomNumber.length > 50 ||
            !Number.isSafeInteger(capacity) || capacity < 1 || capacity > 1000) {
            notify('Select a facility and enter a valid room number and capacity (1–1000).', true); return;
        }
        creating = true; createButton.disabled = true;
        try {
            await request('/api/hostel/rooms', 'POST', { hostelReference, roomNumber, capacity });
            el('hostelRoomNewNumber').value = '';
            el('hostelRoomNewCapacity').value = '';
            notify('Room saved. The room inventory will refresh.');
            const inventory = el('hostelRoomInventoryFilter');
            if (inventory) inventory.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
        } catch (error) { notify(error.message, true); }
        finally { creating = false; createButton.disabled = false; }
    });
    loadFacilities(true);
})();
