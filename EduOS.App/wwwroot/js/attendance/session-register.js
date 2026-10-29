(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const form = el('attendanceReportFilters');
    const body = el('attendanceSessionsRows');
    if (!form || !body) return;
    const status = el('attendanceSessionsStatus');
    const overview = el('attendanceSessionsOverview');
    const filter = el('attendanceSessionsStatusFilter');
    const csvButton = el('attendanceSessionsCsv');
    const printButton = el('attendanceSessionsPrint');
    const previous = el('attendanceSessionsPrevious');
    const next = el('attendanceSessionsNext');
    const pageLabel = el('attendanceSessionsPage');
    let page = 1, pages = 0, request = 0, visible = [];
    let authorizedQuery = null, exportVersion = 0;
    let summaryVersion = 0, summaryKey = '';

    function reset() {
        request++; exportVersion++; authorizedQuery = null;
        page = 1; pages = 0; visible = [];
        body.replaceChildren();
        status.textContent = '';
        overview.textContent = '';
        summaryVersion++; summaryKey = '';
        pageLabel.textContent = '';
        csvButton.disabled = printButton.disabled = previous.disabled = next.disabled = true;
    }
    function cell(value) {
        const td = document.createElement('td');
        td.textContent = String(value ?? '');
        return td;
    }
    async function loadSummary(token, params) {
        const query = new URLSearchParams(params);
        query.delete('page'); query.delete('pageSize'); query.delete('status');
        try {
            const response = await fetch('/api/student-attendance/report/sessions/summary?' + query, {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
            });
            const json = await response.json().catch(() => null);
            if (!response.ok || !json?.success)
                throw new Error('Session overview unavailable.');
            if (token !== summaryVersion) return;
            const data = json.data || {};
            overview.textContent = 'Total ' + (data.totalSessions ?? 0) +
                ' · Draft ' + (data.draftSessions ?? 0) +
                ' · Finalized ' + (data.finalizedSessions ?? 0) +
                ' · Empty drafts ' + (data.emptyDraftSessions ?? 0) +
                ' · Empty finalized ' + (data.emptyFinalizedSessions ?? 0);
        } catch {
            if (token === summaryVersion) overview.textContent = 'Session overview unavailable.';
        }
    }
    async function load(refreshSummary = false) {
        if (!form.reportValidity()) return;
        const from = el('attendanceReportFrom').value, to = el('attendanceReportTo').value;
        const days = (Date.parse(to) - Date.parse(from)) / 86400000;
        if (!Number.isFinite(days) || days < 0 || days > 30) {
            status.textContent = 'Select a range of 1 to 31 days.'; return;
        }
        const token = ++request;
        exportVersion++; authorizedQuery = null;
        visible = [];
        body.replaceChildren();
        csvButton.disabled = printButton.disabled = previous.disabled = next.disabled = true;
        status.textContent = 'Loading attendance sessions…';
        const params = new URLSearchParams({
            academicBatchId: el('attendanceReportBatch').value,
            fromDate: from, toDate: to, page: String(page), pageSize: '20',
            status: filter.value,
            ...(el('attendanceReportSubject').value ?
                { subjectOfferingId: el('attendanceReportSubject').value } : {})
        });
        const key = [params.get('academicBatchId'), from, to,
            params.get('subjectOfferingId') || ''].join('|');
        if (refreshSummary || key !== summaryKey) {
            summaryKey = key;
            overview.textContent = 'Loading session overview…';
            void loadSummary(++summaryVersion, params);
        }
        try {
            const response = await fetch('/api/student-attendance/report/sessions?' + params, {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
            });
            const json = await response.json().catch(() => null);
            if (!response.ok || !json?.success)
                throw new Error(json?.message || 'Session register unavailable.');
            if (token !== request) return;
            visible = json.data?.items || [];
            authorizedQuery = new URLSearchParams(params);
            for (const item of visible) {
                const tr = document.createElement('tr');
                for (const value of [
                    item.attendanceDate, item.subjectOfferingCode,
                    item.isFinalized ? 'Finalized' : 'Not finalized',
                    item.marked, item.present, item.absent, item.late, item.leave, item.excused
                ]) tr.append(cell(value));
                const action = document.createElement('td');
                const open = document.createElement('button');
                open.type = 'button';
                open.className = 'btn btn-sm btn-outline-primary';
                open.textContent = 'Open roster';
                open.setAttribute('aria-label', 'Open attendance roster for ' + item.attendanceDate);
                open.addEventListener('click', () => document.dispatchEvent(
                    new CustomEvent('attendance:session-open', { detail: {
                        sessionId: item.sessionId,
                        isFinalized: item.isFinalized === true,
                        academicBatchId: params.get('academicBatchId'),
                        attendanceDate: item.attendanceDate,
                        subjectOfferingId: item.subjectOfferingId,
                        subjectOfferingCode: item.subjectOfferingCode,
                        batchLabel: el('attendanceReportBatch').selectedOptions[0]?.textContent
                    } })));
                action.append(open);
                tr.append(action);
                body.append(tr);
            }
            if (!visible.length) {
                const tr = document.createElement('tr');
                const td = cell('No sessions match these filters.');
                td.colSpan = 10; td.className = 'text-center text-muted';
                tr.append(td); body.append(tr);
            }
            pages = json.data?.totalPages || 0;
            pageLabel.textContent = 'Page ' + page + ' of ' + pages +
                ' · ' + (json.data?.totalCount || 0) + ' session(s)';
            previous.disabled = page <= 1;
            next.disabled = page >= pages;
            csvButton.disabled = printButton.disabled = visible.length === 0;
            status.textContent = (json.data?.totalCount || 0) + ' matching session(s).';
        } catch (error) {
            if (token === request) {
                body.replaceChildren();
                status.textContent = error.message;
            }
        }
    }
    function scopedUrl(suffix) {
        return authorizedQuery
            ? '/attendance/sessions/register/' + suffix + '?' + authorizedQuery.toString()
            : null;
    }
    async function exportPage() {
        const url = scopedUrl('export.csv');
        if (!url || !visible.length) return;
        const token = ++exportVersion;
        csvButton.disabled = true;
        try {
            const response = await fetch(url, {
                credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'text/csv' }
            });
            if (!response.ok || !response.headers.get('content-type')?.includes('text/csv'))
                throw new Error('Authorized session export unavailable. Reload and retry.');
            const blob = await response.blob();
            if (token !== exportVersion || !authorizedQuery) return;
            const objectUrl = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = objectUrl;
            link.download = 'attendance-session-register-' +
                authorizedQuery.get('academicBatchId') + '-page-' +
                authorizedQuery.get('page') + '.csv';
            document.body.append(link);
            link.click();
            link.remove();
            URL.revokeObjectURL(objectUrl);
        } catch (error) {
            if (token === exportVersion) status.textContent = error.message;
        } finally {
            if (token === exportVersion) csvButton.disabled = !visible.length;
        }
    }
    function printPage() {
        const url = scopedUrl('print');
        if (url && visible.length) window.open(url, '_blank', 'noopener,noreferrer');
    }
    form.addEventListener('submit', () => { page = 1; load(true); });
    filter.addEventListener('change', () => { reset(); load(); });
    el('attendanceSessionsLoad').addEventListener('click', () => { page = 1; load(true); });
    previous.addEventListener('click', () => { if (page > 1) { page--; load(); } });
    next.addEventListener('click', () => { if (page < pages) { page++; load(); } });
    csvButton.addEventListener('click', exportPage);
    printButton.addEventListener('click', printPage);
    for (const id of ['attendanceReportBatch', 'attendanceReportSubject',
        'attendanceReportFrom', 'attendanceReportTo'])
        el(id).addEventListener('change', reset);
})();
