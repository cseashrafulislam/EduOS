export function setupGuardianSave({ $, api, state, render, notice }) {
    const form = $('guardianCreateForm');
    const guard = window.EduOSFormGuard?.create(form);
    form.__guardianGuard = guard;
    if (guard) state.guards.push(guard);
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (state.busy || state.selecting || state.requiresRefresh || !state.sameSession() || !state.student) return;
        if (state.hasPendingInlineDraft?.()) return notice('Finish or cancel the unsaved guardian relationship edit first.', true);
        if (!form.reportValidity()) return;
        if (!$('guardianCreateConfirm').checked) return notice('Confirm creation of a new guardian for the selected student.', true);
        const payload = {
            studentReference: state.student.studentReference,
            studentRowVersion: state.student.studentRowVersion,
            fullName: $('guardianName').value.trim(),
            phone: $('guardianPhone').value.trim() || null,
            email: $('guardianEmail').value.trim() || null,
            occupation: $('guardianOccupation').value.trim() || null,
            address: $('guardianAddress').value.trim() || null,
            relationCode: $('guardianRelation').value,
            isPrimary: $('guardianPrimary').checked,
            canReceiveSms: $('guardianSms').checked,
            canReceiveEmail: $('guardianEmailAllowed').checked,
            isAuthorizedPickup: $('guardianPickup').checked,
            isFinancialContact: $('guardianFinancial').checked,
            isEmergencyContact: $('guardianEmergency').checked
        };
        const fingerprint = JSON.stringify(payload);
        if (!state.requestId || state.requestFingerprint !== fingerprint) state.requestId = crypto.randomUUID();
        state.requestFingerprint = fingerprint;
        payload.clientRequestId = state.requestId;
        state.busy = true;
        form.inert = true;
        state.refreshActions();
        let updated;
        try {
            updated = await api('/api/student-guardians', 'POST', payload);
        } catch (error) {
            if (state.sameSession()) notice(error.message + ' Keep unchanged details to retry with the same request ID.', true);
            return;
        } finally {
            if (!updated) { state.busy = false; form.inert = false; state.refreshActions(); }
        }
        if (!state.sameSession()) { state.busy = false; form.inert = false; state.refreshActions(); return; }
        state.requestId = null; state.requestFingerprint = null;
        form.reset(); guard?.markClean();
        try {
            // Render must see an unlocked workspace so newly created link actions are enabled.
            state.busy = false;
            state.requiresRefresh = false;
            render(updated);
            notice('Guardian linked successfully.');
        } catch (error) {
            state.requiresRefresh = true;
            notice('Guardian was saved, but the relationship display could not refresh. Refresh before another change.', true);
        } finally { state.busy = false; form.inert = false; state.refreshActions(); }
    });
}
