(() => {
    'use strict';
    const root = document.getElementById('studentDocumentsApp');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { student: null, uploadId: null, busy: false };
    const notice = (message, error = false) => {
        $('documentNotice').className = 'alert ' + (error ? 'alert-danger' : 'alert-info');
        $('documentNotice').textContent = message;
    };
    async function api(url, options = {}) {
        const headers = { Accept: 'application/json', ...(options.headers || {}) };
        if (options.method && options.method !== 'GET') headers.RequestVerificationToken = token;
        if (options.body && !(options.body instanceof FormData)) headers['Content-Type'] = 'application/json';
        const response = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store', ...options, headers
        });
        const json = await response.json().catch(() => null);
        if (!response.ok || json?.success !== true)
            throw new Error(json?.message || 'Request failed. Reload and retry.');
        return json.data;
    }
    function buttons() {
        $('documentAttach').disabled = state.busy || !state.student || !$('documentType').value;
        $('documentRefresh').disabled = state.busy || !state.student;
    }
    function messageRow(text) {
        const row = document.createElement('tr'), cell = document.createElement('td');
        cell.colSpan = 4; cell.textContent = text; row.append(cell);
        $('documentRows').replaceChildren(row);
    }
    async function types() {
        const data = await api('/api/student-documents/types');
        const select = $('documentType');
        select.replaceChildren(new Option('Choose a document type', ''));
        for (const type of data || []) select.add(new Option(type.name, type.code));
        buttons();
    }
    async function refresh() {
        if (!state.student) return;
        const docs = await api('/api/student-documents/students/' + encodeURIComponent(state.student.reference));
        const tbody = $('documentRows'); tbody.replaceChildren();
        if (!docs?.length) return messageRow('No documents attached.');
        for (const doc of docs) {
            const row = document.createElement('tr');
            for (const value of [doc.documentTypeCode, (doc.title || '') + ' — ' + doc.fileName,
                doc.isActive ? 'Active' : 'Inactive']) {
                const cell = document.createElement('td'); cell.textContent = value; row.append(cell);
            }
            const actions = document.createElement('td');
            if (doc.isActive) {
                const link = document.createElement('a');
                link.href = '/api/student-documents/students/' + encodeURIComponent(state.student.reference) +
                    '/' + encodeURIComponent(doc.reference) + '/download';
                link.className = 'btn btn-sm btn-outline-primary me-1';
                link.textContent = 'Download'; link.rel = 'noreferrer';
                actions.append(link);
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-sm btn-outline-danger';
                button.textContent = 'Deactivate';
                button.addEventListener('click', async () => {
                    if (state.busy || !confirm('Deactivate this document? The audit history will remain.')) return;
                    state.busy = true; buttons();
                    try {
                        await api('/api/student-documents/students/' + encodeURIComponent(state.student.reference) +
                            '/' + encodeURIComponent(doc.reference) + '/deactivate', {
                            method: 'POST', body: JSON.stringify({ rowVersion: doc.rowVersion })
                        });
                        await refresh(); notice('Document deactivated.');
                    } catch (error) { notice(error.message, true); }
                    finally { state.busy = false; buttons(); }
                });
                actions.append(button);
            } else actions.textContent = '—';
            row.append(actions); tbody.append(row);
        }
    }
    async function selectStudent(reference) {
        state.student = null; state.uploadId = null; buttons();
        try {
            const student = await api('/api/students/' + encodeURIComponent(reference));
            if (!student?.rowVersion) throw new Error('Student version unavailable.');
            state.student = student;
            $('documentSelected').textContent = student.studentCode + ' — ' + student.fullName;
            await refresh(); notice('Student selected. Upload a scanned private document.');
        } catch (error) { messageRow('Unable to load student.'); notice(error.message, true); }
        buttons();
    }
    $('documentSearchForm').addEventListener('submit', async event => {
        event.preventDefault();
        const search = $('documentSearch').value.trim();
        if (search.length < 2) return notice('Enter at least two characters.', true);
        try {
            const data = await api('/api/students?' + new URLSearchParams({ search, page: 1, pageSize: 20 }));
            const box = $('documentMatches'); box.replaceChildren();
            if (!data?.items?.length) { box.textContent = 'No matching students.'; return; }
            for (const student of data.items) {
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'list-group-item list-group-item-action';
                button.textContent = student.studentCode + ' — ' + student.fullName;
                button.addEventListener('click', () => selectStudent(student.reference));
                box.append(button);
            }
        } catch (error) { notice(error.message, true); }
    });
    $('documentAttachForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (state.busy || !state.student) return;
        const file = $('documentFile').files?.[0];
        if (!file || file.size < 1 || file.size > 5242880)
            return notice('Choose a file up to 5 MB.', true);
        if (!$('documentType').value) return notice('Choose a document type.', true);
        if (!state.uploadId) state.uploadId = crypto.randomUUID();
        const form = new FormData();
        form.append('file', file); form.append('requestId', state.uploadId);
        state.busy = true; buttons();
        try {
            const uploaded = await api('/api/document-admin/files', { method: 'POST', body: form });
            await api('/api/student-documents/students/' + encodeURIComponent(state.student.reference), {
                method: 'POST',
                body: JSON.stringify({
                    fileReference: uploaded.reference, documentTypeCode: $('documentType').value,
                    title: $('documentTitle').value.trim() || null,
                    studentRowVersion: state.student.rowVersion
                })
            });
            state.uploadId = null; $('documentFile').value = ''; $('documentTitle').value = '';
            await refresh(); notice('Student document attached.');
        } catch (error) { notice(error.message, true); }
        finally { state.busy = false; buttons(); }
    });
    $('documentFile').addEventListener('change', () => { state.uploadId = null; });
    $('documentTitle').addEventListener('input', () => { state.uploadId = null; });
    $('documentType').addEventListener('change', () => { state.uploadId = null; buttons(); });
    $('documentRefresh').addEventListener('click', async () => {
        try { await refresh(); } catch (error) { notice(error.message, true); }
    });
    types().catch(error => notice(error.message, true));
    const initial = new URLSearchParams(location.search).get('studentReference');
    if (initial && /^[0-9a-f-]{36}$/i.test(initial)) selectStudent(initial);
})();
