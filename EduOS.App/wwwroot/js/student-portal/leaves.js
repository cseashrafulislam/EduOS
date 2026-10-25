(() => {
    'use strict';
    const root = document.getElementById('myStudentLeave');
    if (!root) return;
    const el = id => document.getElementById(id), value = id => el(id).value.trim();
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { reference: null, enrollment: null, requestId: null, uploadId: null, attachmentId: null, saving: false };
    function notify(message, danger = false) {
        const box = el('myLeaveNotice');
        box.className = 'alert ' + (danger ? 'alert-danger' : 'alert-success');
        box.textContent = message;
    }
    async function api(url, options = {}) {
        const response = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store', ...options,
            headers: {
                Accept: 'application/json',
                ...(options.body && !(options.body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}),
                ...(options.method && options.method !== 'GET' ? { RequestVerificationToken: token } : {})
            }
        });
        const data = await response.json().catch(() => null);
        if (!response.ok || data?.success !== true)
            throw new Error(data?.message || 'Request failed. Refresh and retry.');
        return data.data;
    }
    function newId() {
        if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
        const random = new Uint8Array(16); crypto.getRandomValues(random);
        random[6] = random[6] & 15 | 64; random[8] = random[8] & 63 | 128;
        const h = [...random].map(x => x.toString(16).padStart(2, '0')).join('');
        return [h.slice(0, 8), h.slice(8, 12), h.slice(12, 16),
            h.slice(16, 20), h.slice(20)].join('-');
    }
    function ready() { el('myLeaveSubmit').disabled = state.saving || !state.enrollment; }
    async function loadStudents() {
        try {
            const students = await api('/api/portal/students');
            const select = el('myLeaveStudent');
            select.replaceChildren(new Option('Select linked student', ''));
            for (const student of students || [])
                select.add(new Option(student.name + ' (' + student.studentCode + ')', student.reference));
            if (students?.length === 1) {
                select.value = students[0].reference;
                await selectStudent();
            }
            if (!students?.length) notify('No student is linked to this account.', true);
        } catch (e) { notify(e.message, true); }
    }
    async function selectStudent() {
        state.reference = value('myLeaveStudent');
        state.enrollment = null; state.requestId = null; state.uploadId = null; state.attachmentId = null; ready();
        el('myLeaveRows').replaceChildren();
        if (!state.reference) return;
        const chosen = state.reference;
        try {
            const current = await api('/api/academic-enrollments/students/' +
                encodeURIComponent(chosen) + '/current');
            if (chosen !== state.reference) return;
            state.enrollment = current;
            ready(); await load();
        } catch (e) { notify('Active enrollment unavailable: ' + e.message, true); }
    }
    async function load() {
        if (!state.enrollment) return;
        try {
            const page = await api('/api/student-leaves?' + new URLSearchParams({
                enrollmentReference: state.enrollment.reference, page: '1', pageSize: '100'
            }));
            const tbody = el('myLeaveRows'); tbody.replaceChildren();
            for (const row of page.items || []) {
                const tr = document.createElement('tr');
                for (const item of [row.fromDate, row.toDate, row.reason,
                    ({ 1: 'Draft', 2: 'Submitted', 3: 'Approved', 4: 'Rejected', 5: 'Cancelled' })[row.state],
                    row.reviewNote || '—']) {
                    const td = document.createElement('td'); td.textContent = item ?? ''; tr.append(td);
                }
                const action = document.createElement('td');
                if (row.attachmentFileId) {
                    const link = document.createElement('a');
                    link.href = '/api/student-leaves/' + encodeURIComponent(row.id) + '/attachment';
                    link.className = 'btn btn-sm btn-outline-primary me-1';
                    link.textContent = 'Download evidence';
                    link.rel = 'noreferrer';
                    action.append(link);
                }
                if (row.state === 2) {
                    const button = document.createElement('button');
                    button.type = 'button'; button.className = 'btn btn-sm btn-outline-danger';
                    button.textContent = 'Withdraw';
                    button.addEventListener('click', () => cancelLeave(row, button));
                    action.append(button);
                }
                tr.append(action);
                tbody.append(tr);
            }
            if (!page.items?.length) {
                const row = document.createElement('tr'), cell = document.createElement('td');
                cell.colSpan = 6; cell.textContent = 'No leave applications.';
                row.append(cell); tbody.append(row);
            }
            el('myLeavePager').textContent = 'Showing ' + page.items.length + ' of ' + page.totalCount +
                ' applications. Contact the office for older records.';
        } catch (e) { notify(e.message, true); }
    }
    async function cancelLeave(row, button) {
        const reason = prompt('Why are you withdrawing this pending leave?', '');
        if (reason === null) return;
        if (!reason.trim() || reason.trim().length > 500)
            return notify('Provide a cancellation reason (up to 500 characters).', true);
        button.disabled = true;
        try {
            await api('/api/student-leaves/' + row.id + '/cancel', {
                method: 'POST', body: JSON.stringify({
                    reason: reason.trim(), rowVersion: row.rowVersion
                })
            });
            await load(); notify('Pending leave withdrawn.');
        } catch (e) { notify(e.message, true); button.disabled = false; }
    }
    async function submit(event) {
        event.preventDefault();
        if (!state.enrollment || state.saving) return;
        if (value('myLeaveTo') < value('myLeaveFrom'))
            return notify('End date must not be before start date.', true);
        if (!state.requestId) state.requestId = newId();
        state.saving = true; ready();
        const enrollmentRef = state.enrollment.reference;
        try {
            const file = el('myLeaveFile').files[0];
            if (file && !state.attachmentId) {
                if (file.size < 1 || file.size > 5242880)
                    throw new Error('Supporting file must be between 1 byte and 5 MB.');
                if (!state.uploadId) state.uploadId = newId();
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
                clientRequestId: state.requestId,
                studentEnrollmentReference: state.enrollment.reference,
                fromDate: value('myLeaveFrom'), toDate: value('myLeaveTo'),
                reason: value('myLeaveReason'), attachmentFileId: state.attachmentId
            }) });
            state.requestId = null; state.uploadId = null; state.attachmentId = null;
            el('myLeaveFile').value = ''; el('myLeaveFrom').value = '';
            el('myLeaveTo').value = ''; el('myLeaveReason').value = '';
            await load(); notify('Leave request submitted for institutional review.');
        } catch (e) { notify(e.message, true); }
        finally { state.saving = false; ready(); }
    }
    el('myLeaveStudent').addEventListener('change', selectStudent);
    el('myLeaveForm').addEventListener('submit', submit);
    for (const field of ['myLeaveFrom', 'myLeaveTo', 'myLeaveReason', 'myLeaveFile'])
        el(field).addEventListener('change', () => {
            state.requestId = null; state.uploadId = null; state.attachmentId = null;
        });
    loadStudents();
})();
