(() => {
    'use strict';
    const root = document.getElementById('examMarkChanges');
    if (!root) return;
    const el = id => document.getElementById(id);
    const state = { page: 1, size: 20, total: 0, items: [], busy: false, request: 0 };
    const cell = value => { const td = document.createElement('td'); td.textContent = value == null ? '' : String(value); return td; };
    const scope = () => {
        const assessmentId = Number(el('examExam').value);
        const academicBatchId = Number(el('examBatch').value);
        const assessmentSubjectId = Number(el('examSubject').value);
        return assessmentId > 0 && academicBatchId > 0 && assessmentSubjectId > 0
            ? { assessmentId, academicBatchId, assessmentSubjectId } : null;
    };
    const parse = value => {
        try { return value ? JSON.parse(value) : null; } catch { return null; }
    };
    const describe = value => {
        const x = parse(value);
        if (!x) return '—';
        return [x.componentId ? 'Component #' + x.componentId : 'Subject',
            x.obtainedMarks, x.isAbsent ? 'Absent' : '', x.isWithheld ? 'Withheld' : '',
            x.gradeLetter || ''].filter(x => x !== '' && x != null).join(' · ');
    };
    function render() {
        const body = el('examMarkChangesRows'); body.replaceChildren();
        for (const item of state.items) {
            const tr = document.createElement('tr'), evidence = parse(item.after);
            tr.append(cell(item.occurredAt), cell(evidence?.registrationId),
                cell(item.action), cell(describe(item.before)), cell(describe(item.after)),
                cell(item.actorUserId));
            body.append(tr);
        }
        if (!state.items.length) {
            const tr = document.createElement('tr'), td = cell('No recorded changes for this subject.');
            td.colSpan = 6; tr.append(td); body.append(tr);
        }
        el('examMarkChangesStatus').textContent = state.total + ' changes · Page ' + state.page;
        el('examMarkChangesPrev').disabled = state.busy || state.page <= 1;
        el('examMarkChangesNext').disabled = state.busy || state.page * state.size >= state.total;
        el('examMarkChangesCsv').disabled = state.busy || !state.items.length;
    }
    function clear() {
        state.request++; state.page = 1; state.total = 0; state.items = [];
        render();
    }
    async function load(page = 1) {
        const selection = scope();
        if (!selection) { clear(); el('examMarkChangesStatus').textContent = 'Select assessment, batch and subject.'; return; }
        const request = ++state.request; state.busy = true; state.page = page; render();
        try {
            const query = new URLSearchParams({ ...selection, page, pageSize: state.size });
            const response = await fetch('/api/assessment-administration/marks/history?' + query,
                { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
            const data = await response.json();
            if (!response.ok || data?.success !== true) throw new Error(data?.message || 'History unavailable.');
            if (request !== state.request) return;
            state.items = data.data?.items || []; state.total = data.data?.totalCount || 0;
        } catch (error) {
            if (request === state.request) {
                state.items = []; state.total = 0;
                el('examMarkChangesStatus').textContent = error.message;
            }
        } finally { if (request === state.request) { state.busy = false; render(); } }
    }
    function csv() {
        if (!state.items.length) return;
        const safe = x => /^[=+@\-\t\r]/.test(String(x ?? '')) ? "'" + String(x) : x;
        const quote = x => '"' + String(safe(x) ?? '').replace(/"/g, '""') + '"';
        const rows = [['UTC time','Registration ID','Action','Before','After','Actor ID'],
            ...state.items.map(x => [x.occurredAt, parse(x.after)?.registrationId,
                x.action, describe(x.before), describe(x.after), x.actorUserId])];
        const output = rows.map(x => x.map(quote).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF' + output], { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a'); a.href = url; a.download = 'assessment-mark-changes-page.csv';
        document.body.append(a); a.click(); a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    el('examMarkChangesLoad').addEventListener('click', () => load(1));
    el('examMarkChangesPrev').addEventListener('click', () => load(state.page - 1));
    el('examMarkChangesNext').addEventListener('click', () => load(state.page + 1));
    el('examMarkChangesCsv').addEventListener('click', csv);
    for (const id of ['examExam','examBatch','examSubject'])
        el(id).addEventListener('change', clear);
    window.addEventListener('assessment-marks-saved', () => load(1));
    render();
})();
