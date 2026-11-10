export function setupGuardianContact({ $, api, state, render, notice }) {
    const form = $('guardianContactForm');
    const guard = window.EduOSFormGuard?.create(form);
    if (guard) state.guards.push(guard);
    const save = $('guardianContactSave');
    const label = $('guardianContactSelected');
    const fields = {
        phone: $('guardianContactPhone'), email: $('guardianContactEmail'),
        occupation: $('guardianContactOccupation'), address: $('guardianContactAddress'),
        reason: $('guardianContactReason')
    };
    const selected = { student: '', guardian: '', version: '', requestId: '',
        fingerprint: '', busy: false, sequence: 0 };
    state.updateContactAction = () => {
        save.disabled = selected.busy || state.busy || state.selecting || state.requiresRefresh ||
            !state.sameSession() || !selected.guardian || form.classList.contains('d-none');
    };
    function clear() {
        selected.sequence++;
        selected.student = ''; selected.guardian = ''; selected.version = '';
        selected.requestId = ''; selected.fingerprint = '';
        form.reset(); guard?.markClean(); form.classList.add('d-none'); save.disabled = true;
        form.inert = false;
        label.textContent = 'Choose Edit contact details on a linked guardian.';
    }
    const endpoint = (student, guardian) => '/api/student-guardians/students/' +
        encodeURIComponent(student) + '/guardians/' + encodeURIComponent(guardian) + '/contact';
    function uuid() {
        return crypto.randomUUID ? crypto.randomUUID() :
            'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
                const r = crypto.getRandomValues(new Uint8Array(1))[0] & 15;
                return (c === 'x' ? r : (r & 3 | 8)).toString(16);
            });
    }
    async function open(guardianReference) {
        if (!state.student || state.busy || state.selecting || selected.busy ||
            state.requiresRefresh || !state.sameSession()) return;
        if (guard?.isDirty() && !guard.confirmDiscard()) return;
        const studentReference = state.student.studentReference;
        const seq = ++selected.sequence;
        selected.student = studentReference;
        selected.guardian = guardianReference;
        selected.version = ''; selected.requestId = ''; selected.fingerprint = '';
        form.classList.add('d-none'); save.disabled = true; form.inert = true;
        label.textContent = 'Loading guardian contact details...';
        try {
            const profile = await api(endpoint(studentReference, guardianReference));
            if (seq !== selected.sequence || state.student?.studentReference !== studentReference || !state.sameSession()) return;
            for (const key of ['phone', 'email', 'occupation', 'address']) fields[key].value = profile[key] || '';
            fields.reason.value = '';
            selected.version = profile.rowVersion || '';
            label.textContent = 'Editing ' + profile.fullName + ' — changes also affect siblings linked to this guardian in this institution.';
            form.classList.remove('d-none'); form.inert = false;
            guard?.markClean(); save.disabled = false;
        } catch (error) {
            if (seq === selected.sequence && state.sameSession()) {
                label.textContent = 'Unable to load guardian contact.';
                notice(error.message, true);
            }
        } finally { if (seq === selected.sequence) form.inert = false; }
    }
    document.addEventListener('guardian:contact-selected', event => {
        if (event.detail?.guardianReference) void open(event.detail.guardianReference);
    });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (selected.busy || state.busy || state.selecting || state.requiresRefresh || !state.sameSession() ||
            !form.reportValidity() || !selected.student || !selected.guardian) return;
        if (state.student?.studentReference !== selected.student) { clear(); return; }
        const student = selected.student, guardian = selected.guardian, seq = selected.sequence;
        const payload = {
            rowVersion: selected.version,
            phone: fields.phone.value.trim() || null, email: fields.email.value.trim() || null,
            occupation: fields.occupation.value.trim() || null,
            address: fields.address.value.trim() || null, reason: fields.reason.value.trim()
        };
        const fingerprint = JSON.stringify({ student, guardian, ...payload });
        if (!selected.requestId || selected.fingerprint !== fingerprint) selected.requestId = uuid();
        selected.fingerprint = fingerprint;
        payload.clientRequestId = selected.requestId;
        selected.busy = true; state.busy = true; save.disabled = true;
        form.inert = true; state.refreshActions();
        let result;
        try {
            result = await api(endpoint(student, guardian), 'PUT', payload);
        } catch (error) {
            if (seq === selected.sequence && state.sameSession())
                notice(error.message + ' Keep unchanged details for an idempotent retry; refresh after a conflict.', true);
            return;
        } finally {
            if (!result) {
                selected.busy = false; state.busy = false; form.inert = false;
                save.disabled = !selected.student || !selected.guardian || !state.sameSession();
                state.refreshActions();
            }
        }
        if (seq !== selected.sequence || state.student?.studentReference !== student || !state.sameSession()) {
            selected.busy = false; state.busy = false; form.inert = false; state.refreshActions(); return;
        }
        selected.version = result.rowVersion || '';
        selected.requestId = ''; selected.fingerprint = '';
        fields.reason.value = ''; guard?.markClean();
        try {
            const updated = await api('/api/student-guardians/students/' + encodeURIComponent(student));
            if (seq !== selected.sequence || state.student?.studentReference !== student || !state.sameSession()) return;
            if (updated?.studentReference !== student) throw Error('Student readback mismatch.');
            state.busy = false;
            state.requiresRefresh = false;
            render(updated);
            notice('Guardian contact updated. Linked siblings share these institution-local details.');
        } catch (error) {
            if (seq === selected.sequence && state.sameSession()) {
                state.requiresRefresh = true;
                notice('Guardian contact was saved, but the linked list could not refresh. Refresh before another change.', true);
            }
        } finally {
            selected.busy = false; state.busy = false; form.inert = false;
            save.disabled = !selected.student || !selected.guardian || state.requiresRefresh || !state.sameSession();
            state.refreshActions();
        }
    });
    document.addEventListener('guardian:student-selected', event => {
        if (event.detail?.studentReference !== selected.student) clear();
    });
    clear();
}
