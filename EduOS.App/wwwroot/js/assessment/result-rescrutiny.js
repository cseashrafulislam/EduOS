(() => {
    'use strict';
    const root = document.getElementById('resultRescrutinyWorkspace');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const bn = root.dataset.lang === 'bn';
    const tr = (en, bangla) => bn ? bangla : en;
    const state = { scopePage: 1, scopeTotal: 0, scopeSeq: 0, listSeq: 0,
        publication: 0, before: null, previous: [], hasMore: false, items: [],
        selected: null, busy: false, requestKey: null, decisionKey: null, historySeq: 0 };
    const endpoint = '/api/assessment-administration/';
    const token = document.querySelector('#resultRescrutinyWorkspace input[name="__RequestVerificationToken"]')?.value;
    function notice(message, error = false) {
        const box = $('rescrutinyNotice');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = message;
    }
    function cell(value) {
        const td = document.createElement('td');
        td.textContent = value == null ? '—' : String(value);
        return td;
    }
    function placeholder(id, message, span) {
        const body = $(id), row = document.createElement('tr'), td = cell(message);
        td.colSpan = span; row.append(td); body.replaceChildren(row);
    }
    async function api(path, method = 'GET', payload = null) {
        const response = await fetch(endpoint + path, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers: Object.assign({ Accept: 'application/json' },
                payload ? { 'Content-Type': 'application/json', RequestVerificationToken: token || '' } : {}),
            body: payload ? JSON.stringify(payload) : undefined
        });
        const body = await response.json().catch(() => null);
        if (!response.ok || body?.success !== true)
            throw new Error(body?.message || tr('Request failed.', 'অনুরোধ ব্যর্থ হয়েছে।'));
        return body.data;
    }
    function resetPublication() {
        state.publication = 0; state.before = null; state.previous = []; state.items = [];
        state.selected = null; state.requestKey = null; state.decisionKey = null;
        $('rescrutinyStudent').replaceChildren(new Option(tr('Find a student', 'শিক্ষার্থী খুঁজুন'), ''));
        $('rescrutinyStudent').disabled = true; $('rescrutinySubmit').disabled = true;
        $('rescrutinyDecisionForm').classList.add('d-none');
        $('rescrutinyHistory').classList.add('d-none');
        state.historySeq++;
        $('rescrutinyCsv').disabled = true; $('rescrutinyPrev').disabled = true;
        $('rescrutinyNext').disabled = true;
        placeholder('rescrutinyRows', tr('Select a published version.', 'প্রকাশিত সংস্করণ নির্বাচন করুন।'), 5);
    }
    async function loadScopes() {
        const seq = ++state.scopeSeq;
        resetPublication();
        try {
            const params = new URLSearchParams({ page: String(state.scopePage), pageSize: '30',
                search: $('rescrutinyScopeSearch').value.trim() });
            const data = await api('scopes/page?' + params);
            if (seq !== state.scopeSeq) return;
            state.scopeTotal = data.totalCount || 0;
            const select = $('rescrutinyScope');
            select.replaceChildren(new Option(tr('Select assessment / batch', 'মূল্যায়ন / ব্যাচ নির্বাচন করুন'), ''));
            const seen = new Set();
            for (const item of data.items || []) {
                const key = String(item.assessmentId) + ':' + String(item.academicBatchId);
                if (seen.has(key)) continue;
                seen.add(key);
                select.add(new Option(item.assessmentName + ' / ' + item.academicBatchName, key));
            }
            $('rescrutinyScopePrev').disabled = state.scopePage <= 1;
            $('rescrutinyScopeNext').disabled = state.scopePage * 30 >= state.scopeTotal;
            $('rescrutinyPublication').replaceChildren(new Option(tr('Select scope first', 'আগে মূল্যায়ন নির্বাচন করুন'), ''));
            $('rescrutinyPublication').disabled = true;
        } catch (e) { if (seq === state.scopeSeq) notice(e.message, true); }
    }
    async function loadVersions() {
        resetPublication();
        const scope = $('rescrutinyScope').value.split(':').map(Number);
        const select = $('rescrutinyPublication');
        select.replaceChildren(new Option(tr('Loading…', 'লোড হচ্ছে…'), ''));
        select.disabled = true;
        if (scope.length !== 2 || scope.some(x => !Number.isSafeInteger(x) || x <= 0)) return;
        const seq = ++state.scopeSeq;
        try {
            const data = await api('results/publications?' + new URLSearchParams({
                assessmentId: String(scope[0]), academicBatchId: String(scope[1]),
                page: '1', pageSize: '100'
            }));
            if (seq !== state.scopeSeq) return;
            select.replaceChildren(new Option(tr('Select published version', 'প্রকাশিত সংস্করণ নির্বাচন করুন'), ''));
            for (const item of data.items || []) {
                if (item.state !== 2 && item.state !== 'Published') continue;
                select.add(new Option(tr('Version ', 'সংস্করণ ') + item.versionNo, String(item.id)));
            }
            select.disabled = select.options.length <= 1;
        } catch (e) { if (seq === state.scopeSeq) notice(e.message, true); }
    }
    async function loadStudents() {
        const id = state.publication;
        $('rescrutinyStudent').replaceChildren(new Option(tr('Loading…', 'লোড হচ্ছে…'), ''));
        $('rescrutinyStudent').disabled = true; $('rescrutinySubmit').disabled = true;
        if (!id) return;
        try {
            const data = await api('results/tabulation?' + new URLSearchParams({
                publicationId: String(id), page: '1', pageSize: '50',
                search: $('rescrutinyStudentSearch').value.trim()
            }));
            if (state.publication !== id) return;
            const select = $('rescrutinyStudent');
            select.replaceChildren(new Option(tr('Select student', 'শিক্ষার্থী নির্বাচন করুন'), ''));
            for (const item of data.items || [])
                select.add(new Option(item.studentCode + ' / ' + item.rollNo + ' / ' + item.studentName,
                    item.studentReference));
            select.disabled = select.options.length <= 1;
            $('rescrutinySubmit').disabled = select.disabled;
        } catch (e) { notice(e.message, true); }
    }
    function status(value) {
        if (value === 2 || value === 'Rejected') return tr('Rejected', 'প্রত্যাখ্যাত');
        if (value === 3 || value === 'AcceptedForCorrection')
            return tr('Accepted for correction review', 'সংশোধন পর্যালোচনার জন্য গৃহীত');
        return tr('Pending', 'অপেক্ষমাণ');
    }
    async function loadCases() {
        const id = state.publication, before = state.before;
        const seq = ++state.listSeq;
        if (!id) return;
        try {
            const params = new URLSearchParams({ publicationId: String(id), pageSize: '25' });
            if (before) params.set('beforeId', String(before));
            if ($('rescrutinyStatusFilter').value) params.set('status', $('rescrutinyStatusFilter').value);
            const data = await api('results/rescrutiny?' + params);
            if (seq !== state.listSeq || id !== state.publication || before !== state.before) return;
            state.items = data.items || []; state.hasMore = !!data.hasMore;
            state.nextBeforeId = data.nextBeforeId;
            const tbody = $('rescrutinyRows'); tbody.replaceChildren();
            for (const item of state.items) {
                const row = document.createElement('tr');
                row.append(cell(item.studentCode + ' / ' + item.rollNo),
                    cell(new Date(item.requestedAt).toLocaleString()),
                    cell(status(item.status)), cell(item.requestReason));
                const actions = document.createElement('td');
                const historyButton = document.createElement('button');
                historyButton.type = 'button'; historyButton.className = 'btn btn-sm btn-outline-secondary';
                historyButton.textContent = tr('History', 'ইতিহাস');
                historyButton.addEventListener('click', () => loadHistory(item));
                actions.append(historyButton);
                if (item.status === 1 || item.status === 'Requested') {
                    const button = document.createElement('button');
                    button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
                    button.textContent = tr('Review', 'পর্যালোচনা');
                    button.addEventListener('click', () => {
                        state.selected = item; state.decisionKey = null;
                        $('rescrutinyDecisionTitle').textContent =
                            tr('Review case ', 'আবেদন পর্যালোচনা ') + item.studentCode;
                        $('rescrutinyDecisionReason').value = '';
                        $('rescrutinyDecisionForm').classList.remove('d-none');
                        $('rescrutinyDecisionReason').focus();
                    });
                    button.classList.add('ms-1');
                    actions.append(button);
                } else {
                    const reason = document.createElement('span');
                    reason.className = 'small ms-2'; reason.textContent = item.decisionReason || '—';
                    actions.append(reason);
                }
                row.append(actions); tbody.append(row);
            }
            if (!state.items.length)
                placeholder('rescrutinyRows', tr('No applications on this page.', 'এই পৃষ্ঠায় কোনো আবেদন নেই।'), 5);
            $('rescrutinyPrev').disabled = !state.previous.length;
            $('rescrutinyNext').disabled = !state.hasMore;
            $('rescrutinyCsv').disabled = !state.items.length;
            $('rescrutinyPage').textContent = tr('Page ', 'পৃষ্ঠা ') + (state.previous.length + 1);
        } catch (e) { if (seq === state.listSeq) notice(e.message, true); }
    }
    async function loadHistory(item) {
        const seq = ++state.historySeq;
        $('rescrutinyHistory').classList.add('d-none');
        try {
            const data = await api('results/rescrutiny/' +
                encodeURIComponent(item.caseReference) + '/history');
            if (seq !== state.historySeq || state.publication !== item.publicationId) return;
            $('rescrutinyHistoryTitle').textContent = tr('Case history: ', 'আবেদনের ইতিহাস: ') + item.studentCode;
            const rows = $('rescrutinyHistoryRows'); rows.replaceChildren();
            for (const event of data.events || []) {
                const row = document.createElement('tr');
                row.append(cell(event.action === 1 || event.action === 'Requested' ?
                    tr('Application', 'আবেদন') : status(event.action)),
                    cell(new Date(event.occurredAt).toISOString()),
                    cell(event.actorUserId), cell(event.reason));
                rows.append(row);
            }
            $('rescrutinyHistory').classList.remove('d-none');
        } catch (e) { if (seq === state.historySeq) notice(e.message, true); }
    }
    async function selectPublication() {
        const id = Number($('rescrutinyPublication').value);
        resetPublication();
        if (!Number.isSafeInteger(id) || id <= 0) return;
        state.publication = id;
        await Promise.all([loadCases(), loadStudents()]);
    }
    async function submitRequest(event) {
        event.preventDefault();
        if (state.busy || !state.publication || !$('rescrutinyStudent').value) return;
        if (!$('rescrutinyRequestForm').reportValidity()) return;
        state.busy = true; $('rescrutinySubmit').disabled = true;
        state.requestKey ||= crypto.randomUUID();
        try {
            await api('results/rescrutiny', 'POST', {
                clientRequestId: state.requestKey, publicationId: state.publication,
                studentReference: $('rescrutinyStudent').value,
                reason: $('rescrutinyReason').value.trim()
            });
            state.requestKey = null; $('rescrutinyReason').value = '';
            state.before = null; state.previous = [];
            notice(tr('Application recorded.', 'আবেদন সংরক্ষিত হয়েছে।'));
            await loadCases();
        } catch (e) { notice(e.message, true); }
        finally { state.busy = false; $('rescrutinySubmit').disabled = !$('rescrutinyStudent').value; }
    }
    async function decide(event) {
        event.preventDefault();
        const item = state.selected;
        if (!item || state.busy || !$('rescrutinyDecisionForm').reportValidity()) return;
        state.busy = true; $('rescrutinyDecide').disabled = true;
        state.decisionKey ||= crypto.randomUUID();
        try {
            await api('results/rescrutiny/' + encodeURIComponent(item.caseReference) + '/decision', 'POST', {
                clientRequestId: state.decisionKey, requestRowVersion: item.requestRowVersion,
                decision: Number($('rescrutinyDecision').value),
                reason: $('rescrutinyDecisionReason').value.trim()
            });
            state.decisionKey = null; state.selected = null;
            $('rescrutinyDecisionForm').classList.add('d-none');
            notice(tr('Decision recorded. Published marks are unchanged.',
                'সিদ্ধান্ত সংরক্ষিত হয়েছে। প্রকাশিত নম্বর অপরিবর্তিত।'));
            await loadCases();
        } catch (e) { notice(e.message, true); }
        finally { state.busy = false; $('rescrutinyDecide').disabled = false; }
    }
    function csv() {
        if (!state.items.length) return;
        const safe = value => {
            let s = String(value ?? '');
            if (/^[\s]*[=+\-@]/.test(s)) s = "'" + s;
            return '"' + s.replace(/"/g, '""') + '"';
        };
        const rows = [['Student', 'Roll', 'Status', 'Requested UTC', 'Request reason',
            'Decided UTC', 'Decision reason']];
        for (const item of state.items)
            rows.push([item.studentCode, item.rollNo, status(item.status), item.requestedAt,
                item.requestReason, item.decidedAt, item.decisionReason]);
        const blob = new Blob(['\uFEFF' + rows.map(x => x.map(safe).join(',')).join('\r\n')],
            { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob), a = document.createElement('a');
        a.href = url; a.download = 'rescrutiny-current-page.csv'; a.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    $('rescrutinyScopeForm').addEventListener('submit', e => {
        e.preventDefault(); state.scopePage = 1; loadScopes();
    });
    $('rescrutinyScopePrev').addEventListener('click', () => {
        if (state.scopePage > 1) { state.scopePage--; loadScopes(); }
    });
    $('rescrutinyScopeNext').addEventListener('click', () => {
        if (state.scopePage * 30 < state.scopeTotal) { state.scopePage++; loadScopes(); }
    });
    $('rescrutinyScope').addEventListener('change', loadVersions);
    $('rescrutinyPublication').addEventListener('change', selectPublication);
    $('rescrutinyStudentFind').addEventListener('click', loadStudents);
    $('rescrutinyRequestForm').addEventListener('submit', submitRequest);
    $('rescrutinyDecisionForm').addEventListener('submit', decide);
    $('rescrutinyCancel').addEventListener('click', () => {
        state.selected = null; state.decisionKey = null;
        $('rescrutinyDecisionForm').classList.add('d-none');
    });
    $('rescrutinyPrev').addEventListener('click', () => {
        if (!state.previous.length) return;
        state.before = state.previous.pop(); loadCases();
    });
    $('rescrutinyNext').addEventListener('click', () => {
        if (!state.hasMore || !state.nextBeforeId) return;
        state.previous.push(state.before); state.before = state.nextBeforeId; loadCases();
    });
    $('rescrutinyStatusFilter').addEventListener('change', () => {
        state.before = null; state.previous = []; state.historySeq++;
        $('rescrutinyHistory').classList.add('d-none');
        loadCases();
    });
    $('rescrutinyHistoryClose').addEventListener('click', () => {
        state.historySeq++;
        $('rescrutinyHistory').classList.add('d-none');
    });
    $('rescrutinyCsv').addEventListener('click', csv);
    $('rescrutinyPrint').addEventListener('click', () => window.print());
    loadScopes();
})();
