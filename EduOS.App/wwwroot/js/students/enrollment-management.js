(() => {
    'use strict';
    const root = document.getElementById('enrollmentManagement');
    if (!root) return;
    const el = id => document.getElementById(id);
    const state = { student: null, hasCurrent: false, batches: [], curricula: [], requestId: null };
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const text = id => el(id).value.trim();
    const selectedBatch = () => state.batches.find(x => x.id === Number(text('enrollmentBatch')));
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
        return Boolean(state.student && !state.hasCurrent && selectedBatch() &&
            selectedCurriculum() && text('enrollmentRoll') &&
            text('enrollmentDate'));
    }
    function refreshSubmit() { el('enrollmentSubmit').disabled = !canSubmit(); }
    function menu(id, entries, placeholder) {
        const target = el(id), previous = target.value;
        target.replaceChildren(new Option(placeholder, ''));
        for (const item of entries)
            target.add(new Option(item.name + (item.code ? ' (' + item.code + ')' : ''), String(item.id)));
        if ([...target.options].some(x => x.value === previous)) target.value = previous;
    }
    async function searchStudents(event) {
        event?.preventDefault();
        const search = text('enrollmentStudentSearch');
        if (search.length < 2) return message('Enter at least two characters.', true);
        el('enrollmentStudentResults').replaceChildren();
        try {
            const result = await api('/api/students?' + new URLSearchParams({ search, page: 1, pageSize: 20 }));
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
        } catch (e) { message(e.message, true); }
    }
    async function selectStudent(student) {
        state.student = student; state.hasCurrent = true; state.requestId = null;
        el('enrollmentStudentName').textContent = student.fullName;
        el('enrollmentStudentCode').textContent = student.studentCode;
        el('enrollmentCurrent').textContent = 'Checking current enrollment...';
        el('enrollmentStudentSelection').classList.remove('d-none');
        refreshSubmit();
        try {
            const current = await api('/api/academic-enrollments/students/' +
                encodeURIComponent(student.reference) + '/current', {}, true);
            if (state.student?.reference !== student.reference) return;
            state.hasCurrent = Boolean(current);
            el('enrollmentCurrent').textContent = current ?
                'Current enrollment exists. Close/complete it through the authorized academic progression workflow before creating a new one.' :
                'No current enrollment. This student can be enrolled if all batch conditions are valid.';
            refreshSubmit();
        } catch (e) {
            if (state.student?.reference === student.reference) {
                state.hasCurrent = true;
                el('enrollmentCurrent').textContent = 'Unable to verify enrollment.';
                message(e.message, true);
            }
        }
    }
    async function searchBatches() {
        const query = text('enrollmentBatchSearch');
        if (query.length === 1) return message('Search needs at least two characters.', true);
        try {
            const search = query ? '&search=' + encodeURIComponent(query) : '';
            const catalog = await api('/api/academic-setup/catalog?take=100' + search);
            state.batches = (catalog.batches || []).filter(x => x.isActive);
            state.curricula = (catalog.curricula || []).filter(x => x.isActive && x.isCurrent);
            menu('enrollmentBatch', state.batches, 'Select section');
            refreshCurricula();
        } catch (e) { message(e.message, true); }
    }
    function refreshCurricula() {
        const batch = selectedBatch(), date = text('enrollmentDate');
        let curricula = state.curricula.filter(x => batch && x.academicProgramId === batch.academicProgramId &&
            (x.academicTrackId ?? null) === (batch.academicTrackId ?? null) &&
            (x.mediumId ?? null) === (batch.mediumId ?? null));
        if (date) curricula = curricula.filter(x => x.effectiveFrom <= date &&
            (!x.effectiveTo || x.effectiveTo >= date));
        menu('enrollmentCurriculum', curricula, 'Select applicable current curriculum');
        el('enrollmentCurriculumHelp').textContent = curricula.length ?
            'Select the curriculum effective for the enrollment date.' :
            'No matching current curriculum. Verify programme, track, medium, date and section setup.';
        refreshSubmit();
    }
    async function enroll(event) {
        event.preventDefault();
        if (!canSubmit()) return message('Select an eligible student, section, curriculum, roll and date.', true);
        const batch = selectedBatch(), curriculum = selectedCurriculum();
        const date = text('enrollmentDate');
        if (!batch || !curriculum || !state.student) return;
        if (batch.capacity <= 0) return message('This section has no available capacity configured.', true);
        if (batch.startDate && date < batch.startDate || batch.endDate && date > batch.endDate)
            return message('Enrollment date is outside the section dates.', true);
        if (!state.requestId) state.requestId = uniqueId();
        const submit = el('enrollmentSubmit');
        submit.disabled = true;
        try {
            const created = await api('/api/academic-enrollments', {
                method: 'POST', body: JSON.stringify({
                    clientRequestId: state.requestId, studentReference: state.student.reference,
                    academicBatchId: batch.id, academicCurriculumId: curriculum.id,
                    enrollmentDate: date, rollNo: text('enrollmentRoll'),
                    remarks: text('enrollmentRemarks') || null
                })
            });
            state.hasCurrent = true; state.requestId = null;
            el('enrollmentCurrent').textContent = 'Enrolled: ' + (created?.rollNo || text('enrollmentRoll')) +
                ' — ' + batch.name;
            message('Student enrollment saved successfully.');
        } catch (e) { message(e.message, true); }
        finally { refreshSubmit(); }
    }

    el('enrollmentStudentSearchForm').addEventListener('submit', searchStudents);
    el('enrollmentBatchSearchButton').addEventListener('click', searchBatches);
    el('enrollmentBatch').addEventListener('change', () => { state.requestId = null; refreshCurricula(); });
    el('enrollmentCurriculum').addEventListener('change', () => { state.requestId = null; refreshSubmit(); });
    for (const id of ['enrollmentDate', 'enrollmentRoll', 'enrollmentRemarks']) {
        el(id).addEventListener('input', () => {
            state.requestId = null;
            if (id === 'enrollmentDate') refreshCurricula();
            else refreshSubmit();
        });
    }
    el('enrollmentForm').addEventListener('submit', enroll);
    searchBatches();
})();
