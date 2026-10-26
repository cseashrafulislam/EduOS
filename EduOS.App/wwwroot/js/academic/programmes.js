(() => {
    'use strict';
    const vm = window.eduosAcademicManagement;
    if (!vm) return;
    const { el, api, notice, reload } = vm;
    let page = 1, total = 0, rows = [], pending = false, sequence = 0;
    const cell = (tr, value) => {
        const td = document.createElement('td');
        td.textContent = String(value ?? '');
        tr.append(td);
        return td;
    };
    const safeRefresh = () => refresh().catch(e => notice(e.message, true));
    async function refresh() {
        const ticket = ++sequence;
        const result = await api('/api/academic-setup/programmes?page=' + page +
            '&pageSize=20&search=' + encodeURIComponent(el('programmeSearch').value.trim()));
        if (ticket !== sequence) return;
        rows = result.items || [];
        total = result.totalCount || 0;
        const body = el('programmeRows');
        body.replaceChildren();
        for (const row of rows) {
            const tr = document.createElement('tr');
            cell(tr, row.name); cell(tr, row.code);
            cell(tr, row.academicDepartmentName || 'Unassigned');
            cell(tr, row.durationInMonths);
            cell(tr, row.isActive ? (row.isAdmissionOpen ? 'Active / Admissions open' : 'Active / Closed') : 'Inactive');
            const action = cell(tr, '');
            const edit = document.createElement('button');
            edit.type = 'button'; edit.className = 'btn btn-sm btn-outline-primary';
            edit.textContent = 'Edit metadata';
            edit.addEventListener('click', async () => {
                edit.disabled = true;
                try { await editProgramme(row.id); }
                catch (error) { notice(error.message, true); }
                finally { edit.disabled = false; }
            });
            action.append(edit); body.append(tr);
        }
        if (!rows.length) {
            const tr = document.createElement('tr'), td = cell(tr, 'No programmes found.');
            td.colSpan = 6; body.append(tr);
        }
        el('programmePage').textContent = 'Page ' + page + ' of ' +
            Math.max(1, Math.ceil(total / 20)) + ' • ' + total + ' programmes';
        el('programmePrev').disabled = page <= 1;
        el('programmeNext').disabled = page * 20 >= total;
    }
    async function loadDepartments(selectedId = null) {
        const search = el('programmeDepartmentSearch').value.trim();
        const result = await api('/api/academic-setup/departments?page=1&pageSize=100&search=' +
            encodeURIComponent(search));
        const select = el('programmeEditDepartment');
        select.replaceChildren(new Option('No department', ''));
        for (const row of result.items || [])
            if (row.isActive || row.id === selectedId)
                select.add(new Option(row.name + ' (' + row.code + ')' +
                    (row.isActive ? '' : ' [inactive]'), String(row.id)));
        if (selectedId && ![...select.options].some(x => x.value === String(selectedId)))
            select.add(new Option('Current department #' + selectedId + ' (search to change)', String(selectedId)));
        select.value = selectedId ? String(selectedId) : '';
    }
    async function editProgramme(id) {
        const row = await api('/api/academic-setup/programmes/' + encodeURIComponent(id));
        el('programmeEditForm').reset();
        el('programmeDepartmentSearch').value = '';
        await loadDepartments(row.academicDepartmentId);
        const fields = {
            programmeEditId: row.id, programmeEditVersion: row.rowVersion,
            programmeEditCode: row.code, programmeEditName: row.name,
            programmeEditShortName: row.shortName || '', programmeEditDuration: row.durationInMonths,
            programmeEditAward: row.awardTitle || '', programmeEditDescription: row.description || '',
            programmeEditOrder: row.displayOrder
        };
        for (const [key, value] of Object.entries(fields)) el(key).value = value ?? '';
        el('programmeEditActive').checked = row.isActive;
        el('programmeEditAdmissions').checked = row.isAdmissionOpen;
        el('programmeEditForm').classList.remove('d-none');
        el('programmeEditForm').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
    el('programmeSearchButton').addEventListener('click', () => { page = 1; safeRefresh(); });
    el('programmeSearch').addEventListener('keydown', e => {
        if (e.key === 'Enter') { e.preventDefault(); page = 1; safeRefresh(); }
    });
    el('programmePrev').addEventListener('click', () => { if (page > 1) { page--; safeRefresh(); } });
    el('programmeNext').addEventListener('click', () => {
        if (page * 20 < total) { page++; safeRefresh(); }
    });
    el('programmeDepartmentSearchButton').addEventListener('click', () =>
        loadDepartments(Number(el('programmeEditDepartment').value) || null)
            .catch(e => notice(e.message, true)));
    el('programmeEditCancel').addEventListener('click', () =>
        el('programmeEditForm').classList.add('d-none'));
    el('programmeEditForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (pending) return;
        const id = Number(el('programmeEditId').value);
        const body = {
            rowVersion: el('programmeEditVersion').value,
            academicDepartmentId: Number(el('programmeEditDepartment').value) || null,
            name: el('programmeEditName').value.trim(),
            shortName: el('programmeEditShortName').value.trim() || null,
            durationInMonths: Number(el('programmeEditDuration').value),
            awardTitle: el('programmeEditAward').value.trim() || null,
            description: el('programmeEditDescription').value.trim() || null,
            displayOrder: Number(el('programmeEditOrder').value),
            isActive: el('programmeEditActive').checked,
            isAdmissionOpen: el('programmeEditAdmissions').checked
        };
        if (!id || !body.rowVersion || !body.name || body.name.length > 200 ||
            !Number.isInteger(body.durationInMonths) || body.durationInMonths < 0 ||
            body.durationInMonths > 1200 || !Number.isInteger(body.displayOrder) ||
            body.displayOrder < 0 || (!body.isActive && body.isAdmissionOpen)) {
            notice('Check programme name, duration, status and version.', true); return;
        }
        pending = true;
        const button = el('programmeEditSave');
        button.disabled = true;
        try {
            await api('/api/academic-setup/programmes/' + encodeURIComponent(id), {
                method: 'PUT', body: JSON.stringify(body)
            });
            el('programmeEditForm').classList.add('d-none');
            await reload();
            notice('Programme metadata updated. Campus allocation and curriculum remain unchanged.');
        } catch (error) { notice(error.message, true); }
        finally { pending = false; button.disabled = false; }
    });
    el('programmeCsv').addEventListener('click', () => {
        if (!rows.length) return;
        const escape = value => {
            const valueText = String(value ?? '');
            const safe = /^\s*[=+@-]/.test(valueText) ? "'" + valueText : valueText;
            return '"' + safe.replace(/"/g, '""') + '"';
        };
        const data = [['Programme', 'Code', 'Department', 'Duration Months', 'Active', 'Admissions Open'],
            ...rows.map(x => [x.name, x.code, x.academicDepartmentName,
                x.durationInMonths, x.isActive, x.isAdmissionOpen])];
        const csv = '\uFEFF' + data.map(x => x.map(escape).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a');
        a.href = url; a.download = 'academic-programmes-page-' + page + '.csv'; a.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    });
    window.addEventListener('eduos:academic-reloaded', safeRefresh);
    safeRefresh();
})();
