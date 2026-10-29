(() => {
    'use strict';
    const root = document.getElementById('transcriptWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { page: 1, pageSize: 20, total: 0, rows: [], busy: false, requestId: null,
        candidatePage: 1, candidateTotal: 0, candidateRequest: 0, recordRequest: 0, record: null };
    const message = (value, error = false) => {
        const box = el('transcriptNotice');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = value;
    };
    async function api(path, method = 'GET', body) {
        const response = await fetch(path, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json',
                ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
                ...(method === 'GET' ? {} : { RequestVerificationToken: token }) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        const result = await response.json().catch(() => null);
        if (!response.ok || result?.success !== true) throw new Error(result?.message || 'Request failed.');
        return result.data;
    }
    const cell = value => { const td = document.createElement('td'); td.textContent = value == null ? '' : String(value); return td; };
    function render() {
        const tbody = el('transcriptRows'); tbody.replaceChildren();
        for (const row of state.rows) {
            const tr = document.createElement('tr');
            tr.append(cell(row.transcriptNumber), cell(row.studentCode), cell(row.studentName),
                cell(row.issueDate), cell(row.isRevoked ? 'Revoked' : 'Issued'));
            const action = document.createElement('td'); action.className = 'transcript-action';
            if (row.reference) {
                if (!row.isRevoked) {
                    const documentLink = document.createElement('a');
                    documentLink.href = '/AssessmentAdministration/TranscriptDocument?reference=' +
                        encodeURIComponent(row.reference);
                    documentLink.target = '_blank'; documentLink.rel = 'noopener noreferrer';
                    documentLink.className = 'btn btn-sm btn-outline-primary me-1';
                    documentLink.textContent = 'Document'; action.append(documentLink);
                }
                const link = document.createElement('a');
                link.href = '/verify/transcript/' + encodeURIComponent(row.reference);
                link.target = '_blank'; link.rel = 'noopener noreferrer';
                link.className = 'btn btn-sm btn-outline-secondary me-1';
                link.textContent = 'Verify'; action.append(link);
            }
            if (!row.isRevoked && row.rowVersion) {
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-sm btn-outline-danger';
                button.textContent = 'Revoke';
                button.addEventListener('click', () => revoke(row));
                action.append(button);
            }
            tr.append(action); tbody.append(tr);
        }
        if (!state.rows.length) {
            const tr = document.createElement('tr'), td = cell('No transcript records found.');
            td.colSpan = 6; tr.append(td); tbody.append(tr);
        }
        el('transcriptCount').textContent = state.total + ' record(s) · Page ' + state.page;
        el('transcriptPrev').disabled = state.page <= 1;
        el('transcriptNext').disabled = state.page * state.pageSize >= state.total;
    }
    async function load() {
        const q = new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize),
            search: el('transcriptSearch').value.trim() });
        try {
            const data = await api('/api/assessment-administration/transcripts?' + q);
            state.rows = data.items || []; state.total = data.totalCount || 0; render();
        } catch (e) { message(e.message, true); }
    }
    async function candidates(page = 1) {
        const term = el('transcriptStudentSearch').value.trim();
        if (term.length < 2 || term.length > 100) { message('Enter 2 to 100 characters.', true); return; }
        const request = ++state.candidateRequest;
        const select = el('transcriptStudent'); select.replaceChildren(new Option('Loading…', ''));
        try {
            const query = new URLSearchParams({ search: term, page: String(page), pageSize: '20' });
            const data = await api('/api/assessment-administration/transcripts/candidates/page?' + query);
            if (request !== state.candidateRequest || el('transcriptStudentSearch').value.trim() !== term) return;
            select.replaceChildren(new Option('Select student', ''));
            (data.items || []).forEach(row => select.add(new Option(row.studentCode + ' — ' + row.studentName, row.studentReference)));
            state.candidatePage = page; state.candidateTotal = data.totalCount || 0; resetRequest();
            clearRecord();
            el('transcriptCandidateCount').textContent = state.candidateTotal + ' matches · Page ' + page;
            el('transcriptCandidatePrev').disabled = page <= 1;
            el('transcriptCandidateNext').disabled = page * 20 >= state.candidateTotal;
            if (!data.items?.length) message('No students with released published results matched.', true);
        } catch (e) { if (request === state.candidateRequest) {
            select.replaceChildren(new Option('Search again', '')); message(e.message, true);
        } }
    }
    function resetRequest() { state.requestId = null; }
    function clearRecord() {
        ++state.recordRequest;
        state.record = null;
        el('transcriptRecordCsv').disabled = true;
        el('transcriptPreviewRecord').disabled = !el('transcriptStudent').value;
        el('transcriptRecordSummary').textContent = 'Select a student and load immutable published course history.';
        const body = el('transcriptRecordRows'); body.replaceChildren();
        const tr = document.createElement('tr'), td = cell('No course history loaded.');
        td.colSpan = 8; tr.append(td); body.append(tr);
    }
    async function academicRecord() {
        const studentReference = el('transcriptStudent').value;
        if (!studentReference) return;
        const request = ++state.recordRequest;
        const button = el('transcriptPreviewRecord'); button.disabled = true;
        try {
            const data = await api('/api/assessment-administration/transcripts/academic-record/' +
                encodeURIComponent(studentReference));
            if (request !== state.recordRequest || el('transcriptStudent').value !== studentReference) return;
            state.record = data;
            const body = el('transcriptRecordRows'); body.replaceChildren();
            for (const course of data.courses || []) {
                const tr = document.createElement('tr');
                tr.append(cell(course.assessmentName + ' · v' + course.publicationVersionNo),
                    cell(course.academicBatchName), cell(course.subjectCode + ' — ' + course.subjectName),
                    cell(course.obtainedMarks + ' / ' + course.fullMarks), cell(course.creditHours),
                    cell(course.gradePoint), cell(course.gradeLetter),
                    cell(course.isWithheld ? 'Withheld' : course.isPassed ? 'Passed' : 'Not passed'));
                body.append(tr);
            }
            if (!data.courses?.length) {
                const tr = document.createElement('tr'), td = cell('No course-level snapshots in released publications.');
                td.colSpan = 8; tr.append(td); body.append(tr);
            }
            el('transcriptRecordSummary').textContent = data.studentCode + ' · ' + data.studentName +
                ' · Latest published CGPA: ' + (data.latestPublishedCgpa ?? '—') +
                ' · ' + (data.courses?.length || 0) + ' course snapshot(s)' +
                (data.legacyPublishedResults ? ' · ' + data.legacyPublishedResults +
                    ' older publication(s) have no course-level evidence.' : '') +
                ' · Preview only—not a signed transcript.';
            el('transcriptRecordCsv').disabled = !data.courses?.length;
        } catch (error) {
            if (request !== state.recordRequest) return;
            state.record = null;
            el('transcriptRecordCsv').disabled = true;
            message(error.message, true);
        } finally {
            if (request === state.recordRequest) button.disabled = false;
        }
    }
    function recordCsv() {
        if (!state.record?.courses?.length) return;
        const fields = ['Assessment', 'Version', 'Batch', 'Subject code', 'Subject',
            'Marks', 'Maximum', 'Credits', 'Grade point', 'Grade letter', 'Passed', 'Withheld'];
        const rows = [fields, ...state.record.courses.map(x => [x.assessmentName, x.publicationVersionNo,
            x.academicBatchName, x.subjectCode, x.subjectName, x.obtainedMarks, x.fullMarks,
            x.creditHours, x.gradePoint, x.gradeLetter, x.isPassed, x.isWithheld])];
        const safe = value => /^[=+@\-\t\r]/.test(String(value ?? '')) ? "'" + String(value) : value;
        const csv = rows.map(row => row.map(v => '"' + String(safe(v) ?? '').replace(/"/g, '""') + '"').join(','))
            .join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF' + csv], { type: 'text/csv;charset=utf-8' }));
        const link = document.createElement('a'); link.href = url;
        link.download = 'published-course-history-' + state.record.studentCode.replace(/[^\w-]/g, '_') + '.csv';
        document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    async function issue(event) {
        event.preventDefault();
        if (state.busy) return;
        const studentReference = el('transcriptStudent').value;
        const issueDate = el('transcriptDate').value;
        if (!studentReference || !issueDate) { message('Select an eligible student and issue date.', true); return; }
        state.requestId ||= crypto.randomUUID();
        state.busy = true; el('transcriptIssue').disabled = true;
        try {
            await api('/api/assessment-administration/transcripts', 'POST', {
                clientRequestId: state.requestId, studentReference, issueDate
            });
            message('Issue-time transcript document saved. Open Document to print or save as PDF.'); resetRequest();
            state.page = 1; await load();
        } catch (e) { message(e.message, true); }
        finally { state.busy = false; el('transcriptIssue').disabled = false; }
    }
    async function revoke(row) {
        if (state.busy || row.isRevoked) return;
        const reason = window.prompt('Reason for revocation (required, max 1000 characters):', '');
        if (reason === null) return;
        if (!reason.trim() || reason.trim().length > 1000) { message('Enter a revocation reason (max 1000 characters).', true); return; }
        if (!window.confirm('Revoke transcript ' + row.transcriptNumber + '? This cannot be undone.')) return;
        state.busy = true;
        try {
            await api('/api/assessment-administration/transcripts/' + encodeURIComponent(row.reference) + '/revoke',
                'POST', { reason: reason.trim(), rowVersion: row.rowVersion });
            message('Transcript revoked with audit reason.'); await load();
        } catch (e) { message(e.message, true); await load(); }
        finally { state.busy = false; }
    }
    function csv() {
        const fields = ['Transcript number', 'Student code', 'Student', 'Issue date', 'Status'];
        const quote = value => '"' + String(value ?? '').replace(/"/g, '""') + '"';
        const safe = value => /^[=+@\-\t\r]/.test(String(value ?? '')) ? "'" + String(value) : value;
        const lines = [fields, ...state.rows.map(x => [x.transcriptNumber, x.studentCode, x.studentName,
            x.issueDate, x.isRevoked ? 'Revoked' : 'Issued'])];
        const data = lines.map(row => row.map(v => quote(safe(v))).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF' + data], { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a'); a.href = url; a.download = 'transcript-register-page-' + state.page + '.csv';
        document.body.append(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    el('transcriptIssueForm').addEventListener('submit', issue);
    el('transcriptFindStudent').addEventListener('click', () => candidates(1));
    el('transcriptStudentSearch').addEventListener('input', () => {
        ++state.candidateRequest; resetRequest(); state.candidateTotal = 0;
        el('transcriptStudent').replaceChildren(new Option('Search first', ''));
        el('transcriptCandidateCount').textContent = '';
        el('transcriptCandidatePrev').disabled = true; el('transcriptCandidateNext').disabled = true;
        clearRecord();
    });
    el('transcriptCandidatePrev').addEventListener('click', () => { if (state.candidatePage > 1) candidates(state.candidatePage - 1); });
    el('transcriptCandidateNext').addEventListener('click', () => { if (state.candidatePage * 20 < state.candidateTotal) candidates(state.candidatePage + 1); });
    el('transcriptCandidatePrev').disabled = true; el('transcriptCandidateNext').disabled = true;
    el('transcriptStudent').addEventListener('change', () => { resetRequest(); clearRecord(); });
    el('transcriptPreviewRecord').addEventListener('click', academicRecord);
    el('transcriptRecordCsv').addEventListener('click', recordCsv);
    el('transcriptDate').addEventListener('change', resetRequest);
    el('transcriptRefresh').addEventListener('click', () => { state.page = 1; load(); });
    el('transcriptPrev').addEventListener('click', () => { if (state.page > 1) { state.page--; load(); } });
    el('transcriptNext').addEventListener('click', () => { if (state.page * state.pageSize < state.total) { state.page++; load(); } });
    el('transcriptCsv').addEventListener('click', csv);
    el('transcriptPrint').addEventListener('click', () => window.print());
    el('transcriptDate').value = new Date().toLocaleDateString('en-CA');
    load();
})();
