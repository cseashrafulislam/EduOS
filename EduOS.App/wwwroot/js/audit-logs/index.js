(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('auditFilterForm')) return;
    const state = { page: 1, pageSize: 25, totalPages: 1, loading: false, controller: null };
    const rows = el('auditRows');
    function alertMessage(message) {
        const node = el('auditAlert'); node.className = 'alert alert-danger'; node.textContent = message; node.focus();
    }
    function clearAlert() { const node = el('auditAlert'); node.className = 'd-none'; node.textContent = ''; }
    function cell(value) {
        const td = document.createElement('td');
        td.textContent = value === null || value === undefined || value === '' ? '—' : String(value);
        return td;
    }
    function showEmpty(message) {
        rows.replaceChildren();
        const tr = document.createElement('tr'), td = cell(message);
        td.colSpan = 7; td.className = 'text-center text-muted py-4'; tr.append(td); rows.append(tr);
    }
    function params(includePage) {
        const from = el('auditFrom').value, to = el('auditTo').value;
        if (from && to && from > to) throw new Error('From date cannot be later than To date.');
        const userId = el('auditUserId').value.trim();
        if (userId && (!/^[1-9]\d*$/.test(userId) || !Number.isSafeInteger(Number(userId)))) throw new Error('Enter a valid positive User ID.');
        const q = new URLSearchParams();
        if (includePage) { q.set('page', String(state.page)); q.set('pageSize', String(state.pageSize)); }
        if (from) q.set('fromUtc', from + 'T00:00:00Z');
        if (to) q.set('toUtc', to + 'T23:59:59.999Z');
        if (el('auditEntity').value.trim()) q.set('entityName', el('auditEntity').value.trim());
        if (el('auditAction').value) q.set('action', el('auditAction').value);
        if (userId) q.set('userId', userId);
        if (el('auditStatus').value) q.set('isSuccess', el('auditStatus').value);
        return q;
    }
    function updatePaging(result) {
        state.totalPages = Math.max(1, Number(result?.totalPages) || 1);
        el('auditPage').textContent = 'Page ' + state.page + ' of ' + state.totalPages;
        el('auditCount').textContent = (Number(result?.totalCount) || 0).toLocaleString() + ' matching entries';
        el('auditPrevious').disabled = state.loading || state.page <= 1;
        el('auditNext').disabled = state.loading || state.page >= state.totalPages;
    }
    async function load() {
        if (state.controller) state.controller.abort();
        const abort = new AbortController(); state.controller = abort;
        let q; try { q = params(true); } catch (error) { alertMessage(error.message); return; }
        state.loading = true; clearAlert(); showEmpty('Loading audit entries…');
        el('auditPrevious').disabled = true; el('auditNext').disabled = true;
        try {
            const response = await fetch('/api/v1/AuditLog?' + q.toString(), { headers: { Accept: 'application/json' }, credentials: 'same-origin', cache: 'no-store', signal: abort.signal });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error(payload?.message || 'Unable to load audit entries.');
            if (state.controller !== abort) return;
            const result = payload.data || {};
            rows.replaceChildren();
            const items = Array.isArray(result.items) ? result.items : [];
            if (!items.length) showEmpty('No entries match these filters.');
            for (const item of items) {
                const tr = document.createElement('tr');
                const time = item.occurredAt ? new Date(item.occurredAt) : null;
                const when = time && !Number.isNaN(time.getTime()) ? time.toISOString().replace('T', ' ').slice(0, 19) : '—';
                [when, item.userName || (item.userId ? '#' + item.userId : 'System'), item.action,
                    item.entityName, item.entityId, item.ipAddress, item.isSuccess ? 'Success' : 'Failed']
                    .forEach(value => tr.append(cell(value)));
                rows.append(tr);
            }
            updatePaging(result);
        } catch (error) {
            if (error.name !== 'AbortError') { showEmpty('Could not load activity.'); alertMessage(error.message); }
        } finally {
            if (state.controller === abort) { state.controller = null; state.loading = false; el('auditPrevious').disabled = state.page <= 1; el('auditNext').disabled = state.page >= state.totalPages; }
        }
    }
    el('auditFilterForm').addEventListener('submit', e => { e.preventDefault(); state.page = 1; load(); });
    el('auditClear').addEventListener('click', () => { el('auditFilterForm').reset(); state.page = 1; load(); });
    el('auditPrevious').addEventListener('click', () => { if (!state.loading && state.page > 1) { state.page--; load(); } });
    el('auditNext').addEventListener('click', () => { if (!state.loading && state.page < state.totalPages) { state.page++; load(); } });
    load();
})();
