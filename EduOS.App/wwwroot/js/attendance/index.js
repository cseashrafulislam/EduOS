(() => {
    'use strict';
    const form = document.getElementById('attendanceFilters');
    const rows = document.getElementById('attendanceRows');
    const saveButton = document.getElementById('saveAttendance');
    const markAllButton = document.getElementById('markAllPresent');
    const summary = document.getElementById('attendanceSummary');
    const date = document.getElementById('attendanceDate');
    let loaded = false;
    if (!form || !rows || !date) return;
    date.value = new Date().toISOString().slice(0, 10);
    form.addEventListener('submit', loadRoster);
    saveButton?.addEventListener('click', saveAttendance);
    markAllButton?.addEventListener('click', () => { rows.querySelectorAll('[data-status]').forEach(x => x.value = 'Present'); updateSummary(); });
    rows.addEventListener('change', updateSummary);

    async function loadRoster(event) {
        event.preventDefault();
        if (!form.reportValidity()) return;
        clearAlert();
        try {
            const s = scope();
            const response = await fetch(`/api/student-attendance/roster?date=${encodeURIComponent(s.date)}&academicYearId=${s.academicYearId}&classId=${s.classId}&sectionId=${s.sectionId}`, requestOptions());
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data) throw new Error(payload?.message || 'Attendance roster could not be loaded.');
            render(payload.data.students || []);
            loaded = true;
            toggleActions(payload.data.students?.length > 0);
        } catch (error) { loaded = false; render([]); toggleActions(false); showAlert('danger', error.message || 'Attendance roster could not be loaded.'); }
    }

    async function saveAttendance() {
        if (!loaded) return;
        saveButton.disabled = true;
        clearAlert();
        try {
            const request = { ...scope(), items: [...rows.querySelectorAll('tr[data-student-reference]')].map(readRow) };
            const response = await fetch('/api/student-attendance', requestOptions('POST', request));
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data) throw new Error(payload?.message || 'Attendance could not be saved.');
            render(payload.data.students || []);
            showAlert('success', payload.message || 'Attendance saved successfully.');
        } catch (error) { showAlert('danger', error.message || 'Attendance could not be saved.'); }
        finally { saveButton.disabled = false; }
    }

    function render(students) {
        rows.replaceChildren();
        if (!students.length) { const tr = document.createElement('tr'); const td = cell('No active students were found.'); td.colSpan = 6; td.className = 'text-center text-muted py-4'; tr.append(td); rows.append(tr); updateSummary(); return; }
        students.forEach(student => {
            const tr = document.createElement('tr'); tr.dataset.studentReference = student.studentReference;
            tr.append(cell(student.roll || '—'), cell(`${student.studentCode || ''} ${student.studentName || ''}`.trim()));
            const statusCell = document.createElement('td'); const status = document.createElement('select'); status.className = 'form-select form-select-sm'; status.dataset.status = '1';
            ['Present', 'Absent', 'Late', 'Leave'].forEach(value => { const option = document.createElement('option'); option.value = value; option.textContent = value; status.append(option); }); status.value = student.status || 'Present'; statusCell.append(status); tr.append(statusCell);
            tr.append(inputCell('time', 'inTime', student.inTime ? String(student.inTime).slice(0, 5) : ''), inputCell('time', 'outTime', student.outTime ? String(student.outTime).slice(0, 5) : ''), inputCell('text', 'remarks', student.remarks || '', 500)); rows.append(tr);
        });
        updateSummary();
    }

    function readRow(tr) { const time = key => tr.querySelector(`[data-${key}]`).value; return { studentReference: tr.dataset.studentReference, status: tr.querySelector('[data-status]').value, inTime: time('in-time') ? `${time('in-time')}:00` : null, outTime: time('out-time') ? `${time('out-time')}:00` : null, remarks: time('remarks').trim() || null }; }
    function scope() { return { date: date.value, academicYearId: number('academicYearId'), classId: number('classId'), sectionId: number('sectionId') }; }
    function number(id) { return Number.parseInt(document.getElementById(id)?.value || '0', 10); }
    function cell(value) { const td = document.createElement('td'); td.textContent = value; return td; }
    function inputCell(type, key, value, maxLength) { const td = document.createElement('td'); const input = document.createElement('input'); input.type = type; input.className = 'form-control form-control-sm'; input.value = value; if (maxLength) input.maxLength = maxLength; input.dataset[key.replace(/[A-Z]/g, x => `-${x.toLowerCase()}`)] = '1'; td.append(input); return td; }
    function requestOptions(method = 'GET', body = null) { const options = { method, credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } }; if (body) { options.headers['Content-Type'] = 'application/json'; options.body = JSON.stringify(body); } return options; }
    function toggleActions(enabled) { if (saveButton) saveButton.disabled = !enabled; if (markAllButton) markAllButton.disabled = !enabled; }
    function updateSummary() { const values = [...rows.querySelectorAll('[data-status]')].map(x => x.value); const count = value => values.filter(x => x === value).length; summary.textContent = values.length ? `Total ${values.length} · Present ${count('Present')} · Absent ${count('Absent')} · Late ${count('Late')} · Leave ${count('Leave')}` : ''; }
    function clearAlert() { const box = document.getElementById('attendanceAlert'); if (box) { box.className = 'd-none'; box.textContent = ''; } }
    function showAlert(type, message) { const box = document.getElementById('attendanceAlert'); if (!box) return; box.className = `alert alert-${type === 'success' ? 'success' : 'danger'}`; box.textContent = message; box.focus(); }
})();
