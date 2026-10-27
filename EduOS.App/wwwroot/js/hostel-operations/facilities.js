(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('hostelFacilityPanel')) return;
    const rows = el('hostelFacilityRows'), status = el('hostelFacilityStatus');
    const prev = el('hostelFacilityPrev'), next = el('hostelFacilityNext');
    const search = el('hostelFacilitySearch'), campus = el('hostelFacilityCampus');
    let page = 1, totalPages = 1, generation = 0, busy = false, creating = false, saving = false, loadedFilter = null;
    const exportButton = el('hostelFacilityCsv'), filterCampus = el('hostelFacilityFilterCampus');
    const snapshot = () => { const q = new URLSearchParams(); if (filterCampus.value) q.set('campusId', filterCampus.value); if (search.value.trim()) q.set('search', search.value.trim()); return q.toString(); };
    let editing = null;
    const editForm = el('hostelFacilityEdit'), editSave = el('hostelFacilityEditSave');
    function clearEditor() { editing = null; editForm.classList.add('d-none'); editForm.reset(); }
    function showEditor(item) {
        editing = { reference: item.reference, rowVersion: item.rowVersion };
        el('hostelFacilityEditName').value = item.name || '';
        el('hostelFacilityEditGender').value = item.genderRestriction || '';
        el('hostelFacilityEditAddress').value = item.address || '';
        el('hostelFacilityEditIdentity').textContent = 'Campus: ' + item.campusName + ' | Code: ' + item.code;
        editForm.classList.remove('d-none');
        el('hostelFacilityEditName').focus();
    }
    const td = value => { const cell = document.createElement('td'); cell.textContent = String(value ?? ''); return cell; };
    const notify = (message, error = false) => {
        status.textContent = message;
        status.className = error ? 'small text-danger' : 'small text-muted';
    };
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
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Hostel facility request failed.');
        return result.data;
    }
    function controls() {
        prev.disabled = busy || page <= 1;
        next.disabled = busy || page >= totalPages;
        exportButton.disabled = busy || loadedFilter === null || loadedFilter !== snapshot();
        el('hostelFacilityPage').textContent = 'Page ' + page + ' of ' + totalPages;
    }
    async function loadCampuses() {
        try {
            const campuses = await request('/api/hostel/facility-campuses');
            campus.replaceChildren(new Option('Choose campus', ''));
            filterCampus.replaceChildren(new Option('All authorized campuses', ''));
            for (const item of campuses || []) { campus.add(new Option(item.name, String(item.id))); filterCampus.add(new Option(item.name, String(item.id))); }
        } catch (error) { notify(error.message, true); }
    }
    async function load() {
        const current = ++generation;
        busy = true; loadedFilter = null; rows.replaceChildren(); controls(); notify('Loading facilities…');
        try {
            const params = new URLSearchParams({ page: String(page), pageSize: '25' });
            if (filterCampus.value) params.set('campusId', filterCampus.value);
            if (search.value.trim()) params.set('search', search.value.trim());
            const data = await request('/api/hostel/facilities?' + params);
            if (current !== generation) return;
            totalPages = Math.max(1, Number(data.totalPages) || 1);
            for (const item of data.items || []) {
                const tr = document.createElement('tr');
                [item.campusName, item.name, item.code, item.activeRooms, item.occupiedBeds,
                    item.activeReservations, item.isActive ? 'Active' : 'Inactive'].forEach(value => tr.append(td(value)));
                const action = document.createElement('td'), button = document.createElement('button');
                const editButton = document.createElement('button');
                editButton.type = 'button'; editButton.className = 'btn btn-outline-primary btn-sm me-2';
                editButton.textContent = 'Edit details';
                editButton.addEventListener('click', () => showEditor(item));
                button.type = 'button'; button.className = 'btn btn-outline-secondary btn-sm';
                button.textContent = item.isActive ? 'Deactivate' : 'Activate';
                button.disabled = item.isActive && (item.activeRooms > 0 || item.activeReservations > 0);
                button.addEventListener('click', async () => {
                    if (!confirm((item.isActive ? 'Deactivate' : 'Activate') + ' facility ' + item.name + '?')) return;
                    button.disabled = true;
                    try {
                        await request('/api/hostel/facilities/' + encodeURIComponent(item.reference) + '/state', 'PUT',
                            { isActive: !item.isActive, rowVersion: item.rowVersion });
                        await load();
                    } catch (error) { notify(error.message, true); button.disabled = false; }
                });
                action.append(editButton, button); tr.append(action); rows.append(tr);
            }
            if (!data.items?.length) {
                const tr = document.createElement('tr'), cell = td('No facilities match the filter.');
                cell.colSpan = 8; tr.append(cell); rows.append(tr);
            }
            loadedFilter = snapshot();
            notify(String(data.totalCount || 0) + ' matching facilities. Export limited to 1,000 records.');
        } catch (error) { if (current === generation) notify(error.message, true); }
        finally { if (current === generation) { busy = false; controls(); } }
    }
    el('hostelFacilityCreate').addEventListener('submit', async event => {
        event.preventDefault();
        if (creating) return;
        const campusId = Number(campus.value);
        const name = el('hostelFacilityName').value.trim();
        const code = el('hostelFacilityCode').value.trim();
        if (!Number.isSafeInteger(campusId) || campusId <= 0 || name.length < 2 || code.length < 2) {
            notify('Choose a campus and enter a valid facility name and code.', true); return;
        }
        creating = true; el('hostelFacilityCreateButton').disabled = true;
        try {
            await request('/api/hostel/facilities', 'POST', {
                campusId, name, code,
                genderRestriction: el('hostelFacilityGender').value || null,
                address: el('hostelFacilityAddress').value.trim() || null
            });
            el('hostelFacilityName').value = '';
            el('hostelFacilityCode').value = '';
            el('hostelFacilityAddress').value = '';
            page = 1; await load();
        } catch (error) { notify(error.message, true); }
        finally { creating = false; el('hostelFacilityCreateButton').disabled = false; }
    });
    editForm.addEventListener('submit', async event => {
        event.preventDefault();
        if (saving || !editing) return;
        const name = el('hostelFacilityEditName').value.trim();
        const address = el('hostelFacilityEditAddress').value.trim();
        if (name.length < 2 || name.length > 150 || address.length > 500) {
            notify('Enter a valid facility name and address.', true); return;
        }
        saving = true; editSave.disabled = true;
        try {
            await request('/api/hostel/facilities/' + encodeURIComponent(editing.reference), 'PUT', {
                name, genderRestriction: el('hostelFacilityEditGender').value || null,
                address: address || null, rowVersion: editing.rowVersion
            });
            clearEditor(); await load();
        } catch (error) { notify(error.message, true); }
        finally { saving = false; editSave.disabled = false; }
    });
    el('hostelFacilityEditCancel').addEventListener('click', clearEditor);
    el('hostelFacilityFilter').addEventListener('submit', event => { event.preventDefault(); page = 1; load(); });
    search.addEventListener('input', () => {
        generation++; loadedFilter = null; rows.replaceChildren(); page = 1; totalPages = 1;
        notify('Filter changed. Select Search to load updated results.'); controls();
    });
    filterCampus.addEventListener('change', () => { generation++; loadedFilter = null; page = 1; totalPages = 1; rows.replaceChildren(); notify('Filter changed. Select Search to reload.'); controls(); });
    prev.addEventListener('click', () => { if (!busy && page > 1) { page--; load(); } });
    next.addEventListener('click', () => { if (!busy && page < totalPages) { page++; load(); } });
    exportButton.addEventListener('click', async () => {
        const selected = snapshot(), start = generation;
        if (loadedFilter === null || loadedFilter !== selected) return;
        exportButton.disabled = true;
        try {
            const response = await fetch('/api/hostel/facilities/export?' + selected,
                { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/csv' } });
            if (!response.ok) {
                const result = await response.json().catch(() => null);
                throw Error(result?.message || 'Facility export failed.');
            }
            const blob = await response.blob();
            if (start !== generation || loadedFilter !== selected || snapshot() !== selected)
                throw Error('Filters changed during export. Search again.');
            const url = URL.createObjectURL(blob), link = document.createElement('a');
            link.href = url; link.download = 'hostel-facilities.csv'; document.body.append(link);
            link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch (error) { notify(error.message, true); }
        finally { exportButton.disabled = loadedFilter === null || loadedFilter !== snapshot(); }
    });
    loadCampuses(); load();
})();
