(() => {
    'use strict';
    const root = document.getElementById('employeePortalRoot');
    if (!root) return;
    const el = id => document.getElementById(id);
    const form = el('employeeLeaveForm');
    const guard = window.EduOSFormGuard?.create(form);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
        document.querySelector('meta[name="request-verification-token"]')?.content;
    const identity = [document.body?.dataset?.authTenantId || '',
        document.body?.dataset?.authSessionReference || '', document.body?.dataset?.authUserId || ''];
    const sameSession = () => identity[0] === (document.body?.dataset?.authTenantId || '') &&
        identity[1] === (document.body?.dataset?.authSessionReference || '') &&
        identity[2] === (document.body?.dataset?.authUserId || '');
    const attendanceStates = {1:'Present',2:'Absent',3:'Late',4:'Leave',5:'Excused'};
    const leaveStates = {1:'Draft',2:'Submitted',3:'Approved',4:'Rejected',5:'Cancelled'};
    const now = new Date();
    let attendanceSeq = 0, balanceSeq = 0, historySeq = 0, profileSeq = 0;
    let page = 1, pages = 1, submitting = false, accessRevoked = false;
    let availableTypes = new Set(), balanceYear = 0, retryKey = null, retrySignature = '', submittedSignature = '';
    const localDate = d => [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');
    const fmtDate = value => value ? String(value).slice(0,10) : '—';
    const fmtTime = value => value ? String(value).slice(0,5) : '—';
    function notice(text, success=false) {
        if (!sameSession()) return;
        const box = el('employeePortalAlert');
        box.className = 'alert alert-' + (success ? 'success' : 'danger');
        box.textContent = text; box.focus();
    }
    function cell(value) {
        const td = document.createElement('td');
        td.textContent = value === null || value === undefined || value === '' ? '—' : String(value);
        return td;
    }
    function rows(id, count, items, columns) {
        const body = el(id); body.replaceChildren();
        if (!items?.length) {
            const tr = document.createElement('tr'), td = cell('No records found.');
            td.colSpan = count; td.className = 'text-center text-muted py-3'; tr.append(td); body.append(tr);
            return;
        }
        for (const item of items) {
            const tr = document.createElement('tr');
            for (const column of columns) tr.append(cell(typeof column === 'function' ? column(item) : item[column]));
            body.append(tr);
        }
    }
    function handleError(error) {
        if ([401,403].includes(error.statusCode) || !sameSession()) {
            accessRevoked = true; ++attendanceSeq; ++balanceSeq; ++historySeq; ++profileSeq;
            el('employeeProfile').replaceChildren();
            for (const [id,count] of [['employeeAttendanceRows',5],['employeeLeaveBalances',5],['employeeLeaveHistory',6]])
                rows(id,count,[],[]);
            el('employeeLeaveType').replaceChildren(new Option('Access unavailable',''));
            form.reset(); guard?.markClean(); root.inert = true;
            if (!sameSession()) return;
        }
        notice(error.message);
    }
    async function api(url, method='GET', body) {
        if (!sameSession() || accessRevoked) throw Error('Session changed. Reload the page.');
        const headers = {Accept:'application/json'};
        if (method !== 'GET') {
            if (!token) throw Error('Security token missing. Reload the page.');
            headers.RequestVerificationToken = token;
        }
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        const response = await fetch(url, {method, credentials:'same-origin', cache:'no-store', headers,
            ...(body === undefined ? {} : {body:JSON.stringify(body)})});
        const result = await response.json().catch(() => null);
        if (!sameSession()) throw Error('Session changed. Reload the page.');
        if (!response.ok || !result?.success) {
            const error = Error(result?.message || 'Employee request failed.');
            error.statusCode = response.status; throw error;
        }
        return result.data;
    }
    async function loadProfile() {
        const seq = ++profileSeq;
        try {
            const profile = await api('/api/employee-portal/profile');
            if (seq !== profileSeq || accessRevoked) return false;
            const list = el('employeeProfile'); list.replaceChildren();
            for (const [name,value] of [['Employee code',profile.employeeCode],['Full name',profile.fullName],
                ['Phone',profile.phone],['Email',profile.email],['Joined',fmtDate(profile.joiningDate)],
                ['Position',profile.canTeach ? 'Teacher' : 'Staff']]) {
                const dt = document.createElement('dt'), dd = document.createElement('dd');
                dt.className = 'col-sm-4'; dd.className = 'col-sm-8';
                dt.textContent = name; dd.textContent = value || '—'; list.append(dt,dd);
            }
            return true;
        } catch (error) { if (seq === profileSeq) handleError(error); return false; }
    }
    async function loadAttendance() {
        if (accessRevoked) return false;
        const from = el('employeeFromDate').value, to = el('employeeToDate').value;
        if (!from || !to || from > to) return notice('Select a valid attendance date range.'), false;
        const seq = ++attendanceSeq;
        try {
            const data = await api('/api/employee-portal/attendance?' + new URLSearchParams({fromDate:from,toDate:to}));
            if (seq !== attendanceSeq || accessRevoked || from !== el('employeeFromDate').value ||
                to !== el('employeeToDate').value) return false;
            rows('employeeAttendanceRows',5,data,[x => fmtDate(x.attendanceDate),
                x => attendanceStates[x.state] || x.state,x => fmtTime(x.inTime),x => fmtTime(x.outTime),'remarks']);
            return true;
        } catch (error) { if (seq === attendanceSeq) handleError(error); return false; }
    }
    function selectedYear() { return Number(el('employeeLeaveFrom').value.slice(0,4)) || now.getFullYear(); }
    async function loadBalances() {
        if (accessRevoked) return false;
        const year = selectedYear(), seq = ++balanceSeq;
        try {
            const data = await api('/api/employee-portal/leave/balances?year=' + year);
            if (seq !== balanceSeq || accessRevoked || year !== selectedYear()) return false;
            balanceYear = year; availableTypes = new Set((data || []).filter(x => x.leaveTypeId > 0)
                .map(x => String(x.leaveTypeId)));
            el('employeeBalanceYear').textContent = '(' + year + ')';
            rows('employeeLeaveBalances',5,data,['leaveTypeName','annualEntitlement','usedDays','pendingDays','remainingDays']);
            const select = el('employeeLeaveType'), previous = select.value, dirty = guard?.isDirty();
            select.replaceChildren(new Option('Choose a leave type',''));
            for (const item of data || []) {
                if (item.leaveTypeId > 0)
                    select.add(new Option(item.leaveTypeName + ' (' + item.remainingDays + ' days available)',item.leaveTypeId));
            }
            if (previous && !availableTypes.has(previous) && dirty)
                select.add(new Option('Previous selection unavailable — choose another',previous));
            if (previous && (availableTypes.has(previous) || dirty)) select.value = previous;
            if (!dirty) guard?.markClean();
            return true;
        } catch (error) { if (seq === balanceSeq) handleError(error); return false; }
    }
    async function loadLeaveHistory() {
        if (accessRevoked) return false;
        const seq = ++historySeq, requestedPage = page;
        try {
            const data = await api('/api/employee-portal/leave/page?' +
                new URLSearchParams({page:String(requestedPage),pageSize:'20'}));
            if (seq !== historySeq || accessRevoked || requestedPage !== page) return false;
            pages = Math.max(1, Math.ceil((data.totalCount || 0) / 20));
            rows('employeeLeaveHistory',6,data.items,['leaveTypeName',x => fmtDate(x.fromDate),
                x => fmtDate(x.toDate),'totalDays',x => leaveStates[x.state] || x.state,'reason']);
            el('employeeLeavePage').textContent = 'Page ' + page + ' of ' + pages + ' · ' + data.totalCount + ' records';
            el('employeeLeavePrev').disabled = page <= 1;
            el('employeeLeaveNext').disabled = page >= pages;
            return true;
        } catch (error) { if (seq === historySeq) handleError(error); return false; }
    }
    async function applyLeave(event) {
        event.preventDefault();
        if (submitting || accessRevoked || !sameSession() || !form.reportValidity()) return;
        const command = {leaveTypeId:Number(el('employeeLeaveType').value),
            fromDate:el('employeeLeaveFrom').value,toDate:el('employeeLeaveTo').value,
            reason:el('employeeLeaveReason').value.trim()};
        if (!availableTypes.has(String(command.leaveTypeId)) || balanceYear !== selectedYear())
            return notice('Load the selected year and choose an available leave type.');
        if (command.toDate < command.fromDate) return notice('Leave end date must not precede start date.');
        if (command.fromDate.slice(0,4) !== command.toDate.slice(0,4))
            return notice('A leave request must fit within one calendar year.');
        if (command.reason.length < 3) return notice('Give a leave reason of at least 3 characters.');
        const signature = JSON.stringify(command);
        if (signature === submittedSignature) return notice('This leave request was already submitted. Review the history.');
        if (retrySignature !== signature) { retrySignature = signature; retryKey = crypto.randomUUID(); }
        submitting = true; form.inert = true;
        let committed = false;
        try {
            await api('/api/employee-portal/leave','POST',{clientRequestId:retryKey,...command});
            committed = true; submittedSignature = signature; retrySignature = ''; retryKey = null;
            form.reset(); el('employeeLeaveFrom').value = localDate(now);
            el('employeeLeaveTo').value = localDate(now); guard?.markClean(); page = 1;
            const refreshed = await Promise.all([loadBalances(),loadLeaveHistory()]);
            notice(refreshed.every(Boolean) ? 'Leave request submitted.' :
                'Leave request submitted; history or balances could not refresh. Reload to verify.', true);
        } catch (error) {
            handleError(error);
            if (!committed && !accessRevoked)
                notice(error.message + ' Check leave history before retrying with the same request.');
        } finally { submitting = false; form.inert = accessRevoked; }
    }
    const monthAgo = new Date(now); monthAgo.setDate(monthAgo.getDate() - 30);
    el('employeeFromDate').value = localDate(monthAgo); el('employeeToDate').value = localDate(now);
    el('employeeLeaveFrom').value = localDate(now); el('employeeLeaveTo').value = localDate(now);
    guard?.markClean();
    el('employeeAttendanceFilter').addEventListener('submit',event => {
        event.preventDefault(); if (!submitting && !accessRevoked) loadAttendance();
    });
    form.addEventListener('submit',applyLeave);
    el('employeeLeaveFrom').addEventListener('change',() => { if (!submitting) loadBalances(); });
    el('employeeRefreshLeave').addEventListener('click',() => {
        if (!submitting && !accessRevoked) { loadBalances(); loadLeaveHistory(); }
    });
    el('employeeLeavePrev').addEventListener('click',() => {
        if (!submitting && !accessRevoked && page > 1) { --page; loadLeaveHistory(); }
    });
    el('employeeLeaveNext').addEventListener('click',() => {
        if (!submitting && !accessRevoked && page < pages) { ++page; loadLeaveHistory(); }
    });
    loadProfile(); loadAttendance(); loadBalances(); loadLeaveHistory();
})();
