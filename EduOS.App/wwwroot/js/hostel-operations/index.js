(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('hostelTitle')) return;
    const manager = !!el('hostelAllocationForm');
    let requestId = crypto.randomUUID(), studentPage = 1, bedPage = 1, activePage = 1, activePages = 1;
    function today() { const d = new Date(); return [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'), String(d.getDate()).padStart(2, '0')].join('-'); }
    function notify(message, success = false) {
        const node = el('hostelAlert'); node.className = 'alert alert-' + (success ? 'success' : 'danger'); node.textContent = message; node.focus();
    }
    async function api(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' }; if (body !== undefined) headers['Content-Type'] = 'application/json';
        const response = await fetch(url, { method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Hostel request failed.');
        return result.data;
    }
    function td(value) { const cell = document.createElement('td'); cell.textContent = value === undefined || value === null || value === '' ? '—' : String(value); return cell; }
    function option(select, value, label) { const item = document.createElement('option'); item.value = value; item.textContent = label; select.append(item); }
    function rows(id, data, columns, emptyMessage) {
        const body = el(id); body.replaceChildren();
        if (!data?.length) {
            const tr = document.createElement('tr'), cell = td(emptyMessage); cell.colSpan = columns.length; tr.append(cell); body.append(tr); return;
        }
        for (const record of data) {
            const tr = document.createElement('tr');
            for (const column of columns) tr.append(td(typeof column === 'function' ? column(record) : record[column]));
            body.append(tr);
        }
    }
    async function loadMine() {
        try {
            const info = await api('/api/hostel/my-allocation');
            el('hostelMine').textContent = info ?
                [info.hostelName, 'Room ' + info.roomNumber, 'Bed ' + info.bedNumber, 'From ' + info.startDate].join(' · ') :
                'No active hostel allocation.';
        } catch (error) { notify(error.message); }
    }
    async function loadRooms() {
        try {
            const info = await api('/api/hostel/rooms');
            rows('hostelRoomRows', info, ['hostelName','roomNumber','capacity','activeAllocations','rentPerBed'], 'No active hostel rooms.');
        } catch (error) { notify(error.message); }
    }
    async function loadStudents() {
        try {
            const params = new URLSearchParams({ page: String(studentPage), pageSize: '25' });
            const term = el('hostelStudentSearch').value.trim(); if (term) params.set('search', term);
            const info = await api('/api/hostel/eligible-students?' + params);
            const select = el('hostelStudent'); select.replaceChildren(); option(select, '', 'Choose eligible student');
            (info.items || []).forEach(x => option(select, x.enrollmentReference, x.studentName + ' (' + x.studentCode + ', Roll ' + x.roll + ')'));
            el('hostelNextStudent').disabled = studentPage >= info.totalPages;
        } catch (error) { notify(error.message); }
    }
    async function loadBeds() {
        try {
            const params = new URLSearchParams({ page: String(bedPage), pageSize: '25' });
            const term = el('hostelBedSearch').value.trim(); if (term) params.set('search', term);
            const info = await api('/api/hostel/available-beds?' + params);
            const select = el('hostelBed'); select.replaceChildren(); option(select, '', 'Choose unoccupied bed');
            (info.items || []).forEach(x => option(select, String(x.bedId),
                x.hostelName + ' · Room ' + x.roomNumber + ' / Bed ' + x.bedNumber +
                ' · Rent ' + x.rentPerBed + (x.genderRestriction ? ' · ' + x.genderRestriction : '')));
            el('hostelNextBed').disabled = bedPage >= info.totalPages;
        } catch (error) { notify(error.message); }
    }
    async function allocate(event) {
        event.preventDefault(); const form = el('hostelAllocationForm');
        if (!form.reportValidity()) return;
        const button = el('hostelAllocate'); button.disabled = true;
        try {
            const amount = el('hostelRent').value.trim();
            const body = { clientRequestId: requestId, studentEnrollmentReference: el('hostelStudent').value,
                hostelBedId: Number(el('hostelBed').value), startDate: el('hostelStart').value,
                monthlyRent: amount ? Number(amount) : null };
            if (amount && (!Number.isFinite(body.monthlyRent) || body.monthlyRent < 0)) throw new Error('Monthly rent must be nonnegative.');
            await api('/api/hostel/allocations', 'POST', body);
            requestId = crypto.randomUUID(); form.reset(); el('hostelStart').value = today();
            notify('Hostel bed allocated.', true);
            await Promise.all([loadStudents(), loadBeds(), loadRooms(), loadActive()]);
        } catch (error) { notify(error.message); } finally { button.disabled = false; }
    }
    async function loadActive() {
        try {
            const params = new URLSearchParams({ page: String(activePage), pageSize: '25' });
            const term = el('hostelAllocationTerm').value.trim(); if (term) params.set('search', term);
            const info = await api('/api/hostel/active-allocations?' + params);
            const body = el('hostelActiveRows'); body.replaceChildren();
            for (const item of info.items || []) {
                const tr = document.createElement('tr');
                [item.studentName, item.hostelName, item.roomNumber + ' / ' + item.bedNumber, item.startDate].forEach(v => tr.append(td(v)));
                const actions = document.createElement('td'), close = document.createElement('button');
                close.type = 'button'; close.className = 'btn btn-outline-danger btn-sm'; close.textContent = 'Close';
                close.addEventListener('click', () => closeAllocation(item, close)); actions.append(close); tr.append(actions); body.append(tr);
            }
            if (!info.items?.length) { const tr = document.createElement('tr'), cell = td('No active allocations.'); cell.colSpan = 5; tr.append(cell); body.append(tr); }
            activePages = Math.max(1, Number(info.totalPages) || 1);
            el('hostelPageInfo').textContent = 'Page ' + activePage + ' of ' + activePages + ' · ' + info.totalCount + ' total';
            el('hostelPrev').disabled = activePage <= 1; el('hostelNext').disabled = activePage >= activePages;
        } catch (error) { notify(error.message); }
    }
    async function closeAllocation(item, button) {
        if (!confirm('Close the hostel allocation for ' + item.studentName + '?')) return;
        const endDate = prompt('End date (YYYY-MM-DD)', today());
        if (!endDate) return;
        if (!/^\d{4}-\d{2}-\d{2}$/.test(endDate) || endDate < item.startDate) {
            notify('Enter a valid end date on or after the start date.'); return;
        }
        button.disabled = true;
        try {
            await api('/api/hostel/allocations/' + encodeURIComponent(item.id) + '/close', 'POST',
                { endDate, rowVersion: item.rowVersion });
            notify('Hostel allocation closed.', true);
            await Promise.all([loadActive(), loadStudents(), loadBeds(), loadRooms()]);
        } catch (error) { notify(error.message); } finally { button.disabled = false; }
    }
    loadMine(); if (manager) loadRooms();
    if (manager) {
        el('hostelStart').value = today();
        el('hostelFindStudents').addEventListener('click', () => { studentPage = 1; loadStudents(); });
        el('hostelNextStudent').addEventListener('click', () => { studentPage++; loadStudents(); });
        el('hostelFindBeds').addEventListener('click', () => { bedPage = 1; loadBeds(); });
        el('hostelNextBed').addEventListener('click', () => { bedPage++; loadBeds(); });
        el('hostelAllocationForm').addEventListener('submit', allocate);
        el('hostelAllocationSearch').addEventListener('submit', e => { e.preventDefault(); activePage = 1; loadActive(); });
        el('hostelPrev').addEventListener('click', () => { activePage = Math.max(1, activePage - 1); loadActive(); });
        el('hostelNext').addEventListener('click', () => { activePage = Math.min(activePages, activePage + 1); loadActive(); });
        loadStudents(); loadBeds(); loadActive();
    }
})();
