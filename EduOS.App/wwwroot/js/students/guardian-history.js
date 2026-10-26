export function setupGuardianHistory({ $, api, state }) {
    const rows = $('guardianHistoryRows');
    const alert = $('guardianHistoryAlert');
    const previous = $('guardianHistoryPrev'), next = $('guardianHistoryNext');
    let page = 1, totalPages = 1, requestNumber = 0, loading = false;

    function cell(value) {
        const td = document.createElement('td');
        td.textContent = value === null || value === undefined || value === '' ? '—' : String(value);
        return td;
    }
    function empty(message) {
        rows.replaceChildren();
        const tr = document.createElement('tr'), td = cell(message);
        td.colSpan = 6; td.className = 'text-muted text-center py-3';
        tr.append(td); rows.append(tr);
    }
    function paging(total = 0) {
        previous.disabled = loading || page <= 1;
        next.disabled = loading || page >= totalPages;
        $('guardianHistoryPage').textContent = 'Page ' + page + ' of ' + totalPages + ' (' + total + ' events)';
    }
    async function load(resetPage) {
        if (resetPage) page = 1;
        const reference = state.student?.studentReference;
        if (!reference) { empty('Select a student to view history.'); return; }
        const ticket = ++requestNumber;
        loading = true;
        alert.textContent = ''; alert.className = 'd-none';
        empty('Loading guardian relationship history…'); paging();
        try {
            const query = new URLSearchParams({ page: String(page), pageSize: '20' });
            const result = await api('/api/student-guardians/students/' +
                encodeURIComponent(reference) + '/history?' + query);
            if (ticket !== requestNumber || state.student?.studentReference !== reference) return;
            const entries = Array.isArray(result?.items) ? result.items : [];
            totalPages = Math.max(1, Number(result?.totalPages) || 1);
            rows.replaceChildren();
            if (!entries.length) empty('No relationship history for this student.');
            for (const item of entries) {
                const tr = document.createElement('tr');
                const date = item.occurredAt ? new Date(item.occurredAt) : null;
                const when = date && !Number.isNaN(date.getTime())
                    ? date.toISOString().replace('T', ' ').slice(0, 19) : '—';
                [when, item.linkId ? '#' + item.linkId : null, item.action,
                    item.actorUserId ? '#' + item.actorUserId : 'System',
                    item.isSuccess ? 'Success' : 'Failed', item.reason]
                    .forEach(value => tr.append(cell(value)));
                rows.append(tr);
            }
            paging(result?.totalCount || 0);
        } catch (error) {
            if (ticket === requestNumber) {
                empty('Could not load guardian relationship history.');
                alert.className = 'alert alert-danger'; alert.textContent = error.message;
            }
        } finally {
            if (ticket === requestNumber) { loading = false; paging(); }
        }
    }
    previous.addEventListener('click', () => {
        if (!loading && page > 1) { page--; load(false); }
    });
    next.addEventListener('click', () => {
        if (!loading && page < totalPages) { page++; load(false); }
    });
    return { refresh: () => load(true) };
}
