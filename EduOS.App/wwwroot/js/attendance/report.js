(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const form = el('attendanceReportFilters'), body = el('attendanceReportRows');
    if (!form || !body) return;
    const status = el('attendanceReportStatus'), pageLabel = el('attendanceReportPage');
    const previous = el('attendanceReportPrevious'), next = el('attendanceReportNext');
    let page = 1, totalPages = 0, pending = 0;
    let batchPage = 1, batchPages = 0, batchPending = 0;
    let subjectPage = 1, subjectPages = 0, subjectPending = 0;
    let visibleItems = [], dailyItems = [], dailyPending = 0;
    const dailyBody = el('attendanceDailyRows'), dailyStatus = el('attendanceDailyStatus');
    const dailyCsvButton = el('attendanceDailyCsv');
    const csvButton = el('attendanceReportCsv');
    const today = new Date();
    const dateString = new Date(today.getTime() - today.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
    el('attendanceReportFrom').value = dateString;
    el('attendanceReportTo').value = dateString;

    // Export only the authorized, currently displayed page; never bypass server paging.
    // Neutralize spreadsheet formulas in user-controlled names/codes before writing CSV.
    function csvCell(value) {
        const text = String(value ?? '');
        const safe = /^[\s]*[=+\-@\t\r]/.test(text) ? "'" + text : text;
        return '"' + safe.replace(/"/g, '""') + '"';
    }
    function exportVisiblePage() {
        if (!visibleItems.length) return;
        const headings = ["Roll","Student code","Student","Marked","Present","Absent","Late","Leave","Excused","Attendance %"];
        const records = visibleItems.map(x => [x.rollNo, x.studentCode, x.studentName, x.markedSessions, x.present, x.absent, x.late, x.leave, x.excused, x.attendancePercent]);
        const csv = [headings, ...records].map(row => row.map(csvCell).join(',')).join('\r\n');
        const blob = new Blob(['\uFEFF', csv], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'attendance-' + String(el('attendanceReportBatch').value).replace(/[^0-9]/g, '') +
            '-' + String(el('attendanceReportFrom').value).replace(/[^0-9-]/g, '') +
            '-' + String(el('attendanceReportTo').value).replace(/[^0-9-]/g, '') +
            '-subject-' + (el('attendanceReportSubject').value || 'all') +
            '-page-' + page + '.csv';
        document.body.append(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
    }
    const cell = value => {
        const td = document.createElement('td');
        td.textContent = String(value ?? '');
        return td;
    };
    function message(text) { status.textContent = text; }
    async function fetchJson(url) {
        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json' } });
        const data = await response.json().catch(() => null);
        if (!response.ok || !data?.success) throw new Error(data?.message || 'Report unavailable.');
        return data.data;
    }
    function resetDaily() {
        dailyPending++;
        dailyItems = [];
        dailyCsvButton.disabled = true;
        dailyBody.replaceChildren();
        dailyStatus.textContent = '';
    }
    async function loadDaily() {
        if (!form.reportValidity()) return;
        const from = el('attendanceReportFrom').value, to = el('attendanceReportTo').value;
        const days = (Date.parse(to) - Date.parse(from)) / 86400000;
        if (!Number.isFinite(days) || days < 0 || days > 30) return;
        const requestId = ++dailyPending;
        dailyCsvButton.disabled = true;
        dailyItems = [];
        dailyBody.replaceChildren();
        dailyStatus.textContent = 'Loading daily attendance…';
        const params = new URLSearchParams({
            academicBatchId: el('attendanceReportBatch').value,
            fromDate: from, toDate: to,
            ...(el('attendanceReportSubject').value ? { subjectOfferingId: el('attendanceReportSubject').value } : {})
        });
        try {
            const data = await fetchJson('/api/student-attendance/report/daily?' + params);
            if (requestId !== dailyPending) return;
            dailyItems = Array.isArray(data) ? data : [];
            for (const item of dailyItems) {
                const tr = document.createElement('tr');
                for (const value of [item.attendanceDate, item.marked, item.present, item.absent,
                    item.late, item.leave, item.excused, item.attendancePercent + '%'])
                    tr.append(cell(value));
                dailyBody.append(tr);
            }
            if (!dailyItems.length) {
                const tr = document.createElement('tr'), td = cell('No finalized marks in this period.');
                td.colSpan = 8; td.className = 'text-center text-muted'; tr.append(td); dailyBody.append(tr);
            }
            dailyCsvButton.disabled = dailyItems.length === 0;
            dailyStatus.textContent = dailyItems.length + ' day(s) with finalized marks.';
        } catch (error) {
            if (requestId === dailyPending) {
                dailyBody.replaceChildren();
                dailyStatus.textContent = error.message;
            }
        }
    }
    function exportDaily() {
        if (!dailyItems.length) return;
        const headings = ['Date','Marked','Present','Absent','Late','Leave','Excused','Attendance %'];
        const records = dailyItems.map(x => [x.attendanceDate, x.marked, x.present,
            x.absent, x.late, x.leave, x.excused, x.attendancePercent]);
        const csv = [headings, ...records].map(row => row.map(csvCell).join(',')).join('\r\n');
        const blob = new Blob(['\uFEFF', csv], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob), link = document.createElement('a');
        link.href = url;
        link.download = 'attendance-daily-' + String(el('attendanceReportBatch').value).replace(/[^0-9]/g, '') +
            '-' + String(el('attendanceReportFrom').value).replace(/[^0-9-]/g, '') +
            '-' + String(el('attendanceReportTo').value).replace(/[^0-9-]/g, '') + '.csv';
        document.body.append(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
    }
    function resetReport() {
        resetDaily();
        pending++; page = 1; totalPages = 0;
        previous.disabled = next.disabled = true;
        visibleItems = []; csvButton.disabled = true; body.replaceChildren();
        pageLabel.textContent = '';
    }
    function resetSubject() {
        ++subjectPending;
        const select = el('attendanceReportSubject');
        select.replaceChildren(new Option('All subjects', ''));
        select.disabled = true;
        el('attendanceReportSubjectPage').textContent = '';
        el('attendanceReportSubjectPrevious').disabled = true;
        el('attendanceReportSubjectNext').disabled = true;
    }
    async function loadSubjects() {
        const request = ++subjectPending;
        resetReport();
        const select = el('attendanceReportSubject');
        select.replaceChildren(new Option('All subjects', ''));
        select.disabled = true;
        const batch = el('attendanceReportBatch').value;
        if (!batch) return;
        try {
            const params = new URLSearchParams({
                academicBatchId: batch, search: el('attendanceReportSubjectSearch').value.trim(),
                page: String(subjectPage), pageSize: '25'
            });
            const data = await fetchJson('/api/student-attendance/report/subjects?' + params);
            if (request !== subjectPending) return;
            for (const item of data.items || [])
                select.add(new Option(item.subjectName + ' (' + item.subjectCode + ' / ' + item.code + ')', String(item.id)));
            select.disabled = false;
            subjectPages = data.totalPages || 0;
            el('attendanceReportSubjectPage').textContent = 'Page ' + subjectPage +
                ' of ' + subjectPages + ' · ' + (data.totalCount || 0) + ' offerings';
            el('attendanceReportSubjectPrevious').disabled = subjectPage <= 1;
            el('attendanceReportSubjectNext').disabled = subjectPage >= subjectPages;
        } catch (error) {
            if (request === subjectPending) message(error.message);
        }
    }
    async function loadBatches() {
        const request = ++batchPending;
        const select = el('attendanceReportBatch');
        resetSubject();
        select.replaceChildren(new Option('Loading authorized batches…', ''));
        select.disabled = true;
        el('attendanceReportBatchPrevious').disabled = true;
        el('attendanceReportBatchNext').disabled = true;
        el('attendanceReportBatchPage').textContent = '';
        pending++;
        page = 1; totalPages = 0;
        previous.disabled = next.disabled = true;
        visibleItems = []; csvButton.disabled = true; body.replaceChildren();
        pageLabel.textContent = '';
        resetDaily();
        try {
            const params = new URLSearchParams({
                search: el('attendanceReportBatchSearch').value.trim(),
                page: String(batchPage), pageSize: '25'
            });
            const data = await fetchJson('/api/student-attendance/batches?' + params);
            if (request !== batchPending) return;
            select.replaceChildren(new Option('Select academic batch', ''));
            for (const batch of data.items || []) {
                select.add(new Option([batch.academicYearName, batch.academicLevelName, batch.name]
                    .filter(Boolean).join(' / '), String(batch.id)));
            }
            select.disabled = !(data.items || []).length;
            batchPages = data.totalPages || 0;
            el('attendanceReportBatchPage').textContent = 'Page ' + batchPage + ' of ' +
                batchPages + ' · ' + (data.totalCount || 0) + ' batches';
            el('attendanceReportBatchPrevious').disabled = batchPage <= 1;
            el('attendanceReportBatchNext').disabled = batchPage >= batchPages;
            message(select.disabled ? 'No authorized historical batches found.' : 'Select a batch and report dates.');
        } catch (error) {
            if (request === batchPending) {
                select.replaceChildren(new Option('Unable to load batches', ''));
                message(error.message);
            }
        }
    }
    async function load() {
        if (!form.reportValidity()) return;
        const from = el('attendanceReportFrom').value, to = el('attendanceReportTo').value;
        const days = Math.round((Date.parse(to) - Date.parse(from)) / 86400000);
        if (!Number.isFinite(days) || days < 0 || days > 30) {
            message('Select a range of 1 to 31 days.'); return;
        }
        const requestId = ++pending;
        previous.disabled = next.disabled = true;
        visibleItems = []; csvButton.disabled = true;
        message('Loading finalized attendance…');
        try {
            const params = new URLSearchParams({
                academicBatchId: el('attendanceReportBatch').value,
                fromDate: from, toDate: to, page: String(page), pageSize: '20',
                minMarkedSessions: el('attendanceReportMinSessions').value,
                sort: el('attendanceReportSort').value,
                ...(el('attendanceReportRisk').value ? { maxAttendancePercent: el('attendanceReportRisk').value } : {}),
                ...(el('attendanceReportSubject').value ? { subjectOfferingId: el('attendanceReportSubject').value } : {})
            });
            const data = await fetchJson('/api/student-attendance/report?' + params);
            if (requestId !== pending) return;
            visibleItems = data.items || [];
            csvButton.disabled = visibleItems.length === 0;
            body.replaceChildren();
            for (const item of data.items || []) {
                const tr = document.createElement('tr');
                for (const value of [item.rollNo, item.studentCode, item.studentName,
                    item.markedSessions, item.present, item.absent, item.late,
                    item.leave, item.excused, item.attendancePercent + '%']) tr.append(cell(value));
                body.append(tr);
            }
            if (!body.children.length) {
                const tr = document.createElement('tr'), td = cell('No finalized marks in this period.');
                td.colSpan = 10; td.className = 'text-center text-muted'; tr.append(td); body.append(tr);
            }
            totalPages = data.totalPages || 0;
            pageLabel.textContent = 'Page ' + page + ' of ' + totalPages + ' · ' + data.totalCount + ' enrollment(s)';
            previous.disabled = page <= 1;
            next.disabled = page >= totalPages;
            message(data.totalCount + ' matching historical enrollment(s).');
        } catch (error) {
            if (requestId === pending) { body.replaceChildren(); message(error.message); }
        }
    }
    csvButton.addEventListener('click', exportVisiblePage);
    dailyCsvButton.addEventListener('click', exportDaily);
    form.addEventListener('submit', event => { event.preventDefault(); page = 1; load(); loadDaily(); });
    previous.addEventListener('click', () => { if (page > 1) { page--; load(); } });
    next.addEventListener('click', () => { if (page < totalPages) { page++; load(); } });
    el('attendanceReportBatch').addEventListener('change', () => {
        subjectPage = 1; resetSubject(); loadSubjects();
    });
    el('attendanceReportSubject').addEventListener('change', () => {
        resetReport(); message('Load report for the selected subject.');
    });
    el('attendanceReportSubjectFind').addEventListener('click', () => {
        subjectPage = 1; loadSubjects();
    });
    el('attendanceReportSubjectSearch').addEventListener('keydown', event => {
        if (event.key === 'Enter') { event.preventDefault(); subjectPage = 1; loadSubjects(); }
    });
    el('attendanceReportSubjectPrevious').addEventListener('click', () => {
        if (subjectPage > 1) { subjectPage--; loadSubjects(); }
    });
    el('attendanceReportSubjectNext').addEventListener('click', () => {
        if (subjectPage < subjectPages) { subjectPage++; loadSubjects(); }
    });
    for (const id of ['attendanceReportRisk', 'attendanceReportMinSessions', 'attendanceReportSort'])
        el(id).addEventListener('change', () => {
            pending++; page = 1; totalPages = 0;
            visibleItems = []; csvButton.disabled = true; body.replaceChildren();
            previous.disabled = next.disabled = true; pageLabel.textContent = '';
            message('Load report for the selected filters.');
        });
    for (const id of ['attendanceReportBatch', 'attendanceReportFrom', 'attendanceReportTo'])
        el(id).addEventListener('change', () => { pending++; page = 1; totalPages = 0;
            previous.disabled = next.disabled = true;
        visibleItems = []; csvButton.disabled = true; body.replaceChildren();
            resetDaily();
            pageLabel.textContent = ''; message('Load report for the selected filters.'); });
    el('attendanceReportBatchFind').addEventListener('click', () => { batchPage = 1; loadBatches(); });
    el('attendanceReportBatchSearch').addEventListener('keydown', event => {
        if (event.key === 'Enter') { event.preventDefault(); batchPage = 1; loadBatches(); }
    });
    el('attendanceReportBatchPrevious').addEventListener('click', () => {
        if (batchPage > 1) { batchPage--; loadBatches(); }
    });
    el('attendanceReportBatchNext').addEventListener('click', () => {
        if (batchPage < batchPages) { batchPage++; loadBatches(); }
    });
    loadBatches();
})();
