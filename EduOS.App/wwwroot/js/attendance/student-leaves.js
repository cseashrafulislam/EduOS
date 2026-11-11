(() => {
    'use strict';
    const root = document.getElementById('studentLeavesApp');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value;
    const val = id => el(id).value.trim();
    const state = { enrollment: null, page: 1, pages: 1, studentPage: 1, studentPages: 1, requestId: null, uploadId: null, attachmentId: null, saving: false };
    const originalTenant = document.body?.dataset?.authTenantId || '';
    const originalSession = document.body?.dataset?.authSessionReference || '';
    const sameSession = () => !!originalTenant && !!originalSession &&
        document.body?.dataset?.authTenantId === originalTenant &&
        document.body?.dataset?.authSessionReference === originalSession;
    const bn = () => (document.documentElement.lang || '').toLowerCase().startsWith('bn');
    const t = (en, bd) => bn() ? bd : en;
    let searchGeneration = 0, listGeneration = 0;
    const pendingDecisions = new Set();
    const draft = () => JSON.stringify([val('leaveStart'), val('leaveEnd'), val('leaveReason'),
        ...Array.from(el('leaveFile').files || []).map(file => [file.name, file.size, file.lastModified])]);
    let cleanDraft = draft();
    const discardDraft = () => draft() === cleanDraft || confirm(t(
        'Discard the unsaved leave application for this student?',
        'এই শিক্ষার্থীর সংরক্ষণ না করা ছুটির আবেদন বাদ দেবেন?'));
    const checkSession = () => {
        if (sameSession()) return;
        state.enrollment = null; ++searchGeneration; ++listGeneration;
        el('leaveRows').replaceChildren(); el('leaveStudentResults').replaceChildren();
        el('leaveStudentLabel').textContent = '';
        available();
        throw new Error(t('Institution session changed. Reload the page.', 'প্রতিষ্ঠানের সেশন বদলেছে। পৃষ্ঠা রিলোড করুন।'));
    };
    function info(text, failed = false) {
        const box = el('leaveNotice'); box.className = 'alert ' + (failed ? 'alert-danger' : 'alert-success');
        box.textContent = text;
    }
    async function api(url, options = {}) {
        checkSession();
        const response = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store', ...options,
            headers: { Accept: 'application/json', ...(options.body && !(options.body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}),
                ...(options.method && options.method !== 'GET' ? { RequestVerificationToken: token } : {}) }
        });
        const result = await response.json().catch(() => null);
        checkSession();
        if (!response.ok || result?.success !== true)
            throw new Error(result?.message || 'Request failed; reload and retry.');
        return result.data;
    }
    function uuid() {
        if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
        const bytes = new Uint8Array(16); crypto.getRandomValues(bytes);
        bytes[6] = bytes[6] & 15 | 64; bytes[8] = bytes[8] & 63 | 128;
        const h = [...bytes].map(x => x.toString(16).padStart(2, '0')).join('');
        return [h.slice(0, 8), h.slice(8, 12), h.slice(12, 16), h.slice(16, 20), h.slice(20)].join('-');
    }
    function available() { el('leaveSubmit').disabled = state.saving || !state.enrollment; }
    async function searchStudents(pageNumber) {
        if (val('leaveStudentTerm').length < 2) return info('Enter at least two characters.', true);
        const term = val('leaveStudentTerm'), generation = ++searchGeneration;
        try {
            const page = await api('/api/student-leaves/eligible-enrollments?' + new URLSearchParams({
                search: term, page: pageNumber, pageSize: 20
            }));
            if (generation !== searchGeneration || val('leaveStudentTerm') !== term) return;
            state.studentPage = pageNumber;
            state.studentPages = Math.max(1, page.totalPages || 1);
            const list = el('leaveStudentResults'); list.replaceChildren();
            for (const item of page.items || []) {
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'list-group-item list-group-item-action';
                button.textContent = item.fullName + ' — ' + item.studentCode + ' / Roll ' + (item.rollNo || '—');
                button.addEventListener('click', () => choose(item));
                list.append(button);
            }
            if (!page.items?.length) list.textContent = 'No matching active enrollments in your campuses.';
            el('leaveStudentPageText').textContent = 'Page ' + state.studentPage + ' of ' +
                state.studentPages + ' · ' + page.totalCount + ' enrollments';
            el('leaveStudentPrevious').disabled = state.studentPage <= 1;
            el('leaveStudentNext').disabled = state.studentPage >= state.studentPages;
        } catch (e) { if (generation === searchGeneration) info(e.message, true); }
    }
    function choose(student) {
        try { checkSession(); } catch (error) { info(error.message, true); return; }
        if (state.saving) return info(t('Wait for the current application to finish.', 'চলমান আবেদনটি শেষ হওয়া পর্যন্ত অপেক্ষা করুন।'), true);
        if (state.enrollment?.reference === student.reference) return;
        if (state.enrollment?.reference !== student.reference && !discardDraft()) return;
        ++listGeneration;
        state.enrollment = { reference: student.reference, rollNo: student.rollNo };
        state.requestId = null; state.uploadId = null; state.attachmentId = null;
        el('leaveApplyForm').reset(); cleanDraft = draft();
        el('leaveStudentLabel').textContent = student.fullName + ' / ' + student.studentCode +
            ' / Roll ' + (student.rollNo || '—');
        available(); state.page = 1; load();
    }
    async function load() {
        const generation = ++listGeneration, enrollment = state.enrollment?.reference || '';
        const filter = val('leaveFilter'), requestedPage = state.page;
        try {
            const query = new URLSearchParams({ page: state.page, pageSize: 20 });
            if (state.enrollment) query.set('enrollmentReference', state.enrollment.reference);
            if (filter) query.set('state', filter);
            const page = await api('/api/student-leaves?' + query);
            if (generation !== listGeneration || state.enrollment?.reference !== (enrollment || undefined) ||
                val('leaveFilter') !== filter || state.page !== requestedPage) return;
            state.pages = Math.max(1, page.totalPages || 1);
            const tbody = el('leaveRows'); tbody.replaceChildren();
            for (const row of page.items || []) {
                const tr = document.createElement('tr');
                for (const value of [row.fromDate + ' – ' + row.toDate, row.reason,
                    ({ 1: 'Draft', 2: 'Submitted', 3: 'Approved', 4: 'Rejected', 5: 'Cancelled' })[row.state]]) {
                    const cell = document.createElement('td'); cell.textContent = value || ''; tr.append(cell);
                }
                const td = document.createElement('td');
                if (row.attachmentFileId) {
                    const link = document.createElement('a');
                    link.href = '/api/student-leaves/' + encodeURIComponent(row.id) + '/attachment';
                    link.className = 'btn btn-sm btn-outline-primary me-1';
                    link.textContent = 'Evidence';
                    link.rel = 'noreferrer';
                    td.append(link);
                }
                if (row.state === 2) {
                    for (const [name, decision] of [['Approve', 3], ['Reject', 4]]) {
                        const button = document.createElement('button');
                        button.type = 'button';
                        button.className = 'btn btn-sm me-1 ' +
                            (decision === 3 ? 'btn-outline-success' : 'btn-outline-danger');
                        button.textContent = name;
                        button.addEventListener('click', () => review(row, decision, button));
                        td.append(button);
                    }
                    const withdraw = document.createElement('button');
                    withdraw.type = 'button'; withdraw.className = 'btn btn-sm btn-outline-secondary';
                    withdraw.textContent = 'Cancel';
                    withdraw.addEventListener('click', () => cancelLeave(row, withdraw));
                    td.append(withdraw);
                } else {
                    const note = document.createElement('span');
                    note.textContent = row.reviewNote || '—';
                    td.append(note);
                }
                tr.append(td); tbody.append(tr);
            }
            if (!page.items?.length) {
                const tr = document.createElement('tr'), td = document.createElement('td');
                td.colSpan = 4; td.textContent = 'No leave applications.'; tr.append(td); tbody.append(tr);
            }
            el('leavePageText').textContent = 'Page ' + state.page + ' of ' + state.pages +
                ' · ' + page.totalCount + ' records';
            el('leavePrevious').disabled = state.page <= 1;
            el('leaveNext').disabled = state.page >= state.pages;
            return true;
        } catch (e) { if (generation === listGeneration) info(e.message, true); return false; }
    }
    async function apply(e) {
        e.preventDefault();
        if (!state.enrollment || state.saving) return;
        if (val('leaveEnd') < val('leaveStart')) return info('Leave end date is before start.', true);
        if (!state.requestId) state.requestId = uuid();
        state.saving = true; available();
        const controls = Array.from(el('leaveApplyForm').querySelectorAll('input,textarea,select,button'));
        const disabled = controls.map(control => control.disabled);
        controls.forEach(control => { control.disabled = true; });
        const enrollmentRef = state.enrollment.reference;
        try {
            const file = el('leaveFile').files[0];
            if (file && !state.attachmentId) {
                if (file.size < 1 || file.size > 5242880)
                    throw new Error('Supporting file must be between 1 byte and 5 MB.');
                if (!state.uploadId) state.uploadId = state.requestId;
                const form = new FormData();
                form.append('enrollmentReference', enrollmentRef);
                form.append('requestId', state.uploadId);
                form.append('file', file);
                const uploaded = await api('/api/student-leaves/attachments', { method: 'POST', body: form });
                state.attachmentId = uploaded.id;
            }
            if (state.enrollment?.reference !== enrollmentRef)
                throw new Error('Selected student changed. Review and submit again.');
            await api('/api/student-leaves', { method: 'POST', body: JSON.stringify({
                clientRequestId: state.requestId, studentEnrollmentReference: state.enrollment.reference,
                fromDate: val('leaveStart'), toDate: val('leaveEnd'),
                reason: val('leaveReason'), attachmentFileId: state.attachmentId
            }) });
            state.requestId = null; state.uploadId = null; state.attachmentId = null;
            el('leaveApplyForm').reset(); state.page = 1;
            cleanDraft = draft();
            const refreshed = await load();
            checkSession();
            info(refreshed ? t('Leave application submitted.', 'ছুটির আবেদন জমা হয়েছে।') :
                t('Leave submitted; the list could not refresh. Reload to view it.', 'ছুটি জমা হয়েছে; তালিকা হালনাগাদ হয়নি। দেখতে রিলোড করুন।'));
        } catch (error) { info(error.message, true); }
        finally {
            controls.forEach((control, i) => { control.disabled = disabled[i]; });
            state.saving = false; available();
        }
    }
    async function cancelLeave(row, button) {
        if (pendingDecisions.has(row.id)) return;
        const reason = prompt('Reason for cancelling this pending leave:', '');
        if (reason === null) return;
        if (!reason.trim() || reason.trim().length > 500)
            return info('Provide a reason (up to 500 characters).', true);
        pendingDecisions.add(row.id); button.disabled = true;
        try {
            await api('/api/student-leaves/' + row.id + '/cancel', {
                method: 'POST', body: JSON.stringify({
                    reason: reason.trim(), rowVersion: row.rowVersion
                })
            });
            await load(); info('Pending leave cancelled.');
        } catch (e) { info(e.message, true); }
        finally { pendingDecisions.delete(row.id); button.disabled = false; }
    }
    async function review(row, decision, button) {
        if (pendingDecisions.has(row.id)) return;
        const note = prompt('Decision note (optional):', '');
        if (note === null) return;
        if (note.length > 1000) return info('Review note exceeds limit.', true);
        pendingDecisions.add(row.id); button.disabled = true;
        try {
            await api('/api/student-leaves/' + row.id + '/review', {
                method: 'POST', body: JSON.stringify({
                    state: decision, rowVersion: row.rowVersion, reviewNote: note || null
                })
            });
            await load(); info('Leave reviewed.');
        } catch (e) { info(e.message, true); }
        finally { pendingDecisions.delete(row.id); button.disabled = false; }
    }
    el('leaveStudentSearch').addEventListener('submit', e => { e.preventDefault(); searchStudents(1); });
    el('leaveStudentTerm').addEventListener('input', () => {
        ++searchGeneration;
        el('leaveStudentResults').replaceChildren();
    });
    el('leaveStudentPrevious').addEventListener('click', () => {
        if (state.studentPage > 1) searchStudents(state.studentPage - 1);
    });
    el('leaveStudentNext').addEventListener('click', () => {
        if (state.studentPage < state.studentPages) searchStudents(state.studentPage + 1);
    });
    el('leaveApplyForm').addEventListener('submit', apply);
    el('leaveRefresh').addEventListener('click', load);
    el('leaveFilter').addEventListener('change', () => { state.page = 1; load(); });
    el('leavePrevious').addEventListener('click', () => { if (state.page > 1) { state.page--; load(); } });
    el('leaveNext').addEventListener('click', () => { if (state.page < state.pages) { state.page++; load(); } });
    for (const id of ['leaveStart', 'leaveEnd', 'leaveReason', 'leaveFile'])
        el(id).addEventListener('change', () => {
            if (state.saving) return;
            state.requestId = null; state.uploadId = null; state.attachmentId = null;
        });
    window.addEventListener('beforeunload', event => {
        if (!state.saving && draft() === cleanDraft) return;
        event.preventDefault(); event.returnValue = '';
    });
    load();
})();
