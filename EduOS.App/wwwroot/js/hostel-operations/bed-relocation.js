(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const panel = el('hostelBedRelocationPanel');
    if (!panel) return;
    const select = el('hostelBedRelocationRoom'), search = el('hostelBedRelocationSearch');
    const more = el('hostelBedRelocationMore'), save = el('hostelBedRelocationSave');
    const status = el('hostelBedRelocationStatus');
    let bed = null, page = 1, pages = 1, generation = 0, busy = false, saving = false;
    const notify = (message, error = false) => {
        status.textContent = message;
        status.className = error ? 'small text-danger mb-2' : 'small text-muted mb-2';
    };
    async function request(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw Error('Security token missing. Reload the page.');
            headers['Content-Type'] = 'application/json';
            headers.RequestVerificationToken = token;
        }
        const response = await fetch(url, { method, headers, credentials: 'same-origin', cache: 'no-store',
            ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw Error(result?.message || 'Bed relocation request failed.');
        return result.data;
    }
    async function loadRooms(reset = false) {
        if (!bed) return;
        if (reset) { page = 1; select.replaceChildren(new Option('Choose destination room', '')); }
        const current = ++generation, selectedBed = bed.id;
        busy = true; more.disabled = true; save.disabled = true;
        notify('Loading eligible rooms…');
        try {
            const params = new URLSearchParams({ page: String(page), pageSize: '25' });
            if (search.value.trim()) params.set('search', search.value.trim());
            const data = await request('/api/hostel/beds/' + encodeURIComponent(selectedBed) +
                '/relocation-rooms?' + params);
            if (current !== generation || bed?.id !== selectedBed) return;
            pages = Math.max(1, Number(data.totalPages) || 1);
            for (const room of data.items || []) {
                if ([...select.options].some(x => x.value === String(room.id))) continue;
                const option = new Option(room.roomNumber + ' (' + room.physicalBeds + '/' +
                    room.capacity + ' physical beds)', String(room.id));
                option.dataset.version = room.rowVersion;
                select.add(option);
            }
            notify((data.totalCount || 0) + ' eligible destination rooms.');
        } catch (error) { if (current === generation) notify(error.message, true); }
        finally {
            if (current === generation) {
                busy = false; more.disabled = page >= pages; save.disabled = !select.value || saving;
            }
        }
    }
    document.addEventListener('hostel:relocate-bed', event => {
        const item = event.detail;
        if (!item || !item.isActive || item.hasAllocationHistory || !item.rowVersion) return;
        bed = { id: item.id, rowVersion: item.rowVersion, bedNumber: item.bedNumber,
            hostelName: item.hostelName, roomNumber: item.roomNumber };
        generation++; page = 1; pages = 1; search.value = '';
        select.replaceChildren(new Option('Choose destination room', ''));
        el('hostelBedRelocationContext').textContent = 'Move bed ' + bed.bedNumber + ' from ' +
            bed.hostelName + ' / ' + bed.roomNumber + '.';
        panel.classList.remove('d-none');
        loadRooms(true);
    });
    el('hostelBedRelocationSearchForm').addEventListener('submit', event => {
        event.preventDefault(); if (!saving) loadRooms(true);
    });
    search.addEventListener('input', () => {
        generation++; page = 1; pages = 1; more.disabled = true; save.disabled = true;
        select.replaceChildren(new Option('Search to load destination rooms', ''));
        notify('Search changed. Select Search to load rooms.');
    });
    more.addEventListener('click', () => { if (!busy && !saving && page < pages) { page++; loadRooms(); } });
    select.addEventListener('change', () => { save.disabled = busy || saving || !select.value; });
    el('hostelBedRelocationCancel').addEventListener('click', () => {
        generation++; bed = null; panel.classList.add('d-none');
    });
    save.addEventListener('click', async () => {
        if (!bed || busy || saving) return;
        const target = select.selectedOptions[0], targetRoomId = Number(select.value);
        if (!Number.isSafeInteger(targetRoomId) || targetRoomId <= 0 || !target?.dataset.version) {
            notify('Select a destination room.', true); return;
        }
        if (!confirm('Move never-allocated bed ' + bed.bedNumber + ' to the selected room?')) return;
        saving = true; save.disabled = true; more.disabled = true;
        try {
            await request('/api/hostel/beds/' + encodeURIComponent(bed.id) + '/relocate', 'PUT', {
                targetRoomId, bedRowVersion: bed.rowVersion, targetRoomRowVersion: target.dataset.version
            });
            generation++; bed = null; panel.classList.add('d-none');
            for (const id of ['hostelBedInventoryFilter', 'hostelRoomInventoryFilter']) {
                const form = el(id);
                if (form) form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
            }
        } catch (error) { notify(error.message, true); }
        finally { saving = false; save.disabled = !select.value; more.disabled = busy || page >= pages; }
    });
})();
