(() => {
    'use strict';
    const root = document.getElementById('examAdmitCards');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { scopes: [], scopePage: 1, scopeTotal: 0, scopeSize: 100,
        page: 1, pageSize: 20, data: null, busy: false };
    const text = (tag, value, className = '') => {
        const node = document.createElement(tag);
        node.textContent = value == null ? '' : String(value);
        if (className) node.className = className;
        return node;
    };
    const notice = (kind, value) => {
        const box = el('admitAlert');
        box.className = 'alert alert-' + kind + ' admit-controls';
        box.textContent = value;
    };
    async function get(url) {
        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json', RequestVerificationToken: token } });
        const payload = await response.json().catch(() => null);
        if (!response.ok || payload?.success !== true)
            throw new Error(payload?.message || 'Unable to load authorized admit cards.');
        return payload.data;
    }
    function selection() {
        const value = el('admitScope').value;
        return state.scopes.find(x => x.key === value);
    }
    function renderScopes() {
        const select = el('admitScope');
        select.replaceChildren(text('option', 'Select assessment / batch'));
        select.firstChild.value = '';
        const unique = new Map();
        for (const row of state.scopes) {
            const key = row.assessmentId + ':' + row.academicBatchId;
            if (!unique.has(key)) unique.set(key, { ...row, key });
        }
        state.scopes = Array.from(unique.values());
        for (const row of state.scopes) {
            const option = text('option', row.assessmentName + ' — ' + row.academicBatchName);
            option.value = row.key; select.append(option);
        }
        el('admitScopePrev').disabled = state.scopePage <= 1;
        el('admitScopeNext').disabled = state.scopePage * state.scopeSize >= state.scopeTotal;
    }
    async function loadScopes(page) {
        if (state.busy || page < 1) return;
        state.busy = true;
        try {
            const params = new URLSearchParams({ page: String(page), pageSize: String(state.scopeSize),
                search: el('admitScopeSearch').value.trim() });
            const data = await get('/api/assessment-administration/scopes/page?' + params);
            state.scopePage = data.page; state.scopeTotal = data.totalCount;
            state.scopes = data.items || [];
            renderScopes();
            state.data = null; render();
        } catch (error) { notice('danger', error.message); }
        finally { state.busy = false; }
    }
    function render() {
        const target = el('admitCards');
        target.replaceChildren();
        const data = state.data;
        el('admitPrint').disabled = el('admitCsv').disabled = !data?.items?.length;
        el('admitPrev').disabled = !data || data.page <= 1;
        el('admitNext').disabled = !data || data.page * data.pageSize >= data.totalCount;
        el('admitPageTitle').textContent = data ? data.assessmentName + ' — ' + data.batchName : '';
        el('admitCount').textContent = data ?
            ((data.totalCount ? (data.page - 1) * data.pageSize + 1 : 0) + '–' +
            Math.min(data.page * data.pageSize, data.totalCount) + ' of ' + data.totalCount) : '';
        if (!data?.items?.length) {
            target.append(text('p', data ? 'No eligible candidates found.' : 'Select an assessment and load candidates.', 'text-muted'));
            return;
        }
        for (const card of data.items) {
            const article = text('article', '', 'admit-card');
            article.append(text('h2', data.assessmentName, 'h5 mb-2'));
            article.append(text('p', data.batchName, 'small text-muted mb-2'));
            article.append(text('div', card.studentName + ' | ' + card.studentCode + ' | Roll: ' + card.rollNo, 'fw-semibold mb-2'));
            const table = document.createElement('table');
            table.className = 'table table-bordered table-sm';
            const head = document.createElement('thead'), header = document.createElement('tr');
            for (const label of ['Date', 'Time', 'Subject', 'Room']) header.append(text('th', label));
            head.append(header); table.append(head);
            const body = document.createElement('tbody');
            for (const session of card.sessions) {
                const tr = document.createElement('tr');
                const time = [session.startTime?.slice(0, 5), session.endTime?.slice(0, 5)].filter(Boolean).join('–');
                for (const value of [session.date, time, session.subjectName + ' (' + session.subjectCode + ')', session.roomName || '—'])
                    tr.append(text('td', value));
                body.append(tr);
            }
            table.append(body); article.append(table); target.append(article);
        }
    }
    async function loadCards(page) {
        if (state.busy) return;
        const scope = selection();
        if (!scope) { notice('warning', 'Choose an authorized assessment and batch.'); return; }
        state.busy = true; el('admitLoad').disabled = true;
        try {
            const params = new URLSearchParams({ AssessmentId: String(scope.assessmentId),
                AcademicBatchId: String(scope.academicBatchId), Page: String(page),
                PageSize: String(state.pageSize), Search: el('admitStudentSearch').value.trim() });
            const data = await get('/api/assessment-administration/admit-cards?' + params);
            state.page = data.page; state.data = data; render();
            notice('success', 'Authorized candidate page loaded.');
        } catch (error) { state.data = null; render(); notice('danger', error.message); }
        finally { state.busy = false; el('admitLoad').disabled = false; }
    }
    function csvValue(value) {
        let s = String(value ?? '');
        if (/^[\s]*[=+@-]/.test(s)) s = "'" + s;
        return '"' + s.replace(/"/g, '""') + '"';
    }
    function downloadCsv() {
        if (!state.data?.items?.length) return;
        const rows = [['Student code', 'Roll', 'Student', 'Date', 'Start', 'End', 'Subject', 'Room']];
        for (const card of state.data.items)
            for (const session of card.sessions)
                rows.push([card.studentCode, card.rollNo, card.studentName, session.date,
                    session.startTime, session.endTime, session.subjectName, session.roomName]);
        const blob = new Blob(['\uFEFF' + rows.map(row => row.map(csvValue).join(',')).join('\r\n')],
            { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob), a = document.createElement('a');
        a.href = url; a.download = 'exam-admit-cards-page-' + state.page + '.csv';
        document.body.append(a); a.click(); a.remove(); URL.revokeObjectURL(url);
    }
    el('admitFilters').addEventListener('submit', e => { e.preventDefault(); loadCards(1); });
    el('admitScopeSearch').addEventListener('change', () => loadScopes(1));
    el('admitScopePrev').addEventListener('click', () => loadScopes(state.scopePage - 1));
    el('admitScopeNext').addEventListener('click', () => loadScopes(state.scopePage + 1));
    el('admitPrev').addEventListener('click', () => loadCards(state.page - 1));
    el('admitNext').addEventListener('click', () => loadCards(state.page + 1));
    el('admitCsv').addEventListener('click', downloadCsv);
    el('admitPrint').addEventListener('click', () => { if (state.data?.items?.length) window.print(); });
    loadScopes(1);
})();
