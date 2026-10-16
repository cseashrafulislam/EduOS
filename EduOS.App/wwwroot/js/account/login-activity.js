(() => {
    'use strict';
    const byId = id => document.getElementById(id);
    const state = {page: 1, pageSize: 20, generation: 0};
    const rows = byId('loginActivityRows');
    if (!rows) return;
    const status = byId('loginActivityStatus'), alert = byId('loginActivityAlert');
    const info = byId('loginActivityPageInfo'), prev = byId('loginActivityPrev'), next = byId('loginActivityNext');
    const td = (tr, value) => { const cell = document.createElement('td'); cell.textContent = String(value ?? '—'); tr.appendChild(cell); };
    async function load() {
        const generation = ++state.generation;
        prev.disabled = true; next.disabled = true; alert.classList.add('d-none');
        rows.replaceChildren();
        const loading = document.createElement('tr'); td(loading, 'Loading...'); loading.firstChild.colSpan = 2; rows.appendChild(loading);
        const query = new URLSearchParams({page: String(state.page), pageSize: String(state.pageSize)});
        if (status.value !== '') query.set('success', status.value);
        try {
            const response = await fetch('/api/account/login-activity?' + query, {credentials: 'same-origin', cache: 'no-store', headers: {Accept: 'application/json'}});
            const body = await response.json().catch(() => null);
            if (!response.ok || !body?.success) throw Error(body?.message || 'Sign-in history could not be loaded.');
            if (generation !== state.generation) return;
            const data = body.data || {}, items = data.items || [];
            rows.replaceChildren();
            for (const item of items) {
                const tr = document.createElement('tr'), date = item.occurredAt ? new Date(item.occurredAt) : null;
                td(tr, date && !Number.isNaN(date.getTime()) ? date.toLocaleString() : '—');
                td(tr, item.isSuccess ? 'Successful' : 'Unsuccessful');
                rows.appendChild(tr);
            }
            if (!items.length) { const empty = document.createElement('tr'); td(empty, 'No sign-in attempts found.'); empty.firstChild.colSpan = 2; rows.appendChild(empty); }
            const total = Math.max(0, Number(data.totalCount) || 0), pages = Math.max(1, Math.ceil(total / state.pageSize));
            info.textContent = 'Page ' + state.page + ' of ' + pages + ' · ' + total + ' attempts';
            prev.disabled = state.page <= 1; next.disabled = state.page >= pages;
        } catch (error) {
            if (generation !== state.generation) return;
            rows.replaceChildren(); info.textContent = '';
            alert.textContent = error.message || 'Sign-in history unavailable.';
            alert.classList.remove('d-none');
        }
    }
    status.addEventListener('change', () => { state.page = 1; load(); });
    byId('loginActivityRefresh').addEventListener('click', load);
    prev.addEventListener('click', () => { if (state.page > 1) { state.page--; load(); } });
    next.addEventListener('click', () => { state.page++; load(); });
    load();
})();
