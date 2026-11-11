(() => {
    'use strict';
    const root = document.getElementById('hrWorkspaceRoot');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
        document.querySelector('meta[name="request-verification-token"]')?.content;
    const identity = [document.body?.dataset?.authTenantId || '',
        document.body?.dataset?.authSessionReference || '', document.body?.dataset?.authUserId || ''];
    const sameSession = () => identity[0] === (document.body?.dataset?.authTenantId || '') &&
        identity[1] === (document.body?.dataset?.authSessionReference || '') &&
        identity[2] === (document.body?.dataset?.authUserId || '');
    const employeeStates = {1:'Active',2:'Suspended',3:'Resigned',4:'Terminated',5:'Retired'};
    const leaveStates = {1:'Draft',2:'Submitted',3:'Approved',4:'Rejected',5:'Cancelled'};
    const paging = {employees:{page:1,totalPages:1,sequence:0},
        leaves:{page:1,totalPages:1,sequence:0}};
    const pendingNotes = new Map();
    let reviewing = false,accessRevoked = false;
    function notify(message,success=false) {
        if (!sameSession()) return;
        const box = el('hrAlert');box.className = 'alert alert-' + (success ? 'success' : 'danger');
        box.textContent = message;box.focus();
    }
    function clearNotice() {
        el('hrAlert').className = 'd-none';el('hrAlert').textContent = '';
    }
    const td = value => {const cell = document.createElement('td');
        cell.textContent = value == null || value === '' ? '—' : String(value);return cell;};
    function empty(body,count,message) {
        const tr = document.createElement('tr'),cell = td(message);
        cell.colSpan = count;cell.className = 'text-center text-muted py-3';
        tr.append(cell);body.replaceChildren(tr);
    }
    function revoke(error) {
        if ([401,403].includes(error.statusCode) || !sameSession()) {
            accessRevoked = true;++paging.employees.sequence;++paging.leaves.sequence;
            pendingNotes.clear();el('hrEmployeeRows').replaceChildren();
            el('hrLeaveRows').replaceChildren();root.inert = true;
        }
        notify(error.message);
    }
    async function request(url,method='GET',body) {
        if (!sameSession() || accessRevoked) throw Error('Session changed. Reload this page.');
        const headers = {Accept:'application/json'};
        if (method !== 'GET') {
            if (!token) throw Error('Security token missing. Reload this page.');
            headers.RequestVerificationToken = token;
        }
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        const response = await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,
            ...(body === undefined ? {} : {body:JSON.stringify(body)})});
        const result = await response.json().catch(() => null);
        if (!sameSession()) throw Error('Session changed. Reload this page.');
        if (!response.ok || !result?.success) {
            const error = Error(result?.message || 'HR request failed.');error.statusCode = response.status;
            throw error;
        }
        return result.data;
    }
    function setPager(kind,data) {
        const state = paging[kind],prefix = kind === 'employees' ? 'hrEmployee' : 'hrLeave';
        state.totalPages = Math.max(1,Math.ceil((Number(data.totalCount) || 0)/25));
        el(prefix + 'Page').textContent = 'Page ' + state.page + ' of ' + state.totalPages +
            ' · ' + (Number(data.totalCount) || 0) + ' records';
        el(prefix + 'Prev').disabled = state.page <= 1;
        el(prefix + 'Next').disabled = state.page >= state.totalPages;
    }
    const employeeFilter = () => JSON.stringify([el('hrEmployeeText').value.trim(),
        el('hrEmployeeTeacher').value,el('hrEmployeeActive').value]);
    const leaveFilter = () => JSON.stringify([el('hrLeaveText').value.trim(),
        el('hrLeaveStatus').value,el('hrLeaveFrom').value]);
    function mayDiscardNotes() {
        if (!pendingNotes.size) return true;
        if (!window.confirm('Discard unsaved leave review notes?')) return false;
        pendingNotes.clear();return true;
    }
    async function loadEmployees() {
        if (accessRevoked) return false;
        const state = paging.employees,seq = ++state.sequence,page = state.page,filter = employeeFilter();
        empty(el('hrEmployeeRows'),6,'Loading employees…');
        const [text,teacher,active] = JSON.parse(filter),query = new URLSearchParams({page:String(page),pageSize:'25'});
        if (text) query.set('search',text);
        if (teacher) query.set('canTeach',teacher);
        if (active) query.set('state',active);
        try {
            const data = await request('/api/hr/employees?' + query);
            if (seq !== state.sequence || accessRevoked || page !== state.page ||
                filter !== employeeFilter()) return false;
            const body = el('hrEmployeeRows');body.replaceChildren();
            for (const employee of data?.items || []) {
                const tr = document.createElement('tr');
                [employee.employeeCode,employee.fullName,employee.designationName,
                    employee.organizationUnitName,String(employee.joiningDate || '').slice(0,10),
                    employeeStates[employee.state] || employee.state].forEach(value => tr.append(td(value)));
                body.append(tr);
            }
            if (!data?.items?.length) empty(body,6,'No matching employees.');
            setPager('employees',data);return true;
        } catch (error) {
            if (seq === state.sequence) {empty(el('hrEmployeeRows'),6,'Unable to load employees.');revoke(error);}
            return false;
        }
    }
    async function loadLeaves(preserve=false) {
        if (accessRevoked) return false;
        const state = paging.leaves,seq = ++state.sequence,page = state.page,filter = leaveFilter();
        if (!preserve) empty(el('hrLeaveRows'),6,'Loading leave requests…');
        const [text,status,from] = JSON.parse(filter),query = new URLSearchParams({page:String(page),pageSize:'25'});
        if (text) query.set('search',text);
        if (status) query.set('state',status);
        if (from) query.set('fromDate',from);
        try {
            const data = await request('/api/hr/leaves?' + query);
            if (seq !== state.sequence || accessRevoked || page !== state.page ||
                filter !== leaveFilter()) return false;
            const body = el('hrLeaveRows');body.replaceChildren();
            for (const leave of data?.items || []) {
                const tr = document.createElement('tr'),type = td(leave.leaveTypeName);
                const reason = document.createElement('small');
                reason.className = 'd-block text-muted';reason.textContent = 'Reason: ' + (leave.reason || '—');
                type.append(reason);
                [td(leave.employeeName + ' (' + leave.employeeCode + ')'),type,
                    td(String(leave.fromDate || '').slice(0,10) + ' – ' + String(leave.toDate || '').slice(0,10)),
                    td(leave.totalDays),td(leaveStates[leave.state] || leave.state)]
                    .forEach(cell => tr.append(cell));
                const action = document.createElement('td');
                if (leave.state === 2) {
                    const remarks = document.createElement('input');
                    remarks.type = 'text';remarks.maxLength = 1000;remarks.className = 'form-control form-control-sm mb-2';
                    remarks.placeholder = 'Review note (required for rejection)';
                    remarks.value = pendingNotes.get(leave.id) || '';remarks.dataset.remarks = '1';
                    remarks.addEventListener('input',() => {
                        const value = remarks.value.trim();
                        if (value) pendingNotes.set(leave.id,value);else pendingNotes.delete(leave.id);
                    });
                    action.append(remarks);
                    for (const [decision,label,style] of [[3,'Approve','success'],[4,'Reject','danger']]) {
                        const button = document.createElement('button');
                        button.type = 'button';button.className = 'btn btn-sm btn-outline-' + style + ' me-2';
                        button.textContent = label;
                        button.addEventListener('click',() => reviewLeave(leave,decision,tr));
                        action.append(button);
                    }
                } else action.textContent = leave.reviewNote || leaveStates[leave.state] || 'Reviewed';
                tr.append(action);body.append(tr);
            }
            if (!data?.items?.length) empty(body,6,'No matching leave requests.');
            setPager('leaves',data);return true;
        } catch (error) {
            if (seq === state.sequence) {if (!preserve) empty(el('hrLeaveRows'),6,'Unable to load leave requests.');revoke(error);}
            return false;
        }
    }
    async function reviewLeave(leave,decision,tr) {
        if (reviewing || accessRevoked) return;
        if (!leave.rowVersion) return notify('Leave version unavailable. Reload before reviewing.');
        const note = tr.querySelector('[data-remarks]')?.value.trim() || '';
        if (decision === 4 && !note) return notify('A rejection note is required.');
        if (!window.confirm((decision === 3 ? 'Approve' : 'Reject') + ' leave for ' +
            leave.employeeName + '? This decision is audited.')) return;
        reviewing = true;++paging.leaves.sequence;root.inert = true;
        const buttons = [...tr.querySelectorAll('button')];buttons.forEach(button => button.disabled = true);
        let committed = false;
        try {
            await request('/api/hr/leaves/review','POST',{
                id:leave.id,state:decision,reviewNote:note || null,rowVersion:leave.rowVersion
            });
            committed = true;pendingNotes.delete(leave.id);
            const refreshed = await loadLeaves(true);
            notify(refreshed ? 'Leave request ' + leaveStates[decision].toLowerCase() + '.' :
                'Leave decision recorded; the list could not refresh. Reload to verify.',true);
            if (!refreshed) {
                const action = tr.children[5];action.replaceChildren();
                action.textContent = 'Decision recorded — reload to verify.';
            }
        } catch (error) {
            revoke(error);
            if (!committed && !accessRevoked)
                notify(error.message + ' Check the leave status before another review attempt.');
        } finally {
            reviewing = false;root.inert = accessRevoked;
            if (!committed && !accessRevoked) buttons.forEach(button => button.disabled = false);
        }
    }
    function changePage(kind,page) {
        if (reviewing || accessRevoked) return;
        if (kind === 'leaves' && !mayDiscardNotes()) return;
        paging[kind].page = page;
        if (kind === 'employees') loadEmployees();else loadLeaves();
    }
    el('hrEmployeeSearch').addEventListener('submit',event => {
        event.preventDefault();if (reviewing || accessRevoked) return;
        paging.employees.page = 1;clearNotice();loadEmployees();
    });
    el('hrLeaveSearch').addEventListener('submit',event => {
        event.preventDefault();if (reviewing || accessRevoked || !mayDiscardNotes()) return;
        paging.leaves.page = 1;clearNotice();loadLeaves();
    });
    el('hrEmployeePrev').addEventListener('click',() => changePage('employees',Math.max(1,paging.employees.page-1)));
    el('hrEmployeeNext').addEventListener('click',() => changePage('employees',Math.min(paging.employees.totalPages,paging.employees.page+1)));
    el('hrLeavePrev').addEventListener('click',() => changePage('leaves',Math.max(1,paging.leaves.page-1)));
    el('hrLeaveNext').addEventListener('click',() => changePage('leaves',Math.min(paging.leaves.totalPages,paging.leaves.page+1)));
    window.addEventListener('beforeunload',event => {
        if (!pendingNotes.size) return;
        event.preventDefault();event.returnValue = '';
    });
    if (el('hrEmployeeSearch').closest('[data-active-feature]')?.dataset.activeFeature === 'leaves')
        loadLeaves();
    else loadEmployees();
})();
