(() => {
    'use strict';
    const root = document.getElementById('markReviewWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const canReview = root.dataset.canReview === 'true';
    const state = { page: 1, pageSize: 30, total: 0, review: null, busy: false, requestId: null };
    const labels = { 1: 'Submitted', 2: 'Rejected', 3: 'Approved' };
    const history = { cursors: [null], page: 0, items: [], hasMore: false, next: null, sequence: 0 };
    function resetHistory() {
        history.sequence++;
        history.cursors = [null]; history.page = 0;
        history.items = []; history.hasMore = false; history.next = null;
    }
    const cell = value => { const td = document.createElement('td'); td.textContent = value == null ? '' : String(value); return td; };
    const notice = (text, error = false) => {
        const box = el('markReviewNotice');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = text;
    };
    async function api(path, method = 'GET', body) {
        const response = await fetch(path, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json',
                ...(method === 'GET' ? {} : { RequestVerificationToken: token }),
                ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        const data = await response.json().catch(() => null);
        if (!response.ok || data?.success !== true) throw new Error(data?.message || 'Request failed.');
        return data.data;
    }
    function controls() {
        const review = state.review;
        el('markReviewSubmit').disabled = state.busy || !review?.canSubmit;
        if (canReview) {
            el('markReviewApprove').disabled = state.busy || !review?.canReview;
            el('markReviewReject').disabled = state.busy || !review?.canReview;
        }
        el('markReviewCsv').disabled = !review || !history.items.length;
    }
    function render() {
        const review = state.review;
        el('markReviewStatus').textContent = review
            ? (labels[review.currentDecision] || 'Not submitted') : 'Select a subject';
        const tbody = el('markReviewRows'); tbody.replaceChildren();
        for (const event of (review ? history.items : [])) {
            const tr = document.createElement('tr');
            tr.append(cell(event.occurredAt), cell(labels[event.decision] || 'Unknown'),
                cell(event.actorUserId), cell(event.reason));
            tbody.append(tr);
        }
        if (!tbody.children.length) {
            const tr = document.createElement('tr'), td = cell('No review decisions yet.');
            td.colSpan = 4; tr.append(td); tbody.append(tr);
        }
        el('markReviewCount').textContent = review
            ? 'Page ' + (history.page + 1) + ' · ' + history.items.length + ' decisions'
            : '';
        el('markReviewHistoryPrev').disabled = !review || history.page === 0;
        el('markReviewHistoryNext').disabled = !review || !history.hasMore;
        controls();
    }
    async function loadScopes() {
        state.review = null; render();
        const select = el('markReviewSubject');
        const params = new URLSearchParams({ page: String(state.page),
            pageSize: String(state.pageSize), search: el('markReviewTerm').value.trim() });
        try {
            const data = await api('/api/assessment-administration/scopes/page?' + params);
            state.total = data.totalCount || 0;
            select.replaceChildren(new Option('Select authorized subject', ''));
            for (const scope of data.items || [])
                select.add(new Option(scope.assessmentName + ' / ' + scope.academicBatchName +
                    ' / ' + scope.subjectName, String(scope.assessmentSubjectId)));
            el('markReviewPage').textContent = state.total + ' scopes · Page ' + state.page;
            el('markReviewPrev').disabled = state.page <= 1;
            el('markReviewNext').disabled = state.page * state.pageSize >= state.total;
        } catch (error) { notice(error.message, true); }
    }
    async function loadHistory(reset = false) {
        if (reset) resetHistory();
        const id = el('markReviewSubject').value;
        if (!id || !state.review) { render(); return; }
        const sequence = ++history.sequence;
        const params = new URLSearchParams({ pageSize: '25' });
        const beforeId = history.cursors[history.page];
        if (beforeId != null) params.set('beforeId', String(beforeId));
        const decision = el('markReviewHistoryDecision').value;
        if (decision) params.set('decision', decision);
        try {
            const page = await api('/api/assessment-administration/marks/reviews/' +
                encodeURIComponent(id) + '/history?' + params);
            if (sequence !== history.sequence || el('markReviewSubject').value !== id) return;
            history.items = page.items || [];
            history.hasMore = !!page.hasMore;
            history.next = page.nextBeforeId;
            render();
        } catch (error) {
            if (sequence === history.sequence) notice(error.message, true);
        }
    }
    async function loadReview() {
        resetHistory();
        state.review = null; render();
        const id = el('markReviewSubject').value;
        if (!id) return;
        try {
            const review = await api('/api/assessment-administration/marks/reviews/' + encodeURIComponent(id));
            if (el('markReviewSubject').value !== id) return;
            state.review = review;
            render();
            await loadHistory();
        } catch (error) { notice(error.message, true); }
    }
    async function decide(decision) {
        const review = state.review;
        if (!review || state.busy || (decision === 1 ? !review.canSubmit : !review.canReview)) return;
        let reason = '';
        if (decision === 2) {
            const input = window.prompt('Reason for rejecting these marks (required):', '');
            if (input === null) return;
            reason = input.trim();
            if (!reason || reason.length > 500) { notice('Provide a rejection reason of 1–500 characters.', true); return; }
        } else if (!window.confirm((decision === 1 ? 'Submit' : 'Approve') +
            ' this subject marks version?')) return;
        state.busy = true; controls();
        state.requestId ||= crypto.randomUUID();
        try {
            const id = el('markReviewSubject').value;
            state.review = await api('/api/assessment-administration/marks/reviews/' +
                encodeURIComponent(id) + '/decision', 'POST', {
                    clientRequestId: state.requestId, assessmentRowVersion: review.assessmentRowVersion,
                    decision, reason: reason || null
                });
            state.requestId = null; notice('Review decision recorded.'); render();
            await loadHistory(true); if (canReview) loadQueue();
        } catch (error) {
            notice(error.message + ' Refresh before retrying a conflicting decision.', true);
            await loadReview();
            state.requestId = null;
        } finally { state.busy = false; controls(); }
    }
    function csv() {
        const review = state.review;
        if (!review) return;
        const rows = [['UTC time','Decision','Actor ID','Reason'],
            ...history.items.map(x => [x.occurredAt, labels[x.decision], x.actorUserId, x.reason])];
        const safe = value => /^[=+@\-\t\r]/.test(String(value ?? '')) ? "'" + value : value;
        const content = rows.map(row => row.map(value =>
            '"' + String(safe(value) ?? '').replace(/"/g, '""') + '"').join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF', content], { type: 'text/csv;charset=utf-8' }));
        const link = document.createElement('a'); link.href = url; link.download = 'mark-review-history.csv'; link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }

    const queue = { page: 1, pageSize: 25, total: 0, rows: [], sequence: 0 };
    function csvRows(filename, rows) {
        const safe = value => /^[=+@\t\r-]/.test(String(value ?? '')) ? "'" + value : value;
        const content = rows.map(row => row.map(value =>
            '"' + String(safe(value) ?? '').replace(/"/g, '""') + '"').join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF', content], { type: 'text/csv;charset=utf-8' }));
        const link = document.createElement('a');
        link.href = url; link.download = filename; link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    function renderQueue() {
        const tbody = el('markReviewQueueRows'); tbody.replaceChildren();
        for (const item of queue.rows) {
            const tr = document.createElement('tr');
            const status = item.decision == null ? 'Unsubmitted' :
                (labels[item.decision] || 'Unknown') + (item.isCurrent ? '' : ' · Stale');
            tr.append(cell(item.assessmentName), cell(item.academicBatchName),
                cell(item.subjectName), cell(status), cell(item.lastDecisionAt));
            const action = document.createElement('td');
            const button = document.createElement('button');
            button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
            button.textContent = item.canReview ? 'Review' : 'Open';
            button.addEventListener('click', () => {
                const select = el('markReviewSubject'), id = String(item.assessmentSubjectId);
                if (![...select.options].some(option => option.value === id))
                    select.add(new Option(item.assessmentName + ' / ' +
                        item.academicBatchName + ' / ' + item.subjectName, id));
                select.value = id;
                loadReview();
                select.focus();
            });
            action.append(button); tr.append(action); tbody.append(tr);
        }
        if (!tbody.children.length) {
            const tr = document.createElement('tr'), td = cell('No matching subjects.');
            td.colSpan = 6; tr.append(td); tbody.append(tr);
        }
        el('markReviewQueuePage').textContent = queue.total + ' subjects · Page ' + queue.page;
        el('markReviewQueuePrev').disabled = queue.page <= 1;
        el('markReviewQueueNext').disabled = queue.page * queue.pageSize >= queue.total;
        el('markReviewQueueCsv').disabled = !queue.rows.length;
    }
    async function loadQueue() {
        if (!canReview) return;
        const sequence = ++queue.sequence;
        const params = new URLSearchParams({ page: String(queue.page),
            pageSize: String(queue.pageSize), status: el('markReviewQueueStatus').value,
            search: el('markReviewQueueTerm').value.trim() });
        try {
            const data = await api('/api/assessment-administration/marks/reviews/queue?' + params);
            if (sequence !== queue.sequence) return;
            queue.rows = data.items || []; queue.total = data.totalCount || 0;
            renderQueue();
        } catch (error) {
            if (sequence === queue.sequence) notice(error.message, true);
        }
    }
    if (canReview) {
        el('markReviewQueueSearch').addEventListener('submit', event => {
            event.preventDefault(); queue.page = 1; loadQueue();
        });
        el('markReviewQueuePrev').addEventListener('click', () => {
            if (queue.page > 1) { queue.page--; loadQueue(); }
        });
        el('markReviewQueueNext').addEventListener('click', () => {
            if (queue.page * queue.pageSize < queue.total) { queue.page++; loadQueue(); }
        });
        el('markReviewQueueCsv').addEventListener('click', () => csvRows(
            'mark-review-queue.csv', [['Assessment','Batch','Subject','Status','Last decision UTC'],
            ...queue.rows.map(x => [x.assessmentName, x.academicBatchName, x.subjectName,
                x.decision == null ? 'Unsubmitted' : labels[x.decision] +
                    (x.isCurrent ? '' : ' (stale)'), x.lastDecisionAt])]));
    }

    el('markReviewSearch').addEventListener('submit', event => { event.preventDefault(); state.page = 1; loadScopes(); });
    el('markReviewPrev').addEventListener('click', () => { if (state.page > 1) { state.page--; loadScopes(); } });
    el('markReviewNext').addEventListener('click', () => { if (state.page * state.pageSize < state.total) { state.page++; loadScopes(); } });
    el('markReviewSubject').addEventListener('change', loadReview);
    el('markReviewSubmit').addEventListener('click', () => decide(1));
    if (canReview) {
        el('markReviewApprove').addEventListener('click', () => decide(3));
        el('markReviewReject').addEventListener('click', () => decide(2));
    }
    el('markReviewHistoryDecision').addEventListener('change', () => loadHistory(true));
    el('markReviewHistoryPrev').addEventListener('click', () => {
        if (history.page > 0) { history.page--; loadHistory(); }
    });
    el('markReviewHistoryNext').addEventListener('click', () => {
        if (history.hasMore && history.next != null) {
            history.cursors[history.page + 1] = history.next;
            history.page++; loadHistory();
        }
    });
    el('markReviewCsv').addEventListener('click', csv);
    el('markReviewPrint').addEventListener('click', () => window.print());
    loadScopes();
    if (canReview) loadQueue();
})();
