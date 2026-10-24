(() => {
    'use strict';
    const root = document.getElementById('assessmentComponentWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { rows: [], scopes: [], page: 1, pageSize: 20, total: 0, editId: null, rowVersion: null, busy: false };
    const cell = value => { const td = document.createElement('td'); td.textContent = value == null ? '' : String(value); return td; };
    function notice(message, error = false) {
        const box = el('componentNotice');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = message;
    }
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
    const selected = () => state.scopes.find(x => String(x.assessmentSubjectId) === el('componentSubject').value);
    const draft = x => x === 1 || x === 'Draft';
    async function scopes() {
        state.scopes = await api('/api/exams/workflow/scopes') || [];
        const select = el('componentSubject');
        select.replaceChildren(new Option('All assessment subjects', ''));
        for (const x of state.scopes) {
            const label = x.assessmentName + ' / ' + x.academicBatchName + ' / ' + x.subjectName;
            select.add(new Option(label, String(x.assessmentSubjectId)));
        }
        if (!state.scopes.some(x => draft(x.state)))
            notice('No draft assessment subjects available for component creation.', true);
    }
    function render() {
        const body = el('componentRows'); body.replaceChildren();
        for (const row of state.rows) {
            const tr = document.createElement('tr');
            tr.append(cell(row.assessmentName), cell(row.batchName), cell(row.subjectName),
                cell(row.code), cell(row.name), cell(row.fullMarks), cell(row.passMarks),
                cell(row.weightage), cell(row.isMandatoryPass ? 'Yes' : 'No'), cell(row.displayOrder));
            const td = document.createElement('td'); td.className = 'component-action';
            const button = document.createElement('button'); button.type = 'button';
            button.className = 'btn btn-sm btn-outline-primary'; button.textContent = 'Edit';
            button.disabled = state.busy || !draft(row.state) ||
                !state.scopes.some(x => x.assessmentSubjectId === row.assessmentSubjectId);
            button.addEventListener('click', () => edit(row));
            td.append(button); tr.append(td); body.append(tr);
        }
        if (!state.rows.length) {
            const tr = document.createElement('tr'), td = cell('No components found.');
            td.colSpan = 11; tr.append(td); body.append(tr);
        }
        el('componentCount').textContent = state.total + ' component(s) · Page ' + state.page;
        el('componentPrev').disabled = state.page <= 1;
        el('componentNext').disabled = state.page * state.pageSize >= state.total;
    }
    async function load() {
        const query = new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize) });
        if (selected()) query.set('assessmentSubjectId', String(selected().assessmentSubjectId));
        if (el('componentSearchText').value.trim()) query.set('search', el('componentSearchText').value.trim());
        try {
            const data = await api('/api/exams/workflow/components?' + query);
            state.rows = data.items || []; state.total = data.totalCount || 0;
            render();
        } catch (error) { notice(error.message, true); }
    }
    function edit(row) {
        if (!draft(row.state)) { notice('Only draft assessments can be edited.', true); return; }
        el('componentSubject').value = String(row.assessmentSubjectId);
        state.editId = row.id; state.rowVersion = row.rowVersion;
        el('componentSubject').disabled = true;
        el('componentCode').value = row.code;
        el('componentName').value = row.name;
        el('componentFull').value = row.fullMarks;
        el('componentPass').value = row.passMarks;
        el('componentWeight').value = row.weightage;
        el('componentOrder').value = row.displayOrder;
        el('componentMandatory').checked = row.isMandatoryPass;
        el('componentFormTitle').textContent = 'Edit component #' + row.id;
        el('componentSave').textContent = 'Update component';
        el('componentForm').scrollIntoView({ behavior: 'smooth' });
    }
    function clear() {
        state.editId = null; state.rowVersion = null;
        el('componentForm').reset(); el('componentSubject').disabled = false;
        el('componentFormTitle').textContent = 'New assessment component';
        el('componentSave').textContent = 'Save component';
    }
    const valid2 = n => Number.isFinite(n) && Math.abs(Math.round(n * 100) - n * 100) < 1e-7;
    async function save(event) {
        event.preventDefault();
        if (state.busy) return;
        const subject = selected(), full = Number(el('componentFull').value),
            pass = Number(el('componentPass').value), weight = Number(el('componentWeight').value),
            order = Number(el('componentOrder').value);
        if (!subject || !draft(subject.state) || !el('componentCode').value.trim() ||
            !el('componentName').value.trim() || !valid2(full) || full <= 0 ||
            !valid2(pass) || pass < 0 || pass > full ||
            !valid2(weight) || weight <= 0 || weight > 100 ||
            !Number.isSafeInteger(order) || order < 0) {
            notice('Choose a draft subject and valid component code, marks, weightage and order.', true); return;
        }
        const siblings = state.rows.filter(x => x.assessmentSubjectId === subject.assessmentSubjectId && x.id !== state.editId);
        if (siblings.reduce((sum, x) => sum + Number(x.weightage), 0) + weight > 100.000001) {
            notice('Component weights shown on this page exceed 100%; server validates all components.', true); return;
        }
        const payload = {
            assessmentSubjectId: subject.assessmentSubjectId,
            code: el('componentCode').value.trim(), name: el('componentName').value.trim(),
            fullMarks: full, passMarks: pass, weightage: weight,
            isMandatoryPass: el('componentMandatory').checked, displayOrder: order,
            rowVersion: state.rowVersion
        };
        state.busy = true; el('componentSave').disabled = true;
        try {
            await api('/api/exams/workflow/components' +
                (state.editId == null ? '' : '/' + encodeURIComponent(state.editId)),
                state.editId == null ? 'POST' : 'PUT', payload);
            notice('Assessment component saved.'); clear(); await load();
        } catch (error) {
            notice(error.message + ' Check the register before retrying; the write may already have succeeded.', true);
            await load();
        } finally { state.busy = false; el('componentSave').disabled = false; }
    }
    function csv() {
        const safe = value => /^[=+@\-\t\r]/.test(String(value ?? '')) ? "'" + String(value) : value;
        const quote = value => '"' + String(safe(value) ?? '').replace(/"/g, '""') + '"';
        const rows = [['Assessment', 'Batch', 'Subject', 'Code', 'Component', 'Full marks', 'Pass marks', 'Weightage', 'Mandatory pass', 'Order'],
            ...state.rows.map(x => [x.assessmentName, x.batchName, x.subjectName, x.code, x.name,
                x.fullMarks, x.passMarks, x.weightage, x.isMandatoryPass, x.displayOrder])];
        const data = rows.map(x => x.map(quote).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF' + data], { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a'); a.href = url; a.download = 'assessment-components-page-' + state.page + '.csv';
        document.body.append(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    el('componentForm').addEventListener('submit', save);
    el('componentClear').addEventListener('click', clear);
    el('componentSubject').addEventListener('change', () => {
        const subjectId = el('componentSubject').value;
        state.page = 1; clear(); el('componentSubject').value = subjectId; load();
    });
    el('componentSearch').addEventListener('click', () => { state.page = 1; load(); });
    el('componentPrev').addEventListener('click', () => { if (state.page > 1) { state.page--; load(); } });
    el('componentNext').addEventListener('click', () => { if (state.page * state.pageSize < state.total) { state.page++; load(); } });
    el('componentCsv').addEventListener('click', csv);
    el('componentPrint').addEventListener('click', () => window.print());
    scopes().then(load).catch(error => notice(error.message, true));
})();
