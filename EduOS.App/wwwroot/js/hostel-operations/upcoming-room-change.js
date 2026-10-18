(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('hostelRoomMovePanel')) return;
    let pending = null, page = 1, pages = 1, serial = 0, busy = false;
    function notify(message, success = false) {
        const node = el('hostelAlert'); node.className = 'alert alert-' + (success ? 'success' : 'danger');
        node.textContent = message; node.focus();
    }
    async function api(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload the page.');
            headers['Content-Type'] = 'application/json'; headers.RequestVerificationToken = token;
        }
        const response = await fetch(url, { method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Room change failed.');
        return result.data;
    }
    function option(value, label) {
        const node = document.createElement('option'); node.value = value; node.textContent = label;
        el('hostelRoomMoveTarget').append(node);
    }
    function dismiss() {
        pending = null; ++serial; el('hostelRoomMovePanel').classList.add('d-none');
        el('hostelRoomMoveTarget').replaceChildren();
    }
    async function load() {
        if (!pending) return;
        const id = pending.id, current = ++serial;
        el('hostelRoomMoveSave').disabled = true;
        el('hostelRoomMoveTarget').replaceChildren(); option('', 'Loading…');
        try {
            const params = new URLSearchParams({ page: String(page), pageSize: '25' });
            const result = await api('/api/hostel/allocations/' + encodeURIComponent(id) + '/room-options?' + params);
            if (current !== serial || pending?.id !== id) return;
            el('hostelRoomMoveTarget').replaceChildren(); option('', 'Select room and bed');
            (result.items || []).forEach(x => option(String(x.bedId), 'Room ' + x.roomNumber + ' · Bed ' + x.bedNumber));
            pages = Math.max(1, Number(result.totalPages) || 1);
            el('hostelRoomMovePageInfo').textContent = 'Page ' + page + ' of ' + pages + ' · ' + result.totalCount + ' beds';
            el('hostelRoomMovePrev').disabled = page <= 1;
            el('hostelRoomMoveNext').disabled = page >= pages;
            el('hostelRoomMoveSave').disabled = !result.items?.length;
        } catch (error) { if (current === serial) notify(error.message); }
    }
    function begin(item) {
        const d = new Date(), today = [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'),
            String(d.getDate()).padStart(2, '0')].join('-');
        if (!item || item.startDate <= today) { notify('Only future reservations can change rooms.'); return; }
        pending = item; page = 1;
        el('hostelRoomMoveContext').textContent = item.studentName + ' · ' + item.hostelName +
            ' · Room ' + item.roomNumber + ' / Bed ' + item.bedNumber;
        el('hostelRoomMovePanel').classList.remove('d-none');
        load(); el('hostelRoomMovePanel').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
    async function save() {
        if (!pending || busy) return;
        const targetBedId = Number(el('hostelRoomMoveTarget').value);
        if (!Number.isSafeInteger(targetBedId) || targetBedId <= 0) { notify('Select a room and bed.'); return; }
        if (!confirm('Move this upcoming reservation to another room at the same tariff?')) return;
        busy = true; el('hostelRoomMoveSave').disabled = true;
        try {
            await api('/api/hostel/allocations/' + encodeURIComponent(pending.id) + '/reassign-room',
                'POST', { targetBedId, rowVersion: pending.rowVersion });
            dismiss(); notify('Upcoming reservation room updated.', true);
            window.dispatchEvent(new Event('hostel-room-change-completed'));
        } catch (error) { notify(error.message); }
        finally { busy = false; el('hostelRoomMoveSave').disabled = false; }
    }
    el('hostelRoomMovePrev').addEventListener('click', () => { page = Math.max(1, page - 1); load(); });
    el('hostelRoomMoveNext').addEventListener('click', () => { page = Math.min(pages, page + 1); load(); });
    el('hostelRoomMoveCancel').addEventListener('click', dismiss);
    el('hostelRoomMoveSave').addEventListener('click', save);
    window.hostelUpcomingRoomChange = { begin };
})();
