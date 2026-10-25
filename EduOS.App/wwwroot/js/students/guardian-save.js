export function setupGuardianSave({ $, api, state, render, notice }) {
    const form = $('guardianCreateForm');
    form.addEventListener('input', () => { if (!state.busy) state.requestId = null; });
    form.addEventListener('change', () => { if (!state.busy) state.requestId = null; });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (state.busy || !state.student) return;
        if (!form.reportValidity()) return;
        state.requestId ||= crypto.randomUUID();
        const payload = {
            clientRequestId: state.requestId,
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
        state.busy = true;
        $('guardianCreate').disabled = true;
        $('guardianRefresh').disabled = true;
        try {
            const updated = await api('/api/student-guardians', 'POST', payload);
            state.requestId = null;
            form.reset();
            render(updated);
            notice('Guardian linked successfully.');
        } catch (error) {
            notice(error.message + ' Keep the form unchanged to retry safely.', true);
        } finally {
            state.busy = false;
            $('guardianCreate').disabled = !state.student;
            $('guardianRefresh').disabled = !state.student;
        }
    });
}
