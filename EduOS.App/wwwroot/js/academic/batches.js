(() => {
    'use strict';
    const vm = window.eduosAcademicManagement;
    if (!vm) return;
    const { state, el, api, notice, reload } = vm;
    let page = 1, total = 0, rows = [], loaded = false, generation = 0;
    const pageSize = 20;
    const text = (tag, value) => {
        const n = document.createElement(tag);
        n.textContent = String(value ?? '');
        return n;
    };
    const value = id => el(id).value.trim();
    function filters() {
        const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
        if (value('batchSearch')) query.set('search', value('batchSearch'));
        if (value('batchCampusFilter')) query.set('campusId', value('batchCampusFilter'));
        if (value('batchYearFilter')) query.set('academicYearId', value('batchYearFilter'));
        return query.toString();
    }
    function refreshFilters() {
        for (const [id, values, label] of [
            ['batchCampusFilter', state.campuses, 'All campuses'],
            ['batchYearFilter', state.years, 'All years']
        ]) {
            const select = el(id), old = select.value;
            select.replaceChildren(new Option(label, ''));
            (values || []).forEach(x => select.add(new Option(x.name, String(x.id))));
            if ([...select.options].some(x => x.value === old)) select.value = old;
        }
    }
    async function openEditor(id) {
        const row = await api('/api/academic-setup/batches/' + encodeURIComponent(id));
        el('batchEditId').value = row.id;
        el('batchEditVersion').value = row.rowVersion;
        el('batchEditName').value = row.name;
        el('batchEditCapacity').value = row.capacity;
        el('batchEditCapacity').min = String(row.capacity);
        el('batchEditOrder').value = row.displayOrder;
        el('batchEditContext').textContent = [row.code, row.campusName, row.academicYearName,
            row.academicProgramName, row.academicLevelName].filter(Boolean).join(' · ');
        el('batchEditForm').classList.remove('d-none');
        el('batchEditForm').scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }
    function render() {
        const body = el('batchRegisterRows');
        body.replaceChildren();
        if (!rows.length) {
            const tr = text('tr', ''), td = text('td', 'No sections found.');
            td.colSpan = 7; tr.append(td); body.append(tr);
        }
        rows.forEach(row => {
            const tr = document.createElement('tr');
            for (const v of [
                row.name + ' (' + row.code + ')', row.campusName || row.campusId,
                row.academicYearName || row.academicYearId,
                [row.academicProgramName, row.academicLevelName].filter(Boolean).join(' / '),
                row.capacity, row.isActive ? 'Active' : 'Inactive'
            ]) tr.append(text('td', v));
            const action = document.createElement('td'), edit = text('button', 'Edit');
            edit.type = 'button'; edit.className = 'btn btn-sm btn-outline-primary';
            edit.addEventListener('click', async () => {
                edit.disabled = true;
                try { await openEditor(row.id); }
                catch (error) { notice(error.message, true); }
                finally { edit.disabled = false; }
            });
            action.append(edit); tr.append(action); body.append(tr);
        });
        el('batchPageStatus').textContent = total + ' section(s) · page ' + page +
            ' of ' + Math.max(1, Math.ceil(total / pageSize));
        el('batchPrevious').disabled = page <= 1;
        el('batchNext').disabled = page * pageSize >= total;
        el('batchExport').disabled = !rows.length;
    }
    async function load() {
        const current = ++generation;
        const data = await api('/api/academic-setup/batches?' + filters());
        if (current !== generation) return;
        rows = data.items || []; total = data.totalCount || 0;
        loaded = true; render();
    }
    function csvCell(v) {
        let s = String(v ?? '');
        if (/^[\s]*[=+@-]/.test(s)) s = "'" + s;
        return '"' + s.replace(/"/g, '""') + '"';
    }
    el('batchExport').addEventListener('click', () => {
        if (!rows.length) return;
        const lines = [['Section', 'Code', 'Campus', 'Academic year', 'Programme',
            'Class', 'Capacity', 'Active'].map(csvCell).join(',')];
        rows.forEach(x => lines.push([x.name, x.code, x.campusName, x.academicYearName,
            x.academicProgramName, x.academicLevelName, x.capacity, x.isActive].map(csvCell).join(',')));
        const url = URL.createObjectURL(new Blob(['\uFEFF' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a'); a.href = url; a.download = 'academic-sections-page-' + page + '.csv';
        a.click(); URL.revokeObjectURL(url);
    });
    el('batchSearchForm').addEventListener('submit', event => {
        event.preventDefault(); page = 1; load().catch(e => notice(e.message, true));
    });
    el('batchPrevious').addEventListener('click', () => {
        if (page > 1) { page--; load().catch(e => notice(e.message, true)); }
    });
    el('batchNext').addEventListener('click', () => {
        if (page * pageSize < total) { page++; load().catch(e => notice(e.message, true)); }
    });
    el('batchEditCancel').addEventListener('click', () => el('batchEditForm').classList.add('d-none'));
    el('batchEditForm').addEventListener('submit', async event => {
        event.preventDefault();
        const button = event.currentTarget.querySelector('button[type="submit"]');
        const capacity = Number(value('batchEditCapacity')), displayOrder = Number(value('batchEditOrder'));
        if (!Number.isInteger(capacity) || !Number.isInteger(displayOrder) ||
            capacity < Number(el('batchEditCapacity').min) || capacity > 100000 || displayOrder < 0) {
            notice('Capacity cannot decrease; enter valid non-negative values.', true); return;
        }
        button.disabled = true;
        try {
            await api('/api/academic-setup/batches/' + encodeURIComponent(value('batchEditId')), {
                method: 'PUT', body: JSON.stringify({
                    name: value('batchEditName'), capacity, displayOrder,
                    rowVersion: value('batchEditVersion')
                })
            });
            el('batchEditForm').classList.add('d-none');
            await Promise.all([load(), reload()]);
            notice('Section metadata saved.');
        } catch (error) { notice(error.message, true); }
        finally { button.disabled = false; }
    });
    window.addEventListener('eduos:academic-reloaded', () => {
        refreshFilters();
        if (loaded || document.querySelector('button[data-panel="sections"].active'))
            load().catch(e => notice(e.message, true));
    });
    document.querySelector('button[data-panel="sections"]').addEventListener('click', () => {
        if (!loaded) load().catch(e => notice(e.message, true));
    });
})();
