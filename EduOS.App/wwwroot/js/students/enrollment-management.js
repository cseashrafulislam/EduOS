(() => {
    'use strict';
    const root = document.getElementById('enrollmentManagement');
    if (!root) return;
    const el = id => document.getElementById(id);
    const state = { student: null, hasCurrent: false, batches: [], sections: [], curricula: [],
        requestId: null, requestFingerprint: null, saving: false,
        searchVersion: 0, studentVersion: 0, batchVersion: 0 };
    const form = el('enrollmentForm');
    const guard = window.EduOSFormGuard?.create(form);
    const identity = [document.body?.dataset?.authTenantId || '',
        document.body?.dataset?.authSessionReference || ''];
    const sameSession = () => identity[0] === (document.body?.dataset?.authTenantId || '') &&
        identity[1] === (document.body?.dataset?.authSessionReference || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const text = id => el(id).value.trim();
    const selectedBatch = () => state.batches.find(x => x.id === Number(text('enrollmentBatch')));
    const selectedSection = () => state.sections.find(x => x.id === Number(text('enrollmentSection')));
    const selectedCurriculum = () => state.curricula.find(x => x.id === Number(text('enrollmentCurriculum')));

    function message(value, error = false) {
        const banner = el('enrollmentNotice');
        banner.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        banner.textContent = value;
        banner.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
    async function api(url, options = {}, allow404 = false) {
        const response = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store', ...options,
            headers: {
                Accept: 'application/json',
                ...(options.body ? { 'Content-Type': 'application/json' } : {}),
                ...(options.method && options.method !== 'GET' ? { RequestVerificationToken: token } : {}),
                ...(options.headers || {})
            }
        });
        if (allow404 && response.status === 404) return null;
        const payload = await response.json().catch(() => null);
        if (!response.ok || payload?.success !== true)
            throw new Error(payload?.message || 'Request failed. Reload and retry.');
        return payload.data;
    }
    function uniqueId() {
        if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
        const bytes = new Uint8Array(16);
        crypto.getRandomValues(bytes);
        bytes[6] = (bytes[6] & 15) | 64; bytes[8] = (bytes[8] & 63) | 128;
        const hex = [...bytes].map(b => b.toString(16).padStart(2, '0')).join('');
        return [hex.slice(0, 8), hex.slice(8, 12), hex.slice(12, 16),
            hex.slice(16, 20), hex.slice(20)].join('-');
    }
    function canSubmit() {
        return Boolean(sameSession() && !state.saving && state.student && !state.hasCurrent && selectedBatch() &&
            selectedCurriculum() && (!state.sections.length || selectedSection()) &&
            text('enrollmentRoll') &&
            text('enrollmentDate'));
    }
    function refreshSubmit() {
        el('enrollmentSubmit').disabled = !canSubmit();
        el('enrollmentBatchSearchButton').disabled = state.saving || !sameSession();
    }
    function menu(id, entries, placeholder) {
        const target = el(id), previous = target.value;
        target.replaceChildren(new Option(placeholder, ''));
        for (const item of entries)
            target.add(new Option(item.name + (item.code ? ' (' + item.code + ')' : ''), String(item.id)));
        if ([...target.options].some(x => x.value === previous)) target.value = previous;
    }
    async function searchStudents(event) {
        event?.preventDefault();
        if (state.saving || !sameSession()) return;
        const search = text('enrollmentStudentSearch');
        if (search.length < 2 || search.length > 100) return message('Enter 2–100 characters.', true);
        const request = ++state.searchVersion;
        el('enrollmentStudentResults').replaceChildren();
        try {
            const result = await api('/api/students?' + new URLSearchParams({ search, page: 1, pageSize: 20 }));
            if (request !== state.searchVersion || !sameSession()) return;
            if (!Array.isArray(result?.items)) throw new Error('Invalid student directory response.');
            if (!result.items.length) {
                el('enrollmentStudentResults').textContent = 'No matching student found.';
                return;
            }
            for (const student of result.items) {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'list-group-item list-group-item-action';
                button.textContent = student.fullName + ' — ' + student.studentCode +
                    (student.academicBatchName ? ' (' + student.academicBatchName + ')' : '');
                button.addEventListener('click', () => selectStudent(student));
                el('enrollmentStudentResults').append(button);
            }
        } catch (e) { if (request === state.searchVersion && sameSession()) message(e.message, true); }
    }
    async function selectStudent(student) {
        if (state.saving || !sameSession() || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(student.reference)) return;
        if (guard?.isDirty() && !guard.confirmDiscard()) return;
        const request = ++state.studentVersion;
        state.student = student; state.hasCurrent = true;
        state.requestId = null; state.requestFingerprint = null;
        form.reset(); guard?.markClean();
        ++curriculumLookupVersion;
        state.curricula = [];
        menu('enrollmentCurriculum', [], 'Select applicable current curriculum');
        window.dispatchEvent(new CustomEvent("eduos:enrollment-student-selected", { detail: { reference: student.reference } }));
        el('enrollmentStudentName').textContent = student.fullName;
        el('enrollmentStudentCode').textContent = student.studentCode;
        el('enrollmentCurrent').textContent = 'Checking current enrollment...';
        el('enrollmentStudentSelection').classList.remove('d-none');
        refreshSubmit();
        try {
            const current = await api('/api/academic-enrollments/students/' +
                encodeURIComponent(student.reference) + '/current', {}, true);
            if (request !== state.studentVersion || state.student?.reference !== student.reference || !sameSession()) return;
            state.hasCurrent = Boolean(current);
            el('enrollmentCurrent').textContent = current ?
                'Current enrollment exists. Close/complete it through the authorized academic progression workflow before creating a new one.' :
                'No current enrollment. This student can be enrolled if all batch conditions are valid.';
            refreshSubmit();
        } catch (e) {
            if (request === state.studentVersion && state.student?.reference === student.reference && sameSession()) {
                state.hasCurrent = true;
                el('enrollmentCurrent').textContent = 'Unable to verify enrollment.';
                message(e.message, true);
            }
        }
    }
    async function searchBatches() {
        if (state.saving || !sameSession()) return;
        if (selectedBatch() && guard?.isDirty() && !guard.confirmDiscard()) return;
        const query = text('enrollmentBatchSearch');
        if (query.length === 1) return message('Search needs at least two characters.', true);
        const request = ++state.batchVersion;
        try {
            const search = query ? '&search=' + encodeURIComponent(query) : '';
            const catalog = await api('/api/academic-setup/catalog?take=100' + search);
            if (request !== state.batchVersion || !sameSession()) return;
            state.batches = (catalog.batches || []).filter(x => x.isActive);
            menu('enrollmentBatch', state.batches, 'Select academic batch');
            await refreshSections();
            await refreshCurricula();
        } catch (e) { if (request === state.batchVersion && sameSession()) message(e.message, true); }
    }
    let sectionLookupVersion = 0;
    async function refreshSections() {
        const request = ++sectionLookupVersion;
        const batch = selectedBatch();
        state.sections = [];
        menu('enrollmentSection', [], 'Select academic section');
        if (!batch) { refreshSubmit(); return; }
        try {
            const result = await api('/api/academic-sections?academicBatchId=' +
                encodeURIComponent(String(batch.id)) + '&pageSize=100&page=1');
            if (request !== sectionLookupVersion || !sameSession() || selectedBatch()?.id !== batch.id) return;
            state.sections = (result.items || []).filter(x => x.isActive);
            menu('enrollmentSection', state.sections, 'Select academic section');
            if (state.sections.length === 1)
                el('enrollmentSection').value = String(state.sections[0].id);
        } catch (e) {
            if (request === sectionLookupVersion && sameSession()) message(e.message, true);
        }
        refreshSubmit();
    }
    let curriculumLookupVersion = 0;
    async function refreshCurricula() {
        const version = ++curriculumLookupVersion;
        const batch = selectedBatch(), date = text('enrollmentDate');
        state.curricula = [];
        menu('enrollmentCurriculum', [], 'Select applicable current curriculum');
        refreshSubmit();
        if (!batch || !date) {
            el('enrollmentCurriculumHelp').textContent = 'Select a section and enrollment date.';
            return;
        }
        try {
            const curricula = await api('/api/academic-enrollments/batches/' + batch.id +
                '/curricula?enrollmentDate=' + encodeURIComponent(date));
            if (version !== curriculumLookupVersion || !sameSession()) return;
            state.curricula = (curricula || []).filter(x => x.isActive && x.isCurrent);
            menu('enrollmentCurriculum', state.curricula, 'Select applicable current curriculum');
            el('enrollmentCurriculumHelp').textContent = state.curricula.length ?
                'Choose the curriculum effective for this section and date.' :
                'No current curriculum matches this section and date. Complete academic setup first.';
        } catch (e) {
            if (version === curriculumLookupVersion && sameSession()) {
                el('enrollmentCurriculumHelp').textContent = 'Cannot resolve curriculum: ' + e.message;
                message(e.message, true);
            }
        }
        refreshSubmit();
    }
    async function enroll(event) {
        event.preventDefault();
        if (state.saving || !sameSession()) return;
        if (!canSubmit()) return message('Select an eligible student, section, curriculum, roll and date.', true);
        const batch = selectedBatch(), curriculum = selectedCurriculum();
        const date = text('enrollmentDate');
        if (!batch || !curriculum || !state.student) return;
        if (batch.capacity <= 0) return message('This section has no available capacity configured.', true);
        if (batch.startDate && date < batch.startDate || batch.endDate && date > batch.endDate)
            return message('Enrollment date is outside the section dates.', true);
        const payload = { studentReference: state.student.reference,
            academicBatchId: batch.id, academicSectionId: selectedSection()?.id || null,
            academicCurriculumId: curriculum.id,
            enrollmentDate: date, rollNo: text('enrollmentRoll'),
            remarks: text('enrollmentRemarks') || null };
        const fingerprint = JSON.stringify(payload);
        if (!state.requestId || state.requestFingerprint !== fingerprint) state.requestId = uniqueId();
        state.requestFingerprint = fingerprint;
        payload.clientRequestId = state.requestId;
        state.saving = true; form.inert = true; refreshSubmit();
        try {
            const created = await api('/api/academic-enrollments', {
                method: 'POST', body: JSON.stringify(payload)
            });
            if (!sameSession()) return;
            state.hasCurrent = true; state.requestId = null; state.requestFingerprint = null;
            guard?.markClean();
            el('enrollmentCurrent').textContent = 'Enrolled: ' + (created?.rollNo || payload.rollNo) +
                ' — ' + batch.name + (selectedSection() ? ' / ' + selectedSection().name : '');
            message('Student enrollment saved successfully.');
        } catch (e) {
            if (sameSession()) message(e.message + ' If uncertain, retry unchanged details using the same request ID.', true);
        } finally { state.saving = false; form.inert = false; refreshSubmit(); }
    }

    el('enrollmentStudentSearchForm').addEventListener('submit', searchStudents);
    el('enrollmentStudentSearch').addEventListener('input', () => { ++state.searchVersion; });
    el('enrollmentBatchSearchButton').addEventListener('click', searchBatches);
    el('enrollmentBatch').addEventListener('change', () => {
        refreshSections().then(refreshCurricula).catch(e => message(e.message, true));
    });
    el('enrollmentSection').addEventListener('change', refreshSubmit);
    el('enrollmentCurriculum').addEventListener('change', refreshSubmit);
    for (const id of ['enrollmentDate', 'enrollmentRoll', 'enrollmentRemarks']) {
        el(id).addEventListener('input', () => {
            if (id === 'enrollmentDate') refreshCurricula();
            else refreshSubmit();
        });
    }
    el('enrollmentForm').addEventListener('submit', enroll);
    searchBatches();
})();
