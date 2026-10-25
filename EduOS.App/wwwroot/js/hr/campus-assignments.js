(() => {
    'use strict';
    const root = document.getElementById('hrCampusWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = document.querySelector('meta[name="request-verification-token"]')?.content;
    const state = { employee: null, page: 1, pages: 1, requestKey: null, signature: null };
    const cell = value => { const td = document.createElement('td'); td.textContent = value == null || value === '' ? '—' : String(value); return td; };
    const date = value => value ? String(value).slice(0, 10) : '';
    function notice(message, success = false) {
        const box = el('hrCampusAlert');
        box.className = 'alert alert-' + (success ? 'success' : 'danger');
        box.textContent = message;
        box.focus();
    }
    async function api(path, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (method !== 'GET') {
            if (!token) throw new Error('Missing antiforgery token. Reload this page.');
            headers.RequestVerificationToken = token;
        }
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        const response = await fetch(path, { method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Request failed.');
        return result.data;
    }
    function option(value, label) {
        const o = document.createElement('option'); o.value = value; o.textContent = label; return o;
    }
    async function findEmployees() {
        const search = el('hrCampusSearch').value.trim();
        if (search.length < 2 || search.length > 100) return notice('Enter 2–100 characters to search employees.');
        try {
            const data = await api('/api/hr/employees?search=' + encodeURIComponent(search) + '&page=1&pageSize=50');
            const select = el('hrCampusEmployee');
            select.replaceChildren(option('', 'Choose employee'));
            for (const employee of data?.items || []) select.append(option(employee.reference, employee.employeeCode + ' — ' + employee.fullName));
            state.employee = null;
            state.page = 1;
            el('hrCampusRows').replaceChildren();
            el('hrCampusPage').textContent = '';
            el('hrCampusPrev').disabled = true;
            el('hrCampusNext').disabled = true;
            if (!(data?.items || []).length) notice('No matching employees.');
        } catch (error) { notice(error.message); }
    }
    async function loadCampuses() {
        const rows = await api('/api/institution-onboarding/campus-list');
        const select = el('hrCampusId');
        select.replaceChildren(option('', 'Choose active campus'));
        for (const campus of rows || []) if (campus.isActive) select.append(option(campus.id, campus.name + ' (' + campus.code + ')'));
    }
    async function loadHistory() {
        const reference = state.employee;
        if (!reference) return;
        const body = el('hrCampusRows');
        body.replaceChildren();
        try {
            const data = await api('/api/hr/employees/' + encodeURIComponent(reference) + '/campus-assignments?page=' + state.page + '&pageSize=20');
            if (state.employee !== reference) return;
            const rows = data?.items || [];
            for (const row of rows) {
                const tr = document.createElement('tr');
                [row.campusName, row.isPrimary ? 'Yes' : 'No', date(row.effectiveFrom),
                    date(row.effectiveTo) || 'Open', row.isCurrent ? 'Current' : 'Historical'].forEach(value => tr.append(cell(value)));
                body.append(tr);
            }
            if (!rows.length) { const tr = document.createElement('tr'), td = cell('No campus assignments.'); td.colSpan = 5; tr.append(td); body.append(tr); }
            state.pages = Math.max(1, Math.ceil(Number(data?.totalCount || 0) / 20));
            state.page = Number(data?.page) || state.page;
            el('hrCampusPage').textContent = 'Page ' + state.page + ' of ' + state.pages + ' · ' + Number(data?.totalCount || 0) + ' records';
            el('hrCampusPrev').disabled = state.page <= 1;
            el('hrCampusNext').disabled = state.page >= state.pages;
        } catch (error) { notice(error.message); }
    }
    async function assign(event) {
        event.preventDefault();
        if (!state.employee || !event.currentTarget.reportValidity()) return notice('Select an employee and campus.');
        const campusId = Number(el('hrCampusId').value), effectiveFrom = el('hrCampusFrom').value,
            effectiveTo = el('hrCampusTo').value || null, isPrimary = el('hrCampusPrimary').checked;
        if (!Number.isSafeInteger(campusId) || campusId <= 0 || !effectiveFrom ||
            (effectiveTo && effectiveTo < effectiveFrom)) return notice('Invalid campus or effective dates.');
        const payload = { employeeReference: state.employee, campusId, effectiveFrom, effectiveTo, isPrimary };
        const signature = JSON.stringify(payload);
        if (signature !== state.signature) { state.signature = signature; state.requestKey = crypto.randomUUID(); }
        payload.clientRequestId = state.requestKey;
        if (!window.confirm('Assign this campus? Existing assignment periods may be closed.')) return;
        const button = el('hrCampusSave'); button.disabled = true;
        try {
            await api('/api/hr/employees/campuses', 'POST', payload);
            state.requestKey = null; state.signature = null; state.page = 1;
            await loadHistory(); notice('Campus assignment recorded.', true);
        } catch (error) { notice(error.message + ' Keep the same form values for a safe retry.'); }
        finally { button.disabled = false; }
    }
    el('hrCampusFind').addEventListener('click', findEmployees);
    el('hrCampusSearch').addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); findEmployees(); } });
    el('hrCampusEmployee').addEventListener('change', () => {
        state.employee = el('hrCampusEmployee').value || null; state.page = 1;
        state.requestKey = null; state.signature = null; loadHistory();
    });
    el('hrCampusForm').addEventListener('submit', assign);
    el('hrCampusPrev').addEventListener('click', () => { if (state.page > 1) { state.page--; loadHistory(); } });
    el('hrCampusNext').addEventListener('click', () => { if (state.page < state.pages) { state.page++; loadHistory(); } });
    el('hrCampusFrom').value = new Date().toISOString().slice(0, 10);
    loadCampuses().catch(error => notice(error.message));
})();
