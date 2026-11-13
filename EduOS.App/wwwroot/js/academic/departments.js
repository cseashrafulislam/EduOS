(() => {
    'use strict';
    const vm = window.eduosAcademicManagement;
    if (!vm) return;
    const { el, api, notice, sameSession, guard, mayReplace } = vm;
    let page = 1, total = 0, rows = [], pending = false, generation = 0, departmentLookup = 0;
    const formGuard = guard('departmentForm');
    const cell = (tr, value) => {
        const td = document.createElement('td');
        td.textContent = String(value ?? '');
        tr.append(td);
        return td;
    };
    const reset = (force = false) => {
        if (!force && (pending || !mayReplace('departmentForm'))) return false;
        el('departmentForm').reset();
        el('departmentId').value = '';
        el('departmentVersion').value = '';
        el('departmentCode').disabled = false;
        el('departmentActive').checked = true;
        el('departmentSave').textContent = 'Create Department';
        formGuard?.markClean();
        return true;
    };
    async function refresh() {
        const ticket = ++generation;
        const result = await api('/api/academic-setup/departments?page=' + page +
            '&pageSize=20&search=' + encodeURIComponent(el('departmentSearch').value.trim()));
        if (ticket !== generation || !sameSession()) return;
        rows = result.items || [];
        total = result.totalCount || 0;
        const body = el('departmentRows');
        body.replaceChildren();
        for (const row of rows) {
            const tr = document.createElement('tr');
            cell(tr, row.name); cell(tr, row.code);
            cell(tr, row.description); cell(tr, row.isActive ? 'Active' : 'Inactive');
            const td = cell(tr, '');
            const edit = document.createElement('button');
            edit.type = 'button'; edit.className = 'btn btn-sm btn-outline-primary';
            edit.textContent = 'Edit';
            edit.addEventListener('click', () => {
                if (!sameSession() || !reset()) return;
                el('departmentId').value = row.id;
                el('departmentVersion').value = row.rowVersion;
                el('departmentName').value = row.name;
                el('departmentCode').value = row.code;
                el('departmentCode').disabled = true;
                el('departmentDescription').value = row.description || '';
                el('departmentActive').checked = row.isActive;
                el('departmentSave').textContent = 'Update Department';
                formGuard?.markClean();
                el('departmentForm').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            });
            td.append(edit); body.append(tr);
        }
        if (!rows.length) {
            const tr = document.createElement('tr'), td = cell(tr, 'No departments found.');
            td.colSpan = 5; body.append(tr);
        }
        el('departmentPage').textContent = 'Page ' + page + ' of ' +
            Math.max(1, Math.ceil(total / 20)) + ' • ' + total + ' departments';
        el('departmentPrev').disabled = page <= 1;
        el('departmentNext').disabled = page * 20 >= total;
    }
    async function refreshProgrammeDepartments() {
        const ticket = ++departmentLookup;
        const result = await api('/api/academic-setup/departments?page=1&pageSize=100');
        if (ticket !== departmentLookup || !sameSession()) return;
        const programmeGuard = guard('programForm'), clean = !programmeGuard?.isDirty();
        const select = el('programDepartment'), previous = select.value;
        select.replaceChildren(new Option('No department', ''));
        for (const row of result.items || [])
            if (row.isActive) select.add(new Option(row.name + ' (' + row.code + ')', String(row.id)));
        if ([...select.options].some(x => x.value === previous)) select.value = previous;
        if (clean) programmeGuard?.markClean();
    }
    const safeRefresh = () => refresh().catch(e => notice(e.message, true));
    el('departmentSearchButton').addEventListener('click', () => { page = 1; safeRefresh(); });
    el('departmentSearch').addEventListener('keydown', e => {
        if (e.key === 'Enter') { e.preventDefault(); page = 1; safeRefresh(); }
    });
    el('departmentPrev').addEventListener('click', () => { if (page > 1) { page--; safeRefresh(); } });
    el('departmentNext').addEventListener('click', () => {
        if (page * 20 < total) { page++; safeRefresh(); }
    });
    el('departmentCancel').addEventListener('click', () => reset());
    el('departmentForm').addEventListener('submit', async e => {
        e.preventDefault();
        if (pending || !sameSession() || !e.currentTarget.reportValidity()) return;
        const id = Number(el('departmentId').value) || 0;
        const body = {
            name: el('departmentName').value.trim(),
            code: id ? el('departmentCode').value.trim().toUpperCase() : null,
            description: el('departmentDescription').value.trim() || null,
            isActive: el('departmentActive').checked,
            rowVersion: id ? el('departmentVersion').value : null
        };
        if (!body.name || body.name.length > 150 ||
            (body.description || '').length > 500) {
            notice('Enter a valid department name and description.', true); return;
        }
        pending = true; el('departmentSave').disabled = true; e.currentTarget.inert = true;
        let committed = false;
        try {
            await api(id ? '/api/academic-setup/departments/' + id :
                '/api/academic-setup/departments', {
                method: id ? 'PUT' : 'POST', body: JSON.stringify(body)
            });
            committed = true;
            if (!sameSession()) return;
            reset(true); page = 1;
            try {
                await Promise.all([refresh(), refreshProgrammeDepartments()]);
                notice('Academic department saved.');
            } catch { notice('Department saved, but the directory did not refresh. Reload the page.', true); }
        } catch (error) {
            if (sameSession()) notice(committed ? 'Department saved, but the display failed.' : error.message, true);
        } finally { pending = false; el('departmentSave').disabled = false; e.currentTarget.inert = false; }
    });
    el('departmentCsv').addEventListener('click', () => {
        if (!rows.length) return;
        const escape = value => {
            const valueText = String(value ?? '');
            const safe = /^\s*[=+@-]/.test(valueText) ? "'" + valueText : valueText;
            return '"' + safe.replace(/"/g, '""') + '"';
        };
        const data = [['Name', 'Code', 'Description', 'Active'],
            ...rows.map(x => [x.name, x.code, x.description, x.isActive])];
        const csv = '\uFEFF' + data.map(x => x.map(escape).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a'); a.href = url;
        a.download = 'academic-departments-page-' + page + '.csv'; a.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    });
    safeRefresh();
    refreshProgrammeDepartments().catch(e => notice(e.message, true));
})();
