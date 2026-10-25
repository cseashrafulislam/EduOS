(() => {
    'use strict';
    const root = document.getElementById('hrShiftWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = document.querySelector('meta[name="request-verification-token"]')?.content;
    const state = { employee: null, page: 1, pages: 1, requestKey: null, signature: null };
    const cell = value => { const td = document.createElement('td'); td.textContent = value == null || value === '' ? '—' : String(value); return td; };
    const date = value => value ? String(value).slice(0, 10) : '';
    function notice(message, success = false) {
        const box = el('hrShiftAlert');
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
        const search = el('hrShiftSearch').value.trim();
        if (search.length < 2 || search.length > 100) return notice('Enter 2–100 characters to search employees.');
        try {
            const data = await api('/api/hr/employees?search=' + encodeURIComponent(search) + '&page=1&pageSize=50');
            const select = el('hrShiftEmployee');
            select.replaceChildren(option('', 'Choose employee'));
            for (const employee of data?.items || []) select.append(option(employee.reference, employee.employeeCode + ' — ' + employee.fullName));
            state.employee = null;
            el('hrShiftRows').replaceChildren();
            el('hrShiftPage').textContent = '';
            if (!(data?.items || []).length) notice('No matching employees.');
        } catch (error) { notice(error.message); }
    }
    async function loadShifts() {
        const rows = await api('/api/hr/setup/shifts');
        const select = el('hrShiftCode');
        select.replaceChildren(option('', 'Choose active shift'));
        for (const shift of rows || []) if (shift.isActive) select.append(option(shift.id, shift.name + ' (' + String(shift.startTime).slice(0, 5) + '–' + String(shift.endTime).slice(0, 5) + ')'));
    }
    async function loadHistory() {
        const reference = state.employee;
        if (!reference) return;
        const body = el('hrShiftRows');
        body.replaceChildren();
        try {
            const data = await api('/api/hr/employees/' + encodeURIComponent(reference) + '/shift-assignments?page=' + state.page + '&pageSize=20');
            const rows = data?.items || [];
            for (const row of rows) {
                const tr = document.createElement('tr');
                [row.workShiftName, date(row.effectiveFrom), date(row.effectiveTo) || 'Open', row.isCurrent ? 'Current' : 'Historical'].forEach(value => tr.append(cell(value)));
                body.append(tr);
            }
            if (!rows.length) { const tr = document.createElement('tr'), td = cell('No shift assignments.'); td.colSpan = 4; tr.append(td); body.append(tr); }
            state.pages = Math.max(1, Math.ceil(Number(data?.totalCount || 0) / 20));
            state.page = Number(data?.page) || state.page;
            el('hrShiftPage').textContent = 'Page ' + state.page + ' of ' + state.pages + ' · ' + Number(data?.totalCount || 0) + ' records';
            el('hrShiftPrev').disabled = state.page <= 1;
            el('hrShiftNext').disabled = state.page >= state.pages;
        } catch (error) { notice(error.message); }
    }
    async function assign(event) {
        event.preventDefault();
        if (!state.employee || !event.currentTarget.reportValidity()) return notice('Select an employee and shift.');
        const workShiftId = Number(el('hrShiftCode').value), effectiveFrom = el('hrShiftFrom').value,
            effectiveTo = el('hrShiftTo').value || null;
        if (!workShiftId || !effectiveFrom || (effectiveTo && effectiveTo < effectiveFrom)) return notice('Invalid shift or effective dates.');
        const payload = { employeeReference: state.employee, workShiftId, effectiveFrom, effectiveTo };
        const signature = JSON.stringify(payload);
        if (signature !== state.signature) { state.signature = signature; state.requestKey = crypto.randomUUID(); }
        payload.clientRequestId = state.requestKey;
        if (!window.confirm('Assign this shift? Previous effective-dated history will be preserved.')) return;
        const button = el('hrShiftSave'); button.disabled = true;
        try {
            await api('/api/hr/employees/shifts', 'POST', payload);
            state.requestKey = null; state.signature = null; state.page = 1;
            await loadHistory(); notice('Shift assignment recorded.', true);
        } catch (error) { notice(error.message + ' Keep the same form values for a safe retry.'); }
        finally { button.disabled = false; }
    }
    el('hrShiftFind').addEventListener('click', findEmployees);
    el('hrShiftSearch').addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); findEmployees(); } });
    el('hrShiftEmployee').addEventListener('change', () => { state.employee = el('hrShiftEmployee').value || null; state.page = 1; state.requestKey = null; state.signature = null; loadHistory(); });
    el('hrShiftForm').addEventListener('submit', assign);
    el('hrShiftPrev').addEventListener('click', () => { if (state.page > 1) { state.page--; loadHistory(); } });
    el('hrShiftNext').addEventListener('click', () => { if (state.page < state.pages) { state.page++; loadHistory(); } });
    el('hrShiftFrom').value = new Date().toISOString().slice(0, 10);
    loadShifts().catch(error => notice(error.message));
})();
