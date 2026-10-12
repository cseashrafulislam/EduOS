export function setupGuardianSave({ $, api, state, render, notice }) {
$('guardianCreateForm').onsubmit = async event => {
event.preventDefault();
if (!state.student) return;
state.requestId ||= crypto.randomUUID();
const payload = {
clientRequestId: state.requestId,
studentReference: state.student.studentReference,
studentRowVersion: state.student.studentRowVersion,
fullName: $('guardianName').value.trim(),
relationCode: $('guardianRelation').value,
isPrimary: $('guardianPrimary').checked,
canReceiveSms: true, canReceiveEmail: true, isAuthorizedPickup: false
};
try {
render(await api('/api/student-guardians', 'POST', payload));
state.requestId = null;
$('guardianCreateForm').reset();
notice('Guardian linked.');
} catch (error) { notice(error.message); }
};
}
