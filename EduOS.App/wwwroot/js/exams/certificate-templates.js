(() => {
    'use strict';
    const root = document.getElementById('certificateTemplateWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { page: 1, pageSize: 20, total: 0, rows: [], busy: false };
    const notice = (text, error = false) => {
        const box = el('certificateTemplateNotice');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = text;
    };
    async function api(path, method = 'GET', body) {
        const response = await fetch(path, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json',
                ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
                ...(method === 'GET' ? {} : { RequestVerificationToken: token }) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        const result = await response.json().catch(() => null);
        if (!response.ok || result?.success !== true) throw new Error(result?.message || 'Request failed.');
        return result.data;
    }
    const cell = value => { const td = document.createElement('td'); td.textContent = value == null ? '' : String(value); return td; };
    function render() {
        const tbody = el('certificateTemplateRows'); tbody.replaceChildren();
        for (const row of state.rows) {
            const tr = document.createElement('tr');
            tr.append(cell(row.code), cell(row.name), cell(row.isActive ? 'Yes' : 'No'),
                cell(row.isDefault ? 'Yes' : 'No'), cell(row.isLocked ? 'Locked' : 'No'));
            const action = document.createElement('td'); action.className = 'certificate-template-action';
            const button = document.createElement('button');
            button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
            button.textContent = row.isLocked ? 'Issued — locked' : 'Edit';
            button.disabled = row.isLocked || state.busy;
            button.addEventListener('click', () => edit(row));
            action.append(button); tr.append(action); tbody.append(tr);
        }
        if (!state.rows.length) {
            const tr = document.createElement('tr'), td = cell('No templates found.');
            td.colSpan = 6; tr.append(td); tbody.append(tr);
        }
        el('certificateTemplateCount').textContent = state.total + ' template(s) · Page ' + state.page;
        el('certificateTemplatePrev').disabled = state.page <= 1;
        el('certificateTemplateNext').disabled = state.page * state.pageSize >= state.total;
    }
    async function load() {
        const q = new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize),
            search: el('certificateTemplateSearch').value.trim() });
        try {
            const data = await api('/api/exams/workflow/certificate-templates?' + q);
            state.rows = data.items || []; state.total = data.totalCount || 0; render();
        } catch (e) { notice(e.message, true); }
    }
    function clear() {
        el('certificateTemplateForm').reset();
        el('certificateTemplateId').value = '';
        el('certificateTemplateVersion').value = '';
        el('certificateTemplateActive').checked = true;
        el('certificateTemplateFormTitle').textContent = 'New template';
    }
    async function edit(row) {
        if (row.isLocked || state.busy) return;
        try {
            const data = await api('/api/exams/workflow/certificate-templates/' + row.id);
            el('certificateTemplateId').value = data.id;
            el('certificateTemplateVersion').value = data.rowVersion;
            el('certificateTemplateCode').value = data.code;
            el('certificateTemplateName').value = data.name;
            el('certificateTemplateBody').value = data.htmlTemplate;
            el('certificateTemplateActive').checked = data.isActive;
            el('certificateTemplateDefault').checked = data.isDefault;
            el('certificateTemplateFormTitle').textContent = 'Edit template ' + data.code;
            el('certificateTemplateForm').scrollIntoView({ behavior: 'smooth', block: 'start' });
        } catch (e) { notice(e.message, true); }
    }
    async function save(event) {
        event.preventDefault();
        if (state.busy) return;
        const id = el('certificateTemplateId').value;
        const htmlTemplate = el('certificateTemplateBody').value.trim();
        if (!htmlTemplate || htmlTemplate.length > 50000 || /[<>]/.test(htmlTemplate)) {
            notice('Only escaped text without angle brackets is permitted (max 50000 characters).', true); return;
        }
        const body = {
            code: el('certificateTemplateCode').value.trim(),
            name: el('certificateTemplateName').value.trim(),
            htmlTemplate, isActive: el('certificateTemplateActive').checked,
            isDefault: el('certificateTemplateDefault').checked,
            rowVersion: id ? el('certificateTemplateVersion').value : null
        };
        if (!body.code || !body.name || body.code.length > 50 || body.name.length > 150 ||
            (id && !body.rowVersion)) { notice('Enter a valid code, name and version.', true); return; }
        state.busy = true; el('certificateTemplateSave').disabled = true;
        try {
            await api('/api/exams/workflow/certificate-templates' + (id ? '/' + encodeURIComponent(id) : ''),
                id ? 'PUT' : 'POST', body);
            notice('Certificate template saved.'); clear(); state.page = 1; await load();
        } catch (e) { notice(e.message + ' Reload the template if it was changed.', true); await load(); }
        finally { state.busy = false; el('certificateTemplateSave').disabled = false; }
    }
    function csv() {
        const safe = value => /^[=+@\-\t\r]/.test(String(value ?? '')) ? "'" + String(value) : value;
        const quote = value => '"' + String(safe(value) ?? '').replace(/"/g, '""') + '"';
        const rows = [['Code', 'Name', 'Active', 'Default', 'Issued'],
            ...state.rows.map(x => [x.code, x.name, x.isActive ? 'Yes' : 'No',
                x.isDefault ? 'Yes' : 'No', x.isLocked ? 'Locked' : 'No'])];
        const data = rows.map(row => row.map(quote).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF' + data], { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a'); a.href = url;
        a.download = 'certificate-templates-page-' + state.page + '.csv';
        document.body.append(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    el('certificateTemplateForm').addEventListener('submit', save);
    el('certificateTemplateClear').addEventListener('click', clear);
    el('certificateTemplateRefresh').addEventListener('click', () => { state.page = 1; load(); });
    el('certificateTemplatePrev').addEventListener('click', () => { if (state.page > 1) { state.page--; load(); } });
    el('certificateTemplateNext').addEventListener('click', () => { if (state.page * state.pageSize < state.total) { state.page++; load(); } });
    el('certificateTemplateCsv').addEventListener('click', csv);
    el('certificateTemplatePrint').addEventListener('click', () => window.print());
    load();
})();
