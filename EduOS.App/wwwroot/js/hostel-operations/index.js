(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('hostelTitle')) return;
    const manager = !!el('hostelAllocationForm');
    let requestId = crypto.randomUUID(), studentPage = 1, bedPage = 1, activePage = 1, activePages = 1;
    let studentLookup = 0, bedLookup = 0, activeLookup = 0;
    let pendingBedMove = null, movePage = 1, movePages = 1, moveLookup = 0;
    function today() { const d = new Date(); return [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'), String(d.getDate()).padStart(2, '0')].join('-'); }
    function notify(message, success = false) {
        const node = el('hostelAlert'); node.className = 'alert alert-' + (success ? 'success' : 'danger'); node.textContent = message; node.focus();
    }
    async function api(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload the page.');
            headers['Content-Type'] = 'application/json';
            headers.RequestVerificationToken = token;
        }
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
    async function loadStudents() {
        const lookup = ++studentLookup;
        try {
            const params = new URLSearchParams({ page: String(studentPage), pageSize: '25', startDate: el('hostelStart').value || today() });
            if (el('hostelEnd').value) params.set('endDate', el('hostelEnd').value);
            const term = el('hostelStudentSearch').value.trim(); if (term) params.set('search', term);
            const info = await api('/api/hostel/eligible-students?' + params);
            if (lookup !== studentLookup) return;
            const select = el('hostelStudent'); select.replaceChildren(); option(select, '', 'Choose eligible student');
            (info.items || []).forEach(x => option(select, x.enrollmentReference, x.studentName + ' (' + x.studentCode + ', Roll ' + x.roll + ')'));
            el('hostelNextStudent').disabled = studentPage >= info.totalPages;
        } catch (error) { if (lookup === studentLookup) notify(error.message); }
    }
    async function loadBeds() {
        const lookup = ++bedLookup;
        try {
            const params = new URLSearchParams({ page: String(bedPage), pageSize: '25' });
            const term = el('hostelBedSearch').value.trim(); if (term) params.set('search', term);
            const fixed = !!el('hostelEnd').value;
            if (fixed) { params.set('fromDate', el('hostelStart').value); params.set('toDate', el('hostelEnd').value); }
            else params.set('startDate', el('hostelStart').value || today());
            const info = await api((fixed ? '/api/hostel/bed-availability?' : '/api/hostel/available-beds?') + params);
            if (lookup !== bedLookup) return;
            const select = el('hostelBed'); select.replaceChildren(); option(select, '', 'Choose unoccupied bed');
            (info.items || []).forEach(x => option(select, String(x.bedId),
                x.hostelName + ' · Room ' + x.roomNumber + ' / Bed ' + x.bedNumber +
                (fixed ? ' · Available slots ' + x.remainingRoomSlots :
                    ' · Rent ' + x.rentPerBed + (x.genderRestriction ? ' · ' + x.genderRestriction : ''))));
            el('hostelNextBed').disabled = bedPage >= info.totalPages;
        } catch (error) { if (lookup === bedLookup) notify(error.message); }
    }
    async function allocate(event) {
        event.preventDefault(); const form = el('hostelAllocationForm');
        if (!form.reportValidity()) return;
        const button = el('hostelAllocate'); button.disabled = true;
        try {
            const amount = el('hostelRent').value.trim();
            const body = { clientRequestId: requestId, studentEnrollmentReference: el('hostelStudent').value,
                hostelBedId: Number(el('hostelBed').value), startDate: el('hostelStart').value,
                monthlyRent: amount ? Number(amount) : null, endDate: el('hostelEnd').value || null };
            if (amount && (!Number.isFinite(body.monthlyRent) || body.monthlyRent < 0)) throw new Error('Monthly rent must be nonnegative.');
            if (body.endDate && (body.endDate < body.startDate ||
                (Date.parse(body.endDate + 'T00:00:00Z') - Date.parse(body.startDate + 'T00:00:00Z')) / 86400000 > 89))
                throw new Error('Checkout must be on or after arrival and within 90 inclusive days.');
            await api('/api/hostel/allocations', 'POST', body);
            requestId = crypto.randomUUID(); form.reset(); el('hostelStart').value = today();
            notify('Hostel bed allocated.', true);
            await Promise.all([loadStudents(), loadBeds(), loadActive()]);
        } catch (error) { notify(error.message); } finally { button.disabled = false; }
    }
    async function loadActive() {
        const lookup = ++activeLookup;
        try {
            const params = new URLSearchParams({ page: String(activePage), pageSize: '25' });
            const term = el('hostelAllocationTerm').value.trim(); if (term) params.set('search', term);
            const info = await api('/api/hostel/active-allocations?' + params);
            if (lookup !== activeLookup) return;
            const body = el('hostelActiveRows'); body.replaceChildren();
            for (const item of info.items || []) {
                const tr = document.createElement('tr');
                [item.studentName, item.hostelName, item.roomNumber + ' / ' + item.bedNumber, item.startDate, item.endDate].forEach(v => tr.append(td(v)));
                const actions = document.createElement('td'), future = item.startDate > today();
                const button = document.createElement('button'); button.type = 'button';
                button.className = future ? 'btn btn-outline-warning btn-sm' : 'btn btn-outline-danger btn-sm';
                button.textContent = future ? 'Cancel reservation' : 'Close';
                button.addEventListener('click', () => future ? cancelUpcoming(item, button) : closeAllocation(item, button));
                actions.append(button);
                if (future) {
                    const move = document.createElement('button'); move.type = 'button';
                    move.className = 'btn btn-outline-primary btn-sm ms-2';
                    move.textContent = 'Change bed';
                    move.addEventListener('click', () => beginBedMove(item));
                    actions.append(move);
                    const roomMove = document.createElement('button'); roomMove.type = 'button';
                    roomMove.className = 'btn btn-outline-primary btn-sm ms-2';
                    roomMove.textContent = 'Change room';
                    roomMove.addEventListener('click', () => window.hostelUpcomingRoomChange?.begin(item));
                    actions.append(roomMove);
                    const reschedule = document.createElement('button'); reschedule.type = 'button';
                    reschedule.className = 'btn btn-outline-primary btn-sm ms-2';
                    reschedule.textContent = 'Reschedule start';
                    reschedule.addEventListener('click', () => window.hostelUpcomingStartReschedule?.begin(item));
                    actions.append(reschedule);
                }
                if (future && item.endDate && item.endDate > item.startDate) {
                    const shorten = document.createElement('button');
                    shorten.type = 'button';
                    shorten.className = 'btn btn-outline-info btn-sm ms-2';
                    shorten.textContent = 'Shorten checkout';
                    shorten.addEventListener('click', () => window.hostelUpcomingNoChargeShortening?.begin(item));
                    actions.append(shorten);
                }
                if (item.endDate && item.endDate >= today()) {
                    const extend = document.createElement('button');
                    extend.type = 'button';
                    extend.className = 'btn btn-outline-success btn-sm ms-2';
                    extend.textContent = 'Extend stay';
                    extend.addEventListener('click', () => window.hostelNoChargeStayExtension?.begin(item));
                    actions.append(extend);
                }
                if (!future && item.startDate < today()) {
                    const transfer = document.createElement('button');
                    transfer.type = 'button'; transfer.className = 'btn btn-outline-primary btn-sm ms-2';
                    transfer.textContent = 'Transfer resident';
                    transfer.addEventListener('click', () => window.hostelOccupiedTransfer?.begin(item));
                    actions.append(transfer);
                }
                tr.append(actions); body.append(tr);
            }
            if (!info.items?.length) { const tr = document.createElement('tr'), cell = td('No active allocations.'); cell.colSpan = 6; tr.append(cell); body.append(tr); }
            activePages = Math.max(1, Number(info.totalPages) || 1);
            el('hostelPageInfo').textContent = 'Page ' + activePage + ' of ' + activePages + ' · ' + info.totalCount + ' total';
            el('hostelPrev').disabled = activePage <= 1; el('hostelNext').disabled = activePage >= activePages;
        } catch (error) { if (lookup === activeLookup) notify(error.message); }
    }
    async function cancelUpcoming(item, button) {
        if (item.startDate <= today()) { notify('This allocation has started. Use Close instead.'); return; }
        if (!confirm('Cancel the upcoming hostel reservation for ' + item.studentName + '?')) return;
        button.disabled = true;
        try {
            await api('/api/hostel/allocations/' + encodeURIComponent(item.id) + '/cancel', 'POST',
                { rowVersion: item.rowVersion });
            notify('Upcoming reservation cancelled.', true);
            await Promise.all([loadActive(), loadStudents(), loadBeds()]);
        } catch (error) { notify(error.message); } finally { button.disabled = false; }
    }
    function dismissBedMove() {
        pendingBedMove = null; ++moveLookup;
        el('hostelBedMovePanel').classList.add('d-none');
        el('hostelBedMoveTarget').replaceChildren();
    }
    async function beginBedMove(item) {
        if (item.startDate <= today()) { notify('Only future reservations can change beds.'); return; }
        pendingBedMove = item; movePage = 1;
        el('hostelBedMoveContext').textContent = item.studentName + ' · ' + item.hostelName +
            ' · Room ' + item.roomNumber + ' / Bed ' + item.bedNumber;
        el('hostelBedMovePanel').classList.remove('d-none');
        await loadBedMoveOptions();
        el('hostelBedMovePanel').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
    async function loadBedMoveOptions() {
        if (!pendingBedMove) return;
        const lookup = ++moveLookup, id = pendingBedMove.id;
        el('hostelBedMoveSave').disabled = true;
        const select = el('hostelBedMoveTarget'); select.replaceChildren();
        option(select, '', 'Loading available beds…');
        try {
            const params = new URLSearchParams({ page: String(movePage), pageSize: '25' });
            const info = await api('/api/hostel/allocations/' + encodeURIComponent(id) + '/bed-options?' + params);
            if (lookup !== moveLookup || pendingBedMove?.id !== id) return;
            select.replaceChildren(); option(select, '', 'Choose an available bed');
            (info.items || []).forEach(x => option(select, String(x.bedId),
                'Bed ' + x.bedNumber + ' · Room ' + x.roomNumber));
            movePages = Math.max(1, Number(info.totalPages) || 1);
            el('hostelBedMovePageInfo').textContent = 'Page ' + movePage + ' of ' + movePages +
                ' · ' + info.totalCount + ' available beds';
            el('hostelBedMovePrev').disabled = movePage <= 1;
            el('hostelBedMoveNext').disabled = movePage >= movePages;
            el('hostelBedMoveSave').disabled = !info.items?.length;
        } catch (error) { if (lookup === moveLookup) notify(error.message); }
    }
    async function submitBedMove() {
        if (!pendingBedMove) return;
        const targetBedId = Number(el('hostelBedMoveTarget').value);
        if (!Number.isSafeInteger(targetBedId) || targetBedId <= 0) {
            notify('Select an available bed.'); return;
        }
        if (!confirm('Change this upcoming reservation to the selected bed? Financial terms remain unchanged.')) return;
        const button = el('hostelBedMoveSave'); button.disabled = true;
        try {
            await api('/api/hostel/allocations/' + encodeURIComponent(pendingBedMove.id) + '/reassign-bed', 'POST',
                { targetBedId, rowVersion: pendingBedMove.rowVersion });
            dismissBedMove();
            notify('Upcoming reservation bed updated.', true);
            await Promise.all([loadActive(), loadBeds()]);
        } catch (error) { notify(error.message); } finally { button.disabled = false; }
    }
    async function closeAllocation(item, button) {
        if (!confirm('Close the hostel allocation for ' + item.studentName + '?')) return;
        const endDate = prompt('End date (YYYY-MM-DD)', today());
        if (!endDate) return;
        if (!/^\d{4}-\d{2}-\d{2}$/.test(endDate) || endDate < item.startDate || endDate > today()) {
            notify('Enter an end date from the start date through today.'); return;
        }
        const clearanceInput = prompt('Approved Finance clearance ID (leave blank ONLY for no-charge stays without billing):', '');
        if (clearanceInput === null) return;
        const clearanceId = clearanceInput.trim() ? Number(clearanceInput.trim()) : null;
        if (clearanceId !== null && (!Number.isSafeInteger(clearanceId) || clearanceId <= 0)) {
            notify('Enter a valid positive Finance clearance ID.'); return;
        }
        button.disabled = true;
        try {
            await api('/api/hostel/allocations/' + encodeURIComponent(item.id) + '/close', 'POST',
                { endDate, rowVersion: item.rowVersion, financeClearanceId: clearanceId });
            notify('Hostel allocation closed.', true);
            await Promise.all([loadActive(), loadStudents(), loadBeds()]);
        } catch (error) { notify(error.message); } finally { button.disabled = false; }
    }
    if (el('hostelMine')) loadMine();
        if (manager) {
        el('hostelStart').value = today();
        for (const field of ['hostelStart', 'hostelEnd']) {
            el(field).addEventListener('change', () => {
                studentPage = 1; bedPage = 1;
                loadStudents(); loadBeds();
            });
        }
        el('hostelFindStudents').addEventListener('click', () => { studentPage = 1; loadStudents(); });
        el('hostelNextStudent').addEventListener('click', () => { studentPage++; loadStudents(); });
        el('hostelFindBeds').addEventListener('click', () => { bedPage = 1; loadBeds(); });
        el('hostelNextBed').addEventListener('click', () => { bedPage++; loadBeds(); });
        el('hostelBedMovePrev').addEventListener('click', () => { movePage = Math.max(1, movePage - 1); loadBedMoveOptions(); });
        el('hostelBedMoveNext').addEventListener('click', () => { movePage = Math.min(movePages, movePage + 1); loadBedMoveOptions(); });
        el('hostelBedMoveCancel').addEventListener('click', dismissBedMove);
        el('hostelBedMoveSave').addEventListener('click', submitBedMove);
        el('hostelAllocationForm').addEventListener('submit', allocate);
        el('hostelAllocationSearch').addEventListener('submit', e => { e.preventDefault(); activePage = 1; loadActive(); });
        el('hostelPrev').addEventListener('click', () => { activePage = Math.max(1, activePage - 1); loadActive(); });
        el('hostelNext').addEventListener('click', () => { activePage = Math.min(activePages, activePage + 1); loadActive(); });
        window.addEventListener('hostel-stay-shortened', () => {
            dismissBedMove(); loadActive(); loadBeds();
        });
        window.addEventListener('hostel-stay-extended', () => {
            dismissBedMove(); loadActive(); loadBeds();
        });
        window.addEventListener('hostel-start-rescheduled', () => {
            dismissBedMove(); loadActive(); loadBeds();
        });
        window.addEventListener('hostel-room-change-completed', () => {
            dismissBedMove(); loadActive(); loadBeds();
        });
        window.addEventListener('hostel-occupied-transfer-completed', () => {
            dismissBedMove(); loadActive(); loadBeds(); loadMine();
        });
        loadStudents(); loadBeds(); loadActive();
    }
})();
