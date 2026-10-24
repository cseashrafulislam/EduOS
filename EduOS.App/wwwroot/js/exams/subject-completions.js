(() => {
    'use strict';
    const root = document.getElementById('subjectCompletionWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { rows: [], page: 1, size: 20, total: 0, busy: false };
    const cell = value => { const td = document.createElement('td'); td.textContent = value == null ? '' : String(value); return td; };
    function notice(message, error = false) {
        const box = el('completionNotice'); box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = message;
    }
    async function api(path, method = 'GET', body) {
        const response = await fetch(path, { method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
                ...(method === 'GET' ? {} : { RequestVerificationToken: token }) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const result = await response.json().catch(() => null);
        if (!response.ok || result?.success !== true) throw new Error(result?.message || 'Request failed.');
        return result.data;
    }
    const completed = x => x === 5 || x === 'Completed';
    async function init() {
        const scopes = await api('/api/exams/workflow/scopes') || [];
        const batches = new Map();
        for (const s of scopes) if (!batches.has(s.academicBatchId))
            batches.set(s.academicBatchId, s.academicBatchName);
        const select = el('completionBatch'); select.replaceChildren(new Option('Select academic batch', ''));
        for (const [id, name] of batches) select.add(new Option(name, String(id)));
        if (!batches.size) notice('No assessment batches available for reconciliation.', true);
    }
    function render() {
        const body = el('completionRows'); body.replaceChildren();
        for (const row of state.rows) {
            const tr = document.createElement('tr');
            tr.append(cell(row.rollNo), cell(row.studentName + ' (' + row.studentCode + ')'),
                cell(row.subjectName + ' (' + row.subjectCode + ')'),
                cell(completed(row.state) ? 'Completed' : 'Approved'), cell(row.statusMessage));
            const td = document.createElement('td'); td.className = 'completion-action';
            if (row.canComplete && !completed(row.state)) {
                const btn = document.createElement('button'); btn.type = 'button';
                btn.className = 'btn btn-sm btn-outline-primary'; btn.textContent = 'Complete';
                btn.disabled = state.busy; btn.addEventListener('click', () => complete(row)); td.append(btn);
            }
            tr.append(td); body.append(tr);
        }
        if (!state.rows.length) { const tr = document.createElement('tr'), td = cell('No registrations found.'); td.colSpan = 6; tr.append(td); body.append(tr); }
        el('completionCount').textContent = state.total + ' registrations · Page ' + state.page;
        el('completionPrev').disabled = state.busy || state.page <= 1;
        el('completionNext').disabled = state.busy || state.page * state.size >= state.total;
    }
    async function load() {
        const batch = el('completionBatch').value;
        if (!batch) { state.rows = []; state.total = 0; render(); return; }
        const query = new URLSearchParams({ academicBatchId: batch, page: String(state.page), pageSize: String(state.size) });
        if (el('completionSearch').value.trim()) query.set('search', el('completionSearch').value.trim());
        try {
            const data = await api('/api/exams/workflow/subject-completions?' + query);
            state.rows = data.items || []; state.total = data.totalCount || 0; render();
        } catch (error) { notice(error.message, true); }
    }
    async function complete(row) {
        if (state.busy || !row.canComplete) return;
        const reason = prompt('Complete ' + row.studentName + ' / ' + row.subjectName + '? Enter audit reason:', 'Published results verified');
        if (reason === null) return;
        if (!reason.trim() || reason.length > 500) { notice('Audit reason must be 1–500 characters.', true); return; }
        state.busy = true; render();
        try {
            await api('/api/exams/workflow/subject-completions/' + row.registrationId + '/complete', 'POST',
                { rowVersion: row.rowVersion, reason: reason.trim() });
            notice('Subject completion saved with audit trail.'); await load();
        } catch (error) {
            notice(error.message + ' Refresh and review evidence before retrying.', true); await load();
        } finally { state.busy = false; render(); }
    }
    function csv() {
        const data = [['Roll','Student','Subject','State','Evidence'],
            ...state.rows.map(x => [x.rollNo,x.studentName,x.subjectName,completed(x.state)?'Completed':'Approved',x.statusMessage])];
        const text = data.map(row => row.map(x => '"' + String(x ?? '').replace(/"/g, '""') + '"').join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF', text], { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a'); a.href = url; a.download = 'subject-completions.csv'; a.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    el('completionFilter').addEventListener('submit', event => { event.preventDefault(); state.page = 1; load(); });
    el('completionPrev').addEventListener('click', () => { if (state.page > 1) { state.page--; load(); } });
    el('completionNext').addEventListener('click', () => { if (state.page * state.size < state.total) { state.page++; load(); } });
    el('completionCsv').addEventListener('click', csv);
    el('completionPrint').addEventListener('click', () => window.print());
    init().catch(error => notice(error.message, true));
})();
