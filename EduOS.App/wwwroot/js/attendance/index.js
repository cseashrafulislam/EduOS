(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const form = el('attendanceFilters'), rows = el('attendanceRows'), date = el('attendanceDate');
    const saveButton = el('saveAttendance'), markAllButton = el('markAllPresent'), summary = el('attendanceSummary');
    if (!form || !rows || !date) return;
    let loaded = false, batches = [];
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
        loaded = false; saveButton.disabled = true; markAllButton.disabled = true;
        render([]); clearStatus();
    }
    function requestScope() {
        return { date: date.value, academicYearId: Number(el('academicYearId').value),
            classId: Number(el('classId').value), sectionId: Number(el('sectionId').value) };
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
            cell.colSpan = 6; cell.className = 'text-center text-muted py-4'; tr.append(cell); rows.append(tr); updateSummary(); return;
        }
        for (const student of students) {
            const tr = document.createElement('tr'); tr.dataset.studentReference = student.studentReference;
            tr.append(makeCell(student.roll || '—'), makeCell([student.studentCode, student.studentName].filter(Boolean).join(' ')));
            const td = document.createElement('td'), selection = document.createElement('select');
            selection.className = 'form-select form-select-sm'; selection.dataset.status = '1';
            for (const [value, label] of [['','Not marked'],['Present','Present'],['Absent','Absent'],['Late','Late'],['Leave','Leave']]) {
                const opt = document.createElement('option'); opt.value = value; opt.textContent = label; selection.append(opt);
            }
            selection.value = student.status || ''; td.append(selection); tr.append(td);
            tr.append(inputCell('time', 'inTime', student.inTime ? String(student.inTime).slice(0, 5) : ''));
            tr.append(inputCell('time', 'outTime', student.outTime ? String(student.outTime).slice(0, 5) : ''));
            tr.append(inputCell('text', 'remarks', student.remarks || ''));
            rows.append(tr);
        }
        updateSummary();
    }
    async function loadRoster(event) {
        if (event) event.preventDefault();
        if (!form.reportValidity()) return;
        clearStatus(); invalidate();
        try {
            const params = new URLSearchParams(requestScope());
            const result = await api('/api/student-attendance/roster?' + params);
            render(result.data?.students || []);
            loaded = true;
            saveButton.disabled = !(result.data?.students?.length > 0);
            markAllButton.disabled = saveButton.disabled;
        } catch (error) { status('danger', error.message); }
    }
    function readRow(tr) {
        const str = key => tr.querySelector('[data-' + key + ']')?.value || '';
        return { studentReference: tr.dataset.studentReference, status: str('status'),
            inTime: str('inTime') ? str('inTime') + ':00' : null, outTime: str('outTime') ? str('outTime') + ':00' : null,
            remarks: str('remarks').trim() || null };
    }
    async function saveAttendance() {
        if (!loaded || saveButton.disabled) return;
        const items = [...rows.querySelectorAll('tr[data-student-reference]')].map(readRow);
        if (items.some(x => !x.status)) { status('danger', 'Mark every student before saving, or use Mark all present explicitly.'); return; }
        if (items.some(x => x.inTime && x.outTime && x.inTime > x.outTime)) {
            status('danger', 'An out time cannot precede its in time.'); return;
        }
        saveButton.disabled = true; clearStatus();
        try {
            const result = await api('/api/student-attendance', 'POST', { ...requestScope(), items });
            render(result.data?.students || []);
            status('success', result.message || 'Attendance saved.');
        } catch (error) { status('danger', error.message); }
        finally { saveButton.disabled = false; }
    }
    function updateSummary() {
        const values = [...rows.querySelectorAll('[data-status]')].map(x => x.value), count = status => values.filter(x => x === status).length;
        summary.textContent = values.length ? 'Total ' + values.length + ' · Present ' + count('Present') +
            ' · Absent ' + count('Absent') + ' · Late ' + count('Late') + ' · Leave ' + count('Leave') +
            ' · Unmarked ' + count('') : '';
    }
    el('academicYearId').addEventListener('change', yearChanged);
    el('classId').addEventListener('change', levelChanged);
    el('sectionId').addEventListener('change', invalidate);
    date.addEventListener('change', invalidate);
    form.addEventListener('submit', loadRoster);
    rows.addEventListener('change', updateSummary);
    saveButton.addEventListener('click', saveAttendance);
    markAllButton.addEventListener('click', () => {
        rows.querySelectorAll('[data-status]').forEach(select => { select.value = 'Present'; }); updateSummary();
    });
    loadBatches();
})();
