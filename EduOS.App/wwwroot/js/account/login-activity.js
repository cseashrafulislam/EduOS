(() => {
    'use strict';
    const byId = id => document.getElementById(id);
    const root = byId('loginActivityRoot'), rows = byId('loginActivityRows');
    if (!root || !rows) return;
    const state = { page: 1, pageSize: 20, generation: 0, exporting: false };
    const status = byId('loginActivityStatus'), alert = byId('loginActivityAlert');
    const from = byId('loginActivityFrom'), to = byId('loginActivityTo');
    const info = byId('loginActivityPageInfo'), prev = byId('loginActivityPrev'), next = byId('loginActivityNext');
    const exportButton = byId('loginActivityExport');
    const label = (name, fallback) => root.dataset[name] || fallback;
    const td = (tr, value) => {
        const cell = document.createElement('td');
        cell.textContent = String(value ?? '—');
        tr.appendChild(cell);
        return cell;
    };
    const showError = message => { alert.textContent = message; alert.classList.remove('d-none'); };
    function query() {
        const first = from.value, last = to.value;
        if (!!first !== !!last || (first && (first > last ||
            (Date.parse(last + 'T00:00:00Z') - Date.parse(first + 'T00:00:00Z')) / 86400000 > 365))) {
            throw Error(label('rangeError', 'Invalid UTC date range.'));
        }
        const result = new URLSearchParams();
        if (status.value !== '') result.set('success', status.value);
        if (first) { result.set('from', first); result.set('to', last); }
        return result;
    }
    async function load() {
        const generation = ++state.generation;
        prev.disabled = true; next.disabled = true; alert.classList.add('d-none');
        rows.replaceChildren();
        const loading = document.createElement('tr');
        td(loading, label('loading', 'Loading...')).colSpan = 2;
        rows.appendChild(loading);
        try {
            const params = query();
            params.set('page', String(state.page));
            params.set('pageSize', String(state.pageSize));
            const response = await fetch('/api/account/login-activity?' + params, {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
            });
            const body = await response.json().catch(() => null);
            if (!response.ok || !body?.success)
                throw Error(body?.message || label('error', 'Sign-in history unavailable.'));
            if (generation !== state.generation) return;
            const data = body.data || {}, items = Array.isArray(data.items) ? data.items : [];
            rows.replaceChildren();
            for (const item of items) {
                const tr = document.createElement('tr');
                const date = item.occurredAt ? new Date(item.occurredAt) : null;
                td(tr, date && !Number.isNaN(date.getTime()) ? date.toLocaleString() : '—');
                td(tr, item.isSuccess ? label('success', 'Successful') : label('failure', 'Unsuccessful'));
                rows.appendChild(tr);
            }
            if (!items.length) {
                const empty = document.createElement('tr');
                td(empty, label('empty', 'No sign-in attempts found.')).colSpan = 2;
                rows.appendChild(empty);
            }
            const total = Math.max(0, Number(data.totalCount) || 0);
            const pages = Math.max(1, Math.ceil(total / state.pageSize));
            info.textContent = label('page', 'Page') + ' ' + state.page + ' ' +
                label('of', 'of') + ' ' + pages + ' · ' + total + ' ' + label('attempts', 'attempts');
            prev.disabled = state.page <= 1;
            next.disabled = state.page >= pages;
        } catch (error) {
            if (generation !== state.generation) return;
            rows.replaceChildren(); info.textContent = '';
            showError(error.message || label('error', 'Sign-in history unavailable.'));
        }
    }
    async function exportCsv() {
        if (state.exporting) return;
        alert.classList.add('d-none');
        try {
            const params = query();
            state.exporting = true; exportButton.disabled = true;
            const response = await fetch('/api/account/login-activity/export?' + params, {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/csv' }
            });
            if (!response.ok) {
                const body = await response.json().catch(() => null);
                throw Error(body?.message || label('exportError', 'Unable to export sign-in activity.'));
            }
            const blob = await response.blob();
            const url = URL.createObjectURL(blob);
            try {
                const anchor = document.createElement('a');
                anchor.href = url;
                anchor.download = 'sign-in-activity.csv';
                document.body.appendChild(anchor);
                anchor.click();
                anchor.remove();
            } finally { URL.revokeObjectURL(url); }
        } catch (error) {
            showError(error.message || label('exportError', 'Unable to export sign-in activity.'));
        } finally {
            state.exporting = false; exportButton.disabled = false;
        }
    }
    for (const control of [status, from, to]) control.addEventListener('change', () => {
        state.page = 1; load();
    });
    byId('loginActivityRefresh').addEventListener('click', load);
    exportButton.addEventListener('click', exportCsv);
    prev.addEventListener('click', () => { if (state.page > 1) { state.page--; load(); } });
    next.addEventListener('click', () => { state.page++; load(); });
    load();
})();
