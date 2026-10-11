(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const form = el('attendanceFilters'), rows = el('attendanceRows'), date = el('attendanceDate');
    const saveButton = el('saveAttendance'), markAllButton = el('markAllPresent'), summary = el('attendanceSummary');
    if (!form || !rows || !date) return;
    let loaded = false, batches = [], roster = null;
    const local = new Date();
    date.value = [local.getFullYear(), String(local.getMonth() + 1).padStart(2, '0'), String(local.getDate()).padStart(2, '0')].join('-');
    function status(type, message) {
        const box = el('attendanceAlert');
        box.className = 'alert alert-' + type; box.textContent = message; box.focus();
    }
    function clearStatus() { el('attendanceAlert').className = 'd-none'; el('attendanceAlert').textContent = ''; }
    async function api(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        const response = await fetch(url, { method, cache: 'no-store', credentials: 'same-origin', headers,
            ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
        const data = await response.json().catch(() => null);
        if (!response.ok || !data?.success) throw new Error(data?.message || 'Request failed.');
        return data;
    }
    function selectOptions(id, items, title) {
        const select = el(id); select.replaceChildren();
        const placeholder = document.createElement('option'); placeholder.value = ''; placeholder.textContent = title; select.append(placeholder);
        items.forEach(x => {
            const opt = document.createElement('option'); opt.value = String(x.id); opt.textContent = x.name; select.append(opt);
        });
        select.disabled = !items.length;
    }
    async function loadBatches() {
        try {
            const response = await api('/api/academic-setup/catalog');
            batches = (response.data?.batches || []).filter(x => x.isActive);
            const years = new Map();
            batches.forEach(x => years.set(x.academicYearId, x.academicYearName || 'Year ' + x.academicYearId));
            selectOptions('academicYearId', [...years].map(([id, name]) => ({ id, name })).sort((a,b) => b.id - a.id), 'Select academic year');
            selectOptions('classId', [], 'Select a year first');
            selectOptions('sectionId', [], 'Select a level first');
            if (!batches.length) status('warning', 'No active academic batches exist. Create a batch before recording attendance.');
        } catch (error) {
            selectOptions('academicYearId', [], 'Unable to load years');
            status('danger', error.message);
        }
    }
    function yearChanged() {
        invalidate();
        const year = Number(el('academicYearId').value);
        const levels = new Map();
        batches.filter(x => x.academicYearId === year).forEach(x =>
            levels.set(x.academicLevelId, x.academicLevelName || 'Level ' + x.academicLevelId));
        selectOptions('classId', [...levels].map(([id,name]) => ({ id,name })).sort((a,b) => a.name.localeCompare(b.name)), 'Select level');
        selectOptions('sectionId', [], 'Select a level first');
    }
    function levelChanged() {
        invalidate();
        const year = Number(el('academicYearId').value), level = Number(el('classId').value);
        const options = batches.filter(x => x.academicYearId === year && x.academicLevelId === level)
            .map(x => ({ id: x.id, name: x.name + (x.code ? ' (' + x.code + ')' : '') }));
        selectOptions('sectionId', options, 'Select batch / section');
    }
    function invalidate() {
        loaded = false; roster = null; saveButton.disabled = true; markAllButton.disabled = true;
        render([]); clearStatus();
    }
    function requestScope() {
        return { academicBatchId: Number(el('sectionId').value), attendanceDate: date.value };
    }
    function makeCell(value) { const cell = document.createElement('td'); cell.textContent = value === null || value === undefined ? '' : String(value); return cell; }
    function inputCell(type, key, value) {
        const td = document.createElement('td'), input = document.createElement('input');
        input.type = type; input.className = 'form-control form-control-sm'; input.dataset[key] = '1'; input.value = value || '';
        if (type === 'text') input.maxLength = 500;
        td.append(input); return td;
    }
    function render(students) {
        rows.replaceChildren();
        if (!students?.length) {
            const tr = document.createElement('tr'), cell = makeCell('No roster loaded.');
            cell.colSpan = 5; cell.className = 'text-center text-muted py-4';
            tr.append(cell); rows.append(tr); updateSummary(); return;
        }
        for (const student of students) {
            const tr = document.createElement('tr');
            tr.dataset.enrollmentReference = student.studentEnrollmentReference;
            tr.dataset.rowVersion = student.rowVersion || '';
            tr.append(makeCell(student.rollNo || '—'),
                makeCell([student.studentCode, student.studentName].filter(Boolean).join(' ')));
            const td = document.createElement('td'), selection = document.createElement('select');
            selection.className = 'form-select form-select-sm'; selection.dataset.state = '1';
            for (const [value, label] of [['','Not marked'],['1','Present'],['2','Absent'],
                ['3','Late'],['4','Leave'],['5','Excused']]) {
                const opt = document.createElement('option');
                opt.value = value; opt.textContent = label; selection.append(opt);
            }
            selection.value = student.state ? String(student.state) : '';
            td.append(selection); tr.append(td);
            tr.append(inputCell('time', 'checkIn', student.checkInTime ? String(student.checkInTime).slice(0, 5) : ''));
            tr.append(inputCell('text', 'remarks', student.remarks || ''));
            rows.append(tr);
        }
        updateSummary();
    }
    async function loadRoster(event) {
        if (event) event.preventDefault();
        if (!form.reportValidity()) return;
        invalidate();
        try {
            const result = await api('/api/student-attendance/roster?' + new URLSearchParams(requestScope()));
            roster = result.data;
            render(roster?.students || []);
            loaded = true;
            saveButton.disabled = !(roster?.students?.length > 0);
            markAllButton.disabled = saveButton.disabled;
        } catch (error) { status('danger', error.message); }
    }
    function readRow(tr) {
        const stateValue = tr.querySelector('[data-state]')?.value || '';
        const checkIn = tr.querySelector('[data-check-in]')?.value || '';
        return {
            studentEnrollmentReference: tr.dataset.enrollmentReference,
            state: Number(stateValue), checkInTime: checkIn ? checkIn + ':00' : null,
            remarks: tr.querySelector('[data-remarks]')?.value.trim() || null,
            rowVersion: tr.dataset.rowVersion || null
        };
    }
    async function saveAttendance() {
        if (!loaded || saveButton.disabled) return;
        const students = [...rows.querySelectorAll('tr[data-enrollment-reference]')].map(readRow);
        if (students.some(x => !x.state)) {
            status('danger', 'Mark every student before saving, or use Mark all present.'); return;
        }
        saveButton.disabled = true; clearStatus();
        try {
            if (!roster?.attendanceSessionId) {
                const created = await api('/api/student-attendance/sessions', 'POST', requestScope());
                if (!created.data?.attendanceSessionId || !created.data?.sessionRowVersion)
                    throw new Error('Attendance session could not be opened. Reload and retry.');
                if (created.data.students?.some(x => x.id > 0)) {
                    status('warning', 'Attendance was recorded by another user. Reload the roster before saving.');
                    await loadRoster(); return;
                }
                roster = created.data;
            }
            const result = await api('/api/student-attendance', 'POST', {
                attendanceSessionId: roster.attendanceSessionId,
                sessionRowVersion: roster.sessionRowVersion, students
            });
            roster = result.data;
            render(roster?.students || []);
            status('success', result.message || 'Attendance saved.');
        } catch (error) { status('danger', error.message); }
        finally { saveButton.disabled = false; }
    }
    function updateSummary() {
        const values = [...rows.querySelectorAll('[data-state]')].map(x => x.value);
        const count = value => values.filter(x => x === value).length;
        summary.textContent = values.length ? 'Total ' + values.length + ' · Present ' + count('1') +
            ' · Absent ' + count('2') + ' · Late ' + count('3') + ' · Leave ' + count('4') +
            ' · Excused ' + count('5') + ' · Unmarked ' + count('') : '';
    }
    el('academicYearId').addEventListener('change', yearChanged);
    el('classId').addEventListener('change', levelChanged);
    el('sectionId').addEventListener('change', invalidate);
    date.addEventListener('change', invalidate);
    form.addEventListener('submit', loadRoster);
    rows.addEventListener('change', updateSummary);
    saveButton.addEventListener('click', saveAttendance);
    markAllButton.addEventListener('click', () => {
        rows.querySelectorAll('[data-status]').forEach(select => { select.value = '1'; }); updateSummary();
    });
    loadBatches();
})();
