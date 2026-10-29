(() => {
    'use strict';
    const root = document.getElementById('assessmentSetupWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { current: null, requestKey: null, years: [], campuses: [], terms: [],
        schemes: [], batches: [], offerings: [], definitions: [], busy: false };
    async function api(path, method = 'GET', body) {
        const res = await fetch(path, { method, cache: 'no-store', credentials: 'same-origin',
            headers: { Accept: 'application/json', ...(body ? { 'Content-Type': 'application/json' } : {}),
                ...(method === 'GET' ? {} : { RequestVerificationToken: token }) },
            ...(body ? { body: JSON.stringify(body) } : {}) });
        const result = await res.json().catch(() => null);
        if (!res.ok || !result?.success) throw new Error(result?.message || 'Request failed.');
        return result.data;
    }
    function notify(message, danger = false) {
        el('setupAlert').textContent = message;
        el('setupAlert').className = 'alert alert-' + (danger ? 'danger' : 'info');
    }
    function fillSelect(id, rows, key, name, placeholder, preserve = false) {
        const select = el(id), selected = preserve ? select.value : '';
        select.replaceChildren(new Option(placeholder, ''));
        for (const item of rows) select.add(new Option(name(item), String(item[key])));
        if (selected) select.value = selected;
    }
    function status() {
        const detail = state.current;
        el('setupStatus').textContent = detail ?
            `Saved · ${detail.code} · ${Number(detail.state) === 1 ? 'Draft' : 'Read-only'}` : 'New draft';
        el('setupSave').disabled = state.busy || !!detail && Number(detail.state) !== 1;
        el('setupAddSubject').disabled = state.busy || !detail || Number(detail.state) !== 1;
    }
    function syncTerm() {
        const yearId = Number(el('setupYear').value);
        fillSelect('setupTerm', state.terms.filter(x => Number(x.academicYearId) === yearId),
            'id', x => x.name, 'No term', true);
    }
    function syncYears() {
        const campusId = Number(el('setupCampus').value);
        fillSelect('setupYear', state.years.filter(x => x.campusId == null || Number(x.campusId) === campusId),
            'id', x => x.name, 'Select academic year', true);
        syncTerm();
    }
    function reset() {
        state.current = null; state.requestKey = null;
        el('setupDefinition').reset();
        el('setupExisting').value = '';
        el('setupCampus').value = ''; el('setupYear').value = '';
        syncTerm(); emptySubjects(); status();
    }
    function emptySubjects() {
        const body = el('setupSubjectRows'); body.replaceChildren();
        const row = document.createElement('tr'), cell = document.createElement('td');
        cell.colSpan = 4; cell.className = 'text-muted';
        cell.textContent = 'No assessment subjects saved.'; row.append(cell); body.append(row);
    }
    function renderSubjects() {
        const body = el('setupSubjectRows'); body.replaceChildren();
        for (const subject of state.current?.subjects || []) {
            const tr = document.createElement('tr');
            for (const value of [`${subject.subjectCode} — ${subject.subjectName}`,
                subject.fullMarks, subject.passMarks, subject.weightage]) {
                const td = document.createElement('td'); td.textContent = String(value ?? ''); tr.append(td);
            }
            body.append(tr);
        }
        if (!body.children.length) emptySubjects();
    }
    async function definitions() {
        const query = new URLSearchParams({ search: el('setupFind').value.trim(), page: '1', pageSize: '50' });
        const data = await api('/api/assessment-administration/assessments/page?' + query);
        state.definitions = data.items || [];
        fillSelect('setupExisting', state.definitions, 'reference',
            x => `${x.code} — ${x.name} (${Number(x.state) === 1 ? 'Draft' : 'In workflow'})`, 'New assessment');
        el('setupCount').textContent = `${data.totalCount || 0} assessments · Showing first 50. Search by code to refine.`;
    }
    async function loadDetail(reference) {
        if (!reference) { reset(); return; }
        const detail = await api('/api/assessment-administration/assessments/' + encodeURIComponent(reference));
        state.current = detail; state.requestKey = null;
        for (const [id, key] of [['setupName', 'name'], ['setupCode', 'code'],
            ['setupStart', 'startDate'], ['setupEnd', 'endDate'], ['setupRemarks', 'remarks']])
            el(id).value = detail[key] || '';
        for (const [id, key] of [['setupCampus', 'campusId'], ['setupYear', 'academicYearId'],
            ['setupScheme', 'gradeSchemeId'], ['setupType', 'type'], ['setupAttemptMode', 'attemptMode']])
            el(id).value = detail[key] == null ? '' : String(detail[key]);
        syncYears(); el('setupYear').value = String(detail.academicYearId);
        syncTerm(); el('setupTerm').value = detail.academicTermId ? String(detail.academicTermId) : '';
        el('setupCgpa').checked = !!detail.countsTowardCgpa;
        status(); renderSubjects();
    }
    async function saveAssessment() {
        if (state.busy) return;
        const campusId = Number(el('setupCampus').value), academicYearId = Number(el('setupYear').value);
        const startDate = el('setupStart').value, endDate = el('setupEnd').value;
        const attemptMode = Number(el('setupAttemptMode').value), type = Number(el('setupType').value);
        const countsTowardCgpa = el('setupCgpa').checked;
        if (!campusId || !academicYearId || !startDate || !endDate || endDate < startDate ||
            !el('setupName').value.trim() || !el('setupCode').value.trim()) {
            notify('Complete name, code, campus, year and valid dates.', true); return;
        }
        if (attemptMode !== 0 && (!countsTowardCgpa || type !== 1 || !el('setupScheme').value)) {
            notify('Retake/improvement must be a credit-weighted terminal exam with a grade scheme.', true); return;
        }
        state.requestKey ||= crypto.randomUUID(); state.busy = true; status();
        const body = { clientRequestId: state.requestKey,
            campusId, academicYearId, academicTermId: el('setupTerm').value ? Number(el('setupTerm').value) : null,
            gradeSchemeId: el('setupScheme').value ? Number(el('setupScheme').value) : null,
            name: el('setupName').value.trim(), code: el('setupCode').value.trim(), type,
            attemptMode, countsTowardCgpa, startDate, endDate, remarks: el('setupRemarks').value.trim(),
            rowVersion: state.current?.rowVersion || null };
        try {
            const detail = await api('/api/assessment-administration/assessments' +
                (state.current ? '/' + encodeURIComponent(state.current.reference) : ''),
            state.current ? 'PUT' : 'POST', body);
            state.requestKey = null;
            await loadDetail(detail.reference);
            notify('Assessment draft saved. Add applicable subject offerings below.');
            await definitions();
            el('setupExisting').value = detail.reference;
        } catch (err) { notify(err.message + ' Retry the same request after checking fields.', true); }
        finally { state.busy = false; status(); }
    }
    async function loadReferences() {
        const [referenceData, schemePage] = await Promise.all([
            api('/api/assessment-administration/setup/references'),
            api('/api/assessment-administration/grade-schemes?' +
                new URLSearchParams({ page: '1', pageSize: '50', activeOnly: 'true' }))
        ]);
        state.campuses = referenceData.campuses; state.years = referenceData.academicYears;
        state.terms = referenceData.academicTerms;
        state.schemes = schemePage.items || [];
        fillSelect('setupCampus', state.campuses, 'id', x => x.name, 'Select campus');
        fillSelect('setupScheme', state.schemes, 'id', x => `${x.code} — ${x.name}`, 'Select grade scheme');
        syncYears();
    }
    async function loadBatches() {
        const query = new URLSearchParams({ search: el('setupBatchSearch').value.trim(), page: '1', pageSize: '50' });
        if (el('setupCampus').value) query.set('campusId', el('setupCampus').value);
        if (el('setupYear').value) query.set('academicYearId', el('setupYear').value);
        const data = await api('/api/assessment-administration/setup/batches?' + query);
        state.batches = data.items || [];
        fillSelect('setupBatch', state.batches, 'id', x => `${x.code} — ${x.name}`, 'Select batch');
        fillSelect('setupOffering', [], 'reference', x => x.code, 'Select batch first');
    }
    async function loadOfferings() {
        const batchId = Number(el('setupBatch').value);
        if (!batchId) { fillSelect('setupOffering', [], 'reference', x => x.code, 'Select batch'); return; }
        const query = new URLSearchParams({ search: el('setupOfferingSearch').value.trim(), page: '1', pageSize: '50' });
        const data = await api(`/api/assessment-administration/setup/batches/${batchId}/offerings?` + query);
        state.offerings = data.items || [];
        fillSelect('setupOffering', state.offerings, 'reference',
            x => `${x.code} — ${x.subjectName}`, 'Select subject');
    }
    async function addSubject() {
        if (state.busy || !state.current || Number(state.current.state) !== 1) return;
        const batch = state.batches.find(x => String(x.id) === el('setupBatch').value);
        const reference = el('setupOffering').value;
        const fullMarks = Number(el('setupFull').value), passMarks = Number(el('setupPass').value);
        const weightage = Number(el('setupWeight').value);
        if (!batch || !reference || Number(batch.campusId) !== Number(state.current.campusId) ||
            Number(batch.academicYearId) !== Number(state.current.academicYearId) ||
            !Number.isFinite(fullMarks) || fullMarks <= 0 || !Number.isFinite(passMarks) ||
            passMarks < 0 || passMarks > fullMarks || !Number.isFinite(weightage) ||
            weightage <= 0 || weightage > 100 ||
            [fullMarks, passMarks, weightage].some(x => Math.abs(Math.round(x * 100) - x * 100) > 1e-7)) {
            notify('Select a matching batch/offering and enter valid two-decimal marks and weightage.', true); return;
        }
        state.busy = true; status();
        try {
            await api('/api/assessment-administration/subjects', 'POST', {
                assessmentReference: state.current.reference, subjectOfferingReference: reference,
                fullMarks, passMarks, weightage
            });
            await loadDetail(state.current.reference);
            notify('Assessment subject saved. Configure schedule/components from the exam workspace.');
        } catch (err) { notify(err.message, true); }
        finally { state.busy = false; status(); }
    }
    el('setupFindForm').addEventListener('submit', event => {
        event.preventDefault(); void definitions().catch(err => notify(err.message, true));
    });
    el('setupNew').addEventListener('click', reset);
    el('setupExisting').addEventListener('change', () => {
        void loadDetail(el('setupExisting').value).catch(err => notify(err.message, true));
    });
    el('setupYear').addEventListener('change', () => { syncTerm(); void loadBatches().catch(err => notify(err.message, true)); });
    el('setupCampus').addEventListener('change', () => {
        syncYears(); void loadBatches().catch(err => notify(err.message, true));
    });
    el('setupDefinition').addEventListener('submit', event => { event.preventDefault(); void saveAssessment(); });
    el('setupBatchFind').addEventListener('click', () => { void loadBatches().catch(err => notify(err.message, true)); });
    el('setupBatch').addEventListener('change', () => { void loadOfferings().catch(err => notify(err.message, true)); });
    el('setupOfferingFind').addEventListener('click', () => { void loadOfferings().catch(err => notify(err.message, true)); });
    el('setupAddSubject').addEventListener('click', () => { void addSubject(); });
    void Promise.all([loadReferences(), definitions()]).then(() => loadBatches())
        .catch(err => notify(err.message, true));
})();
