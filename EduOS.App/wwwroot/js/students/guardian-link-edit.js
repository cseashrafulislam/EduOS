export function renderGuardianLinks({ $, api, state, render, notice }) {
    const box = $('guardianLinks');
    box.replaceChildren();
    const guardians = state.student?.guardians || [];
    if (!guardians.length) {
        const empty = document.createElement('p');
        empty.className = 'text-muted mb-0';
        empty.textContent = 'No guardians linked to this student.';
        box.append(empty);
        return;
    }
    for (const guardian of guardians) {
        const card = document.createElement('section');
        card.className = 'border rounded p-3 mb-2';
        const heading = document.createElement('div');
        heading.className = 'd-flex align-items-center justify-content-between gap-2 flex-wrap';
        const summary = document.createElement('div');
        const name = document.createElement('strong');
        name.textContent = guardian.fullName;
        const detail = document.createElement('div');
        detail.className = 'small text-muted';
        detail.textContent = [guardian.relationCode, guardian.isPrimary ? 'Primary guardian' : '',
            guardian.phone || '', guardian.email || ''].filter(Boolean).join(' · ');
        summary.append(name, detail);
        const edit = document.createElement('button');
        edit.type = 'button'; edit.className = 'btn btn-sm btn-outline-primary';
        edit.textContent = 'Edit relationship';
        edit.disabled = state.busy;
        heading.append(summary, edit);
        const permissions = document.createElement('div');
        permissions.className = 'small mt-2';
        permissions.textContent = [
            guardian.canReceiveSms ? 'SMS allowed' : 'SMS disabled',
            guardian.canReceiveEmail ? 'Email allowed' : 'Email disabled',
            guardian.isAuthorizedPickup ? 'Pickup authorized' : 'Pickup not authorized'
        ].join(' · ');
        const editor = document.createElement('form');
        editor.className = 'row g-2 mt-2 d-none';
        const relationWrap = document.createElement('div');
        relationWrap.className = 'col-md-5';
        const relationLabel = document.createElement('label');
        relationLabel.className = 'form-label';
        relationLabel.textContent = 'Relationship';
        const relation = document.createElement('select');
        relation.className = 'form-select';
        relation.required = true;
        for (const code of [...new Set(['Father', 'Mother', 'LegalGuardian', 'Other', guardian.relationCode])]) {
            const option = document.createElement('option');
            option.value = code; option.textContent = code;
            relation.append(option);
        }
        relation.value = guardian.relationCode;
        relationLabel.append(relation); relationWrap.append(relationLabel);
        editor.append(relationWrap);
        function checkbox(label, checked) {
            const wrapper = document.createElement('label');
            wrapper.className = 'form-check col-md-6';
            const input = document.createElement('input');
            input.type = 'checkbox'; input.className = 'form-check-input';
            input.checked = checked;
            const text = document.createElement('span');
            text.className = 'form-check-label ms-1'; text.textContent = label;
            wrapper.append(input, text); editor.append(wrapper);
            return input;
        }
        const primary = checkbox('Primary guardian', guardian.isPrimary);
        const sms = checkbox('May receive SMS', guardian.canReceiveSms);
        const email = checkbox('May receive email', guardian.canReceiveEmail);
        const pickup = checkbox('Authorized pickup', guardian.isAuthorizedPickup);
        const actions = document.createElement('div');
        actions.className = 'col-12 d-flex gap-2';
        const save = document.createElement('button');
        save.type = 'submit'; save.className = 'btn btn-primary btn-sm';
        save.textContent = 'Save relationship';
        const cancel = document.createElement('button');
        cancel.type = 'button'; cancel.className = 'btn btn-outline-secondary btn-sm';
        cancel.textContent = 'Cancel';
        cancel.addEventListener('click', () => editor.classList.add('d-none'));
        actions.append(save, cancel); editor.append(actions);
        edit.addEventListener('click', () => {
            for (const open of box.querySelectorAll('form')) open.classList.add('d-none');
            editor.classList.remove('d-none');
        });
        editor.addEventListener('submit', async event => {
            event.preventDefault();
            if (state.busy || !state.student) return;
            if (guardian.isPrimary && !primary.checked &&
                !state.student.guardians.some(x => x.linkId !== guardian.linkId && x.isPrimary)) {
                notice('Assign another primary guardian before removing the only primary.', true);
                return;
            }
            state.busy = true; save.disabled = true; edit.disabled = true;
            try {
                const reference = state.student.studentReference;
                const url = '/api/student-guardians/students/' + encodeURIComponent(reference) +
                    '/links/' + encodeURIComponent(String(guardian.linkId));
                const updated = await api(url, 'PUT', {
                    relationCode: relation.value, isPrimary: primary.checked,
                    canReceiveSms: sms.checked, canReceiveEmail: email.checked,
                    isAuthorizedPickup: pickup.checked, rowVersion: guardian.rowVersion
                });
                render(updated);
                notice('Guardian relationship updated.');
            } catch (error) {
                notice(error.message + ' Refresh the student before retrying a conflict.', true);
            } finally {
                state.busy = false; save.disabled = false; edit.disabled = false;
            }
        });
        card.append(heading, permissions, editor);
        box.append(card);
    }
}
