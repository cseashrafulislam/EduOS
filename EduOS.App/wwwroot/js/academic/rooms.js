(() => {
    'use strict';
    const vm = window.eduosAcademicManagement;
    if (!vm) return;
    const { state, el, api, notice, reload } = vm;
    const pageSize = 20;
    let page = 1, total = 0, rows = [], loaded = false, generation = 0;
    const value = id => el(id).value.trim();
    function campusOptions() {
        for (const [id, label] of [['roomCampusFilter', 'All campuses'], ['roomCreateCampus', 'Choose campus']]) {
            const select = el(id), old = select.value;
            select.replaceChildren(new Option(label, ''));
            (state.campuses || []).forEach(x => select.add(new Option(x.name, String(x.id))));
            if ([...select.options].some(x => x.value === old)) select.value = old;
        }
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
                button.disabled = true;
                try {
                    const row = await api('/api/academic-setup/rooms/' + encodeURIComponent(room.id));
                    el('roomEditId').value = row.id;
                    el('roomEditVersion').value = row.rowVersion;
                    el('roomEditName').value = row.name;
                    el('roomEditCapacity').value = row.capacity;
                    el('roomEditCapacity').min = String(row.capacity);
                    el('roomEditType').value = row.roomType || '';
                    el('roomEditContext').textContent = row.code + ' · ' + (row.campusName || row.campusId);
                    el('roomEditForm').classList.remove('d-none');
                } catch (error) { notice(error.message, true); }
                finally { button.disabled = false; }
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
        if (current !== generation) return;
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
    el('roomCancel').addEventListener('click', () => el('roomEditForm').classList.add('d-none'));
    el('roomCreateForm').addEventListener('submit', async e => {
        e.preventDefault();
        const button = e.currentTarget.querySelector('button[type="submit"]');
        const capacity = Number(value('roomCreateCapacity'));
        if (!Number.isInteger(capacity) || capacity < 0 || capacity > 100000 ||
            !Number(value('roomCreateCampus'))) { notice('Choose a campus and valid capacity.', true); return; }
        button.disabled = true;
        try {
            await api('/api/academic-setup/rooms', { method: 'POST', body: JSON.stringify({
                campusId: Number(value('roomCreateCampus')), name: value('roomCreateName'),
                code: value('roomCreateCode'), capacity, roomType: value('roomCreateType'), isActive: true
            }) });
            el('roomCreateForm').reset();
            page = 1; await Promise.all([load(), reload()]);
            notice('Room saved.');
        } catch (error) { notice(error.message, true); }
        finally { button.disabled = false; }
    });
    el('roomEditForm').addEventListener('submit', async e => {
        e.preventDefault();
        const button = e.currentTarget.querySelector('button[type="submit"]');
        const capacity = Number(value('roomEditCapacity'));
        if (!Number.isInteger(capacity) || capacity < Number(el('roomEditCapacity').min) ||
            capacity > 100000) { notice('Room capacity cannot decrease.', true); return; }
        button.disabled = true;
        try {
            await api('/api/academic-setup/rooms/' + encodeURIComponent(value('roomEditId')), {
                method: 'PUT', body: JSON.stringify({
                    name: value('roomEditName'), capacity, roomType: value('roomEditType'),
                    rowVersion: value('roomEditVersion')
                })
            });
            el('roomEditForm').classList.add('d-none');
            await Promise.all([load(), reload()]);
            notice('Room metadata saved.');
        } catch (error) { notice(error.message, true); }
        finally { button.disabled = false; }
    });
    window.addEventListener('eduos:academic-reloaded', () => {
        campusOptions();
        if (loaded || document.querySelector('button[data-panel="rooms"].active'))
            load().catch(x => notice(x.message, true));
    });
    document.querySelector('button[data-panel="rooms"]').addEventListener('click', () => {
        if (!loaded) load().catch(x => notice(x.message, true));
    });
})();
