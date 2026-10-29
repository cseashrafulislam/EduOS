(() => {
    'use strict';
    const root = document.getElementById('assessmentAttemptWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { scopes: [], selection: null, candidatePage: 1, candidateTotal: 0,
        queuePage: 1, queueTotal: 0, size: 30, busy: false };
    async function api(path, method = 'GET', body) {
        const res = await fetch(path, { method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json', ...(body ? { 'Content-Type': 'application/json' } : {}),
                ...(method === 'GET' ? {} : { RequestVerificationToken: token }) },
            ...(body ? { body: JSON.stringify(body) } : {}) });
        const result = await res.json().catch(() => null);
        if (!res.ok || !result?.success) throw new Error(result?.message || 'Unable to process request.');
        return result.data;
    }
    function tell(message, danger = false) {
        const box = el('attemptAlert'); box.textContent = message;
        box.className = 'alert alert-' + (danger ? 'danger' : 'info');
    }
    function td(value) {
        const node = document.createElement('td'); node.textContent = value == null ? '' : String(value);
        return node;
    }
    function empty(target, count, message) {
        const body = el(target); body.replaceChildren(); const row = document.createElement('tr');
        const cell = td(message); cell.colSpan = count; cell.className = 'text-center text-muted';
        row.append(cell); body.append(row);
    }
    function button(label, click, disabled = false) {
        const b = document.createElement('button'); b.type = 'button'; b.textContent = label;
        b.className = 'btn btn-sm btn-outline-primary'; b.disabled = disabled;
        b.addEventListener('click', click); return b;
    }
    async function searchScopes() {
        if (state.busy) return;
        const term = el('attemptScopeSearch').value.trim();
        state.busy = true;
        try {
            const data = await api('/api/assessment-administration/scopes/page?' +
                new URLSearchParams({ search: term, page: '1', pageSize: '100' }));
            const unique = new Map();
            for (const s of data.items || []) unique.set(String(s.assessmentSubjectId), s);
            state.scopes = [...unique.values()];
            const selection = el('attemptSubject'); selection.replaceChildren(new Option('Select a subject', ''));
            for (const row of state.scopes) {
                const label = `${row.assessmentName} / ${row.academicBatchName} / ${row.subjectName}`;
                selection.add(new Option(label, String(row.assessmentSubjectId)));
            }
            el('attemptScopeInfo').textContent = `Loaded ${state.scopes.length} subject options. Search again to refine.`;
            tell('Select a subject from the results.');
        } catch (err) { tell(err.message, true); }
        finally { state.busy = false; }
    }
    async function chooseSubject() {
        const subId = Number(el('attemptSubject').value);
        state.selection = state.scopes.find(x => Number(x.assessmentSubjectId) === subId) || null;
        state.candidatePage = state.queuePage = 1;
        if (!state.selection) { empty('attemptCandidates', 4, 'Select a subject'); empty('attemptQueue', 5, 'Select an assessment'); return; }
        try {
            const detail = await api('/api/assessment-administration/assessments/' +
                encodeURIComponent(state.selection.assessmentReference));
            if (!Number(detail.attemptMode)) {
                tell('This is a regular assessment. Select an assessment configured as Retake or Improvement.', true);
                empty('attemptCandidates', 4, 'Not a targeted assessment');
                empty('attemptQueue', 5, 'Not a targeted assessment'); return;
            }
            tell((Number(detail.attemptMode) === 1 ? 'Retake' : 'Improvement') +
                ' assessment · requests need published course evidence and approval before marks entry.');
            await Promise.all([loadCandidates(), loadQueue()]);
        } catch (err) { tell(err.message, true); }
    }
    async function loadCandidates() {
        if (!state.selection) return;
        const query = new URLSearchParams({ assessmentSubjectId: String(state.selection.assessmentSubjectId),
            search: el('attemptStudentSearch').value.trim(), page: String(state.candidatePage),
            pageSize: String(state.size) });
        const data = await api('/api/assessment-administration/attempts/candidates?' + query);
        state.candidateTotal = data.totalCount || 0;
        const body = el('attemptCandidates'); body.replaceChildren();
        for (const item of data.items || []) {
            const row = document.createElement('tr');
            row.append(td(item.rollNo), td(`${item.studentCode} — ${item.studentName}`), td(item.priorOutcome));
            const actions = td('');
            if (item.canRequest) actions.append(button('Request', () => requestAttempt(item)));
            else actions.textContent = 'Unavailable or already requested';
            row.append(actions); body.append(row);
        }
        if (!body.children.length) empty('attemptCandidates', 4, 'No matching registrations');
        pageControls('attemptCandidate', state.candidatePage, state.candidateTotal);
    }
    async function requestAttempt(item) {
        const reason = el('attemptRequestReason').value.trim();
        if (!reason || reason.length > 500) { tell('Enter a request reason (up to 500 characters).', true); return; }
        if (state.busy || !state.selection || !item.priorResultPublicationId) return;
        state.busy = true;
        try {
            await api('/api/assessment-administration/attempts', 'POST', {
                assessmentSubjectId: state.selection.assessmentSubjectId,
                studentSubjectRegistrationId: item.studentSubjectRegistrationId,
                priorResultPublicationId: item.priorResultPublicationId, reason
            });
            tell('Attempt request saved. A controller decision is required.');
            await Promise.all([loadCandidates(), loadQueue()]);
        } catch (err) { tell(err.message, true); }
        finally { state.busy = false; }
    }
    async function loadQueue() {
        if (!state.selection) return;
        const query = new URLSearchParams({ assessmentId: String(state.selection.assessmentId),
            page: String(state.queuePage), pageSize: String(state.size) });
        const data = await api('/api/assessment-administration/attempts?' + query);
        state.queueTotal = data.totalCount || 0;
        const body = el('attemptQueue'); body.replaceChildren();
        for (const item of data.items || []) {
            const row = document.createElement('tr');
            const status = { 1: 'Requested', 2: 'Approved', 3: 'Rejected' }[Number(item.decision)] || 'Unknown';
            row.append(td(`${item.studentCode} — ${item.studentName}`), td(item.subjectName),
                td(item.reason), td(status));
            const actions = td('');
            if (Number(item.decision) === 1) {
                actions.append(button('Approve', () => decide(item, 2)));
                actions.append(document.createTextNode(' '));
                actions.append(button('Reject', () => decide(item, 3)));
            } else actions.textContent = item.decisionNote || 'Decision recorded';
            row.append(actions); body.append(row);
        }
        if (!body.children.length) empty('attemptQueue', 5, 'No attempt requests');
        pageControls('attemptQueue', state.queuePage, state.queueTotal);
    }
    async function decide(item, decision) {
        const reason = el('attemptDecisionReason').value.trim();
        if (!reason || reason.length > 500) { tell('Enter an approval/rejection note.', true); return; }
        if (!window.confirm((decision === 2 ? 'Approve' : 'Reject') + ' this attempt?')) return;
        if (state.busy) return;
        state.busy = true;
        try {
            await api('/api/assessment-administration/attempts/' + encodeURIComponent(item.id) + '/decision',
                'POST', { decision, reason, rowVersion: item.rowVersion });
            tell('Decision recorded with audit history.');
            await Promise.all([loadCandidates(), loadQueue()]);
        } catch (err) { tell(err.message + ' Reload before retrying.', true); }
        finally { state.busy = false; }
    }
    function pageControls(prefix, page, total) {
        el(prefix + 'Prev').disabled = page <= 1;
        el(prefix + 'Next').disabled = page * state.size >= total;
        el(prefix + 'Page').textContent = `Page ${page} · ${total} record(s)`;
    }
    el('attemptFindForm').addEventListener('submit', event => { event.preventDefault(); void searchScopes(); });
    el('attemptSubject').addEventListener('change', () => { void chooseSubject(); });
    el('attemptCandidateForm').addEventListener('submit', event => {
        event.preventDefault(); state.candidatePage = 1; void loadCandidates().catch(err => tell(err.message, true));
    });
    for (const kind of ['Candidate', 'Queue']) {
        for (const [suffix, step] of [['Prev', -1], ['Next', 1]]) {
            el('attempt' + kind + suffix).addEventListener('click', () => {
                const key = kind.toLowerCase() + 'Page'; state[key] += step;
                void (kind === 'Candidate' ? loadCandidates() : loadQueue()).catch(err => tell(err.message, true));
            });
        }
    }
    pageControls('attemptCandidate', 1, 0); pageControls('attemptQueue', 1, 0);
    void searchScopes();
})();
