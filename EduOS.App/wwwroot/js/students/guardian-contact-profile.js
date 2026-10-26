export function setupGuardianContact({ $, api, state, render, notice }) {
    const form = $('guardianContactForm');
    const save = $('guardianContactSave');
    const label = $('guardianContactSelected');
    const fields = {
        phone: $('guardianContactPhone'), email: $('guardianContactEmail'),
        occupation: $('guardianContactOccupation'), address: $('guardianContactAddress'),
        reason: $('guardianContactReason')
    };
    const selected = { student: '', guardian: '', version: '', requestId: '', busy: false, sequence: 0 };
    function clear() {
        selected.sequence++;
        selected.student = ''; selected.guardian = ''; selected.version = ''; selected.requestId = '';
        form.reset(); form.classList.add('d-none'); save.disabled = true;
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
        if (!state.student || state.busy || selected.busy) return;
        const studentReference = state.student.studentReference;
        const seq = ++selected.sequence;
        selected.student = studentReference;
        selected.guardian = guardianReference;
        selected.version = ''; selected.requestId = '';
        form.classList.add('d-none'); save.disabled = true;
        label.textContent = 'Loading guardian contact details...';
        try {
            const profile = await api(endpoint(studentReference, guardianReference));
            if (seq !== selected.sequence || state.student?.studentReference !== studentReference) return;
            for (const key of ['phone', 'email', 'occupation', 'address']) fields[key].value = profile[key] || '';
            fields.reason.value = '';
            selected.version = profile.rowVersion || '';
            label.textContent = 'Editing ' + profile.fullName + ' — changes also affect siblings linked to this guardian in this institution.';
            form.classList.remove('d-none'); save.disabled = false;
        } catch (error) {
            if (seq === selected.sequence) {
                label.textContent = 'Unable to load guardian contact.';
                notice(error.message, true);
            }
        }
    }
    document.addEventListener('guardian:contact-selected', event => {
        if (event.detail?.guardianReference) void open(event.detail.guardianReference);
    });
    form.addEventListener('input', () => { if (!selected.busy) selected.requestId = ''; });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (selected.busy || state.busy || !form.reportValidity() || !selected.student || !selected.guardian) return;
        if (state.student?.studentReference !== selected.student) { clear(); return; }
        selected.busy = true; save.disabled = true;
        selected.requestId ||= uuid();
        const student = selected.student, guardian = selected.guardian, seq = selected.sequence;
        const payload = {
            clientRequestId: selected.requestId, rowVersion: selected.version,
            phone: fields.phone.value.trim() || null, email: fields.email.value.trim() || null,
            occupation: fields.occupation.value.trim() || null,
            address: fields.address.value.trim() || null, reason: fields.reason.value.trim()
        };
        try {
            const result = await api(endpoint(student, guardian), 'PUT', payload);
            if (seq !== selected.sequence || state.student?.studentReference !== student) return;
            selected.version = result.rowVersion || '';
            selected.requestId = ''; fields.reason.value = '';
            // Refresh relationship readback so the card shows the updated phone/email.
            const updated = await api('/api/student-guardians/students/' + encodeURIComponent(student));
            if (seq !== selected.sequence || state.student?.studentReference !== student) return;
            render(updated);
            notice('Guardian contact updated. Linked siblings share these institution-local details.');
        } catch (error) {
            if (seq === selected.sequence) notice(error.message + ' Verify current details before retrying.', true);
        } finally {
            selected.busy = false;
            save.disabled = !selected.student || !selected.guardian;
        }
    });
    document.addEventListener('guardian:student-selected', event => {
        if (event.detail?.studentReference !== selected.student) clear();
    });
    clear();
}
