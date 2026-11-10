export function setupGuardianReuse({ $, api, state, render, notice }) {
    const form = $('guardianReuseForm');
    const guard = window.EduOSFormGuard?.create(form);
    form.__guardianGuard = guard;
    if (guard) state.guards.push(guard);
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (state.busy || state.selecting || state.requiresRefresh || !state.sameSession() ||
            !state.student || !form.reportValidity()) return;
        const guardianReference = $('guardianExistingReference').value.trim();
        if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(guardianReference)) {
            notice('Enter a valid guardian reference from a linked student.', true);
            return;
        }
        const payload = {
            studentReference: state.student.studentReference,
            guardianReference,
            studentRowVersion: state.student.studentRowVersion,
            relationCode: $('guardianExistingRelation').value,
            isPrimary: $('guardianExistingPrimary').checked,
            canReceiveSms: $('guardianExistingSms').checked,
            canReceiveEmail: $('guardianExistingEmail').checked,
            isAuthorizedPickup: $('guardianExistingPickup').checked,
            isFinancialContact: $('guardianExistingFinancial').checked,
            isEmergencyContact: $('guardianExistingEmergency').checked
        };
        state.busy = true;
        form.inert = true;
        state.refreshActions();
        let updated;
        try {
            updated = await api('/api/student-guardians/existing', 'POST', payload);
        } catch (error) {
            if (state.sameSession()) notice(error.message + ' Verify this student\'s links before an uncertain retry.', true);
            return;
        } finally {
            if (!updated) { state.busy = false; form.inert = false; state.refreshActions(); }
        }
        if (!state.sameSession()) { state.busy = false; form.inert = false; state.refreshActions(); return; }
        form.reset(); guard?.markClean();
        try {
            state.busy = false;
            state.requiresRefresh = false;
            render(updated);
            notice('Existing guardian linked to the selected student.');
        } catch (error) {
            state.requiresRefresh = true;
            notice('Guardian link was saved, but the relationship display could not refresh. Refresh before another change.', true);
        } finally { state.busy = false; form.inert = false; state.refreshActions(); }
    });
}
