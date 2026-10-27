(() => {
    'use strict';
    const form = document.getElementById('studentOperationalStatusForm');
    if (!form) return;
    const target = document.getElementById('studentOperationalTarget');
    const reason = document.getElementById('studentOperationalReason');
    const status = document.getElementById('studentOperationalMessage');
    const submit = document.getElementById('studentOperationalSave');
    const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { reference: '', version: '', current: '', requestId: '', busy: false, sequence: 0 };
    function message(value, success) {
        status.textContent = value;
        status.className = 'small mt-2 ' + (success ? 'text-success' : 'text-danger');
    }
    function refreshButton() {
        submit.disabled = state.busy || !state.reference || !state.version ||
            target.value === state.current || !reason.value.trim();
    }
    document.addEventListener('student:opened', async event => {
        const reference = event.detail?.reference;
        if (!reference) return;
        state.reference = reference;
        state.version = '';
        state.current = '';
        state.requestId = '';
        reason.value = '';
        const sequence = ++state.sequence;
        refreshButton();
        message('Loading operational status…', true);
        try {
            const response = await fetch('/api/students/' + encodeURIComponent(reference) + '/operational-status', {
                credentials: 'same-origin', cache: 'no-store', referrerPolicy: 'no-referrer',
                headers: { Accept: 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (sequence !== state.sequence) return;
            if (!response.ok || !payload?.success || !payload.data)
                throw new Error(payload?.message || 'Operational status unavailable.');
            state.version = payload.data.rowVersion || '';
            state.current = payload.data.status || '';
            target.value = state.current === 'Active' ? 'Suspended' : 'Active';
            message('Current status: ' + state.current + '. Enrollment and historical records will not change.', true);
        } catch (error) {
            if (sequence === state.sequence) message(error.message || 'Unable to load status.', false);
        } finally { if (sequence === state.sequence) refreshButton(); }
    });
    form.addEventListener('input', () => { if (!state.busy) state.requestId = ''; refreshButton(); });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (state.busy || !state.reference || !state.version || !form.reportValidity() ||
            target.value === state.current) return;
        state.busy = true;
        refreshButton();
        state.requestId ||= crypto.randomUUID();
        const reference = state.reference;
        const sequence = state.sequence;
        message('Saving operational status…', true);
        try {
            const response = await fetch('/api/students/' + encodeURIComponent(reference) + '/operational-status', {
                method: 'PUT', credentials: 'same-origin', cache: 'no-store', referrerPolicy: 'no-referrer',
                headers: { 'Content-Type': 'application/json', Accept: 'application/json',
                    RequestVerificationToken: token },
                body: JSON.stringify({ clientRequestId: state.requestId, rowVersion: state.version,
                    targetStatus: target.value, reason: reason.value.trim() })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data)
                throw new Error(payload?.message || 'Status change rejected. Reload the student.');
            if (state.sequence !== sequence || state.reference !== reference) return;
            state.current = payload.data.status;
            state.version = payload.data.rowVersion || '';
            state.requestId = '';
            reason.value = '';
            target.value = state.current === 'Active' ? 'Suspended' : 'Active';
            message('Status updated to ' + state.current + '.', true);
            document.dispatchEvent(new CustomEvent('student:status-updated', { detail: { reference } }));
        } catch (error) {
            if (state.sequence === sequence) message(error.message || 'Status update failed.', false);
        } finally {
            state.busy = false;
            if (state.sequence === sequence) refreshButton();
        }
    });
    document.getElementById('studentModal')?.addEventListener('hidden.bs.modal', () => {
        ++state.sequence; state.reference = ''; state.version = ''; state.requestId = '';
        refreshButton();
    });
})();
