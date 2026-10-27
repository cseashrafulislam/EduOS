(() => {
    'use strict';
    const root = document.getElementById('enrollmentManagement');
    if (!root) return;
    const el = id => document.getElementById(id);
    const state = { reference: null, page: 1, totalPages: 0, pending: 0 };
    const pageSize = 20;
    const columns = ['enrollmentDate', 'endDate', 'batchName', 'rollNo', 'state'];
    function setStatus(value, error = false) {
        const node = el('enrollmentHistoryStatus');
        node.textContent = value;
        node.classList.toggle('text-danger', error);
    }
    function render(data) {
        const body = el('enrollmentHistoryRows');
        body.replaceChildren();
        state.totalPages = data.totalPages || 0;
        el('enrollmentHistoryPage').textContent = 'Page ' + data.page +
            ' of ' + Math.max(1, state.totalPages) + ' (' + data.totalCount + ' records)';
        el('enrollmentHistoryPrevious').disabled = !data.hasPreviousPage;
        el('enrollmentHistoryNext').disabled = !data.hasNextPage;
        if (!data.items?.length) {
            const tr = document.createElement('tr'), td = document.createElement('td');
            td.colSpan = 5;
            td.textContent = 'No enrollment history on this page.';
            tr.append(td); body.append(tr);
            return;
        }
        for (const item of data.items) {
            const tr = document.createElement('tr');
            for (const field of columns) {
                const td = document.createElement('td');
                const value = field === 'batchName'
                    ? ((item.batchName || 'Catalog unavailable') + (item.batchCode ? ' (' + item.batchCode + ')' : ''))
                    : field === 'state' ? (item.state + (item.isCurrent ? ' (Current)' : ''))
                    : item[field];
                td.textContent = value == null || value === '' ? '—' : String(value);
                tr.append(td);
            }
            body.append(tr);
        }
    }
    async function load(page) {
        if (!state.reference) return;
        const request = ++state.pending, reference = state.reference;
        state.page = page;
        setStatus('Loading enrollment history…');
        try {
            const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
            const response = await fetch('/api/academic-enrollments/students/' +
                encodeURIComponent(reference) + '/history?' + params, {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (request !== state.pending || reference !== state.reference) return;
            if (!response.ok || payload?.success !== true)
                throw new Error(payload?.message || 'History unavailable.');
            render(payload.data);
            setStatus('Enrollment records use original roll and campus. Batch names are current catalog labels.');
        } catch (error) {
            if (request !== state.pending || reference !== state.reference) return;
            el('enrollmentHistoryRows').replaceChildren();
            el('enrollmentHistoryPrevious').disabled = true;
            el('enrollmentHistoryNext').disabled = true;
            setStatus(error.message || 'History unavailable.', true);
        }
    }
    window.addEventListener('eduos:enrollment-student-selected', event => {
        state.reference = event.detail?.reference || null;
        ++state.pending;
        el('enrollmentHistoryPanel').classList.toggle('d-none', !state.reference);
        if (state.reference) load(1);
    });
    el('enrollmentHistoryPrevious').addEventListener('click', () => {
        if (state.page > 1) load(state.page - 1);
    });
    el('enrollmentHistoryNext').addEventListener('click', () => {
        if (state.page < state.totalPages) load(state.page + 1);
    });
})();
