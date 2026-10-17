(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('auditInsights') || !el('auditFilterForm')) return;
    let active = null;
    const metrics = {
        auditStatTotal: 'totalLogs', auditStatToday: 'todayLogs',
        auditStatWeek: 'thisWeekLogs', auditStatMonth: 'thisMonthLogs',
        auditStatSuccess: 'successfulOperations', auditStatFailed: 'failedOperations',
        auditStatCreated: 'createActions', auditStatUpdated: 'updateActions',
        auditStatDeleted: 'deleteActions'
    };
    const cell = value => {
        const td = document.createElement('td');
        td.textContent = value == null || value === '' ? '—' : String(value);
        return td;
    };
    function renderTop(id, items, label) {
        const body = el(id); body.replaceChildren();
        if (!Array.isArray(items) || !items.length) {
            const tr = document.createElement('tr'), td = cell('No matching activity.');
            td.colSpan = 2; tr.append(td); body.append(tr); return;
        }
        for (const item of items) {
            const tr = document.createElement('tr');
            const count = cell(Number(item.activityCount || 0).toLocaleString());
            count.className = 'text-end';
            tr.append(cell(label(item)), count); body.append(tr);
        }
    }
    function clear() {
        for (const id of Object.keys(metrics)) el(id).textContent = '—';
        el('auditTopEntities').replaceChildren(); el('auditTopUsers').replaceChildren();
    }
    function query() {
        const from = el('auditFrom').value, to = el('auditTo').value;
        if (from && to && from > to) throw Error('From date cannot be later than To date.');
        const userId = el('auditUserId').value.trim();
        if (userId && (!/^[1-9]\d*$/.test(userId) || !Number.isSafeInteger(Number(userId))))
            throw Error('Enter a valid positive User ID.');
        const q = new URLSearchParams();
        if (from) q.set('fromUtc', from + 'T00:00:00Z');
        if (to) q.set('toUtc', to + 'T23:59:59.999Z');
        const entity = el('auditEntity').value.trim();
        if (entity) q.set('entityName', entity);
        if (el('auditAction').value) q.set('action', el('auditAction').value);
        if (userId) q.set('userId', userId);
        if (el('auditStatus').value) q.set('isSuccess', el('auditStatus').value);
        return q.toString();
    }
    async function load() {
        active?.abort();
        const controller = new AbortController(); active = controller;
        clear(); el('auditStatsStatus').textContent = 'Loading overview…';
        let filters;
        try { filters = query(); }
        catch (error) { el('auditStatsStatus').textContent = error.message; return; }
        try {
            const response = await fetch('/api/v1/AuditLog/statistics?' + filters, {
                credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json' }, signal: controller.signal
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success)
                throw Error(response.status >= 500 ? 'Unable to load activity overview.' : payload?.message || 'Overview request failed.');
            if (active !== controller) return;
            const data = payload.data || {};
            for (const [id, key] of Object.entries(metrics))
                el(id).textContent = Number(data[key] || 0).toLocaleString();
            renderTop('auditTopEntities', data.topEntities, item => item.entityName || 'Unknown');
            renderTop('auditTopUsers', data.topUsers, item => item.userName || ('#' + item.userId));
            el('auditStatsStatus').textContent = 'Overview updated for current filters.';
        } catch (error) {
            if (active === controller && error.name !== 'AbortError')
                el('auditStatsStatus').textContent = error.message;
        } finally { if (active === controller) active = null; }
    }
    el('auditFilterForm').addEventListener('submit', event => { event.preventDefault(); void load(); });
    el('auditClear').addEventListener('click', () => queueMicrotask(() => void load()));
    void load();
})();
