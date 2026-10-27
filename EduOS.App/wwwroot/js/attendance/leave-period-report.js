(() => {
    'use strict';
    const root = document.getElementById('studentLeavePeriodReport');
    if (!root) return;
    const el = id => document.getElementById(id);
    const form = el('leaveReportFilters'), rows = el('leaveReportRows');
    const status = el('leaveReportStatus'), pageText = el('leaveReportPage');
    const previous = el('leaveReportPrevious'), next = el('leaveReportNext');
    let page = 1, totalPages = 0, pending = 0;
    const now = new Date();
    const today = new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
    el('leaveReportFrom').value = today;
    el('leaveReportTo').value = today;
    const states = { 1: 'Draft', 2: 'Submitted', 3: 'Approved', 4: 'Rejected', 5: 'Cancelled' };
    function cell(value) {
        const td = document.createElement('td');
        td.textContent = String(value ?? '');
        return td;
    }
    async function json(url) {
        const response = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
        });
        const result = await response.json().catch(() => null);
        if (!response.ok || result?.success !== true)
            throw new Error(result?.message || 'Unable to load student leave report.');
        return result.data;
    }
    async function loadBatches() {
        try {
            const catalog = await json('/api/academic-setup/catalog');
            const select = el('leaveReportBatch');
            select.replaceChildren(new Option('Select academic batch', ''));
            for (const batch of (catalog?.batches || []).filter(x => x.isActive)) {
                select.add(new Option([batch.academicYearName, batch.academicLevelName, batch.name]
                    .filter(Boolean).join(' / '), String(batch.id)));
            }
        } catch (error) { status.textContent = error.message; }
    }
    async function load() {
        if (!form.reportValidity()) return;
        const from = el('leaveReportFrom').value, to = el('leaveReportTo').value;
        const days = Math.round((Date.parse(to) - Date.parse(from)) / 86400000);
        if (!Number.isFinite(days) || days < 0 || days > 365) {
            status.textContent = 'Select a date range of 1 to 366 days.'; return;
        }
        const request = ++pending;
        previous.disabled = next.disabled = true;
        status.textContent = 'Loading leave report…';
        try {
            const params = new URLSearchParams({
                academicBatchId: el('leaveReportBatch').value, fromDate: from, toDate: to,
                page: String(page), pageSize: '20'
            });
            if (el('leaveReportState').value) params.set('state', el('leaveReportState').value);
            const data = await json('/api/student-leaves/report?' + params);
            if (request !== pending) return;
            rows.replaceChildren();
            for (const item of data.items || []) {
                const tr = document.createElement('tr');
                for (const value of [item.rollNo, item.studentCode, item.studentName,
                    item.fromDate + ' – ' + item.toDate, item.periodDays,
                    states[item.state] || 'Unknown',
                    item.reviewedAt ? new Date(item.reviewedAt).toLocaleDateString() : '—'])
                    tr.append(cell(value));
                rows.append(tr);
            }
            if (!rows.children.length) {
                const tr = document.createElement('tr'), td = cell('No leave requests in this period.');
                td.colSpan = 7; tr.append(td); rows.append(tr);
            }
            totalPages = data.totalPages || 0;
            pageText.textContent = 'Page ' + page + ' of ' + totalPages +
                ' · ' + data.totalCount + ' request(s)';
            previous.disabled = page <= 1;
            next.disabled = page >= totalPages;
            status.textContent = 'Historical enrollment roll; days count only the selected period. Student name is current display data.';
        } catch (error) {
            if (request === pending) {
                rows.replaceChildren(); status.textContent = error.message;
            }
        }
    }
    function invalidate() {
        pending++; page = 1; totalPages = 0;
        previous.disabled = next.disabled = true;
        rows.replaceChildren(); pageText.textContent = '';
        status.textContent = 'Load report for the selected filters.';
    }
    form.addEventListener('submit', event => { event.preventDefault(); page = 1; load(); });
    previous.addEventListener('click', () => { if (page > 1) { page--; load(); } });
    next.addEventListener('click', () => { if (page < totalPages) { page++; load(); } });
    for (const id of ['leaveReportBatch', 'leaveReportFrom', 'leaveReportTo', 'leaveReportState'])
        el(id).addEventListener('change', invalidate);
    loadBatches();
})();
