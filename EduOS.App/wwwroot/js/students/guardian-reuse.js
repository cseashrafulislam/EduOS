export function setupGuardianReuse({ $, api, state, render, notice }) {
    const form = $('guardianReuseForm');
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (state.busy || !state.student || !form.reportValidity()) return;
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
        $('guardianReuse').disabled = true;
        $('guardianCreate').disabled = true;
        $('guardianRefresh').disabled = true;
        try {
            const updated = await api('/api/student-guardians/existing', 'POST', payload);
            state.busy = false;
            form.reset();
            render(updated);
            notice('Existing guardian linked to the selected student.');
        } catch (error) {
            notice(error.message + ' Keep the form unchanged to retry safely.', true);
        } finally {
            state.busy = false;
            $('guardianReuse').disabled = !state.student;
            $('guardianCreate').disabled = !state.student;
            $('guardianRefresh').disabled = !state.student;
        }
    });
}
