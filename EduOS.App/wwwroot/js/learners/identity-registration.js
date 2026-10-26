(() => {
    'use strict';
    const form = document.getElementById('learnerIdentityForm');
    if (!form) return;

    const student = document.getElementById('identityStudentReference');
    const search = document.getElementById('identityStudentSearch');
    const result = document.getElementById('identityResult');
    const submit = document.getElementById('identitySubmit');
    const previous = document.getElementById('identityPrev');
    const next = document.getElementById('identityNext');
    const pageText = document.getElementById('identityPage');
    const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { page: 1, totalPages: 1, loading: false, saving: false };

    function inform(message, ok) {
        result.className = 'small ' + (ok ? 'text-success' : 'text-danger');
        result.textContent = message;
    }

    async function loadStudents() {
        if (state.loading) return;
        state.loading = true;
        document.getElementById('identitySearch').disabled = true;
        previous.disabled = true;
        next.disabled = true;
        student.disabled = true;
        student.replaceChildren(new Option('Loading...', ''));
        const query = new URLSearchParams({ page: String(state.page), pageSize: '20', status: 'Active' });
        if (search.value.trim()) query.set('search', search.value.trim());
        try {
            const response = await fetch('/api/students?' + query, {
                credentials: 'same-origin', cache: 'no-store', referrerPolicy: 'no-referrer',
                headers: { Accept: 'application/json' }
            });
            const data = await response.json().catch(() => null);
            if (!response.ok || !data?.success || !Array.isArray(data.data?.items))
                throw new Error('Unable to load active students.');
            const items = data.data.items.filter(x => x.status === 'Active' && x.reference);
            student.replaceChildren(new Option(items.length ? 'Select a student' : 'No active students found', ''));
            for (const item of items) {
                const label = [item.studentCode, item.fullName, item.academicBatchName].filter(Boolean).join(' · ');
                student.append(new Option(label, item.reference));
            }
            state.totalPages = Math.max(1, Number(data.data.totalPages) || 1);
            pageText.textContent = `Page ${state.page} of ${state.totalPages}`;
            inform('', true);
        } catch (error) {
            student.replaceChildren(new Option('Please retry student search', ''));
            inform(error.message || 'Student search failed.', false);
        } finally {
            state.loading = false;
            student.disabled = false;
            document.getElementById('identitySearch').disabled = false;
            previous.disabled = state.page <= 1;
            next.disabled = state.page >= state.totalPages;
        }
    }

    async function save(event) {
        event.preventDefault();
        if (state.saving || state.loading) return;
        const identifier = document.getElementById('identityValue');
        if (!student.value || !identifier.value.trim()) {
            inform('Select an active student and enter a valid identifier.', false);
            return;
        }
        state.saving = true;
        submit.disabled = true;
        inform('Processing the identity request...', true);
        const payload = {
            studentReference: student.value,
            identifierType: Number(document.getElementById('identityType').value),
            identifierValue: identifier.value.trim(),
            purpose: Number(document.getElementById('identityPurpose').value),
            requestedScopes: 1 + (document.getElementById('identityMembership').checked ? 2 : 0) +
                (document.getElementById('identityAcademic').checked ? 4 : 0)
        };
        try {
            const response = await fetch('/api/learner-identities/register-or-request', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store', referrerPolicy: 'no-referrer',
                headers: { 'Content-Type': 'application/json', Accept: 'application/json', RequestVerificationToken: token },
                body: JSON.stringify(payload)
            });
            const data = await response.json().catch(() => null);
            if (!response.ok || !data?.success) throw new Error(data?.message || 'Identity registration failed.');
            if (data.data?.consentRequired)
                inform('Consent request created. The learner or guardian must review it in the Student Portal.', true);
            else if (data.data?.state === 'AlreadyLinked')
                inform('This student is already linked to this identity.', true);
            else
                inform('Identity registration completed successfully.', true);
        } catch (error) {
            inform(error.message || 'Unable to save. Check the record before retrying.', false);
        } finally {
            identifier.value = '';
            state.saving = false;
            submit.disabled = false;
        }
    }

    document.getElementById('identitySearch').addEventListener('click', () => { state.page = 1; loadStudents(); });
    search.addEventListener('keydown', event => {
        if (event.key === 'Enter') { event.preventDefault(); state.page = 1; loadStudents(); }
    });
    previous.addEventListener('click', () => { if (state.page > 1) { state.page--; loadStudents(); } });
    next.addEventListener('click', () => { if (state.page < state.totalPages) { state.page++; loadStudents(); } });
    form.addEventListener('submit', save);
    loadStudents();
})();
