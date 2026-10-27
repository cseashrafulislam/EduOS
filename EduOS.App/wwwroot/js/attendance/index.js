(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const form = el('attendanceFilters'), rows = el('attendanceRows'), date = el('attendanceDate');
    const canCorrect = document.querySelector('section[data-can-correct]')?.dataset.canCorrect === 'true';
    const saveButton = el('saveAttendance'), markAllButton = el('markAllPresent');
    const finalizeButton = el('finalizeAttendance'), summary = el('attendanceSummary');
    if (!form || !rows || !date) return;
    let loaded = false, roster = null;
    let batchPage = 1, batchPages = 0, batchRequest = 0;
    const local = new Date();
    date.value = [local.getFullYear(), String(local.getMonth() + 1).padStart(2, '0'), String(local.getDate()).padStart(2, '0')].join('-');
    function status(type, message) {
        const box = el('attendanceAlert');
        box.className = 'alert alert-' + type; box.textContent = message; box.focus();
    }
    function clearStatus() { el('attendanceAlert').className = 'd-none'; el('attendanceAlert').textContent = ''; }
    async function api(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (method !== 'GET') {
            const csrf = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (!csrf) throw new Error('Security token missing. Reload the page.');
            headers.RequestVerificationToken = csrf;
        }
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
        const request = ++batchRequest;
        invalidate();
        selectOptions('sectionId', [], 'Loading authorized batches…');
        el('attendanceBatchPrevious').disabled = true;
        el('attendanceBatchNext').disabled = true;
        el('attendanceBatchPage').textContent = '';
        try {
            const params = new URLSearchParams({
                attendanceDate: date.value, search: el('attendanceBatchSearch').value.trim(),
                page: String(batchPage), pageSize: '25'
            });
            const result = await api('/api/student-attendance/batches?' + params);
            if (request !== batchRequest) return;
            const data = result.data || {};
            const choices = (data.items || []).map(x => ({
                id: x.id,
                name: [x.academicYearName, x.academicLevelName, x.name + (x.code ? ' (' + x.code + ')' : '')]
                    .filter(Boolean).join(' / ')
            }));
            selectOptions('sectionId', choices, 'Select authorized academic batch');
            batchPages = data.totalPages || 0;
            el('attendanceBatchPage').textContent = 'Page ' + batchPage + ' of ' + batchPages +
                ' · ' + (data.totalCount || 0) + ' batches';
            el('attendanceBatchPrevious').disabled = batchPage <= 1;
            el('attendanceBatchNext').disabled = batchPage >= batchPages;
            if (!choices.length) status('warning', 'No eligible academic batches for this date and search.');
        } catch (error) {
            if (request === batchRequest) {
                selectOptions('sectionId', [], 'Unable to load batches');
                status('danger', error.message);
            }
        }
    }
    function invalidate() {
        loaded = false; roster = null; saveButton.disabled = true; markAllButton.disabled = true;
        if (finalizeButton) finalizeButton.disabled = true;
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
            tr.dataset.attendanceId = String(student.id || 0);
            const studentCell = makeCell([student.studentCode, student.studentName].filter(Boolean).join(' '));
            if (student.hasApprovedLeave) {
                const hint = document.createElement('small');
                hint.className = 'd-block text-info';
                hint.textContent = 'Approved leave on this date';
                studentCell.append(hint);
                tr.dataset.approvedLeave = 'true';
            }
            tr.append(makeCell(student.rollNo || '—'), studentCell);
            const td = document.createElement('td'), selection = document.createElement('select');
            selection.className = 'form-select form-select-sm'; selection.dataset.state = '1';
            for (const [value, label] of [['','Not marked'],['1','Present'],['2','Absent'],
                ['3','Late'],['4','Leave'],['5','Excused']]) {
                const opt = document.createElement('option');
                opt.value = value; opt.textContent = label; selection.append(opt);
            }
            selection.value = student.state ? String(student.state) : '';
            td.append(selection);
            if (canCorrect && roster?.isFinalized && student.id > 0) {
                const edit = document.createElement('button');
                edit.type = 'button'; edit.className = 'btn btn-sm btn-outline-warning mt-1';
                edit.textContent = 'Correct (audit)';
                edit.addEventListener('click', () => correctAttendance(tr, edit));
                td.append(edit);
                const history = document.createElement('button');
                history.type = 'button'; history.className = 'btn btn-sm btn-outline-secondary mt-1 ms-1';
                history.textContent = 'Correction history';
                history.addEventListener('click', () => document.dispatchEvent(
                    new CustomEvent('attendance:corrections-open', {
                        detail: { attendanceId: student.id, studentName: student.studentName }
                    })));
                td.append(history);
            }
            tr.append(td);
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
            saveButton.disabled = !(roster?.students?.length > 0) || roster.isFinalized;
            markAllButton.disabled = saveButton.disabled;
            if (finalizeButton) finalizeButton.disabled = !roster.attendanceSessionId ||
                roster.isFinalized || roster.summary?.unmarked !== 0 || !roster.students?.length;
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
            if (finalizeButton) finalizeButton.disabled = roster.isFinalized ||
                roster.summary?.unmarked !== 0 || !roster.students?.length;
            status('success', result.message || 'Attendance saved.');
        } catch (error) { status('danger', error.message); }
        finally { saveButton.disabled = false; }
    }
    async function correctAttendance(tr, button) {
        if (!roster?.isFinalized || !canCorrect) return;
        const newState = Number(tr.querySelector('[data-state]')?.value);
        if (!newState || !(newState >= 1 && newState <= 5))
            return status('danger', 'Select the corrected status first.');
        const reason = prompt('Reason for changing this finalized attendance record:', '');
        if (reason === null) return;
        if (!reason.trim() || reason.trim().length > 1000)
            return status('danger', 'Provide a reason (up to 1000 characters).');
        button.disabled = true;
        try {
            await api('/api/student-attendance/' + tr.dataset.attendanceId + '/correct', 'POST', {
                newState, reason: reason.trim(), rowVersion: tr.dataset.rowVersion
            });
            await loadRoster();
            status('success', 'Attendance corrected with an audit record.');
        } catch (error) { status('danger', error.message); }
        finally { button.disabled = false; }
    }
    async function finalizeAttendance() {
        if (!roster?.attendanceSessionId || roster.isFinalized || !roster.sessionRowVersion) return;
        if (roster.summary?.unmarked !== 0)
            return status('warning', 'All students must be marked before finalization.');
        if (!confirm('Finalize this register? Future changes require a manager correction with audit.'))
            return;
        finalizeButton.disabled = true;
        try {
            const result = await api('/api/student-attendance/sessions/' +
                roster.attendanceSessionId + '/finalize?rowVersion=' +
                encodeURIComponent(roster.sessionRowVersion), 'POST');
            roster = result.data;
            render(roster?.students || []);
            saveButton.disabled = true; markAllButton.disabled = true;
            status('success', result.message || 'Attendance finalized.');
        } catch (error) {
            status('danger', error.message);
            finalizeButton.disabled = false;
        }
    }
    function updateSummary() {
        const values = [...rows.querySelectorAll('[data-state]')].map(x => x.value);
        const count = value => values.filter(x => x === value).length;
        summary.textContent = values.length ? 'Total ' + values.length + ' · Present ' + count('1') +
            ' · Absent ' + count('2') + ' · Late ' + count('3') + ' · Leave ' + count('4') +
            ' · Excused ' + count('5') + ' · Unmarked ' + count('') : '';
    }
    el('sectionId').addEventListener('change', invalidate);
    date.addEventListener('change', () => { batchPage = 1; loadBatches(); });
    el('attendanceBatchFind').addEventListener('click', () => { batchPage = 1; loadBatches(); });
    el('attendanceBatchSearch').addEventListener('keydown', event => {
        if (event.key === 'Enter') { event.preventDefault(); batchPage = 1; loadBatches(); }
    });
    el('attendanceBatchPrevious').addEventListener('click', () => {
        if (batchPage > 1) { batchPage--; loadBatches(); }
    });
    el('attendanceBatchNext').addEventListener('click', () => {
        if (batchPage < batchPages) { batchPage++; loadBatches(); }
    });
    form.addEventListener('submit', loadRoster);
    rows.addEventListener('change', updateSummary);
    saveButton.addEventListener('click', saveAttendance);
    finalizeButton?.addEventListener('click', finalizeAttendance);
    markAllButton.addEventListener('click', () => {
        rows.querySelectorAll('tr[data-enrollment-reference]').forEach(tr => {
            // Preserve existing marks and approved-leave students; mass marking
            // must not quietly erase valid leave or previously recorded attendance.
            const select = tr.querySelector('[data-state]');
            if (select && !select.value && tr.dataset.approvedLeave !== 'true') select.value = '1';
        });
        updateSummary();
    });
    loadBatches();
})();
