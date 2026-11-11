(() => {
    'use strict';
    const root = document.getElementById('studentDocumentsApp');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { student: null, uploadId: null, stagedFile: null, busy: false,
        studentSeq: 0, searchSeq: 0, documentSeq: 0 };
    const context = [document.body?.dataset.authTenantId || '', document.body?.dataset.authSessionReference || ''];
    const live = () => context[0] === (document.body?.dataset.authTenantId || '') &&
        context[1] === (document.body?.dataset.authSessionReference || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
    const notice = (message, error = false) => {
        $('documentNotice').className = 'alert ' + (error ? 'alert-danger' : 'alert-info');
        $('documentNotice').textContent = message;
    };
    async function api(url, options = {}) {
        if (!live()) throw new Error('Institution session changed. Reload the page.');
        const headers = { Accept: 'application/json', ...(options.headers || {}) };
        if (options.method && options.method !== 'GET') headers.RequestVerificationToken = token;
        if (options.body && !(options.body instanceof FormData)) headers['Content-Type'] = 'application/json';
        const response = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store', ...options, headers
        });
        const json = await response.json().catch(() => null);
        if (!live()) throw new Error('Institution session changed. Reload the page.');
        if (!response.ok || json?.success !== true)
            throw new Error(json?.message || 'Request failed. Reload and retry.');
        return json.data;
    }
    function buttons() {
        $('documentAttach').disabled = state.busy || !state.student || !$('documentType').value;
        $('documentRefresh').disabled = state.busy || !state.student;
        for (const id of ['documentType', 'documentTitle', 'documentFile'])
            $(id).disabled = state.busy || !live();
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
        const reference = state.student.reference, sequence = ++state.documentSeq;
        const docs = await api('/api/student-documents/students/' + encodeURIComponent(reference));
        if (sequence !== state.documentSeq || state.student?.reference !== reference || !live()) return;
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
                    if (state.busy || !live() || state.student?.reference !== reference ||
                        !confirm('Deactivate this document? The audit history will remain.')) return;
                    state.busy = true; buttons();
                    try {
                        await api('/api/student-documents/students/' + encodeURIComponent(reference) +
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
        if (state.busy || !live()) return;
        if (state.student && state.student.reference !== reference &&
            ($('documentFile').files?.length || $('documentTitle').value.trim()) &&
            !confirm('Discard the unsaved document draft before switching students?')) return;
        const sequence = ++state.studentSeq;
        ++state.documentSeq;
        state.student = null; state.uploadId = null; state.stagedFile = null; buttons();
        $('documentFile').value = ''; $('documentTitle').value = '';
        $('documentSelected').textContent = '';
        messageRow('Loading student documents…');
        try {
            const student = await api('/api/students/' + encodeURIComponent(reference));
            if (sequence !== state.studentSeq || !live()) return;
            if (!student?.rowVersion) throw new Error('Student version unavailable.');
            state.student = student;
            $('documentSelected').textContent = student.studentCode + ' — ' + student.fullName;
            await refresh(); notice('Student selected. Upload a scanned private document.');
        } catch (error) {
            if (sequence === state.studentSeq) { messageRow('Unable to load student.'); notice(error.message, true); }
        }
        if (sequence === state.studentSeq) buttons();
    }
    $('documentSearchForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (!live()) return notice('Institution session changed. Reload the page.', true);
        const search = $('documentSearch').value.trim();
        if (search.length < 2) return notice('Enter at least two characters.', true);
        const sequence = ++state.searchSeq;
        try {
            const data = await api('/api/students?' + new URLSearchParams({ search, page: 1, pageSize: 20 }));
            if (sequence !== state.searchSeq || !live()) return;
            const box = $('documentMatches'); box.replaceChildren();
            if (!data?.items?.length) { box.textContent = 'No matching students.'; return; }
            for (const student of data.items) {
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'list-group-item list-group-item-action';
                button.textContent = student.studentCode + ' — ' + student.fullName;
                button.addEventListener('click', () => selectStudent(student.reference));
                box.append(button);
            }
        } catch (error) { if (sequence === state.searchSeq) notice(error.message, true); }
    });
    $('documentAttachForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (state.busy || !state.student || !live()) return;
        const file = $('documentFile').files?.[0];
        if (!file || file.size < 1 || file.size > 5242880)
            return notice('Choose a file up to 5 MB.', true);
        if (!$('documentType').value) return notice('Choose a document type.', true);
        const student = state.student, documentType = $('documentType').value;
        const title = $('documentTitle').value.trim() || null;
        if (!state.uploadId) state.uploadId = crypto.randomUUID();
        state.busy = true; buttons();
        try {
            if (!state.stagedFile) {
                const form = new FormData();
                form.append('file', file); form.append('requestId', state.uploadId);
                const uploaded = await api('/api/document-admin/files', { method: 'POST', body: form });
                state.stagedFile = uploaded.reference;
            }
            if (state.student?.reference !== student.reference || !live())
                throw new Error('Selected student or institution changed. Review the document and retry.');
            await api('/api/student-documents/students/' + encodeURIComponent(student.reference), {
                method: 'POST',
                body: JSON.stringify({
                    fileReference: state.stagedFile, documentTypeCode: documentType,
                    title, studentRowVersion: student.rowVersion
                })
            });
            state.uploadId = null; state.stagedFile = null;
            $('documentFile').value = ''; $('documentTitle').value = '';
            notice('Student document attached.'); await refresh();
        } catch (error) { notice(error.message, true); }
        finally { state.busy = false; buttons(); }
    });
    const changed = () => { state.uploadId = null; state.stagedFile = null; };
    $('documentFile').addEventListener('change', changed);
    $('documentTitle').addEventListener('input', changed);
    $('documentType').addEventListener('change', () => { changed(); buttons(); });
    $('documentRefresh').addEventListener('click', async () => {
        if (!live()) return notice('Institution session changed. Reload the page.', true);
        try { await refresh(); } catch (error) { notice(error.message, true); }
    });
    function reset() {
        ++state.studentSeq; ++state.searchSeq; ++state.documentSeq;
        state.student = null; state.uploadId = null; state.stagedFile = null;
        $('documentSelected').textContent = '';
        $('documentMatches').replaceChildren(); $('documentRows').replaceChildren();
        $('documentFile').value = ''; $('documentTitle').value = '';
        buttons();
    }
    const observer = new MutationObserver(() => { if (!live()) { reset(); observer.disconnect(); } });
    observer.observe(document.body, { attributes: true,
        attributeFilter: ['data-auth-tenant-id', 'data-auth-session-reference'] });
    window.addEventListener('pagehide', reset);
    window.addEventListener('beforeunload', event => {
        if (!live() || !state.student ||
            (!$('documentFile').files?.length && !$('documentTitle').value.trim())) return;
        event.preventDefault(); event.returnValue = '';
    });
    types().catch(error => notice(error.message, true));
    const initial = new URLSearchParams(location.search).get('studentReference');
    if (initial && /^[0-9a-f-]{36}$/i.test(initial)) selectStudent(initial);
})();
