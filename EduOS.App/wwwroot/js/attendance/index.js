(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const form = el('attendanceFilters'), rows = el('attendanceRows'), date = el('attendanceDate');
    const canCorrect = document.querySelector('section[data-can-correct]')?.dataset.canCorrect === 'true';
    const saveButton = el('saveAttendance'), markAllButton = el('markAllPresent');
    const finalizeButton = el('finalizeAttendance'), summary = el('attendanceSummary');
    const historicalCsvButton = el('attendanceHistoricalCsv');
    const historicalPrintButton = el('attendanceHistoricalPrint');
    if (!form || !rows || !date) return;
    let loaded = false, roster = null, dirty = false;
    let historicalSessionId = null;
    let rosterPage = 1, rosterPages = 0, rosterRequest = 0;
    let batchPage = 1, batchPages = 0, batchRequest = 0;
    let subjectPage = 1, subjectPages = 0, subjectRequest = 0;
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
        historicalSessionId = null;
        rosterPage = 1;
        const request = ++batchRequest;
        invalidate();
        ++subjectRequest;
        subjectPage = 1;
        resetSubjects('Select an academic batch');
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

    function resetSubjects(label = 'All subjects (batch register)') {
        const select = el('attendanceSubject');
        select.replaceChildren();
        const option = document.createElement('option');
        option.value = ''; option.textContent = label; select.append(option);
        select.disabled = true;
        subjectPages = 0;
        el('attendanceSubjectPrevious').disabled = true;
        el('attendanceSubjectNext').disabled = true;
        el('attendanceSubjectPage').textContent = '';
    }
    async function loadSubjects() {
        historicalSessionId = null;
        rosterPage = 1;
        const request = ++subjectRequest;
        invalidate();
        const batchId = Number(el('sectionId').value);
        resetSubjects(batchId ? 'Loading subjects…' : 'Select an academic batch');
        if (!batchId || !date.value) return;
        try {
            const params = new URLSearchParams({
                academicBatchId: String(batchId), attendanceDate: date.value,
                search: el('attendanceSubjectSearch').value.trim(),
                page: String(subjectPage), pageSize: '25'
            });
            const result = await api('/api/student-attendance/subjects?' + params);
            if (request !== subjectRequest || batchId !== Number(el('sectionId').value)) return;
            const data = result.data || {};
            const select = el('attendanceSubject');
            select.replaceChildren();
            const all = document.createElement('option');
            all.value = ''; all.textContent = 'All subjects (batch register)';
            select.append(all);
            for (const subject of data.items || []) {
                const option = document.createElement('option');
                option.value = String(subject.id);
                option.textContent = [subject.subjectName, subject.subjectCode, subject.code]
                    .filter(Boolean).join(' / ');
                select.append(option);
            }
            select.disabled = false;
            subjectPages = data.totalPages || 0;
            el('attendanceSubjectPage').textContent = 'Page ' + subjectPage + ' of ' +
                subjectPages + ' · ' + (data.totalCount || 0) + ' subjects';
            el('attendanceSubjectPrevious').disabled = subjectPage <= 1;
            el('attendanceSubjectNext').disabled = subjectPage >= subjectPages;
        } catch (error) {
            if (request === subjectRequest) {
                resetSubjects('Unable to load subjects');
                status('danger', error.message);
            }
        }
    }
    function historicalPageReady() {
        return loaded && roster?.isHistoricalReadOnly === true &&
            Number(roster.attendanceSessionId) === historicalSessionId &&
            roster?.students?.length > 0 && Number.isSafeInteger(rosterPage) && rosterPage > 0;
    }
    function updateHistoricalActions() {
        const disabled = !historicalPageReady();
        if (historicalCsvButton) historicalCsvButton.disabled = disabled;
        if (historicalPrintButton) historicalPrintButton.disabled = disabled;
    }
    function historicalPageUrl(action) {
        return '/attendance/sessions/' + historicalSessionId + '/' + action +
            '?page=' + rosterPage;
    }
    async function exportHistoricalPage() {
        if (!historicalPageReady() || !historicalCsvButton) return;
        const sessionId = historicalSessionId, page = rosterPage, token = rosterRequest;
        historicalCsvButton.disabled = true;
        try {
            const response = await fetch(historicalPageUrl('export.csv'), {
                credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'text/csv' }
            });
            if (!response.ok || !response.headers.get('content-type')?.includes('text/csv'))
                throw new Error('Historical attendance export is unavailable or access has changed.');
            const blob = await response.blob();
            if (token !== rosterRequest || sessionId !== historicalSessionId ||
                page !== rosterPage || !historicalPageReady()) return;
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = 'attendance-session-' + sessionId + '-page-' + page + '.csv';
            document.body.append(link); link.click(); link.remove();
            URL.revokeObjectURL(url);
        } catch (error) {
            if (token === rosterRequest) status('danger', error.message);
        } finally {
            updateHistoricalActions();
        }
    }
    function printHistoricalPage() {
        if (!historicalPageReady()) return;
        window.open(historicalPageUrl('print'), '_blank', 'noopener,noreferrer');
    }
    function invalidate() {
        ++rosterRequest; dirty = false; loaded = false; roster = null; saveButton.disabled = true; markAllButton.disabled = true;
        if (finalizeButton) finalizeButton.disabled = true;
        render([]); clearStatus();
        rosterPages = 0;
        updateHistoricalActions();
        el('attendanceRosterPrevious').disabled = true;
        el('attendanceRosterNext').disabled = true;
        el('attendanceRosterPage').textContent = '';
    }
    function requestScope() {
        const selected = el('attendanceSubject').value;
        return { academicBatchId: Number(el('sectionId').value),
            attendanceDate: date.value, subjectOfferingId: selected ? Number(selected) : null };
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
            selection.disabled = roster?.isHistoricalReadOnly === true;
            td.append(selection);
            if (canCorrect && roster?.isFinalized && !roster?.isHistoricalReadOnly && student.id > 0) {
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
            const checkIn = inputCell('time', 'checkIn', student.checkInTime ? String(student.checkInTime).slice(0, 5) : '');
            const remarks = inputCell('text', 'remarks', student.remarks || '');
            if (roster?.isHistoricalReadOnly) {
                checkIn.querySelector('input').disabled = true;
                remarks.querySelector('input').disabled = true;
            }
            tr.append(checkIn, remarks);
            rows.append(tr);
        }
        updateSummary();
    }
    async function loadRoster(event, expectedSessionId = null) {
        if (event) event.preventDefault();
        if (!form.reportValidity()) return;
        if (dirty && !confirm('Discard unsaved changes on this roster page?')) return;
        invalidate();
        const request = rosterRequest;
        try {
            const scope = requestScope();
            const params = new URLSearchParams({
                academicBatchId: String(scope.academicBatchId), attendanceDate: scope.attendanceDate,
                page: String(rosterPage), pageSize: '50'
            });
            if (scope.subjectOfferingId) params.set('subjectOfferingId', String(scope.subjectOfferingId));
            const result = historicalSessionId === null
                ? await api('/api/student-attendance/roster-page?' + params)
                : await api('/api/student-attendance/report/sessions/' + historicalSessionId +
                    '/roster?page=' + rosterPage + '&pageSize=50');
            if (request !== rosterRequest) return;
            if (expectedSessionId !== null && Number(result.data?.attendanceSessionId) !== expectedSessionId) {
                status('warning', 'The selected attendance session changed. Reload the session register.');
                return;
            }
            roster = result.data;
            rosterPages = roster?.totalPages || 0;
            render(roster?.students || []);
            loaded = true;
            updateHistoricalActions();
            saveButton.disabled = roster?.isHistoricalReadOnly || !(roster?.students?.length > 0) || roster.isFinalized;
            markAllButton.disabled = saveButton.disabled;
            if (finalizeButton) finalizeButton.disabled = !roster.attendanceSessionId ||
                roster.isFinalized || roster.summary?.unmarked !== 0 ||
                !(roster.summary?.totalStudents > 0);
            el('attendanceRosterPrevious').disabled = rosterPage <= 1;
            el('attendanceRosterNext').disabled = rosterPage >= rosterPages;
            el('attendanceRosterPage').textContent = 'Page ' + rosterPage + ' of ' +
                rosterPages + ' · ' + (roster.summary?.totalStudents || 0) +
                (roster.isHistoricalReadOnly ? ' historical marks (read-only)' : ' eligible students');
        } catch (error) {
            if (request === rosterRequest) status('danger', error.message);
        }
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
        if (!loaded || saveButton.disabled || roster?.isHistoricalReadOnly) return;
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
                if (created.data.summary?.marked > 0) {
                    status('warning', 'Attendance was recorded by another user. Reload the roster before saving.');
                    await loadRoster(); return;
                }
                roster = created.data;
            }
            const result = await api('/api/student-attendance', 'POST', {
                attendanceSessionId: roster.attendanceSessionId,
                sessionRowVersion: roster.sessionRowVersion, students
            });
            dirty = false;
            await loadRoster();
            status('success', result.message || 'Attendance saved.');
        } catch (error) { status('danger', error.message); }
        finally { saveButton.disabled = !loaded || roster?.isFinalized || !(roster?.students?.length > 0); }
    }
    async function correctAttendance(tr, button) {
        if (!roster?.isFinalized || roster?.isHistoricalReadOnly || !canCorrect) return;
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
            dirty = false;
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
            dirty = false;
            await loadRoster();
            saveButton.disabled = true; markAllButton.disabled = true;
            status('success', result.message || 'Attendance finalized.');
        } catch (error) {
            status('danger', error.message);
            finalizeButton.disabled = false;
        }
    }
    function updateSummary() {
        const total = roster?.summary;
        if (!total) { summary.textContent = ''; return; }
        summary.textContent = (roster?.isHistoricalReadOnly ? 'Historical marks ' : 'Eligible ') + total.totalStudents + ' · Saved ' + total.marked +
            ' · Present ' + total.present + ' · Absent ' + total.absent +
            ' · Late ' + total.late + ' · Leave ' + total.leave +
            ' · Excused ' + total.excused + ' · Unmarked ' + total.unmarked +
            (dirty ? ' · Unsaved edits on this page' : '');
    }
    el('sectionId').addEventListener('change', () => { subjectPage = 1; loadSubjects(); });
    el('attendanceSubject').addEventListener('change', () => { historicalSessionId = null; rosterPage = 1; invalidate(); });
    el('attendanceSubjectFind').addEventListener('click', () => { subjectPage = 1; loadSubjects(); });
    el('attendanceSubjectSearch').addEventListener('keydown', event => {
        if (event.key === 'Enter') { event.preventDefault(); subjectPage = 1; loadSubjects(); }
    });
    el('attendanceSubjectPrevious').addEventListener('click', () => {
        if (subjectPage > 1) { subjectPage--; loadSubjects(); }
    });
    el('attendanceSubjectNext').addEventListener('click', () => {
        if (subjectPage < subjectPages) { subjectPage++; loadSubjects(); }
    });
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
    form.addEventListener('submit', event => { historicalSessionId = null; rosterPage = 1; loadRoster(event); });
    // Report navigation does not grant access: the roster API checks every scope again.
    document.addEventListener('attendance:session-open', async event => {
        const detail = event.detail || {};
        const sessionId = Number(detail.sessionId);
        const batchId = Number(detail.academicBatchId);
        const offeringId = detail.subjectOfferingId == null ? null : Number(detail.subjectOfferingId);
        const selectedDate = String(detail.attendanceDate || '');
        if (!Number.isSafeInteger(sessionId) || sessionId <= 0 ||
            !Number.isSafeInteger(batchId) || batchId <= 0 ||
            (offeringId !== null && (!Number.isSafeInteger(offeringId) || offeringId <= 0)) ||
            !/^\d{4}-\d{2}-\d{2}$/.test(selectedDate)) return;
        if (dirty && !confirm('Discard unsaved changes before opening this session?')) return;
        dirty = false;
        historicalSessionId = detail.isFinalized === true ? sessionId : null;
        ++batchRequest; ++subjectRequest;
        date.value = selectedDate;
        selectOptions('sectionId', [{ id: batchId,
            name: String(detail.batchLabel || 'Selected academic batch') }], 'Select authorized academic batch');
        el('sectionId').value = String(batchId);
        resetSubjects();
        const subject = el('attendanceSubject');
        if (offeringId !== null) {
            const option = document.createElement('option');
            option.value = String(offeringId);
            option.textContent = String(detail.subjectOfferingCode || 'Selected subject offering');
            subject.append(option);
            subject.value = String(offeringId);
        }
        subject.disabled = false;
        rosterPage = 1;
        await loadRoster(null, sessionId);
        if (roster?.attendanceSessionId === sessionId)
            el('attendanceRosterTitle')?.scrollIntoView({ block: 'start', behavior: 'auto' });
    });
    rows.addEventListener('change', () => { dirty = true; updateSummary(); });
    rows.addEventListener('input', () => { dirty = true; updateSummary(); });
    el('attendanceRosterPrevious').addEventListener('click', async () => {
        if (rosterPage <= 1) return;
        if (dirty && !confirm('Discard unsaved changes on this roster page?')) return;
        dirty = false; rosterPage--; await loadRoster();
    });
    el('attendanceRosterNext').addEventListener('click', async () => {
        if (rosterPage >= rosterPages) return;
        if (dirty && !confirm('Discard unsaved changes on this roster page?')) return;
        dirty = false; rosterPage++; await loadRoster();
    });
    historicalCsvButton?.addEventListener('click', exportHistoricalPage);
    historicalPrintButton?.addEventListener('click', printHistoricalPage);
    saveButton.addEventListener('click', saveAttendance);
    finalizeButton?.addEventListener('click', finalizeAttendance);
    markAllButton.addEventListener('click', () => {
        rows.querySelectorAll('tr[data-enrollment-reference]').forEach(tr => {
            // Preserve existing marks and approved-leave students; mass marking
            // must not quietly erase valid leave or previously recorded attendance.
            const select = tr.querySelector('[data-state]');
            if (select && !select.value && tr.dataset.approvedLeave !== 'true') select.value = '1';
        });
        dirty = true; updateSummary();
    });
    loadBatches();
})();
