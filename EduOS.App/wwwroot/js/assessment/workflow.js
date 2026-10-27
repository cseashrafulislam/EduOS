(() => {
    'use strict';
    const root = document.getElementById('examWorkflow');
    if (!root) return;
    const el = id => document.getElementById(id);
    const canPublish = root.dataset.canPublish === 'true';
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { scopes: [], scopePage: 1, scopeSize: 30, scopeTotal: 0, scopeSearch: '',
        scopeBusy: false, roster: null, sheet: null, sheetMode: null, busy: false, publishKey: null,
        readiness: null, readinessPage: 1, readinessBusy: false, readinessSize: 20 };
    const option = (value, label) => new Option(label, String(value));
    const cell = value => { const td = document.createElement('td'); td.textContent = value == null ? '' : String(value); return td; };
    const validDecimal = n => Number.isFinite(n) && Math.abs(Math.round(n * 100) - n * 100) < 1e-7;
    const locked = s => s?.state === 5 || s?.state === 'Locked';
    const marksOpen = s => [3, 4, 'InProgress', 'MarksEntry'].includes(s?.state);
    function message(type, value) {
        const box = el('examAlert'); box.className = 'alert alert-' + type;
        box.textContent = value; box.focus();
    }
    async function api(path, method = 'GET', body) {
        const response = await fetch(path, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json',
                ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
                ...(method === 'GET' ? {} : { RequestVerificationToken: token }) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        const result = await response.json().catch(() => null);
        if (!response.ok || result?.success !== true) throw new Error(result?.message || 'Request failed.');
        return result.data;
    }
    function select(id, rows, valueKey, labelKey, placeholder) {
        const target = el(id), previous = target.value;
        target.replaceChildren(option('', placeholder));
        const seen = new Set();
        for (const row of rows) {
            const key = String(row[valueKey]);
            if (!seen.has(key)) { seen.add(key); target.add(option(key, row[labelKey])); }
        }
        if (seen.has(previous)) target.value = previous;
    }
    function selected() {
        return state.scopes.find(x =>
            String(x.assessmentId) === el('examExam').value &&
            String(x.academicBatchId) === el('examBatch').value &&
            String(x.assessmentSubjectId) === el('examSubject').value) || null;
    }
    function scope() {
        const row = selected();
        if (!row) throw new Error('Choose an authorized assessment, batch and subject.');
        return row;
    }
    function resetRoster() {
        state.roster = null;
        el('examSaveMarks').disabled = true;
        el('examRosterInfo').textContent = '';
        const tbody = el('examRoster'); tbody.replaceChildren();
        const tr = document.createElement('tr'), td = cell('Load a subject to view its marks register.');
        td.colSpan = 7; td.className = 'text-center text-muted p-4'; tr.append(td); tbody.append(tr);
    }
    function resetResults() {
        state.sheet = null; state.sheetMode = null; state.publishKey = null;
        state.readiness = null;
        if (!canPublish) return;
        el('examReadinessCsv').disabled = true;
        el('examReadinessInfo').textContent = '';
        el('examReadinessRows').replaceChildren();
        el('examReadinessPrev').disabled = true;
        el('examReadinessNext').disabled = true;
        el('examGenerate').disabled = !selected();
        el('examLoadResults').disabled = !selected();
        el('examPublish').disabled = true;
        el('examResultSummary').textContent = 'Preview draft calculations or view the immutable published snapshot.';
        el('examResults').replaceChildren();
    }

    const assessmentStages = { 1: ['Draft', 2, 'Schedule assessment'],
        2: ['Scheduled', 3, 'Start assessment'],
        3: ['In progress', 4, 'Open marks entry'],
        4: ['Marks entry', 5, 'Verify and lock'] };
    const stageNames = { 5: 'Locked', 6: 'Published', 7: 'Cancelled' };
    function currentAssessment() {
        return state.scopes.find(x => String(x.assessmentId) === el('examExam').value) || null;
    }
    function renderAssessmentState() {
        if (!canPublish) return;
        const s = currentAssessment();
        const stage = s && assessmentStages[Number(s.state)];
        el('examStateCurrent').textContent = s
            ? (stage?.[0] || stageNames[Number(s.state)] || 'Unknown state')
            : 'Select an assessment';
        el('examStateAdvance').textContent = stage?.[2] || 'No further stage';
        el('examStateAdvance').disabled = state.busy || !s || !stage;
    }
    async function advanceAssessmentState() {
        if (!canPublish || state.busy) return;
        const s = currentAssessment(), next = s && assessmentStages[Number(s.state)];
        if (!next) return;
        if (next[1] === 5 && !window.confirm(
            'Lock all batches only after marks and component evidence are complete?')) return;
        state.busy = true;
        renderAssessmentState();
        try {
            const url = '/api/assessment-administration/assessments/' +
                encodeURIComponent(s.assessmentReference);
            const latest = await api(url);
            if (Number(latest.state) !== Number(s.state))
                throw new Error('Assessment state changed. Reload the register before proceeding.');
            const updated = await api(url + '/state', 'POST', {
                state: next[1], rowVersion: latest.rowVersion
            });
            for (const row of state.scopes)
                if (row.assessmentId === s.assessmentId) row.state = updated.state;
            resetRoster(); resetResults();
            message('success', 'Assessment advanced to ' +
                (stageNames[Number(updated.state)] || assessmentStages[Number(updated.state)]?.[0] || 'next stage') + '.');
            await loadScopes(state.scopePage);
        } catch (error) {
            message('danger', error.message);
        } finally {
            state.busy = false;
            renderAssessmentState();
        }
    }
    function examChanged() {
        renderAssessmentState();
        const rows = state.scopes.filter(x => String(x.assessmentId) === el('examExam').value);
        select('examBatch', rows, 'academicBatchId', 'academicBatchName', 'Select batch');
        batchChanged();
    }
    function batchChanged() {
        const rows = state.scopes.filter(x =>
            String(x.assessmentId) === el('examExam').value &&
            String(x.academicBatchId) === el('examBatch').value);
        select('examSubject', rows, 'assessmentSubjectId', 'subjectName', 'Select subject');
        resetRoster(); resetResults();
    }
    async function loadScopes(page) {
        if (state.scopeBusy || page < 1) return;
        state.scopeBusy = true;
        el('examScopeApply').disabled = true;
        el('examScopePrev').disabled = true;
        el('examScopeNext').disabled = true;
        try {
            const params = new URLSearchParams({
                page: String(page), pageSize: String(state.scopeSize), search: state.scopeSearch
            });
            const data = await api('/api/assessment-administration/scopes/page?' + params);
            state.scopes = data.items || [];
            state.scopePage = data.page;
            state.scopeTotal = data.totalCount;
            select('examExam', state.scopes, 'assessmentId', 'assessmentName', 'Select assessment');
            examChanged();
            const first = state.scopeTotal ? (state.scopePage - 1) * state.scopeSize + 1 : 0;
            const last = Math.min(state.scopePage * state.scopeSize, state.scopeTotal);
            el('examScopePageInfo').textContent = first + '–' + last + ' of ' + state.scopeTotal + ' scopes';
            if (!state.scopeTotal) message('warning', 'No authorized assessment subjects match the search.');
        } catch (error) {
            state.scopes = [];
            select('examExam', [], 'assessmentId', 'assessmentName', 'Select assessment');
            examChanged();
            el('examScopePageInfo').textContent = 'Unable to load assessment scopes';
            message('danger', error.message);
        } finally {
            state.scopeBusy = false;
            el('examScopeApply').disabled = false;
            el('examScopePrev').disabled = state.scopePage <= 1;
            el('examScopeNext').disabled = state.scopePage * state.scopeSize >= state.scopeTotal;
        }
    }
    async function init() { await loadScopes(1); }
    function renderRoster(data) {
        state.roster = data;
        const editable = !data.isResultPublished && marksOpen(selected());
        const tbody = el('examRoster'); tbody.replaceChildren();
        el('examRosterInfo').textContent = (data.students?.length || 0) + ' students · Full ' +
            data.fullMarks + ' · Pass ' + data.passMarks +
            (data.isResultPublished ? ' · Published (immutable)' : editable ? ' · Marks entry open' : ' · Read-only');
        for (const student of data.students || []) {
            const tr = document.createElement('tr');
            tr.dataset.registrationId = String(student.studentSubjectRegistrationId);
            tr.dataset.rowVersion = student.rowVersion || '';
            const mark = document.createElement('input');
            mark.type = 'number'; mark.min = '0'; mark.max = String(data.fullMarks);
            mark.step = '0.01'; mark.className = 'form-control form-control-sm';
            mark.value = student.obtainedMarks == null ? '' : String(student.obtainedMarks);
            mark.dataset.initial = mark.value;
            const absent = document.createElement('input');
            absent.type = 'checkbox'; absent.className = 'form-check-input';
            absent.checked = !!student.isAbsent; absent.dataset.initial = String(absent.checked);
            const withheld = document.createElement('input');
            withheld.type = 'checkbox'; withheld.className = 'form-check-input';
            withheld.checked = !!student.isWithheld; withheld.dataset.initial = String(withheld.checked);
            for (const input of [mark, absent, withheld]) {
                input.disabled = !editable || (input === mark && (absent.checked || withheld.checked));
            }
            tr.append(cell(student.rollNo), cell(student.studentCode), cell(student.studentName));
            for (const input of [mark, absent, withheld]) {
                const td = document.createElement('td'); td.append(input); tr.append(td);
            }
            tr.append(cell(student.gradeLetter || '—')); tbody.append(tr);
        }
        if (!data.students?.length) {
            const tr = document.createElement('tr'), td = cell('No eligible registered students.');
            td.colSpan = 7; tr.append(td); tbody.append(tr);
        }
        el('examSaveMarks').disabled = !editable || !data.students?.length;
    }
    async function loadRoster(event) {
        event?.preventDefault();
        if (state.busy) return;
        let s;
        try { s = scope(); } catch (error) { message('warning', error.message); return; }
        el('examLoadRoster').disabled = true;
        try {
            const q = new URLSearchParams({ assessmentId: String(s.assessmentId),
                academicBatchId: String(s.academicBatchId), assessmentSubjectId: String(s.assessmentSubjectId) });
            renderRoster(await api('/api/assessment-administration/mark-roster?' + q));
        } catch (error) { resetRoster(); message('danger', error.message); }
        finally { el('examLoadRoster').disabled = false; }
    }
    function changedMarks() {
        const changed = [];
        for (const tr of el('examRoster').querySelectorAll('tr[data-registration-id]')) {
            const [mark, absent, withheld] = tr.querySelectorAll('input');
            if (mark.value === mark.dataset.initial &&
                String(absent.checked) === absent.dataset.initial &&
                String(withheld.checked) === withheld.dataset.initial) continue;
            if (absent.checked && withheld.checked) throw new Error('A student cannot be both absent and withheld.');
            if (!absent.checked && !withheld.checked && mark.value.trim() === '')
                throw new Error('Enter a mark, or explicitly select absent/withheld.');
            const value = absent.checked || withheld.checked ? 0 : Number(mark.value);
            if (!validDecimal(value) || value < 0 || value > state.roster.fullMarks)
                throw new Error('Marks must be within the full mark with at most two decimals.');
            changed.push({ studentSubjectRegistrationId: Number(tr.dataset.registrationId),
                obtainedMarks: value, isAbsent: absent.checked, isWithheld: withheld.checked,
                rowVersion: tr.dataset.rowVersion || null });
        }
        if (!changed.length) throw new Error('No changed marks to save.');
        if (changed.length > 2000) throw new Error('Save at most 2,000 changed marks at once.');
        return changed;
    }
    async function saveMarks() {
        if (state.busy || !state.roster || state.roster.isResultPublished || !marksOpen(selected())) return;
        let body;
        try { body = { assessmentSubjectId: scope().assessmentSubjectId, marks: changedMarks() }; }
        catch (error) { message('warning', error.message); return; }
        state.busy = true; el('examSaveMarks').disabled = true;
        let error = null;
        try {
            await api('/api/assessment-administration/marks', 'POST', body);
        } catch (e) { error = e; }
        finally { state.busy = false; await loadRoster(); }
        message(error ? 'danger' : 'success', error
            ? error.message + ' The roster was refreshed; review versions before retrying.'
            : 'Changed marks saved. The register has been refreshed.');
    }
    function renderResults(data, mode) {
        state.sheet = data; state.sheetMode = mode;
        if (!canPublish) return;
        el('examResultSummary').textContent = (data.assessmentName || 'Assessment') + ' · ' +
            (data.subjectCount || 0) + ' subjects · ' + (data.results?.length || 0) + ' students · ' +
            (mode === 'published' ? 'Published snapshot (immutable)' : 'Draft preview (not saved)');
        const tbody = el('examResults'); tbody.replaceChildren();
        for (const item of data.results || []) {
            const tr = document.createElement('tr');
            tr.append(cell(item.meritPosition ?? '—'), cell(item.rollNo),
                cell(item.studentName), cell(item.totalMarks + ' / ' + item.maximumMarks),
                cell(item.percentage), cell(item.gradePointAverage ?? '—'),
                cell(item.gradeLetter || '—'),
                cell(item.isWithheld ? 'Withheld' : item.isPassed ? 'Passed' : 'Not passed'));
            tbody.append(tr);
        }
        el('examPublish').disabled = mode !== 'preview' || !locked(selected()) || !data.results?.length;
    }
    async function resultAction(mode) {
        if (!canPublish || state.busy) return;
        let s;
        try { s = scope(); } catch (error) { message('warning', error.message); return; }
        state.busy = true;
        el('examGenerate').disabled = true; el('examLoadResults').disabled = true;
        el('examPublish').disabled = true;
        try {
            const body = { assessmentId: s.assessmentId, academicBatchId: s.academicBatchId };
            const result = mode === 'published'
                ? await api('/api/assessment-administration/results?' + new URLSearchParams(body))
                : await api('/api/assessment-administration/results/preview', 'POST', body);
            renderResults(result, mode);
            message('success', mode === 'published' ? 'Published snapshot loaded.' : 'Draft results calculated; nothing was published.');
        } catch (error) {
            state.sheet = null; state.sheetMode = null;
            el('examResults').replaceChildren();
            el('examResultSummary').textContent = 'No result sheet loaded.';
            message('danger', error.message);
        } finally {
            state.busy = false; el('examGenerate').disabled = !selected();
            el('examLoadResults').disabled = !selected();
        }
    }
    async function loadReadiness(page) {
        if (!canPublish || state.readinessBusy || page < 1) return;
        let s;
        try { s = scope(); } catch (error) { message('warning', error.message); return; }
        state.readinessBusy = true;
        for (const id of ['examReadinessLoad', 'examReadinessPrev', 'examReadinessNext']) el(id).disabled = true;
        try {
            const params = new URLSearchParams({ assessmentId: String(s.assessmentId),
                academicBatchId: String(s.academicBatchId), page: String(page),
                pageSize: String(state.readinessSize) });
            const data = await api('/api/assessment-administration/results/readiness?' + params);
            state.readiness = data; state.readinessPage = data.page;
            const tbody = el('examReadinessRows'); tbody.replaceChildren();
            for (const student of data.students || []) {
                const tr = document.createElement('tr');
                const issues = (student.issues || []).map(x => x.subjectName + ': ' + x.code).join('; ');
                tr.append(cell(student.rollNo), cell(student.studentCode), cell(student.studentName),
                    cell(student.applicableSubjects), cell(student.recordedMarks), cell(student.absentMarks),
                    cell(student.isReady ? 'Ready' : issues));
                tbody.append(tr);
            }
            if (!data.students?.length) {
                const tr = document.createElement('tr'), td = cell('No active students on this page.');
                td.colSpan = 7; tr.append(td); tbody.append(tr);
            }
            const first = data.totalCount ? (data.page - 1) * data.pageSize + 1 : 0;
            const last = Math.min(data.page * data.pageSize, data.totalCount);
            el('examReadinessInfo').textContent = first + '–' + last + ' of ' + data.totalCount +
                ' students · page-level evidence only';
            el('examReadinessCsv').disabled = !data.students?.length;
            el('examReadinessPrev').disabled = data.page <= 1;
            el('examReadinessNext').disabled = data.page * data.pageSize >= data.totalCount;
        } catch (error) {
            state.readiness = null; el('examReadinessCsv').disabled = true;
            el('examReadinessInfo').textContent = 'Readiness check failed';
            message('danger', error.message);
        } finally { state.readinessBusy = false; el('examReadinessLoad').disabled = false; }
    }
    async function publish() {
        if (!canPublish || state.busy || state.sheetMode !== 'preview' ||
            !state.sheet?.results?.length || !locked(selected())) return;
        const s = scope();
        if (!s.assessmentReference) {
            message('danger', 'Assessment reference unavailable. Reload the page before publishing.'); return;
        }
        if (window.prompt('Publication is immutable. Type PUBLISH to confirm:', '') !== 'PUBLISH') return;
        state.publishKey ||= crypto.randomUUID();
        const body = { clientRequestId: state.publishKey, assessmentReference: s.assessmentReference,
            academicBatchId: s.academicBatchId,
            visibleToStudent: el('examVisibleStudent').checked,
            visibleToGuardian: el('examVisibleGuardian').checked,
            publishNote: el('examPublishNote').value.trim() || null };
        state.busy = true; el('examPublish').disabled = true;
        try {
            await api('/api/assessment-administration/results/publish', 'POST', body);
            const snapshot = await api('/api/assessment-administration/results?' +
                new URLSearchParams({ assessmentId: s.assessmentId, academicBatchId: s.academicBatchId }));
            renderResults(snapshot, 'published'); state.publishKey = null;
            message('success', 'Results published. The saved snapshot is immutable.');
            state.busy = false; await loadRoster(); state.busy = true;
        } catch (error) {
            // An interrupted response may follow a successful commit. Never blindly issue a new publication.
            try {
                const snapshot = await api('/api/assessment-administration/results?' +
                    new URLSearchParams({ assessmentId: s.assessmentId, academicBatchId: s.academicBatchId }));
                renderResults(snapshot, 'published'); state.publishKey = null;
                message('success', 'Publication confirmed by the saved result snapshot.');
            } catch {
                message('danger', error.message + ' Verify publication status before retrying.');
            }
        } finally {
            state.busy = false;
            if (state.sheetMode === 'preview' && locked(selected())) el('examPublish').disabled = false;
        }
    }
    function csv(kind) {
        const roster = kind === 'marks' ? state.roster : kind === 'readiness' ? state.readiness : state.sheet;
        if (!roster) { message('warning', 'Load the register or result sheet before exporting.'); return; }
        const safe = value => /^[=+@\-\t\r]/.test(String(value ?? '')) ? "'" + String(value) : value;
        const quote = value => '"' + String(safe(value) ?? '').replace(/"/g, '""') + '"';
        const rows = kind === 'readiness'
            ? [['Roll', 'Code', 'Student', 'Subjects', 'Marks', 'Absent', 'Ready', 'Issues'],
                ...(roster.students || []).map(x => [x.rollNo, x.studentCode, x.studentName,
                    x.applicableSubjects, x.recordedMarks, x.absentMarks, x.isReady,
                    (x.issues || []).map(i => i.subjectName + ': ' + i.code).join('; ')])]
            : kind === 'marks'
            ? [['Roll', 'Student code', 'Student', 'Mark', 'Absent', 'Withheld', 'Grade'],
                ...(roster.students || []).map(x => [x.rollNo, x.studentCode, x.studentName,
                    x.obtainedMarks, x.isAbsent, x.isWithheld, x.gradeLetter])]
            : [['Position', 'Roll', 'Student code', 'Student', 'Marks', 'Maximum', 'Percentage', 'GPA', 'Grade', 'Passed', 'Withheld', 'Status'],
                ...(roster.results || []).map(x => [x.meritPosition, x.rollNo, x.studentCode, x.studentName,
                    x.totalMarks, x.maximumMarks, x.percentage, x.gradePointAverage, x.gradeLetter,
                    x.isPassed, x.isWithheld, state.sheetMode])];
        const output = rows.map(row => row.map(quote).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF' + output], { type: 'text/csv;charset=utf-8' }));
        const anchor = document.createElement('a'); anchor.href = url;
        anchor.download = 'assessment-' + kind + (kind === 'results' ? '-' + state.sheetMode : '') + '.csv';
        document.body.append(anchor); anchor.click(); anchor.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    el('examScopeSearch').addEventListener('submit', event => {
        event.preventDefault();
        state.scopeSearch = el('examScopeTerm').value.trim();
        loadScopes(1);
    });
    el('examScopePrev').addEventListener('click', () => loadScopes(state.scopePage - 1));
    el('examScopeNext').addEventListener('click', () => loadScopes(state.scopePage + 1));
    el('examFilters').addEventListener('submit', loadRoster);
    el('examExam').addEventListener('change', examChanged);
    el('examBatch').addEventListener('change', batchChanged);
    el('examSubject').addEventListener('change', () => { resetRoster(); resetResults(); });
    el('examRoster').addEventListener('change', event => {
        if (!event.target.matches('input[type="checkbox"]')) return;
        const tr = event.target.closest('tr');
        if (!tr?.dataset.registrationId) return;
        const [mark, absent, withheld] = tr.querySelectorAll('input');
        mark.disabled = absent.checked || withheld.checked || !marksOpen(selected()) || !!state.roster?.isResultPublished;
    });
    el('examSaveMarks').addEventListener('click', saveMarks);
    el('examStateAdvance')?.addEventListener('click', advanceAssessmentState);
    el('examReadinessLoad')?.addEventListener('click', () => loadReadiness(1));
    el('examReadinessPrev')?.addEventListener('click', () => loadReadiness(state.readinessPage - 1));
    el('examReadinessNext')?.addEventListener('click', () => loadReadiness(state.readinessPage + 1));
    el('examReadinessCsv')?.addEventListener('click', () => csv('readiness'));
    el('examLoadResults')?.addEventListener('click', () => resultAction('published'));
    el('examGenerate')?.addEventListener('click', () => resultAction('preview'));
    el('examPublish')?.addEventListener('click', publish);
    el('examMarksCsv').addEventListener('click', () => csv('marks'));
    el('examResultsCsv')?.addEventListener('click', () => csv('results'));
    el('examPrint').addEventListener('click', () => window.print());
    window.addEventListener('assessment-publication-withdrawn', () => {
        resetResults();
        resetRoster();
        el('examResultSummary').textContent = 'Publication withdrawn. Reload versions or preview after reconciliation.';
    });
    init();
})();