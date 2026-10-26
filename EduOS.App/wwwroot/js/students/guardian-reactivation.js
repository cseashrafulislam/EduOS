export function renderRevokedGuardianLinks({ $, api, state, render, notice }) {
    const box = $('guardianRevokedLinks');
    box.replaceChildren();
    const revoked = state.student?.revokedGuardians || [];
    if (!revoked.length) {
        const empty = document.createElement('p');
        empty.className = 'small text-muted';
        empty.textContent = 'No revoked guardian relationships.';
        box.append(empty);
        return;
    }
    for (const guardian of revoked) {
        const card = document.createElement('section');
        card.className = 'border rounded p-3 mb-2';
        const heading = document.createElement('div');
        heading.className = 'd-flex justify-content-between align-items-center gap-2';
        const name = document.createElement('strong');
        name.textContent = guardian.fullName + ' · ' + guardian.relationCode;
        const button = document.createElement('button');
        button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
        button.textContent = 'Review reactivation';
        button.disabled = state.busy;
        heading.append(name, button);
        const date = document.createElement('div');
        date.className = 'small text-muted';
        date.textContent = guardian.revokedAt ? 'Revoked: ' + new Date(guardian.revokedAt).toLocaleDateString() : 'Previously revoked';
        const form = document.createElement('form');
        form.className = 'row g-2 mt-2 d-none';
        const relationLabel = document.createElement('label');
        relationLabel.className = 'col-12 form-label';
        relationLabel.textContent = 'Relationship';
        const relation = document.createElement('select');
        relation.className = 'form-select'; relation.required = true;
        for (const code of [...new Set(['Father', 'Mother', 'LegalGuardian', 'Other', guardian.relationCode])]) {
            const option = document.createElement('option');
            option.value = code; option.textContent = code;
            relation.append(option);
        }
        relation.value = guardian.relationCode;
        relationLabel.append(relation); form.append(relationLabel);
        function checkbox(label) {
            const wrapper = document.createElement('label');
            wrapper.className = 'form-check col-md-6';
            const input = document.createElement('input');
            input.type = 'checkbox'; input.className = 'form-check-input';
            const text = document.createElement('span');
            text.className = 'form-check-label ms-1'; text.textContent = label;
            wrapper.append(input, text); form.append(wrapper);
            return input;
        }
        // Intentionally unchecked: revoked privileges must be explicitly granted again.
        const primary = checkbox('Primary guardian');
        const sms = checkbox('May receive SMS');
        const email = checkbox('May receive email');
        const pickup = checkbox('Authorized pickup');
        const financial = checkbox('Financial contact');
        const emergency = checkbox('Emergency contact');
        const reasonLabel = document.createElement('label');
        reasonLabel.className = 'col-12 form-label';
        reasonLabel.textContent = 'Reason for reactivation (10-500 characters)';
        const reason = document.createElement('textarea');
        reason.className = 'form-control'; reason.required = true;
        reason.minLength = 10; reason.maxLength = 500; reason.rows = 2;
        reasonLabel.append(reason); form.append(reasonLabel);
        const actions = document.createElement('div');
        actions.className = 'col-12 d-flex gap-2';
        const save = document.createElement('button');
        save.type = 'submit'; save.className = 'btn btn-primary btn-sm';
        save.textContent = 'Confirm reactivation';
        const cancel = document.createElement('button');
        cancel.type = 'button'; cancel.className = 'btn btn-outline-secondary btn-sm';
        cancel.textContent = 'Cancel';
        cancel.addEventListener('click', () => form.classList.add('d-none'));
        actions.append(save, cancel); form.append(actions);
        button.addEventListener('click', () => {
            if (state.busy) return;
            for (const open of box.querySelectorAll('form')) open.classList.add('d-none');
            form.classList.remove('d-none'); reason.focus();
        });
        form.addEventListener('submit', async event => {
            event.preventDefault();
            if (state.busy || !state.student || !form.reportValidity()) return;
            state.busy = true; save.disabled = true; button.disabled = true;
            try {
                const url = '/api/student-guardians/students/' +
                    encodeURIComponent(state.student.studentReference) + '/links/' +
                    encodeURIComponent(String(guardian.linkId)) + '/reactivate';
                const updated = await api(url, 'POST', {
                    relationship: {
                        relationCode: relation.value, isPrimary: primary.checked,
                        canReceiveSms: sms.checked, canReceiveEmail: email.checked,
                        isAuthorizedPickup: pickup.checked,
                        isFinancialContact: financial.checked, isEmergencyContact: emergency.checked,
                        rowVersion: guardian.rowVersion
                    },
                    reason: reason.value.trim()
                });
                state.busy = false;
                render(updated);
                notice('Guardian relationship reactivated. Only the newly authorized permissions apply.');
            } catch (error) {
                notice(error.message + ' Refresh the student before retrying a conflict.', true);
            } finally {
                state.busy = false; save.disabled = false; button.disabled = false;
            }
        });
        card.append(heading, date, form); box.append(card);
    }
}
