(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const loadButton = el('attendanceCorrectionAuditLoad');
    if (!loadButton) return;
    const rows = el('attendanceCorrectionAuditRows');
    const status = el('attendanceCorrectionAuditStatus');
    const previous = el('attendanceCorrectionAuditPrevious');
    const next = el('attendanceCorrectionAuditNext');
    const pageLabel = el('attendanceCorrectionAuditPage');
    const csvButton = el('attendanceCorrectionAuditCsv');
    const names = { 1: 'Present', 2: 'Absent', 3: 'Late', 4: 'Leave', 5: 'Excused' };
    let page = 1, pages = 0, request = 0, visible = [], applied = null;

    function reset() {
        ++request; page = 1; pages = 0; applied = null; visible = [];
        rows.replaceChildren(); pageLabel.textContent = '';
        previous.disabled = next.disabled = csvButton.disabled = true;
        status.textContent = 'Select report filters and load corrections.';
    }
    function cell(value) {
        const td = document.createElement('td');
        td.textContent = value == null ? '' : String(value);
        return td;
    }
    function filters() {
        const batch = el('attendanceReportBatch').value;
        const from = el('attendanceReportFrom').value;
        const to = el('attendanceReportTo').value;
        const days = (Date.parse(to) - Date.parse(from)) / 86400000;
        if (!/^\d+$/.test(batch) || !from || !to || !Number.isFinite(days) ||
            days < 0 || days > 30) throw new Error('Choose a batch and 1–31 attendance days.');
        return {
            academicBatchId: batch, fromDate: from, toDate: to,
            ...(el('attendanceReportSubject').value ?
                { subjectOfferingId: el('attendanceReportSubject').value } : {})
        };
    }
    async function load() {
        if (!applied) return;
        const token = ++request;
        previous.disabled = next.disabled = csvButton.disabled = true;
        rows.replaceChildren();
        status.textContent = 'Loading correction audit…';
        try {
            const params = new URLSearchParams({ ...applied, page: String(page), pageSize: '20' });
            const response = await fetch('/api/student-attendance/corrections/report?' + params, {
                credentials: 'same-origin', cache: 'no-store', referrerPolicy: 'no-referrer',
                headers: { Accept: 'application/json' }
            });
            const result = await response.json().catch(() => null);
            if (token !== request) return;
            if (!response.ok || !result?.success || !Array.isArray(result.data?.items))
                throw new Error(result?.message || 'Correction audit unavailable.');
            visible = result.data.items;
            pages = Number(result.data.totalPages) || 0;
            for (const item of visible) {
                const tr = document.createElement('tr');
                for (const value of [
                    item.attendanceDate, item.rollNo, item.studentCode, item.studentName,
                    item.subjectOfferingId || 'Batch', names[item.previousState] || item.previousState,
                    names[item.newState] || item.newState, item.reason,
                    item.changedAt, item.changedByUserId
                ]) tr.append(cell(value));
                rows.append(tr);
            }
            status.textContent = visible.length ? '' : 'No corrections for these filters.';
            pageLabel.textContent = 'Page ' + page + ' of ' + pages +
                ' · ' + Number(result.data.totalCount || 0) + ' corrections';
            previous.disabled = page <= 1;
            next.disabled = page >= pages;
            csvButton.disabled = !visible.length;
        } catch (error) {
            if (token === request) {
                visible = []; csvButton.disabled = true; pageLabel.textContent = '';
                status.textContent = error.message || 'Correction audit unavailable.';
            }
        }
    }
    function csvCell(value) {
        const raw = String(value ?? '');
        const safe = /^\s*[=+\-@\t\r]/.test(raw) ? "'" + raw : raw;
        return '"' + safe.replace(/"/g, '""') + '"';
    }
    csvButton.addEventListener('click', () => {
        if (!visible.length || !applied) return;
        const headers = ['Attendance date','Roll','Student code','Student','Subject offering',
            'Previous','New','Reason','Changed at UTC','Changed by user'];
        const data = visible.map(x => [x.attendanceDate, x.rollNo, x.studentCode, x.studentName,
            x.subjectOfferingId || 'Batch', names[x.previousState] || x.previousState,
            names[x.newState] || x.newState, x.reason, x.changedAt, x.changedByUserId]);
        const csv = [headers, ...data].map(line => line.map(csvCell).join(',')).join('\r\n');
        const blob = new Blob(['\uFEFF', csv], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'attendance-corrections-batch-' + applied.academicBatchId +
            '-page-' + page + '.csv';
        document.body.append(a); a.click(); a.remove(); URL.revokeObjectURL(url);
    });
    loadButton.addEventListener('click', () => {
        try { applied = filters(); page = 1; void load(); }
        catch (error) { reset(); status.textContent = error.message; }
    });
    previous.addEventListener('click', () => {
        if (page > 1) { page--; void load(); }
    });
    next.addEventListener('click', () => {
        if (page < pages) { page++; void load(); }
    });
    for (const id of ['attendanceReportBatch','attendanceReportFrom','attendanceReportTo',
        'attendanceReportSubject','attendanceReportBatchSearch','attendanceReportSubjectSearch']) {
        el(id).addEventListener('change', reset);
    }
    reset();
})();
