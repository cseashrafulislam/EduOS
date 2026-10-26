(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('identityHistoryFilters')) return;
    const state = { page: 1, pageSize: 25, totalPages: 1, controller: null };
    const rows = el('identityHistoryRows');

    function showMessage(message) {
        const alert = el('identityHistoryAlert');
        alert.className = 'alert alert-danger';
        alert.textContent = message;
    }
    function clearMessage() {
        const alert = el('identityHistoryAlert');
        alert.className = 'd-none'; alert.textContent = '';
    }
    function cell(value) {
        const td = document.createElement('td');
        td.textContent = value === null || value === undefined || value === '' ? '—' : String(value);
        return td;
    }
    function showEmpty(message) {
        rows.replaceChildren();
        const tr = document.createElement('tr'), td = cell(message);
        td.colSpan = 9; td.className = 'text-center text-muted py-4';
        tr.append(td); rows.append(tr);
    }
    function setPaging(totalCount, totalPages) {
        state.totalPages = Math.max(1, Number(totalPages) || 1);
        el('identityHistoryCount').textContent = (Number(totalCount) || 0).toLocaleString() + ' events';
        el('identityHistoryPage').textContent = 'Page ' + state.page + ' of ' + state.totalPages;
        el('identityHistoryPrevious').disabled = state.page <= 1 || Boolean(state.controller);
        el('identityHistoryNext').disabled = state.page >= state.totalPages || Boolean(state.controller);
    }
    function parameters() {
        const from = el('identityHistoryFrom').value, to = el('identityHistoryTo').value;
        if (from && to && from > to) throw new Error('From date cannot be later than To date.');
        const student = el('identityHistoryStudent').value.trim();
        if (student && !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(student))
            throw new Error('Enter a valid student reference UUID.');
        const user = el('identityHistoryUser').value.trim();
        if (user && (!/^[1-9]\d*$/.test(user) || !Number.isSafeInteger(Number(user))))
            throw new Error('Enter a valid positive actor user ID.');
        const query = new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize) });
        if (from) query.set('fromUtc', from + 'T00:00:00Z');
        if (to) query.set('toUtc', to + 'T23:59:59.999Z');
        if (student) query.set('studentReference', student);
        if (user) query.set('userId', user);
        if (el('identityHistoryAction').value) query.set('action', el('identityHistoryAction').value);
        if (el('identityHistoryOutcome').value.trim()) query.set('outcomeCode', el('identityHistoryOutcome').value.trim());
        return query;
    }
    async function load() {
        if (state.controller) state.controller.abort();
        let query;
        try { query = parameters(); } catch (error) { showMessage(error.message); return; }
        const controller = new AbortController(); state.controller = controller;
        clearMessage(); showEmpty('Loading identity access history…');
        el('identityHistoryPrevious').disabled = true;
        el('identityHistoryNext').disabled = true;
        try {
            const response = await fetch('/api/learner-identities/access-history?' + query, {
                headers: { Accept: 'application/json' }, credentials: 'same-origin',
                cache: 'no-store', signal: controller.signal
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error(payload?.message || 'Unable to load identity access history.');
            if (state.controller !== controller) return;
            const result = payload.data || {}, items = Array.isArray(result.items) ? result.items : [];
            rows.replaceChildren();
            if (!items.length) showEmpty('No identity access events match these filters.');
            for (const item of items) {
                const tr = document.createElement('tr');
                const timestamp = item.accessedAt ? new Date(item.accessedAt) : null;
                const utc = timestamp && !Number.isNaN(timestamp.getTime())
                    ? timestamp.toISOString().replace('T', ' ').slice(0, 19) : '—';
                [utc, item.studentId ? '#' + item.studentId : null,
                    item.personId ? '#' + item.personId : null, item.userId ? '#' + item.userId : null,
                    item.action, item.outcomeCode, item.reasonCode, item.purpose, item.ipAddress]
                    .forEach(value => tr.append(cell(value)));
                rows.append(tr);
            }
            setPaging(result.totalCount, result.totalPages);
        } catch (error) {
            if (error.name !== 'AbortError') { showEmpty('Unable to load identity access history.'); showMessage(error.message); }
        } finally {
            if (state.controller === controller) {
                state.controller = null;
                el('identityHistoryPrevious').disabled = state.page <= 1;
                el('identityHistoryNext').disabled = state.page >= state.totalPages;
            }
        }
    }
    function setDefaultDates() {
        const today = new Date(), start = new Date(today);
        start.setUTCDate(start.getUTCDate() - 30);
        el('identityHistoryFrom').value = start.toISOString().slice(0, 10);
        el('identityHistoryTo').value = today.toISOString().slice(0, 10);
    }
    el('identityHistoryFilters').addEventListener('submit', event => { event.preventDefault(); state.page = 1; load(); });
    el('identityHistoryReset').addEventListener('click', () => {
        el('identityHistoryFilters').reset(); setDefaultDates(); state.page = 1; load();
    });
    el('identityHistoryPrevious').addEventListener('click', () => {
        if (!state.controller && state.page > 1) { state.page--; load(); }
    });
    el('identityHistoryNext').addEventListener('click', () => {
        if (!state.controller && state.page < state.totalPages) { state.page++; load(); }
    });
    setDefaultDates(); load();
})();
