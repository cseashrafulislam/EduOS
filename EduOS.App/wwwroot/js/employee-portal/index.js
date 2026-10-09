(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('employeePortalTitle')) return;
    const today = new Date();
    function localDate(date) {
        const y = date.getFullYear(), m = String(date.getMonth() + 1).padStart(2, '0'), d = String(date.getDate()).padStart(2, '0');
        return y + '-' + m + '-' + d;
    }
    function showAlert(text, success = false) {
        const box = el('employeePortalAlert');
        box.className = 'alert alert-' + (success ? 'success' : 'danger');
        box.textContent = text;
        box.focus();
    }
    async function request(url, options = {}) {
        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json', ...(options.body ? { 'Content-Type': 'application/json' } : {}) }, ...options });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Unable to load employee information.');
        return result.data;
    }
    function td(value) { const cell = document.createElement('td'); cell.textContent = value === null || value === undefined || value === '' ? '—' : String(value); return cell; }
    function fillRows(id, count, records, columns) {
        const body = el(id); body.replaceChildren();
        if (!records?.length) {
            const tr = document.createElement('tr'), cell = td('No records found.');
            cell.colSpan = count; cell.className = 'text-center text-muted py-3'; tr.append(cell); body.append(tr); return;
        }
        for (const record of records) {
            const tr = document.createElement('tr');
            columns.forEach(column => tr.append(td(typeof column === 'function' ? column(record) : record[column])));
            body.append(tr);
        }
    }
    const fmtDate = value => value ? String(value).slice(0, 10) : '—';
    const fmtTime = value => value ? String(value).slice(0, 5) : '—';
    async function loadProfile() {
        try {
            const profile = await request('/api/employee-portal/profile');
            const list = el('employeeProfile'); list.replaceChildren();
            for (const [name, value] of [['Employee code', profile.employeeCode], ['Full name', profile.fullName],
                ['Phone', profile.phone], ['Email', profile.email], ['Joined', fmtDate(profile.joiningDate)],
                ['Position', profile.isTeacher ? 'Teacher' : 'Staff']]) {
                const label = document.createElement('dt'); label.className = 'col-sm-4'; label.textContent = name;
                const detail = document.createElement('dd'); detail.className = 'col-sm-8'; detail.textContent = value || '—';
                list.append(label, detail);
            }
        } catch (err) { showAlert(err.message); }
    }
    async function loadAttendance() {
        const from = el('employeeFromDate').value, to = el('employeeToDate').value;
        if (from > to) { showAlert('From date cannot be after To date.'); return; }
        try {
            const query = new URLSearchParams({ fromDate: from, toDate: to });
            const data = await request('/api/employee-portal/attendance?' + query);
            fillRows('employeeAttendanceRows', 5, data, [x => fmtDate(x.date), 'status', x => fmtTime(x.inTime), x => fmtTime(x.outTime), 'remarks']);
        } catch (err) { showAlert(err.message); }
    }
    async function loadBalances() {
        try {
            const data = await request('/api/employee-portal/leave/balances?year=' + today.getFullYear());
            fillRows('employeeLeaveBalances', 5, data, ['leaveType', 'annualEntitlement', 'usedDays', 'pendingDays', 'remainingDays']);
            const select = el('employeeLeaveType'), selected = select.value;
            select.replaceChildren();
            const placeholder = document.createElement('option'); placeholder.value = ''; placeholder.textContent = 'Choose a leave type'; select.append(placeholder);
            for (const item of data || []) {
                if (item.leaveTypeId <= 0) continue;
                const option = document.createElement('option'); option.value = String(item.leaveTypeId);
                option.textContent = item.leaveType + ' (' + item.remainingDays + ' days available)';
                select.append(option);
            }
            if ([...select.options].some(x => x.value === selected)) select.value = selected;
        } catch (err) { showAlert(err.message); }
    }
    async function loadLeaveHistory() {
        try {
            const data = await request('/api/employee-portal/leave');
            fillRows('employeeLeaveHistory', 6, data, ['leaveType', x => fmtDate(x.fromDate), x => fmtDate(x.toDate), 'totalDays', 'status', 'reason']);
        } catch (err) { showAlert(err.message); }
    }
    async function applyLeave(event) {
        event.preventDefault();
        const form = el('employeeLeaveForm');
        if (!form.reportValidity()) return;
        const from = el('employeeLeaveFrom').value, to = el('employeeLeaveTo').value;
        if (to < from) { showAlert('Leave end date must be on or after start date.'); return; }
        const button = el('employeeApplyLeave'); button.disabled = true;
        try {
            const body = { leaveTypeId: Number(el('employeeLeaveType').value), fromDate: from, toDate: to, reason: el('employeeLeaveReason').value.trim() };
            if (!body.reason) throw new Error('Leave reason is required.');
            await request('/api/employee-portal/leave', { method: 'POST', body: JSON.stringify(body) });
            form.reset(); showAlert('Leave request submitted.', true);
            await Promise.all([loadBalances(), loadLeaveHistory()]);
        } catch (err) { showAlert(err.message); }
        finally { button.disabled = false; }
    }
    const monthAgo = new Date(today); monthAgo.setDate(monthAgo.getDate() - 30);
    el('employeeFromDate').value = localDate(monthAgo); el('employeeToDate').value = localDate(today);
    el('employeeLeaveFrom').value = localDate(today); el('employeeLeaveTo').value = localDate(today);
    el('employeeAttendanceFilter').addEventListener('submit', e => { e.preventDefault(); loadAttendance(); });
    el('employeeLeaveForm').addEventListener('submit', applyLeave);
    el('employeeRefreshLeave').addEventListener('click', () => { loadBalances(); loadLeaveHistory(); });
    loadProfile(); loadAttendance(); loadBalances(); loadLeaveHistory();
})();
