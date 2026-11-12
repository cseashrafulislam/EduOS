(() => {
    'use strict';
    const vm = window.eduosAcademicManagement;
    if (!vm) return;
    const { state, el, api, notice, reload, sameSession, guard, mayReplace } = vm;
    const pageSize = 20;
    let page = 1, total = 0, rows = [], loaded = false, generation = 0;
    let createSaving = false, editSaving = false, editSequence = 0;
    const createGuard = guard('roomCreateForm'), editGuard = guard('roomEditForm');
    const value = id => el(id).value.trim();
    function campusOptions() {
        const wasClean = !createGuard?.isDirty();
        for (const [id, label] of [['roomCampusFilter', 'All campuses'], ['roomCreateCampus', 'Choose campus']]) {
            const select = el(id), old = select.value;
            select.replaceChildren(new Option(label, ''));
            (state.campuses || []).forEach(x => select.add(new Option(x.name, String(x.id))));
            if ([...select.options].some(x => x.value === old)) select.value = old;
        }
        if (wasClean) createGuard?.markClean();
    }
    const node = (tag, value) => {
        const x = document.createElement(tag);
        x.textContent = String(value ?? '');
        return x;
    };
    function render() {
        const body = el('roomRows');
        body.replaceChildren();
        if (!rows.length) {
            const tr = node('tr', ''), td = node('td', 'No rooms found.');
            td.colSpan = 7; tr.append(td); body.append(tr);
        }
        rows.forEach(room => {
            const tr = document.createElement('tr');
            [room.name, room.code, room.campusName || room.campusId, room.roomType || '',
                room.capacity, room.isActive ? 'Active' : 'Inactive'].forEach(v => tr.append(node('td', v)));
            const action = document.createElement('td'), button = node('button', 'Edit');
            button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
            button.addEventListener('click', async () => {
                if (!sameSession() || editSaving || !mayReplace('roomEditForm')) return;
                const selection = ++editSequence;
                button.disabled = true;
                const form = el('roomEditForm');
                form.inert = true;
                try {
                    const row = await api('/api/academic-setup/rooms/' + encodeURIComponent(room.id));
                    if (!sameSession() || selection !== editSequence || editGuard?.isDirty()) return;
                    el('roomEditId').value = row.id;
                    el('roomEditVersion').value = row.rowVersion;
                    el('roomEditName').value = row.name;
                    el('roomEditCapacity').value = row.capacity;
                    el('roomEditCapacity').min = String(row.capacity);
                    el('roomEditType').value = row.roomType || '';
                    el('roomEditContext').textContent = row.code + ' · ' + (row.campusName || row.campusId);
                    el('roomEditForm').classList.remove('d-none');
                    editGuard?.markClean();
                } catch (error) { if (sameSession()) notice(error.message, true); }
                finally { button.disabled = false; if (selection === editSequence) form.inert = false; }
            });
            action.append(button); tr.append(action); body.append(tr);
        });
        el('roomPage').textContent = total + ' rooms · page ' + page + ' of ' +
            Math.max(1, Math.ceil(total / pageSize));
        el('roomPrev').disabled = page <= 1;
        el('roomNext').disabled = page * pageSize >= total;
        el('roomCsv').disabled = !rows.length;
    }
    async function load() {
        const current = ++generation;
        const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
        if (value('roomSearch')) params.set('search', value('roomSearch'));
        if (value('roomCampusFilter')) params.set('campusId', value('roomCampusFilter'));
        const result = await api('/api/academic-setup/rooms?' + params);
        if (current !== generation || !sameSession()) return;
        rows = result.items || [];
        total = result.totalCount || 0;
        loaded = true; render();
    }
    function csv(v) {
        let s = String(v ?? '');
        if (/^\s*[=+@-]/.test(s)) s = "'" + s;
        return '"' + s.replace(/"/g, '""') + '"';
    }
    el('roomCsv').addEventListener('click', () => {
        if (!rows.length) return;
        const lines = [['Room', 'Code', 'Campus', 'Type', 'Capacity', 'Active'].map(csv).join(',')];
        rows.forEach(x => lines.push([x.name, x.code, x.campusName, x.roomType,
            x.capacity, x.isActive].map(csv).join(',')));
        const url = URL.createObjectURL(new Blob(['\uFEFF' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a');
        a.href = url; a.download = 'academic-rooms-page-' + page + '.csv'; a.click();
        URL.revokeObjectURL(url);
    });
    el('roomSearchForm').addEventListener('submit', e => {
        e.preventDefault(); page = 1; load().catch(x => notice(x.message, true));
    });
    el('roomPrev').addEventListener('click', () => {
        if (page > 1) { page--; load().catch(x => notice(x.message, true)); }
    });
    el('roomNext').addEventListener('click', () => {
        if (page * pageSize < total) { page++; load().catch(x => notice(x.message, true)); }
    });
    el('roomCancel').addEventListener('click', () => {
        if (editSaving || !mayReplace('roomEditForm')) return;
        ++editSequence; // Invalidate a pending edit lookup before cancelling.
        el('roomEditForm').classList.add('d-none'); editGuard?.markClean();
    });
    el('roomCreateForm').addEventListener('submit', async e => {
        e.preventDefault();
        const form = e.currentTarget;
        if (createSaving || !sameSession() || !form.reportValidity()) return;
        const button = e.currentTarget.querySelector('button[type="submit"]');
        const capacity = Number(value('roomCreateCapacity'));
        if (!Number.isInteger(capacity) || capacity < 0 || capacity > 100000 ||
            !Number(value('roomCreateCampus'))) { notice('Choose a campus and valid capacity.', true); return; }
        createSaving = true; button.disabled = true; form.inert = true;
        let committed = false;
        try {
            await api('/api/academic-setup/rooms', { method: 'POST', body: JSON.stringify({
                campusId: Number(value('roomCreateCampus')), name: value('roomCreateName'),
                code: value('roomCreateCode'), capacity, roomType: value('roomCreateType'), isActive: true
            }) });
            committed = true;
            if (!sameSession()) return;
            form.reset(); createGuard?.markClean();
            try { page = 1; await Promise.all([load(), reload()]); notice('Room saved.'); }
            catch { notice('Room saved, but the directory did not refresh. Reload the page.', true); }
        } catch (error) { if (sameSession()) notice(committed ? 'Room saved, but the display failed.' : error.message, true); }
        finally { createSaving = false; button.disabled = false; form.inert = false; }
    });
    el('roomEditForm').addEventListener('submit', async e => {
        e.preventDefault();
        const form = e.currentTarget;
        if (editSaving || !sameSession() || !form.reportValidity()) return;
        const button = e.currentTarget.querySelector('button[type="submit"]');
        const capacity = Number(value('roomEditCapacity'));
        if (!Number.isInteger(capacity) || capacity < Number(el('roomEditCapacity').min) ||
            capacity > 100000) { notice('Room capacity cannot decrease.', true); return; }
        ++editSequence; // Do not allow a pending edit lookup to replace the saving form.
        editSaving = true; button.disabled = true; form.inert = true;
        let committed = false;
        try {
            await api('/api/academic-setup/rooms/' + encodeURIComponent(value('roomEditId')), {
                method: 'PUT', body: JSON.stringify({
                    name: value('roomEditName'), capacity, roomType: value('roomEditType'),
                    rowVersion: value('roomEditVersion')
                })
            });
            committed = true;
            if (!sameSession()) return;
            form.classList.add('d-none'); editGuard?.markClean();
            try { await Promise.all([load(), reload()]); notice('Room metadata saved.'); }
            catch { notice('Room saved, but the directory did not refresh. Reload the page.', true); }
        } catch (error) { if (sameSession()) notice(committed ? 'Room saved, but the display failed.' : error.message, true); }
        finally { editSaving = false; button.disabled = false; form.inert = false; }
    });
    window.addEventListener('eduos:academic-reloaded', () => {
        campusOptions();
        if (loaded || document.querySelector('button[data-panel="rooms"]')?.classList.contains('active'))
            load().catch(x => notice(x.message, true));
    });
    document.querySelector('button[data-panel="rooms"]')?.addEventListener('click', () => {
        if (!loaded) load().catch(x => notice(x.message, true));
    });
})();
