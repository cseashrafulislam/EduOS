(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const form = el('hostelCapacityForm');
    if (!form) return;
    const status = el('hostelCapacityStatus'), roomSelect = el('hostelCapacityRoom');
    let rooms = [], generation = 0, busy = false;
    function message(text, error = false) {
        status.textContent = text;
        status.className = error ? 'small text-danger' : 'small text-muted';
    }
    async function request(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload the page.');
            headers['Content-Type'] = 'application/json';
            headers.RequestVerificationToken = token;
        }
        const response = await fetch(url, {
            method, headers, credentials: 'same-origin', cache: 'no-store',
            ...(body !== undefined ? { body: JSON.stringify(body) } : {})
        });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Capacity request failed.');
        return result.data;
    }
    function option(value, text) {
        const o = document.createElement('option'); o.value = value; o.textContent = text; return o;
    }
    async function load() {
        const current = ++generation;
        roomSelect.replaceChildren(option('', 'Loading rooms…'));
        message('Searching rooms…');
        try {
            const params = new URLSearchParams({ page: '1', pageSize: '100' });
            const term = el('hostelCapacitySearch').value.trim();
            if (term) params.set('search', term);
            const data = await request('/api/hostel/room-inventory?' + params);
            if (current !== generation) return;
            rooms = data.items || [];
            roomSelect.replaceChildren(option('', 'Select room'));
            rooms.forEach(room => roomSelect.append(option(String(room.id),
                room.hostelName + ' / ' + room.roomNumber + ' (capacity ' + room.capacity + ')')));
            message(String(data.totalCount || 0) + ' matching rooms. Showing first ' + rooms.length + '; narrow search if needed.');
        } catch (error) { if (current === generation) message(error.message, true); }
    }
    roomSelect.addEventListener('change', () => {
        const room = rooms.find(x => String(x.id) === roomSelect.value);
        if (room) el('hostelCapacityValue').value = String(room.capacity);
    });
    el('hostelCapacityFind').addEventListener('click', load);
    el('hostelCapacitySearch').addEventListener('input', () => {
        generation++; rooms = []; roomSelect.replaceChildren(option('', 'Search again to select a room'));
    });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (busy || !form.reportValidity()) return;
        const room = rooms.find(x => String(x.id) === roomSelect.value);
        const capacity = Number(el('hostelCapacityValue').value);
        if (!room || !Number.isInteger(capacity) || capacity < 1 || capacity > 1000) {
            message('Select a room and a whole-number capacity from 1 to 1000.', true); return;
        }
        busy = true; el('hostelCapacitySave').disabled = true;
        try {
            await request('/api/hostel/rooms/' + encodeURIComponent(room.id) + '/capacity', 'PUT',
                { capacity, rowVersion: room.rowVersion });
            message('Room capacity saved. Refreshing inventory…');
            window.location.reload();
        } catch (error) { message(error.message, true); }
        finally { busy = false; el('hostelCapacitySave').disabled = false; }
    });
    load();
})();
