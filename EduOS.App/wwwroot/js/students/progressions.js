(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const val = id => el(id)?.value?.trim() || '';
    const state = { options: null, requestId: null, saving: false };
    const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

    document.addEventListener('DOMContentLoaded', () => {
        el('promotionLookup')?.addEventListener('submit', loadStudent);
        el('promotionForm')?.addEventListener('submit', submitProgression);
        const reference = new URLSearchParams(location.search).get('studentReference');
        if (reference && guid.test(reference)) {
            el('promotionStudentReference').value = reference;
            loadStudent();
        }
    }, { once: true });

    function notice(message, success = false) {
        const box = el('promotionAlert');
        box.className = 'alert alert-' + (success ? 'success' : 'danger');
        box.textContent = message;
        box.focus();
    }

    async function api(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        if (method !== 'GET' && token) headers.RequestVerificationToken = token;
        const response = await fetch(url, { method, credentials: 'same-origin', cache: 'no-store',
            headers, ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const payload = await response.json().catch(() => null);
        if (!response.ok || !payload?.success) throw Error(payload?.message || 'Request failed. Reload and retry.');
        return payload.data;
    }

    async function loadStudent(event) {
        event?.preventDefault();
        const reference = val('promotionStudentReference');
        el('promotionEditor').classList.add('d-none');
        state.options = null;
        state.requestId = null;
        if (!guid.test(reference)) { notice('Enter a valid student reference.'); return; }
        try {
            const data = await api('/api/students/' + encodeURIComponent(reference) + '/promotions/options');
            if (!data || !Array.isArray(data.targets)) throw Error('Promotion options are unavailable.');
            state.options = data;
            state.requestId = crypto.randomUUID();
            el('promotionStudentSummary').textContent = 'Student: ' + (data.studentCode || '') + ' · Current enrollment #' + data.sourceEnrollmentId;
            const select = el('promotionTarget');
            select.replaceChildren();
            for (const target of data.targets) {
                const option = document.createElement('option');
                option.value = String(target.academicBatchId);
                option.textContent = target.name;
                select.append(option);
            }
            if (!data.targets.length) { notice('No eligible next-year batches are available for this student.'); return; }
            el('promotionRoll').value = '';
            el('promotionNote').value = '';
            el('promotionEditor').classList.remove('d-none');
        } catch (error) { notice(error.message || 'Unable to load student.'); }
    }

    async function submitProgression(event) {
        event.preventDefault();
        if (state.saving || !state.options || !state.requestId) return;
        const selected = state.options.targets.find(x => String(x.academicBatchId) === val('promotionTarget'));
        const roll = val('promotionRoll');
        if (!selected || !roll || roll.length > 50) { notice('Select a batch and enter a valid roll.'); return; }
        const request = {
            clientRequestId: state.requestId,
            sourceEnrollmentId: state.options.sourceEnrollmentId,
            targetAcademicYearId: selected.academicYearId,
            targetAcademicLevelId: selected.academicLevelId,
            targetAcademicBatchId: selected.academicBatchId,
            targetAcademicTrackId: selected.academicTrackId,
            targetRoll: roll, decision: selected.decision,
            studentRowVersion: state.options.studentRowVersion,
            sourceEnrollmentRowVersion: state.options.sourceEnrollmentRowVersion,
            note: val('promotionNote') || null
        };
        state.saving = true;
        el('promotionSubmit').disabled = true;
        try {
            const data = await api('/api/students/' + encodeURIComponent(state.options.studentReference) + '/promotions', 'POST', request);
            notice(data?.alreadyProcessed ? 'Progression already recorded; no duplicate created.' : 'Student progression recorded.', true);
            el('promotionEditor').classList.add('d-none');
            state.options = null;
            state.requestId = null;
        } catch (error) { notice(error.message || 'Progression failed. Retry with the same request.'); }
        finally { state.saving = false; el('promotionSubmit').disabled = false; }
    }
})();
