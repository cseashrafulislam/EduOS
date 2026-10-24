(() => {
    'use strict';
    const root = document.getElementById('transferRequestsApp');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { reference: null, eligibility: null, openRequest: false, requestId: null, busy: false };
    const value = id => el(id).value.trim();
    const guid = () => crypto.randomUUID();
    function notice(message, error = false) {
        const box = el('transferNotice');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-info');
        box.textContent = message;
    }
    async function api(url, options = {}) {
        const response = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store', ...options,
            headers: {
                Accept: 'application/json',
                ...(options.body ? { 'Content-Type': 'application/json' } : {}),
                ...(options.method && options.method !== 'GET' ? { RequestVerificationToken: token } : {}),
                ...(options.headers || {})
            }
        });
        const json = await response.json().catch(() => null);
        if (!response.ok || json?.success !== true)
            throw new Error(json?.message || 'Request failed. Reload and retry.');
        return json.data;
    }
    const states = { 1: 'Draft', 2: 'Submitted', 3: 'Source approved', 4: 'Destination accepted',
        5: 'Rejected', 6: 'Completed', 7: 'Cancelled' };
    const isSubmitted = state => state === 2 || state === 'Submitted';
    const isOpen = state => [2, 3, 4].includes(Number(state)) ||
        ['Submitted', 'SourceApproved', 'DestinationAccepted'].includes(String(state));
    function refreshButtons() {
        el('transferSubmit').disabled = state.busy || !state.eligibility ||
            state.openRequest || !value('transferReason');
        el('transferRefresh').disabled = state.busy || !state.reference;
    }
    async function search(event) {
        event.preventDefault();
        const query = value('transferSearch');
        if (query.length < 2) return notice('Enter at least two characters.', true);
        const box = el('transferMatches');
        box.textContent = 'Searching...';
        try {
            const data = await api('/api/students?' + new URLSearchParams({ search: query, page: 1, pageSize: 20 }));
            box.replaceChildren();
            if (!data?.items?.length) { box.textContent = 'No matching students.'; return; }
            for (const student of data.items) {
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'list-group-item list-group-item-action';
                button.textContent = student.studentCode + ' — ' + student.fullName + ' (' + student.status + ')';
                button.addEventListener('click', () => selectStudent(student.reference));
                box.append(button);
            }
        } catch (error) { box.textContent = ''; notice(error.message, true); }
    }
    async function selectStudent(reference) {
        state.reference = reference; state.eligibility = null; state.requestId = null;
        state.openRequest = false; refreshButtons();
        el('transferSelected').classList.add('d-none');
        try {
            const rows = await api('/api/student-transfer-requests/students/' +
                encodeURIComponent(reference) + '/requests');
            renderHistory(rows || []);
            try {
                const eligibility = await api('/api/student-transfer-requests/students/' +
                    encodeURIComponent(reference) + '/eligibility');
                if (eligibility.studentReference !== reference) throw new Error('Student reference mismatch.');
                state.eligibility = eligibility;
                el('transferSelected').classList.remove('d-none');
                el('transferStudentName').textContent = eligibility.studentName + ' (' + eligibility.studentCode + ')';
                el('transferEnrollment').textContent = 'Current roll: ' + eligibility.rollNo +
                    ' · Campus ID: ' + eligibility.campusId;
                notice(state.openRequest ? 'Existing open transfer request. Review its status below.' :
                    'Student eligible to submit a transfer request.');
            } catch (error) { notice(error.message, true); }
        } catch (error) { renderHistory([]); notice(error.message, true); }
        refreshButtons();
    }
    function renderHistory(items) {
        const body = el('transferHistory');
        body.replaceChildren();
        state.openRequest = items.some(x => isOpen(x.state));
        if (!items.length) {
            const tr = document.createElement('tr'), td = document.createElement('td');
            td.colSpan = 5; td.textContent = 'No transfer requests recorded.';
            tr.append(td); body.append(tr); return;
        }
        for (const item of items) {
            const tr = document.createElement('tr');
            for (const value of [item.requestedAt, item.destinationTenantId ?? 'Unspecified',
                states[item.state] || item.state, item.reason || '—']) {
                const td = document.createElement('td');
                td.textContent = String(value ?? ''); tr.append(td);
            }
            const action = document.createElement('td');
            if (isSubmitted(item.state)) {
                for (const [approve, label] of [[true, 'Approve'], [false, 'Reject']]) {
                    const button = document.createElement('button');
                    button.type = 'button'; button.className = 'btn btn-sm me-1 ' +
                        (approve ? 'btn-outline-success' : 'btn-outline-danger');
                    button.textContent = label;
                    button.disabled = state.busy;
                    button.addEventListener('click', () => review(item, approve));
                    action.append(button);
                }
            } else action.textContent = '—';
            tr.append(action); body.append(tr);
        }
    }
    async function submit(event) {
        event.preventDefault();
        if (state.busy || !state.eligibility || state.openRequest) return;
        const destination = value('transferDestination');
        if (destination && (!Number.isSafeInteger(Number(destination)) || Number(destination) <= 0))
            return notice('Destination tenant ID must be a positive integer.', true);
        if (!value('transferReason')) return notice('Transfer reason is required.', true);
        if (!confirm('Submit this student transfer request for source-institution review?')) return;
        const selected = state.eligibility;
        if (!state.requestId) state.requestId = guid();
        const payload = {
            clientRequestId: state.requestId, studentReference: selected.studentReference,
            enrollmentReference: selected.enrollmentReference, destinationTenantId: destination ? Number(destination) : null,
            reason: value('transferReason'), studentRowVersion: selected.studentRowVersion,
            enrollmentRowVersion: selected.enrollmentRowVersion
        };
        state.busy = true; refreshButtons();
        try {
            await api('/api/student-transfer-requests', { method: 'POST', body: JSON.stringify(payload) });
            state.requestId = null;
            await selectStudent(selected.studentReference);
            notice('Transfer request submitted. Source approval is required.');
        } catch (error) { notice(error.message, true); }
        finally { state.busy = false; refreshButtons(); }
    }
    async function review(item, approve) {
        if (state.busy || !confirm((approve ? 'Approve' : 'Reject') +
            ' this source transfer request? This does not close enrollment.')) return;
        state.busy = true; refreshButtons();
        try {
            await api('/api/student-transfer-requests/' + encodeURIComponent(item.reference) + '/review',
                { method: 'POST', body: JSON.stringify({ approve, rowVersion: item.rowVersion }) });
            await selectStudent(state.reference);
            notice(approve ? 'Source approved. Destination acceptance is a separate workflow.' : 'Transfer request rejected.');
        } catch (error) { notice(error.message, true); }
        finally { state.busy = false; refreshButtons(); }
    }
    el('transferSearchForm').addEventListener('submit', search);
    el('transferSubmitForm').addEventListener('submit', submit);
    el('transferRefresh').addEventListener('click', () => state.reference && selectStudent(state.reference));
    for (const id of ['transferReason', 'transferDestination']) {
        el(id).addEventListener('input', () => { state.requestId = null; refreshButtons(); });
    }
    const initial = new URLSearchParams(location.search).get('studentReference');
    if (initial && /^[0-9a-f-]{36}$/i.test(initial)) selectStudent(initial);
    let destinationBusy = false;
    function destinationMessage(message) {
        const tbody = el('destinationRows');
        const row = document.createElement('tr');
        const cell = document.createElement('td');
        cell.colSpan = 5;
        cell.textContent = message;
        row.append(cell);
        tbody.replaceChildren(row);
    }
    function renderDestination(items) {
        const tbody = el('destinationRows');
        tbody.replaceChildren();
        if (!items.length) return destinationMessage('No incoming source-approved transfers.');
        for (const item of items) {
            const row = document.createElement('tr');
            for (const text of [item.reference, item.sourceTenantId,
                item.requestedAt, states[item.state] || item.state]) {
                const cell = document.createElement('td');
                cell.textContent = String(text ?? '');
                row.append(cell);
            }
            const cell = document.createElement('td');
            if (Number(item.state) === 3 || item.state === 'SourceApproved') {
                for (const [accept, label] of [[true, 'Accept'], [false, 'Reject']]) {
                    const button = document.createElement('button');
                    button.type = 'button';
                    button.className = 'btn btn-sm me-1 ' +
                        (accept ? 'btn-outline-success' : 'btn-outline-danger');
                    button.textContent = label;
                    button.disabled = destinationBusy;
                    button.addEventListener('click', () => decideDestination(item, accept));
                    cell.append(button);
                }
            } else cell.textContent = 'Acknowledged';
            row.append(cell);
            tbody.append(row);
        }
    }
    async function refreshDestination() {
        if (destinationBusy) return;
        el('destinationRefresh').disabled = true;
        try {
            const items = await api('/api/student-transfer-requests/destination/inbox');
            renderDestination(Array.isArray(items) ? items : []);
        } catch (error) { destinationMessage(error.message); }
        finally { el('destinationRefresh').disabled = false; }
    }
    async function decideDestination(item, accept) {
        if (destinationBusy || !confirm((accept ? 'Accept' : 'Reject') +
            ' the incoming transfer from institution #' + item.sourceTenantId +
            '? This does not create an enrollment or transfer personal records.')) return;
        destinationBusy = true;
        el('destinationRefresh').disabled = true;
        try {
            await api('/api/student-transfer-requests/destination/' +
                encodeURIComponent(item.reference) + '/decision', {
                method: 'POST',
                body: JSON.stringify({ accept, rowVersion: item.rowVersion })
            });
            notice(accept ? 'Destination handoff accepted. Enrollment and completion are separate steps.' :
                'Incoming handoff rejected.');
        } catch (error) { notice(error.message, true); }
        finally {
            destinationBusy = false;
            el('destinationRefresh').disabled = false;
            await refreshDestination();
        }
    }
    el('destinationRefresh').addEventListener('click', refreshDestination);
    refreshDestination();
})();
