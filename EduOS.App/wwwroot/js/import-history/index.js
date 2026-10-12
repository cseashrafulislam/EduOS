(() => {
    'use strict';
    const e = id => document.getElementById(id);
    if (!e('importHistoryFilter')) return;
    const state = { page: 1, pages: 1, rowPage: 1, rowPages: 1, reference: null, request: null, rowRequest: null };
    const records = e('importHistoryRecords'), rowBody = e('importHistoryRows');

    function message(text) {
        const alert = e('importHistoryAlert');
        alert.className = 'alert alert-danger'; alert.textContent = text || 'Unable to load import history.';
        alert.focus();
    }
    function clearMessage() { const alert = e('importHistoryAlert'); alert.className = 'd-none'; alert.textContent = ''; }
    function cell(value) { const td = document.createElement('td'); td.textContent = value === null || value === undefined || value === '' ? '—' : String(value); return td; }
    function empty(body, colspan, text) { const tr = document.createElement('tr'), td = cell(text); td.colSpan = colspan; td.className = 'text-center text-muted py-3'; tr.append(td); body.replaceChildren(tr); }
    function utc(value) { if (!value) return '—'; const date = new Date(value); return Number.isNaN(date.getTime()) ? '—' : date.toISOString().replace('T', ' ').slice(0, 19); }
    function updatePagination(pageId, prevId, nextId, current, total) {
        e(pageId).textContent = 'Page ' + current + ' of ' + total;
        e(prevId).disabled = current <= 1; e(nextId).disabled = current >= total;
    }
    async function api(path, signal) {
        const response = await fetch('/api/import-history' + path, { credentials: 'same-origin', cache: 'no-store', signal, headers: { Accept: 'application/json' } });
        const payload = await response.json().catch(() => null);
        if (!response.ok || !payload?.success) throw new Error(response.status >= 500 ? 'Server error loading import history.' : payload?.message || 'Import history request failed.');
        return payload.data;
    }
    async function loadHistory() {
        state.request?.abort();
        const request = new AbortController(); state.request = request;
        clearMessage(); empty(records, 7, 'Loading imports…');
        const query = new URLSearchParams({ page: String(state.page), pageSize: '25' });
        const type = e('importHistoryType').value.trim();
        if (type) query.set('importType', type);
        try {
            const data = await api('?' + query, request.signal);
            if (state.request !== request) return;
            state.pages = Math.max(1, Number(data.totalPages) || 1);
            e('importHistoryCount').textContent = (Number(data.totalCount) || 0).toLocaleString() + ' imports';
            records.replaceChildren();
            if (!data.items?.length) empty(records, 7, 'No imports match the selected filter.');
            for (const item of data.items || []) {
                const tr = document.createElement('tr');
                const status = item.completedAt ? 'Completed' : 'In progress';
                [utc(item.startedAt), item.importType, item.totalRows, item.successRows, item.failedRows, status].forEach(v => tr.append(cell(v)));
                const actions = document.createElement('td'), open = document.createElement('button');
                open.type = 'button'; open.className = 'btn btn-sm btn-outline-primary'; open.textContent = 'View rows';
                open.addEventListener('click', () => openDetails(item.reference));
                actions.append(open); tr.append(actions); records.append(tr);
            }
            updatePagination('importHistoryPage', 'importHistoryPrevious', 'importHistoryNext', state.page, state.pages);
        } catch (error) { if (error.name !== 'AbortError') { empty(records, 7, 'Unable to load imports.'); message(error.message); } }
        finally { if (state.request === request) state.request = null; }
    }
    async function openDetails(reference) {
        if (!/^[a-f0-9-]{36}$/i.test(String(reference))) return;
        state.reference = reference; state.rowPage = 1;
        e('importHistoryDetails').classList.remove('d-none');
        e('importHistorySummary').textContent = 'Loading details…';
        e('importHistoryErrors').textContent = '';
        try {
            const result = await api('/' + encodeURIComponent(reference));
            if (state.reference !== reference) return;
            e('importHistorySummary').textContent = result.importType + ' · ' + result.totalRows + ' total · ' + result.successRows + ' succeeded · ' + result.failedRows + ' failed · ' + utc(result.startedAt) + ' UTC';
            e('importHistoryErrors').textContent = result.errorSummary || '';
            await loadRows();
            e('importHistoryDetails').scrollIntoView({ behavior: 'smooth', block: 'start' });
        } catch (error) { if (state.reference === reference) message(error.message); }
    }
    async function loadRows() {
        if (!state.reference) return;
        state.rowRequest?.abort();
        const reference = state.reference, request = new AbortController(); state.rowRequest = request;
        empty(rowBody, 4, 'Loading rows…');
        try {
            const query = new URLSearchParams({ page: String(state.rowPage), pageSize: '25' });
            const data = await api('/' + encodeURIComponent(reference) + '/rows?' + query, request.signal);
            if (state.rowRequest !== request || state.reference !== reference) return;
            state.rowPages = Math.max(1, Number(data.totalPages) || 1);
            rowBody.replaceChildren();
            if (!data.items?.length) empty(rowBody, 4, 'No row-level history recorded.');
            for (const item of data.items || []) {
                const tr = document.createElement('tr');
                [item.rowNumber, item.businessKey, item.isSuccess ? 'Success' : 'Failed', item.errorMessage].forEach(v => tr.append(cell(v)));
                rowBody.append(tr);
            }
            updatePagination('importRowsPage', 'importRowsPrevious', 'importRowsNext', state.rowPage, state.rowPages);
        } catch (error) { if (error.name !== 'AbortError') { empty(rowBody, 4, 'Unable to load rows.'); message(error.message); } }
        finally { if (state.rowRequest === request) state.rowRequest = null; }
    }
    e('importHistoryFilter').addEventListener('submit', event => { event.preventDefault(); state.page = 1; loadHistory(); });
    e('importHistoryClear').addEventListener('click', () => { e('importHistoryType').value = ''; state.page = 1; loadHistory(); });
    e('importHistoryPrevious').addEventListener('click', () => { if (state.page > 1) { state.page--; loadHistory(); } });
    e('importHistoryNext').addEventListener('click', () => { if (state.page < state.pages) { state.page++; loadHistory(); } });
    e('importRowsPrevious').addEventListener('click', () => { if (state.rowPage > 1) { state.rowPage--; loadRows(); } });
    e('importRowsNext').addEventListener('click', () => { if (state.rowPage < state.rowPages) { state.rowPage++; loadRows(); } });
    e('importHistoryClose').addEventListener('click', () => { state.reference = null; state.rowRequest?.abort(); e('importHistoryDetails').classList.add('d-none'); });
    loadHistory();
})();
