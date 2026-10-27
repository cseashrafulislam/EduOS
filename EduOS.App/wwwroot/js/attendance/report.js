(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const form = el('attendanceReportFilters'), body = el('attendanceReportRows');
    if (!form || !body) return;
    const status = el('attendanceReportStatus'), pageLabel = el('attendanceReportPage');
    const previous = el('attendanceReportPrevious'), next = el('attendanceReportNext');
    let page = 1, totalPages = 0, pending = 0;
    let visibleItems = [];
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
    async function loadBatches() {
        try {
            const data = await fetchJson('/api/academic-setup/catalog');
            const select = el('attendanceReportBatch');
            select.replaceChildren(new Option('Select academic batch', ''));
            for (const batch of (data?.batches || []).filter(x => x.isActive)) {
                select.add(new Option([batch.academicYearName, batch.academicLevelName, batch.name]
                    .filter(Boolean).join(' / '), String(batch.id)));
            }
        } catch (error) { message(error.message); }
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
                fromDate: from, toDate: to, page: String(page), pageSize: '20'
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
            message('Only finalized sessions are counted; roll numbers come from historical enrollments.');
        } catch (error) {
            if (requestId === pending) { body.replaceChildren(); message(error.message); }
        }
    }
    csvButton.addEventListener('click', exportVisiblePage);
    form.addEventListener('submit', event => { event.preventDefault(); page = 1; load(); });
    previous.addEventListener('click', () => { if (page > 1) { page--; load(); } });
    next.addEventListener('click', () => { if (page < totalPages) { page++; load(); } });
    for (const id of ['attendanceReportBatch', 'attendanceReportFrom', 'attendanceReportTo'])
        el(id).addEventListener('change', () => { pending++; page = 1; totalPages = 0;
            previous.disabled = next.disabled = true;
        visibleItems = []; csvButton.disabled = true; body.replaceChildren();
            pageLabel.textContent = ''; message('Load report for the selected filters.'); });
    loadBatches();
})();
