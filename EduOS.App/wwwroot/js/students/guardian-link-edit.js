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
        const reference = document.createElement('div');
        reference.className = 'small text-muted';
        reference.textContent = 'Guardian reference: ' + guardian.guardianReference;
        const copy = document.createElement('button');
        copy.type = 'button'; copy.className = 'btn btn-sm btn-link p-0';
        copy.textContent = 'Copy reference for sibling';
        copy.addEventListener('click', async () => {
            try {
                await navigator.clipboard.writeText(guardian.guardianReference);
                notice('Guardian reference copied. Select the sibling and link the existing guardian.');
            } catch {
                notice('Select and copy the visible guardian reference.', true);
            }
        });
        summary.append(name, detail, reference, copy);
        const edit = document.createElement('button');
        edit.type = 'button'; edit.className = 'btn btn-sm btn-outline-primary';
        edit.textContent = 'Edit relationship';
        edit.disabled = state.busy;
        const unlink = document.createElement('button');
        unlink.type = 'button'; unlink.className = 'btn btn-sm btn-outline-danger';
        unlink.textContent = 'Unlink guardian';
        unlink.disabled = state.busy;
        const headingActions = document.createElement('div');
        headingActions.className = 'd-flex gap-2';
        const editContact = document.createElement('button');
        editContact.type = 'button'; editContact.className = 'btn btn-sm btn-outline-secondary';
        editContact.textContent = 'Edit contact details';
        editContact.disabled = state.busy;
        editContact.addEventListener('click', () => {
            document.dispatchEvent(new CustomEvent('guardian:contact-selected', {
                detail: { guardianReference: guardian.guardianReference }
            }));
            document.getElementById('guardianContactHeading')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        });
        headingActions.append(edit, editContact, unlink);
        heading.append(summary, headingActions);
        const permissions = document.createElement('div');
        permissions.className = 'small mt-2';
        permissions.textContent = [
            guardian.canReceiveSms ? 'SMS allowed' : 'SMS disabled',
            guardian.canReceiveEmail ? 'Email allowed' : 'Email disabled',
            guardian.isAuthorizedPickup ? 'Pickup authorized' : 'Pickup not authorized',
            guardian.isFinancialContact ? 'Financial contact' : 'Not financial contact',
            guardian.isEmergencyContact ? 'Emergency contact' : 'Not emergency contact'
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
        const financial = checkbox('Financial contact (institution-authorized)', guardian.isFinancialContact);
        const emergency = checkbox('Emergency contact (institution-authorized)', guardian.isEmergencyContact);
        const actions = document.createElement('div');
        actions.className = 'col-12 d-flex gap-2';
        const save = document.createElement('button');
        save.type = 'submit'; save.className = 'btn btn-primary btn-sm';
        save.textContent = 'Save relationship';
        const cancel = document.createElement('button');
        cancel.type = 'button'; cancel.className = 'btn btn-outline-secondary btn-sm';
        cancel.textContent = 'Cancel';
        actions.append(save, cancel); editor.append(actions);
        const resetEditor = () => {
            relation.value = guardian.relationCode;
            for (const [field, value] of [[primary, guardian.isPrimary], [sms, guardian.canReceiveSms],
                [email, guardian.canReceiveEmail], [pickup, guardian.isAuthorizedPickup],
                [financial, guardian.isFinancialContact], [emergency, guardian.isEmergencyContact]])
                field.checked = value;
            editor.classList.add('d-none'); editorEntry.guard?.markClean();
        };
        const editorEntry = state.registerInline?.('active', editor, resetEditor) || { guard: null };
        cancel.addEventListener('click', () => {
            if (!state.busy && (!editorEntry.guard?.isDirty() || editorEntry.guard.confirmDiscard())) resetEditor();
        });
        edit.addEventListener('click', () => {
            if (state.busy || state.selecting || state.requiresRefresh || !state.sameSession()) return;
            if (!state.discardInline('active', editorEntry)) return;
            editor.classList.remove('d-none');
        });
        editor.addEventListener('submit', async event => {
            event.preventDefault();
            if (state.busy || state.selecting || state.requiresRefresh || !state.sameSession() ||
                !state.student || !editor.reportValidity()) return;
            if (state.hasPendingInlineDraft?.(editorEntry))
                return notice('Finish or cancel the other unsaved guardian action first.', true);
            if (guardian.isPrimary && !primary.checked &&
                !state.student.guardians.some(x => x.linkId !== guardian.linkId && x.isPrimary)) {
                notice('Assign another primary guardian before removing the only primary.', true);
                return;
            }
            state.busy = true; editor.inert = true; save.disabled = true; edit.disabled = true;
            state.refreshActions();
            let updated;
            try {
                const reference = state.student.studentReference;
                const url = '/api/student-guardians/students/' + encodeURIComponent(reference) +
                    '/links/' + encodeURIComponent(String(guardian.linkId));
                updated = await api(url, 'PUT', {
                    relationCode: relation.value, isPrimary: primary.checked,
                    canReceiveSms: sms.checked, canReceiveEmail: email.checked,
                    isAuthorizedPickup: pickup.checked,
                    isFinancialContact: financial.checked, isEmergencyContact: emergency.checked,
                    rowVersion: guardian.rowVersion
                });
            } catch (error) {
                if (state.sameSession()) notice(error.message + ' Refresh to reconcile an uncertain relationship update.', true);
                return;
            } finally { if (!updated) { state.busy = false; editor.inert = false; save.disabled = false; edit.disabled = false; state.refreshActions(); } }
            if (!state.sameSession()) { state.busy = false; editor.inert = false; state.refreshActions(); return; }
            editorEntry.guard?.markClean();
            try {
                state.busy = false; state.requiresRefresh = false;
                render(updated);
                notice('Guardian relationship updated.');
            } catch (error) {
                state.requiresRefresh = true;
                notice('Relationship saved, but its display did not refresh. Refresh before another change.', true);
            } finally {
                state.busy = false; editor.inert = false; save.disabled = false; edit.disabled = false; state.refreshActions();
            }
        });
        const unlinkForm = document.createElement('form');
        unlinkForm.className = 'border rounded p-3 mt-3 d-none';
        const unlinkLabel = document.createElement('label');
        unlinkLabel.className = 'form-label';
        unlinkLabel.textContent = 'Reason for revoking this guardian relationship (10-500 characters)';
        const unlinkReason = document.createElement('textarea');
        unlinkReason.className = 'form-control'; unlinkReason.required = true;
        unlinkReason.minLength = 10; unlinkReason.maxLength = 500; unlinkReason.rows = 2;
        unlinkLabel.append(unlinkReason);
        const unlinkActions = document.createElement('div');
        unlinkActions.className = 'd-flex gap-2 mt-2';
        const confirmUnlink = document.createElement('button');
        confirmUnlink.type = 'submit'; confirmUnlink.className = 'btn btn-danger btn-sm';
        confirmUnlink.textContent = 'Confirm unlink';
        const cancelUnlink = document.createElement('button');
        cancelUnlink.type = 'button'; cancelUnlink.className = 'btn btn-outline-secondary btn-sm';
        cancelUnlink.textContent = 'Keep guardian';
        unlinkActions.append(confirmUnlink, cancelUnlink);
        unlinkForm.append(unlinkLabel, unlinkActions);
        const resetUnlink = () => {
            unlinkReason.value = '';
            unlinkForm.classList.add('d-none'); unlinkEntry.guard?.markClean();
        };
        const unlinkEntry = state.registerInline?.('active', unlinkForm, resetUnlink) || { guard: null };
        cancelUnlink.addEventListener('click', () => {
            if (!state.busy && (!unlinkEntry.guard?.isDirty() || unlinkEntry.guard.confirmDiscard())) resetUnlink();
        });
        unlink.addEventListener('click', () => {
            if (state.busy || state.selecting || state.requiresRefresh || !state.sameSession()) return;
            if (guardian.isPrimary && !guardians.some(x => x.linkId !== guardian.linkId && x.isPrimary)) {
                notice('Assign another primary guardian before unlinking this one.', true);
                return;
            }
            if (!state.discardInline('active', unlinkEntry)) return;
            unlinkForm.classList.remove('d-none');
            unlinkReason.focus();
        });
        unlinkForm.addEventListener('submit', async event => {
            event.preventDefault();
            if (state.busy || state.selecting || state.requiresRefresh || !state.sameSession() ||
                !state.student || !unlinkForm.reportValidity()) return;
            if (state.hasPendingInlineDraft?.(unlinkEntry))
                return notice('Finish or cancel the other unsaved guardian action first.', true);
            state.busy = true; unlinkForm.inert = true; confirmUnlink.disabled = true;
            unlink.disabled = true; edit.disabled = true; state.refreshActions();
            let updated;
            try {
                const url = '/api/student-guardians/students/' +
                    encodeURIComponent(state.student.studentReference) + '/links/' +
                    encodeURIComponent(String(guardian.linkId)) + '/unlink';
                updated = await api(url, 'POST', {
                    rowVersion: guardian.rowVersion, reason: unlinkReason.value.trim()
                });
            } catch (error) {
                if (state.sameSession()) notice(error.message + ' Verify the link before retrying an uncertain revocation.', true);
                return;
            } finally { if (!updated) { state.busy = false; unlinkForm.inert = false; confirmUnlink.disabled = false; unlink.disabled = false; edit.disabled = false; state.refreshActions(); } }
            if (!state.sameSession()) { state.busy = false; unlinkForm.inert = false; state.refreshActions(); return; }
            unlinkEntry.guard?.markClean();
            try {
                state.busy = false; state.requiresRefresh = false;
                render(updated);
                notice('Guardian relationship revoked. Portal access for this student has ended.');
            } catch (error) {
                state.requiresRefresh = true;
                notice('Guardian was revoked, but the display could not refresh. Refresh before another change.', true);
            } finally {
                state.busy = false; unlinkForm.inert = false; confirmUnlink.disabled = false;
                unlink.disabled = false; edit.disabled = false; state.refreshActions();
            }
        });
        card.append(heading, permissions, editor, unlinkForm);
        box.append(card);
    }
}
