(() => {
    'use strict';
    const root = document.getElementById('transferRequestsApp');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { reference: null, eligibility: null, openRequest: false, requestId: null, requestFingerprint: null,
        busy: false, searchGeneration: 0, studentGeneration: 0, destinationGeneration: 0 };
    const transferForm = el('transferSubmitForm');
    const transferGuard = window.EduOSFormGuard?.create(transferForm);
    const destinationForm = el('destinationEnrollmentForm');
    const destinationGuard = window.EduOSFormGuard?.create(destinationForm);
    const identity = [document.body?.dataset?.authTenantId || '',
        document.body?.dataset?.authSessionReference || ''];
    const sameSession = () => identity[0] === (document.body?.dataset?.authTenantId || '') &&
        identity[1] === (document.body?.dataset?.authSessionReference || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
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
    const isAccepted = state => Number(state) === 4 || state === 'DestinationAccepted';
    const validGuid = text => /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(text);
    const isOpen = state => [2, 3, 4].includes(Number(state)) ||
        ['Submitted', 'SourceApproved', 'DestinationAccepted'].includes(String(state));
    function refreshButtons() {
        el('transferSubmit').disabled = !sameSession() || state.busy || !state.eligibility ||
            state.openRequest || !value('transferReason');
        el('transferRefresh').disabled = !sameSession() || state.busy || !state.reference;
    }
    async function search(event) {
        event.preventDefault();
        if (state.busy || !sameSession()) return;
        const query = value('transferSearch');
        if (query.length < 2) return notice('Enter at least two characters.', true);
        const generation = ++state.searchGeneration;
        const box = el('transferMatches');
        box.textContent = 'Searching...';
        try {
            const data = await api('/api/students?' + new URLSearchParams({ search: query, page: 1, pageSize: 20 }));
            if (generation !== state.searchGeneration || !sameSession()) return;
            box.replaceChildren();
            if (!data?.items?.length) { box.textContent = 'No matching students.'; return; }
            for (const student of data.items) {
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'list-group-item list-group-item-action';
                button.textContent = student.studentCode + ' — ' + student.fullName + ' (' + student.status + ')';
                button.addEventListener('click', () => selectStudent(student.reference));
                box.append(button);
            }
        } catch (error) {
            if (generation !== state.searchGeneration || !sameSession()) return;
            box.textContent = ''; notice(error.message, true);
        }
    }
    async function selectStudent(reference, internal = false) {
        if (!sameSession() || (state.busy && !internal)) return false;
        const changed = state.reference !== reference;
        if (changed && transferGuard?.isDirty() && !transferGuard.confirmDiscard()) return false;
        const generation = ++state.studentGeneration;
        if (changed) {
            transferForm.reset();
            transferGuard?.markClean();
            state.requestId = null;
            state.requestFingerprint = null;
        }
        state.reference = reference; state.eligibility = null;
        state.openRequest = false; refreshButtons();
        el('transferSelected').classList.add('d-none');
        try {
            const rows = await api('/api/student-transfer-requests/students/' +
                encodeURIComponent(reference) + '/requests');
            if (generation !== state.studentGeneration || state.reference !== reference || !sameSession()) {
                refreshButtons(); return false;
            }
            renderHistory(rows || []);
            try {
                const eligibility = await api('/api/student-transfer-requests/students/' +
                    encodeURIComponent(reference) + '/eligibility');
                if (generation !== state.studentGeneration || state.reference !== reference || !sameSession()) {
                    refreshButtons(); return false;
                }
                if (eligibility.studentReference !== reference) throw new Error('Student reference mismatch.');
                state.eligibility = eligibility;
                el('transferSelected').classList.remove('d-none');
                el('transferStudentName').textContent = eligibility.studentName + ' (' + eligibility.studentCode + ')';
                el('transferEnrollment').textContent = 'Current roll: ' + eligibility.rollNo +
                    ' · Campus ID: ' + eligibility.campusId;
                notice(state.openRequest ? 'Existing open transfer request. Review its status below.' :
                    'Student eligible to submit a transfer request.');
                refreshButtons();
                return true;
            } catch (error) {
                if (generation === state.studentGeneration && sameSession()) notice(error.message, true);
            }
        } catch (error) {
            if (generation !== state.studentGeneration || !sameSession()) return false;
            renderHistory([]); notice(error.message, true);
        }
        refreshButtons();
        return false;
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
            } else if (isAccepted(item.state)) {
                const exit = document.createElement('a');
                exit.className = 'btn btn-sm btn-outline-secondary me-1';
                exit.href = '/StudentLifecycle/Index?studentReference=' + encodeURIComponent(state.reference);
                exit.textContent = 'Final Exit & TC';
                action.append(exit);
                const confirmedMs = item.destinationEnrollmentConfirmedAt ?
                    new Date(item.destinationEnrollmentConfirmedAt).getTime() : NaN;
                const confirmationAge = Date.now() - confirmedMs;
                const freshConfirmation = item.destinationEnrollmentLinked &&
                    Number.isFinite(confirmedMs) && confirmationAge >= 0 &&
                    confirmationAge <= 10 * 60 * 1000;
                if (freshConfirmation) {
                    const complete = document.createElement('button');
                    complete.type = 'button';
                    complete.className = 'btn btn-sm btn-outline-success';
                    complete.textContent = 'Complete transfer';
                    complete.disabled = state.busy;
                    complete.addEventListener('click', () => completeTransfer(item));
                    action.append(complete);
                } else {
                    const message = !item.destinationEnrollmentLinked ?
                        ' Await destination enrollment link.' :
                        !Number.isFinite(confirmedMs) ?
                            ' Destination confirmation required or invalidated; ask destination to reconfirm.' :
                            ' Destination confirmation expired; ask destination to reconfirm.';
                    action.append(document.createTextNode(message));
                }
            } else action.textContent = '—';
            tr.append(action); body.append(tr);
        }
    }
    async function completeTransfer(item) {
        if (!sameSession() || state.busy || !confirm('Complete only after the source final Transfer exit and TC have been issued. Continue?')) return;
        state.busy = true; refreshButtons();
        let committed = false;
        try {
            await api('/api/student-transfer-requests/' + encodeURIComponent(item.reference) + '/complete', {
                method: 'POST', body: JSON.stringify({ rowVersion: item.rowVersion })
            });
            committed = true;
            const refreshed = await selectStudent(state.reference, true);
            if (sameSession()) notice(refreshed ? 'Transfer completed and its certificate linked.' :
                'Transfer completed, but status refresh failed. Reload before taking another action.', !refreshed);
        } catch (error) {
            if (sameSession()) notice(committed ? 'Transfer completed, but status refresh failed. Reload to verify.' :
                error.message, true);
        }
        finally { state.busy = false; refreshButtons(); }
    }
    async function submit(event) {
        event.preventDefault();
        if (!sameSession() || state.busy || !state.eligibility || state.openRequest) return;
        const destination = value('transferDestination');
        if (destination && (!Number.isSafeInteger(Number(destination)) || Number(destination) <= 0))
            return notice('Destination tenant ID must be a positive integer.', true);
        if (!value('transferReason')) return notice('Transfer reason is required.', true);
        if (!confirm('Submit this student transfer request for source-institution review?')) return;
        const selected = state.eligibility;
        const payload = {
            studentReference: selected.studentReference,
            enrollmentReference: selected.enrollmentReference, destinationTenantId: destination ? Number(destination) : null,
            reason: value('transferReason'), studentRowVersion: selected.studentRowVersion,
            enrollmentRowVersion: selected.enrollmentRowVersion
        };
        const fingerprint = JSON.stringify(payload);
        if (!state.requestId || state.requestFingerprint !== fingerprint) state.requestId = guid();
        state.requestFingerprint = fingerprint;
        payload.clientRequestId = state.requestId;
        state.busy = true; refreshButtons();
        transferForm.inert = true;
        let committed = false;
        try {
            await api('/api/student-transfer-requests', { method: 'POST', body: JSON.stringify(payload) });
            committed = true;
            state.requestId = null;
            state.requestFingerprint = null;
            transferForm.reset();
            transferGuard?.markClean();
            const refreshed = await selectStudent(selected.studentReference, true);
            if (sameSession()) notice(refreshed ? 'Transfer request submitted. Source approval is required.' :
                'Transfer request saved, but status refresh failed. Reload before submitting again.', !refreshed);
        } catch (error) {
            if (sameSession()) notice(committed ? 'Transfer request saved, but status refresh failed. Reload to verify.' :
                error.message + ' If the result is uncertain, retry unchanged details with the same request ID.', true);
        }
        finally { state.busy = false; transferForm.inert = false; refreshButtons(); }
    }
    async function review(item, approve) {
        if (!sameSession() || state.busy || !confirm((approve ? 'Approve' : 'Reject') +
            ' this source transfer request? This does not close enrollment.')) return;
        state.busy = true; refreshButtons();
        let committed = false;
        try {
            await api('/api/student-transfer-requests/' + encodeURIComponent(item.reference) + '/review',
                { method: 'POST', body: JSON.stringify({ approve, rowVersion: item.rowVersion }) });
            committed = true;
            const refreshed = await selectStudent(state.reference, true);
            if (sameSession()) notice(refreshed ? (approve ? 'Source approved. Destination acceptance is a separate workflow.' :
                'Transfer request rejected.') : 'Review recorded, but status refresh failed. Reload to verify.', !refreshed);
        } catch (error) {
            if (sameSession()) notice(committed ? 'Review recorded, but status refresh failed. Reload to verify.' : error.message, true);
        }
        finally { state.busy = false; refreshButtons(); }
    }
    el('transferSearchForm').addEventListener('submit', search);
    el('transferSubmitForm').addEventListener('submit', submit);
    el('transferRefresh').addEventListener('click', () => state.reference && selectStudent(state.reference));
    for (const id of ['transferReason', 'transferDestination']) {
        el(id).addEventListener('input', refreshButtons);
    }
    el('transferSearch').addEventListener('input', () => { ++state.searchGeneration; });
    const initial = new URLSearchParams(location.search).get('studentReference');
    if (initial && /^[0-9a-f-]{36}$/i.test(initial)) selectStudent(initial);
    let destinationBusy = false;
    let destinationSelected = null;
    function selectDestinationEnrollment(item) {
        if (destinationBusy || !sameSession()) return;
        if (destinationSelected?.reference === item.reference) {
            el('destinationStudentReference').focus();
            return;
        }
        if (destinationSelected?.reference !== item.reference && destinationGuard?.isDirty() &&
            !destinationGuard.confirmDiscard()) return;
        destinationSelected = item;
        el('destinationEnrollmentForm').classList.remove('d-none');
        el('destinationSelectedReference').textContent = item.reference;
        el('destinationStudentReference').value = '';
        el('destinationEnrollmentReference').value = '';
        destinationGuard?.markClean();
        el('destinationStudentReference').focus();
    }
    async function linkDestinationEnrollment(event) {
        event.preventDefault();
        if (!sameSession() || destinationBusy || !destinationSelected) return;
        const studentReference = value('destinationStudentReference');
        const enrollmentReference = value('destinationEnrollmentReference');
        if (!validGuid(studentReference) || !validGuid(enrollmentReference))
            return notice('Enter valid destination student and enrollment references.', true);
        if (!confirm('Link this existing active destination enrollment to the incoming transfer?')) return;
        destinationBusy = true;
        el('destinationEnrollmentSubmit').disabled = true;
        destinationForm.inert = true;
        ++state.destinationGeneration;
        let linked = false;
        try {
            await api('/api/student-transfer-requests/destination/' +
                encodeURIComponent(destinationSelected.reference) + '/enrollment', {
                method: 'POST', body: JSON.stringify({
                    studentReference, enrollmentReference, rowVersion: destinationSelected.rowVersion
                })
            });
            if (!sameSession()) return;
            destinationSelected = null;
            destinationForm.reset();
            destinationGuard?.markClean();
            el('destinationEnrollmentForm').classList.add('d-none');
            linked = true;
            notice('Destination enrollment linked. The source institution can now issue its final transfer exit and complete the transfer.');
        } catch (error) {
            if (sameSession()) notice(error.message + ' If the result is uncertain, refresh the inbox before retrying.', true);
        }
        finally {
            destinationBusy = false; destinationForm.inert = false;
            el('destinationEnrollmentSubmit').disabled = false;
            if (linked) await refreshDestination();
            else el('destinationRefresh').disabled = false;
        }
    }
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
        if (!sameSession()) return;
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
            } else if (isAccepted(item.state) && !item.destinationEnrollmentLinked) {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'btn btn-sm btn-outline-primary';
                button.textContent = 'Link enrollment';
                button.disabled = destinationBusy;
                button.addEventListener('click', () => selectDestinationEnrollment(item));
                cell.append(button);
            } else if (isAccepted(item.state) && item.destinationEnrollmentLinked) {
                const label = document.createElement('span');
                label.className = 'small me-2';
                label.textContent = item.destinationEnrollmentConfirmedAt ?
                    'Confirmed: ' + new Date(item.destinationEnrollmentConfirmedAt).toLocaleString() : 'Confirmation required or invalidated';
                cell.append(label);
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'btn btn-sm btn-outline-success';
                button.textContent = 'Reconfirm active enrollment';
                button.disabled = destinationBusy;
                button.addEventListener('click', () => confirmDestinationEnrollment(item));
                cell.append(button);
            } else cell.textContent = 'Acknowledged';
            row.append(cell);
            tbody.append(row);
        }
    }
    async function confirmDestinationEnrollment(item) {
        if (!sameSession() || destinationBusy || !confirm('Verify the linked student has an active current enrollment now? Confirmation expires after 10 minutes.')) return;
        destinationBusy = true;
        ++state.destinationGeneration;
        el('destinationRefresh').disabled = true;
        try {
            await api('/api/student-transfer-requests/destination/' +
                encodeURIComponent(item.reference) + '/confirm-enrollment', {
                method: 'POST', body: JSON.stringify({ rowVersion: item.rowVersion })
            });
            if (sameSession()) notice('Destination enrollment confirmed for 10 minutes. Source may now finalize after exit and TC.');
        } catch (error) {
            if (sameSession()) notice(error.message + ' Refresh the inbox before retrying an uncertain confirmation.', true);
        }
        finally {
            destinationBusy = false;
            el('destinationRefresh').disabled = false;
            await refreshDestination();
        }
    }
    async function refreshDestination() {
        if (destinationBusy || !sameSession()) return;
        const generation = ++state.destinationGeneration;
        el('destinationRefresh').disabled = true;
        try {
            const items = await api('/api/student-transfer-requests/destination/inbox');
            if (generation !== state.destinationGeneration || !sameSession()) return;
            renderDestination(Array.isArray(items) ? items : []);
        } catch (error) {
            if (generation === state.destinationGeneration && sameSession()) destinationMessage(error.message);
        } finally {
            if (generation === state.destinationGeneration) el('destinationRefresh').disabled = false;
        }
    }
    async function decideDestination(item, accept) {
        if (!sameSession() || destinationBusy || !confirm((accept ? 'Accept' : 'Reject') +
            ' the incoming transfer from institution #' + item.sourceTenantId +
            '? This does not create an enrollment or transfer personal records.')) return;
        destinationBusy = true;
        ++state.destinationGeneration;
        el('destinationRefresh').disabled = true;
        try {
            await api('/api/student-transfer-requests/destination/' +
                encodeURIComponent(item.reference) + '/decision', {
                method: 'POST',
                body: JSON.stringify({ accept, rowVersion: item.rowVersion })
            });
            if (sameSession()) notice(accept ? 'Destination handoff accepted. Enrollment and completion are separate steps.' :
                'Incoming handoff rejected.');
        } catch (error) {
            if (sameSession()) notice(error.message + ' Refresh the inbox before retrying an uncertain decision.', true);
        }
        finally {
            destinationBusy = false;
            el('destinationRefresh').disabled = false;
            await refreshDestination();
        }
    }
    el('destinationEnrollmentForm').addEventListener('submit', linkDestinationEnrollment);
    el('destinationRefresh').addEventListener('click', refreshDestination);
    refreshDestination();
})();
