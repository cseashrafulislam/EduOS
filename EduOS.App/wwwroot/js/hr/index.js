(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('hrEmployeeSearch')) return;
    const csrfToken = document.querySelector('meta[name="request-verification-token"]')?.content;
    const employeeStates = { 1: 'Active', 2: 'Suspended', 3: 'Resigned', 4: 'Terminated', 5: 'Retired' };
    const leaveStates = { 1: 'Draft', 2: 'Submitted', 3: 'Approved', 4: 'Rejected', 5: 'Cancelled' };
    const paging = { employees: { page: 1, totalPages: 1 }, leaves: { page: 1, totalPages: 1 } };
    function notify(message, success = false) {
        const alert = el('hrAlert');
        alert.className = 'alert alert-' + (success ? 'success' : 'danger');
        alert.textContent = message;
        alert.focus();
    }
    function clearNotice() { el('hrAlert').className = 'd-none'; el('hrAlert').textContent = ''; }
    async function request(url, method = 'GET', body = undefined) {
        const headers = { Accept: 'application/json' };
        if (method !== 'GET') {
            if (!csrfToken) throw new Error('Missing antiforgery token. Reload the page.');
            headers.RequestVerificationToken = csrfToken;
        }
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        const response = await fetch(url, { method, credentials: 'same-origin', cache: 'no-store', headers, ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw new Error(result?.message || 'The request failed. Please reload and try again.');
        return result.data;
    }
    function td(value) {
        const cell = document.createElement('td');
        cell.textContent = value === null || value === undefined || value === '' ? '—' : String(value);
        return cell;
    }
    function empty(table, count, message) {
        table.replaceChildren();
        const tr = document.createElement('tr');
        const cell = td(message);
        cell.colSpan = count;
        cell.className = 'text-center text-muted py-3';
        tr.append(cell);
        table.append(tr);
    }
    function changePage(kind, page) {
        paging[kind].page = page;
        if (kind === 'employees') loadEmployees(); else loadLeaves();
    }
    function setPager(kind, data) {
        const state = paging[kind], prefix = kind === 'employees' ? 'hrEmployee' : 'hrLeave';
        state.totalPages = Math.max(1, Number(data.totalPages) || 1);
        state.page = Number(data.page) || state.page;
        el(prefix + 'Page').textContent = 'Page ' + state.page + ' of ' + state.totalPages + ' · ' + (Number(data.totalCount) || 0) + ' records';
        el(prefix + 'Prev').disabled = state.page <= 1;
        el(prefix + 'Next').disabled = state.page >= state.totalPages;
    }
    async function loadEmployees() {
        const body = el('hrEmployeeRows');
        empty(body, 6, 'Loading employees…');
        const query = new URLSearchParams({ page: String(paging.employees.page), pageSize: '25' });
        if (el('hrEmployeeText').value.trim()) query.set('search', el('hrEmployeeText').value.trim());
        if (el('hrEmployeeTeacher').value) query.set('canTeach', el('hrEmployeeTeacher').value);
        if (el('hrEmployeeActive').value) query.set('state', el('hrEmployeeActive').value);
        try {
            const data = await request('/api/hr/employees?' + query);
            const items = data?.items || [];
            body.replaceChildren();
            if (!items.length) empty(body, 6, 'No matching employees.');
            for (const employee of items) {
                const tr = document.createElement('tr');
                [employee.employeeCode, employee.fullName, employee.designationName, employee.organizationUnitName, String(employee.joiningDate || '').slice(0, 10), employeeStates[employee.state] || employee.state].forEach(v => tr.append(td(v)));
                body.append(tr);
            }
            setPager('employees', data);
        } catch (error) { empty(body, 6, 'Unable to load employees.'); notify(error.message); }
    }
    async function reviewLeave(leave, decision, tr) {
        if (!leave.rowVersion) { notify('Leave concurrency version missing. Reload before reviewing.'); return; }
        const note = tr.querySelector('[data-remarks]').value.trim();
        if (decision === 4 && !note) { notify('A rejection note is required.'); return; }
        if (!window.confirm((decision === 3 ? 'Approve' : 'Reject') + ' leave for ' + leave.employeeName + '?')) return;
        const buttons = [...tr.querySelectorAll('button')];
        buttons.forEach(button => button.disabled = true);
        try {
            await request('/api/hr/leaves/review', 'POST', {
                id: leave.id, state: decision, reviewNote: note || null, rowVersion: leave.rowVersion
            });
            notify('Leave request ' + leaveStates[decision].toLowerCase() + '.', true);
            await loadLeaves();
        } catch (error) { notify(error.message); buttons.forEach(button => button.disabled = false); }
    }
    async function loadLeaves() {
        const body = el('hrLeaveRows');
        empty(body, 6, 'Loading leave requests…');
        const query = new URLSearchParams({ page: String(paging.leaves.page), pageSize: '25' });
        if (el('hrLeaveText').value.trim()) query.set('search', el('hrLeaveText').value.trim());
        if (el('hrLeaveStatus').value) query.set('state', el('hrLeaveStatus').value);
        if (el('hrLeaveFrom').value) query.set('fromDate', el('hrLeaveFrom').value);
        try {
            const data = await request('/api/hr/leaves?' + query);
            const items = data?.items || [];
            body.replaceChildren();
            if (!items.length) empty(body, 6, 'No matching leave requests.');
            for (const leave of items) {
                const tr = document.createElement('tr');
                [leave.employeeName + ' (' + leave.employeeCode + ')', leave.leaveTypeName,
                    String(leave.fromDate || '').slice(0, 10) + ' – ' + String(leave.toDate || '').slice(0, 10),
                    leave.totalDays, leaveStates[leave.state] || leave.state].forEach(value => tr.append(td(value)));
                const action = document.createElement('td');
                if (leave.state === 2) {
                    const remarks = document.createElement('input');
                    remarks.type = 'text'; remarks.maxLength = 1000; remarks.className = 'form-control form-control-sm mb-2';
                    remarks.placeholder = 'Review note (required for rejection)'; remarks.dataset.remarks = '1';
                    action.append(remarks);
                    for (const [decision, label, style] of [[3, 'Approve', 'success'], [4, 'Reject', 'danger']]) {
                        const button = document.createElement('button');
                        button.type = 'button'; button.className = 'btn btn-sm btn-outline-' + style + ' me-2';
                        button.textContent = label;
                        button.addEventListener('click', () => reviewLeave(leave, decision, tr));
                        action.append(button);
                    }
                } else action.textContent = leave.reviewNote || leaveStates[leave.state] || 'Reviewed';
                tr.append(action); body.append(tr);
            }
            setPager('leaves', data);
        } catch (error) { empty(body, 6, 'Unable to load leave requests.'); notify(error.message); }
    }
    el('hrEmployeeSearch').addEventListener('submit', event => { event.preventDefault(); paging.employees.page = 1; clearNotice(); loadEmployees(); });
    el('hrLeaveSearch').addEventListener('submit', event => { event.preventDefault(); paging.leaves.page = 1; clearNotice(); loadLeaves(); });
    el('hrEmployeePrev').addEventListener('click', () => changePage('employees', Math.max(1, paging.employees.page - 1)));
    el('hrEmployeeNext').addEventListener('click', () => changePage('employees', Math.min(paging.employees.totalPages, paging.employees.page + 1)));
    el('hrLeavePrev').addEventListener('click', () => changePage('leaves', Math.max(1, paging.leaves.page - 1)));
    el('hrLeaveNext').addEventListener('click', () => changePage('leaves', Math.min(paging.leaves.totalPages, paging.leaves.page + 1)));
    loadEmployees(); loadLeaves();
})();
