(() => {
    'use strict';
    const root = document.getElementById('componentMarksWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { scopes: [], roster: null, busy: false };
    const option = (value, label) => new Option(label, String(value));
    const cell = value => { const td = document.createElement('td'); td.textContent = value == null ? '' : String(value); return td; };
    const two = n => Number.isFinite(n) && Math.abs(Math.round(n * 100) - n * 100) < 1e-7;
    function notice(message, error = false) {
        const box = el('componentMarksNotice');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = message;
    }
    async function api(url, method = 'GET', body) {
        const response = await fetch(url, { method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json',
                ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
                ...(method === 'GET' ? {} : { RequestVerificationToken: token }) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const result = await response.json().catch(() => null);
        if (!response.ok || result?.success !== true) throw new Error(result?.message || 'Request failed.');
        return result.data;
    }
    function selection() {
        const index = Number(el('cmScope').value);
        return Number.isInteger(index) ? state.scopes[index] : null;
    }
    function component() { return state.roster?.components?.find(x => String(x.id) === el('cmComponent').value); }
    function clear() {
        state.roster = null;
        el('cmRows').replaceChildren();
        el('cmComponent').replaceChildren(option('', 'Load a subject first'));
        el('cmSave').disabled = true;
        el('cmInfo').textContent = '';
    }
    async function init() {
        state.scopes = await api('/api/exams/workflow/scopes') || [];
        el('cmScope').replaceChildren(option('', 'Select assessment / batch / subject'));
        state.scopes.forEach((x, i) => el('cmScope').add(option(i,
            x.assessmentName + ' / ' + x.academicBatchName + ' / ' + x.subjectName)));
        if (!state.scopes.length) notice('No authorized assessment subjects found.', true);
    }
    function render() {
        const roster = state.roster, comp = component(), body = el('cmRows');
        body.replaceChildren();
        el('cmSave').disabled = true;
        if (!roster || !comp) return;
        const editable = roster.isEditable && !state.busy;
        el('cmHeading').textContent = roster.assessmentName + ' / ' + roster.subjectName + ' / ' + comp.name;
        el('cmInfo').textContent = roster.students.length + ' students · Full ' + comp.fullMarks +
            ' · Pass ' + comp.passMarks + (roster.isResultPublished ? ' · Published (locked)' : '');
        for (const student of roster.students) {
            const tr = document.createElement('tr');
            const previous = (student.marks || []).find(x => x.assessmentComponentId === comp.id);
            const mark = document.createElement('input'); mark.type = 'number'; mark.className = 'form-control form-control-sm';
            mark.min = '0'; mark.max = String(comp.fullMarks); mark.step = '0.01';
            mark.value = previous ? String(previous.obtainedMarks) : '';
            mark.dataset.initial = mark.value;
            const absent = document.createElement('input'); absent.type = 'checkbox'; absent.className = 'form-check-input';
            absent.checked = !!previous?.isAbsent; absent.dataset.initial = String(absent.checked);
            const withheld = document.createElement('input'); withheld.type = 'checkbox'; withheld.className = 'form-check-input';
            withheld.checked = !!previous?.isWithheld; withheld.dataset.initial = String(withheld.checked);
            mark.disabled = absent.disabled = withheld.disabled = !editable || !student.studentAssessmentMarkId;
            if (absent.checked || withheld.checked) mark.disabled = true;
            tr.dataset.masterId = String(student.studentAssessmentMarkId || '');
            tr.dataset.recordId = String(previous?.id || '');
            tr.dataset.version = previous?.rowVersion || '';
            tr.append(cell(student.rollNo), cell(student.studentCode), cell(student.studentName));
            for (const input of [mark, absent, withheld]) { const td = document.createElement('td'); td.append(input); tr.append(td); }
            tr.append(cell(student.studentAssessmentMarkId ? (previous ? 'Recorded' : 'Pending') : 'Save subject mark first'));
            body.append(tr);
        }
        if (!roster.students.length) {
            const tr = document.createElement('tr'), td = cell('No eligible registered students.');
            td.colSpan = 7; tr.append(td); body.append(tr);
        }
        el('cmSave').disabled = !editable || !roster.students.some(x => x.studentAssessmentMarkId);
    }
    async function load(event) {
        event?.preventDefault();
        if (state.busy) return;
        const s = selection();
        if (!s) { notice('Select an assessment, batch and subject.', true); return; }
        state.busy = true; el('cmLoad').disabled = true;
        try {
            const q = new URLSearchParams({ assessmentId: s.assessmentId,
                academicBatchId: s.academicBatchId, assessmentSubjectId: s.assessmentSubjectId });
            const roster = await api('/api/exams/workflow/component-marks/roster?' + q);
            const old = el('cmComponent').value;
            state.roster = roster;
            el('cmComponent').replaceChildren(option('', 'Select component'));
            for (const c of roster.components || []) el('cmComponent').add(option(c.id, c.name + ' (' + c.code + ')'));
            if (roster.components?.some(x => String(x.id) === old)) el('cmComponent').value = old;
            else if (roster.components?.length) el('cmComponent').value = String(roster.components[0].id);
            notice(roster.isEditable ? 'Roster loaded.' : 'Roster is read-only until marks entry is open.', !roster.isEditable);
        } catch (error) { clear(); notice(error.message, true); }
        finally { state.busy = false; el('cmLoad').disabled = false; render(); }
    }
    function changes() {
        const comp = component(), changed = [];
        if (!comp || !state.roster?.isEditable) throw new Error('Component is not open for marks entry.');
        for (const tr of el('cmRows').querySelectorAll('tr[data-master-id]')) {
            const [mark, absent, withheld] = tr.querySelectorAll('input');
            if (mark.value === mark.dataset.initial &&
                String(absent.checked) === absent.dataset.initial &&
                String(withheld.checked) === withheld.dataset.initial) continue;
            if (!tr.dataset.masterId) throw new Error('Save the student subject mark before component marks.');
            if (absent.checked && withheld.checked) throw new Error('A student cannot be both absent and withheld.');
            if (!absent.checked && !withheld.checked && mark.value.trim() === '')
                throw new Error('Enter a component mark, or select absent/withheld.');
            const value = absent.checked || withheld.checked ? 0 : Number(mark.value);
            if (!two(value) || value < 0 || value > comp.fullMarks)
                throw new Error('Marks must be within the component maximum with at most two decimals.');
            changed.push({ id: tr.dataset.recordId, version: tr.dataset.version,
                body: { assessmentComponentId: comp.id, studentAssessmentMarkId: Number(tr.dataset.masterId),
                    obtainedMarks: value, isAbsent: absent.checked, isWithheld: withheld.checked,
                    rowVersion: tr.dataset.version || null } });
        }
        if (!changed.length) throw new Error('No changed marks to save.');
        if (changed.length > 20) throw new Error('Save at most 20 changed rows per operation.');
        return changed;
    }
    async function save() {
        if (state.busy) return;
        let rows;
        try { rows = changes(); } catch (error) { notice(error.message, true); return; }
        state.busy = true; el('cmSave').disabled = true;
        let saved = 0, error = null;
        for (const row of rows) {
            try {
                await api('/api/exams/workflow/component-marks' + (row.id ? '/' + encodeURIComponent(row.id) : ''),
                    row.id ? 'PUT' : 'POST', row.body);
                saved++;
            } catch (e) { error = e; break; }
        }
        state.busy = false;
        await load();
        notice(error ? saved + ' saved; stopped on error: ' + error.message +
            '. Review refreshed records before retrying.' : saved + ' component mark(s) saved.', !!error);
    }
    function csv() {
        const comp = component();
        if (!state.roster || !comp) return;
        const safe = v => /^[=+@\-\t\r]/.test(String(v ?? '')) ? "'" + String(v) : v;
        const quote = v => '"' + String(safe(v) ?? '').replace(/"/g, '""') + '"';
        const rows = [['Roll', 'Student code', 'Student', 'Component', 'Mark', 'Absent', 'Withheld'],
            ...state.roster.students.map(s => {
                const m = (s.marks || []).find(x => x.assessmentComponentId === comp.id);
                return [s.rollNo, s.studentCode, s.studentName, comp.name,
                    m ? m.obtainedMarks : '', m?.isAbsent ?? '', m?.isWithheld ?? ''];
            })];
        const blob = new Blob(['\uFEFF' + rows.map(r => r.map(quote).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob), link = document.createElement('a');
        link.href = url; link.download = 'component-marks.csv'; document.body.append(link); link.click();
        link.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    el('componentMarksFilters').addEventListener('submit', load);
    el('cmScope').addEventListener('change', clear);
    el('cmComponent').addEventListener('change', render);
    el('cmRows').addEventListener('change', event => {
        if (event.target.type !== 'checkbox') return;
        const inputs = event.target.closest('tr')?.querySelectorAll('input');
        if (!inputs?.length) return;
        if (event.target.checked) {
            for (const other of [inputs[1], inputs[2]]) if (other !== event.target) other.checked = false;
        }
        inputs[0].disabled = !state.roster?.isEditable || inputs[1].checked || inputs[2].checked;
    });
    el('cmSave').addEventListener('click', save);
    el('cmCsv').addEventListener('click', csv);
    el('cmPrint').addEventListener('click', () => window.print());
    init().catch(error => notice(error.message, true));
})();
