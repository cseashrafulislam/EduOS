(() => {
    'use strict';
    const root = document.getElementById('studentLeavesApp');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value;
    const state = { enrollment: null, page: 1, pages: 1, requestId: null, saving: false };
    const val = id => el(id).value.trim();
    function info(text, failed = false) {
        const box = el('leaveNotice'); box.className = 'alert ' + (failed ? 'alert-danger' : 'alert-success');
        box.textContent = text;
    }
    async function api(url, options = {}) {
        const response = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store', ...options,
            headers: { Accept: 'application/json', ...(options.body ? { 'Content-Type': 'application/json' } : {}),
                ...(options.method && options.method !== 'GET' ? { RequestVerificationToken: token } : {}) }
        });
        const result = await response.json().catch(() => null);
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
    async function search(e) {
        e.preventDefault();
        if (val('leaveStudentTerm').length < 2) return info('Enter at least two characters.', true);
        try {
            const page = await api('/api/students?' + new URLSearchParams({
                search: val('leaveStudentTerm'), page: 1, pageSize: 20
            }));
            const list = el('leaveStudentResults'); list.replaceChildren();
            for (const item of page.items || []) {
                const b = document.createElement('button');
                b.type = 'button'; b.className = 'list-group-item list-group-item-action';
                b.textContent = item.fullName + ' — ' + item.studentCode;
                b.addEventListener('click', () => choose(item));
                list.append(b);
            }
            if (!page.items?.length) list.textContent = 'No matching students.';
        } catch (e) { info(e.message, true); }
    }
    async function choose(student) {
        state.enrollment = null; state.requestId = null; available();
        el('leaveStudentLabel').textContent = 'Checking ' + student.studentCode + '...';
        try {
            const current = await api('/api/academic-enrollments/students/' +
                encodeURIComponent(student.reference) + '/current');
            state.enrollment = current;
            el('leaveStudentLabel').textContent = student.fullName + ' / ' + student.studentCode +
                ' / Roll ' + (current.rollNo || '—');
            available(); state.page = 1; await load();
        } catch (e) { el('leaveStudentLabel').textContent = 'No active enrollment.'; info(e.message, true); }
    }
    async function load() {
        try {
            const query = new URLSearchParams({ page: state.page, pageSize: 20 });
            if (state.enrollment) query.set('enrollmentReference', state.enrollment.reference);
            if (val('leaveFilter')) query.set('state', val('leaveFilter'));
            const page = await api('/api/student-leaves?' + query);
            state.pages = Math.max(1, page.totalPages || 1);
            const tbody = el('leaveRows'); tbody.replaceChildren();
            for (const row of page.items || []) {
                const tr = document.createElement('tr');
                for (const value of [row.fromDate + ' – ' + row.toDate, row.reason,
                    ({ 1: 'Draft', 2: 'Submitted', 3: 'Approved', 4: 'Rejected', 5: 'Cancelled' })[row.state]]) {
                    const cell = document.createElement('td'); cell.textContent = value || ''; tr.append(cell);
                }
                const td = document.createElement('td');
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
                } else td.textContent = row.reviewNote || '—';
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
        } catch (e) { info(e.message, true); }
    }
    async function apply(e) {
        e.preventDefault();
        if (!state.enrollment || state.saving) return;
        if (val('leaveEnd') < val('leaveStart')) return info('Leave end date is before start.', true);
        if (!state.requestId) state.requestId = uuid();
        state.saving = true; available();
        try {
            await api('/api/student-leaves', { method: 'POST', body: JSON.stringify({
                clientRequestId: state.requestId, studentEnrollmentReference: state.enrollment.reference,
                fromDate: val('leaveStart'), toDate: val('leaveEnd'),
                reason: val('leaveReason'), attachmentFileId: null
            }) });
            state.requestId = null; el('leaveApplyForm').reset(); state.page = 1;
            await load(); info('Leave application submitted.');
        } catch (error) { info(error.message, true); }
        finally { state.saving = false; available(); }
    }
    async function cancelLeave(row, button) {
        const reason = prompt('Reason for cancelling this pending leave:', '');
        if (reason === null) return;
        if (!reason.trim() || reason.trim().length > 500)
            return info('Provide a reason (up to 500 characters).', true);
        button.disabled = true;
        try {
            await api('/api/student-leaves/' + row.id + '/cancel', {
                method: 'POST', body: JSON.stringify({
                    reason: reason.trim(), rowVersion: row.rowVersion
                })
            });
            await load(); info('Pending leave cancelled.');
        } catch (e) { info(e.message, true); button.disabled = false; }
    }
    async function review(row, decision, button) {
        const note = prompt('Decision note (optional):', '');
        if (note === null) return;
        if (note.length > 1000) return info('Review note exceeds limit.', true);
        button.disabled = true;
        try {
            await api('/api/student-leaves/' + row.id + '/review', {
                method: 'POST', body: JSON.stringify({
                    state: decision, rowVersion: row.rowVersion, reviewNote: note || null
                })
            });
            await load(); info('Leave reviewed.');
        } catch (e) { info(e.message, true); }
        finally { button.disabled = false; }
    }
    el('leaveStudentSearch').addEventListener('submit', search);
    el('leaveApplyForm').addEventListener('submit', apply);
    el('leaveRefresh').addEventListener('click', load);
    el('leaveFilter').addEventListener('change', () => { state.page = 1; load(); });
    el('leavePrevious').addEventListener('click', () => { if (state.page > 1) { state.page--; load(); } });
    el('leaveNext').addEventListener('click', () => { if (state.page < state.pages) { state.page++; load(); } });
    for (const id of ['leaveStart', 'leaveEnd', 'leaveReason'])
        el(id).addEventListener('input', () => { state.requestId = null; });
    load();
})();
