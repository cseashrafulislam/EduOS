(() => {
    'use strict';
    const root = document.getElementById('examWorkflow');
    if (!root) return;
    const el = id => document.getElementById(id);
    const canPublish = root.dataset.canPublish === 'true';
    const state = { scopes: [], roster: null, sheet: null, busy: false };
    const option = (value, label) => { const x = document.createElement('option'); x.value = String(value); x.textContent = label; return x; };
    const cell = value => { const x = document.createElement('td'); x.textContent = value == null ? '' : String(value); return x; };
    function message(type, text) { const x = el('examAlert'); x.className = 'alert alert-' + type; x.textContent = text; x.focus(); }
    function clearMessage() { const x = el('examAlert'); x.className = 'd-none'; x.textContent = ''; }
    async function api(url, method = 'GET', body) {
        const res = await fetch(url, { method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const data = await res.json().catch(() => null);
        if (!res.ok || !data?.success) throw new Error(data?.message || 'Request failed.');
        return data.data;
    }
    function select(id, values, placeholder) {
        const e = el(id), previous = e.value; e.replaceChildren(option('', placeholder));
        for (const x of values) e.append(option(x.id, x.label));
        if (values.some(x => String(x.id) === previous)) e.value = previous;
    }
    function unique(items, id, label) {
        return [...new Map(items.map(x => [x[id], { id: x[id], label: x[label] }])).values()];
    }
    function scope() {
        const examId = Number(el('examExam').value), sectionId = Number(el('examBatch').value), subjectId = Number(el('examSubject').value);
        const match = state.scopes.find(x => x.examId === examId && x.sectionId === sectionId && x.subjectId === subjectId);
        if (!match) throw new Error('Choose a scheduled assessment, batch and subject.');
        return { examId, classId: match.classId, sectionId, subjectId };
    }
    function examChanged() {
        const examId = Number(el('examExam').value);
        const rows = state.scopes.filter(x => x.examId === examId);
        select('examBatch', unique(rows, 'sectionId', 'sectionName'), 'Select batch');
        batchChanged();
    }
    function batchChanged() {
        const examId = Number(el('examExam').value), sectionId = Number(el('examBatch').value);
        const rows = state.scopes.filter(x => x.examId === examId && x.sectionId === sectionId);
        select('examSubject', unique(rows, 'subjectId', 'subjectName'), 'Select subject');
        resetResults(); resetRoster();
    }
    function resetRoster() {
        state.roster = null; el('examSaveMarks').disabled = true;
        el('examRosterInfo').textContent = '';
        const tbody = el('examRoster'); tbody.replaceChildren();
        const tr = document.createElement('tr'), td = cell('Load a subject to view its mark register.');
        td.colSpan = 6; td.className = 'text-center text-muted p-4'; tr.append(td); tbody.append(tr);
    }
    function resetResults() {
        state.sheet = null;
        if (!canPublish) return;
        el('examGenerate').disabled = !el('examBatch').value;
        el('examPublish').disabled = true;
        el('examResultSummary').textContent = 'Load results to review before publication.';
        el('examResults').replaceChildren();
    }
    async function init() {
        try {
            state.scopes = await api('/api/exams/workflow/scopes') || [];
            select('examExam', unique(state.scopes, 'examId', 'examName'), 'Select assessment');
            examChanged();
            if (!state.scopes.length) message('warning', 'No scheduled assessment subjects are available to your account. Configure the assessment, batch and subject offering first.');
        } catch (error) { message('danger', error.message); }
    }
    function renderRoster(data) {
        state.roster = data;
        const tbody = el('examRoster'); tbody.replaceChildren();
        el('examRosterInfo').textContent = (data.students?.length || 0) + ' student(s) · Full mark ' + data.fullMark + ' · Pass mark ' + data.passMark + (data.isResultPublished ? ' · Published / locked' : '');
        if (!data.students?.length) { const tr = document.createElement('tr'), td = cell('No enrolled students registered for this offering.'); td.colSpan = 6; tr.append(td); tbody.append(tr); }
        for (const student of data.students || []) {
            const tr = document.createElement('tr'); tr.dataset.studentReference = student.studentReference;
            const mark = document.createElement('input'); mark.type = 'number'; mark.min = '0'; mark.max = String(data.fullMark); mark.step = '0.01'; mark.className = 'form-control form-control-sm'; mark.style.minWidth = '6rem'; mark.value = student.obtainedMark == null ? '' : String(student.obtainedMark);
            mark.dataset.initialValue = mark.value; mark.dataset.mark = '1'; mark.disabled = !!data.isResultPublished || !!student.isAbsent;
            const absent = document.createElement('input'); absent.type = 'checkbox'; absent.className = 'form-check-input'; absent.dataset.absent = '1'; absent.checked = !!student.isAbsent; absent.dataset.initialAbsent = String(absent.checked); absent.disabled = !!data.isResultPublished;
            const markCell = document.createElement('td'), absentCell = document.createElement('td'); markCell.append(mark); absentCell.append(absent);
            tr.append(cell(student.roll), cell(student.studentCode), cell(student.studentName), markCell, absentCell, cell(student.grade || '—'));
            tbody.append(tr);
        }
        el('examSaveMarks').disabled = !!data.isResultPublished || !data.students?.length;
    }
    async function loadRoster(event) {
        if (event) event.preventDefault();
        let query; try { query = scope(); } catch (error) { message('danger', error.message); return; }
        clearMessage(); el('examLoadRoster').disabled = true;
        try { const q = new URLSearchParams(query); renderRoster(await api('/api/exams/workflow/mark-roster?' + q)); }
        catch (error) { resetRoster(); message('danger', error.message); }
        finally { el('examLoadRoster').disabled = false; }
    }
    function changes() {
        const result = [];
        for (const tr of el('examRoster').querySelectorAll('tr[data-student-reference]')) {
            const mark = tr.querySelector('[data-mark]'), absent = tr.querySelector('[data-absent]');
            if (mark.value === mark.dataset.initialValue && String(absent.checked) === absent.dataset.initialAbsent) continue;
            const raw = mark.value.trim();
            if (!absent.checked && raw === '') throw new Error('Enter a mark or explicitly mark absent for each changed student.');
            const number = absent.checked ? 0 : Number(raw);
            if (!Number.isFinite(number) || number < 0 || number > state.roster.fullMark || Math.round(number * 100) !== number * 100)
                throw new Error('Marks must be between zero and the full mark, with at most 2 decimal places.');
            result.push({ studentReference: tr.dataset.studentReference, obtainedMark: number, isAbsent: absent.checked });
        }
        if (!result.length) throw new Error('No changed marks to save.');
        if (result.length > 250) throw new Error('Save no more than 250 changed marks at once.');
        return result;
    }
    async function saveMarks() {
        if (state.busy || !state.roster || state.roster.isResultPublished) return;
        let body; try { body = { ...scope(), items: changes() }; } catch (error) { message('warning', error.message); return; }
        state.busy = true; el('examSaveMarks').disabled = true;
        try { renderRoster(await api('/api/exams/workflow/marks', 'POST', body)); message('success', 'Changed marks saved.'); }
        catch (error) { message('danger', error.message); }
        finally { state.busy = false; if (state.roster && !state.roster.isResultPublished) el('examSaveMarks').disabled = false; }
    }
    function renderResults(data) {
        state.sheet = data;
        if (!canPublish) return;
        el('examResultSummary').textContent = (data.examName || 'Assessment') + ' · ' + (data.subjectCount || 0) + ' subject(s) · ' + (data.results?.length || 0) + ' student(s)';
        const body = el('examResults'); body.replaceChildren();
        for (const item of data.results || []) {
            const tr = document.createElement('tr'); tr.append(
                cell(item.position || '—'), cell(item.roll), cell(item.studentName),
                cell(String(item.totalMark) + ' / ' + String(item.totalFullMark)),
                cell(item.percentage), cell(item.totalGPA), cell(item.finalGrade || '—'),
                cell(item.isPublished ? 'Published' : item.isPassed ? 'Passed (draft)' : 'Not passed (draft)'));
            body.append(tr);
        }
        const hasRows = data.results?.length > 0;
        el('examPublish').disabled = !hasRows || data.results.some(x => x.isPublished);
    }
    async function resultAction(action) {
        if (!canPublish || state.busy) return;
        let s; try { s = scope(); } catch (error) { message('danger', error.message); return; }
        delete s.subjectId;
        if (action === 'publish') {
            if (!state.sheet?.results?.length || state.sheet.results.some(x => x.isPublished)) return;
            if (window.prompt('Publishing makes results visible and immutable. Type PUBLISH to confirm:', '') !== 'PUBLISH') return;
        }
        if (action === 'generate' && !confirm('Regenerate draft results from saved marks? Published results cannot be regenerated.')) return;
        state.busy = true; el('examGenerate').disabled = true; el('examPublish').disabled = true; el('examLoadResults').disabled = true;
        try {
            const url = '/api/exams/workflow/results' + (action === 'view' ? '?' + new URLSearchParams(s) : action === 'generate' ? '/generate' : '/publish');
            renderResults(await api(url, action === 'view' ? 'GET' : 'POST', action === 'view' ? undefined : s));
            message('success', action === 'view' ? 'Result sheet loaded.' : action === 'generate' ? 'Draft results generated.' : 'Results published.');
            if (action === 'publish') await loadRoster();
        } catch (error) { message('danger', error.message); }
        finally { state.busy = false; el('examGenerate').disabled = !el('examBatch').value; el('examLoadResults').disabled = false; }
    }
    el('examFilters').addEventListener('submit', loadRoster);
    el('examExam').addEventListener('change', examChanged);
    el('examBatch').addEventListener('change', batchChanged);
    el('examSubject').addEventListener('change', () => { resetRoster(); resetResults(); });
    el('examRoster').addEventListener('change', event => {
        if (!event.target.matches('[data-absent]')) return;
        const tr = event.target.closest('tr'), mark = tr.querySelector('[data-mark]');
        mark.disabled = event.target.checked || !!state.roster?.isResultPublished;
    });
    el('examSaveMarks').addEventListener('click', saveMarks);
    el('examLoadResults')?.addEventListener('click', () => resultAction('view'));
    el('examGenerate')?.addEventListener('click', () => resultAction('generate'));
    el('examPublish')?.addEventListener('click', () => resultAction('publish'));
    init();
})();
