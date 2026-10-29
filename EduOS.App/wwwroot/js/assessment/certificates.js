(() => {
    'use strict';
    const root = document.getElementById('certificateIssueWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { page: 1, pageSize: 20, total: 0, rows: [], busy: false, requestId: null, candidates: [], candidatePage: 1, candidateTotal: 0, candidateRequest: 0 };
    function notice(value, error = false) {
        const box = el('certificateRegisterNotice');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = value;
    }
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
        const tbody = el('certificateRegisterRows'); tbody.replaceChildren();
        for (const row of state.rows) {
            const tr = document.createElement('tr');
            tr.append(cell(row.certificateNumber), cell(row.studentCode), cell(row.studentName),
                cell(row.campusName + ' / ' + row.batchName), cell(row.templateName),
                cell(row.issueDate), cell(row.isRevoked ? 'Revoked' : 'Issued'));
            const action = document.createElement('td'); action.className = 'certificate-register-action';
            if (!row.isRevoked && row.rowVersion) {
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-sm btn-outline-danger';
                button.textContent = 'Revoke'; button.disabled = state.busy;
                button.addEventListener('click', () => revoke(row)); action.append(button);
            }
            if (row.reference) {
                if (row.hasPrintableDocument && !row.isRevoked) {
                    const print = document.createElement('a');
                    print.className = 'btn btn-sm btn-outline-primary ms-1';
                    print.textContent = 'Print';
                    print.href = '/AssessmentAdministration/CertificateDocument?reference=' + encodeURIComponent(row.reference);
                    print.target = '_blank'; print.rel = 'noopener noreferrer';
                    action.append(print);
                }
                const verify = document.createElement('a');
                verify.className = 'btn btn-sm btn-outline-secondary ms-1';
                verify.textContent = 'Verify';
                verify.href = '/verify/certificate/' + encodeURIComponent(row.reference);
                verify.target = '_blank';
                verify.rel = 'noopener noreferrer';
                action.append(verify);
            }
            tr.append(action); tbody.append(tr);
        }
        if (!state.rows.length) {
            const tr = document.createElement('tr'), td = cell('No certificate records found.');
            td.colSpan = 8; tr.append(td); tbody.append(tr);
        }
        el('certificateRegisterCount').textContent = state.total + ' record(s) · Page ' + state.page;
        el('certificateRegisterPrev').disabled = state.page <= 1;
        el('certificateRegisterNext').disabled = state.page * state.pageSize >= state.total;
    }
    async function load() {
        const q = new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize),
            search: el('certificateRegisterSearch').value.trim() });
        try {
            const data = await api('/api/assessment-administration/certificates?' + q);
            state.rows = data.items || []; state.total = data.totalCount || 0; render();
        } catch (e) { notice(e.message, true); }
    }
    async function templates() {
        try {
            const data = await api('/api/assessment-administration/certificate-templates?page=1&pageSize=100');
            const select = el('certificateTemplate'); select.replaceChildren(new Option('Select active template', ''));
            (data.items || []).filter(x => x.isActive).forEach(x => select.add(new Option(x.code + ' — ' + x.name, String(x.id))));
            if (data.totalCount > 100) notice('Only the first 100 templates are shown. Refine templates before issuing.', true);
        } catch (e) { el('certificateTemplate').replaceChildren(new Option('Templates unavailable', '')); notice(e.message, true); }
    }
    async function candidates(page = 1) {
        const term = el('certificateCandidateSearch').value.trim();
        if (term.length < 2 || term.length > 100) { notice('Enter 2 to 100 characters.', true); return; }
        const request = ++state.candidateRequest;
        const select = el('certificateCandidate'); select.replaceChildren(new Option('Loading…', ''));
        try {
            const query = new URLSearchParams({ search: term, page: String(page), pageSize: '20' });
            const data = await api('/api/assessment-administration/certificates/candidates/page?' + query);
            if (request !== state.candidateRequest || el('certificateCandidateSearch').value.trim() !== term) return;
            state.candidates = data.items || []; state.candidatePage = page;
            state.candidateTotal = data.totalCount || 0; resetRequest();
            select.replaceChildren(new Option('Select eligible enrollment', ''));
            state.candidates.forEach((row, index) => select.add(new Option(row.studentCode + ' — ' + row.studentName +
                ' (' + row.campusName + ' / ' + row.batchName + ')', String(index))));
            el('certificateCandidateCount').textContent = state.candidateTotal + ' matches · Page ' + page;
            el('certificateCandidatePrev').disabled = page <= 1;
            el('certificateCandidateNext').disabled = page * 20 >= state.candidateTotal;
            if (!state.candidates.length) notice('No passed, released published result matched.', true);
        } catch (e) { if (request === state.candidateRequest) {
            state.candidates = []; select.replaceChildren(new Option('Search again', '')); notice(e.message, true);
        } }
    }
    function resetRequest() { state.requestId = null; }
    async function issue(event) {
        event.preventDefault();
        if (state.busy) return;
        const selected = el('certificateCandidate').value;
        const candidate = selected === '' ? null : state.candidates[Number(selected)];
        const templateId = Number(el('certificateTemplate').value);
        const issueDate = el('certificateIssueDate').value;
        if (!candidate || !templateId || !issueDate) { notice('Select an eligible enrollment, template and date.', true); return; }
        state.requestId ||= crypto.randomUUID();
        state.busy = true; el('certificateIssueSubmit').disabled = true;
        try {
            await api('/api/assessment-administration/certificates', 'POST', {
                clientRequestId: state.requestId, certificateTemplateId: templateId,
                studentReference: candidate.studentReference,
                studentEnrollmentReference: candidate.studentEnrollmentReference, issueDate
            });
            notice('Certificate record issued.'); resetRequest(); state.page = 1; await load();
        } catch (e) { notice(e.message, true); }
        finally { state.busy = false; el('certificateIssueSubmit').disabled = false; }
    }
    async function revoke(row) {
        if (state.busy || row.isRevoked) return;
        const reason = window.prompt('Reason for revocation (required, max 1000 characters):', '');
        if (reason === null) return;
        if (!reason.trim() || reason.trim().length > 1000) { notice('Enter a valid revocation reason.', true); return; }
        if (!window.confirm('Revoke certificate ' + row.certificateNumber + '? This cannot be undone.')) return;
        state.busy = true;
        try {
            await api('/api/assessment-administration/certificates/' + encodeURIComponent(row.id) + '/revoke',
                'POST', { reason: reason.trim(), rowVersion: row.rowVersion });
            notice('Certificate revoked with audit reason.'); await load();
        } catch (e) { notice(e.message, true); await load(); }
        finally { state.busy = false; }
    }
    function csv() {
        const safe = value => /^[=+@\-\t\r]/.test(String(value ?? '')) ? "'" + String(value) : value;
        const quote = value => '"' + String(safe(value) ?? '').replace(/"/g, '""') + '"';
        const rows = [['Certificate', 'Student code', 'Student', 'Campus', 'Batch', 'Template', 'Issue date', 'Status'],
            ...state.rows.map(x => [x.certificateNumber, x.studentCode, x.studentName, x.campusName,
                x.batchName, x.templateName, x.issueDate, x.isRevoked ? 'Revoked' : 'Issued'])];
        const data = rows.map(row => row.map(quote).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF' + data], { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a'); a.href = url;
        a.download = 'certificate-register-page-' + state.page + '.csv';
        document.body.append(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    el('certificateIssueForm').addEventListener('submit', issue);
    el('certificateFindCandidate').addEventListener('click', () => candidates(1));
    el('certificateCandidateSearch').addEventListener('input', () => {
        ++state.candidateRequest; resetRequest(); state.candidates = []; state.candidateTotal = 0;
        el('certificateCandidate').replaceChildren(new Option('Search first', ''));
        el('certificateCandidateCount').textContent = '';
        el('certificateCandidatePrev').disabled = true; el('certificateCandidateNext').disabled = true;
    });
    el('certificateCandidatePrev').addEventListener('click', () => { if (state.candidatePage > 1) candidates(state.candidatePage - 1); });
    el('certificateCandidateNext').addEventListener('click', () => { if (state.candidatePage * 20 < state.candidateTotal) candidates(state.candidatePage + 1); });
    el('certificateCandidatePrev').disabled = true; el('certificateCandidateNext').disabled = true;
    el('certificateCandidate').addEventListener('change', resetRequest);
    el('certificateTemplate').addEventListener('change', resetRequest);
    el('certificateIssueDate').addEventListener('change', resetRequest);
    el('certificateRegisterRefresh').addEventListener('click', () => { state.page = 1; load(); });
    el('certificateRegisterPrev').addEventListener('click', () => { if (state.page > 1) { state.page--; load(); } });
    el('certificateRegisterNext').addEventListener('click', () => { if (state.page * state.pageSize < state.total) { state.page++; load(); } });
    el('certificateRegisterCsv').addEventListener('click', csv);
    el('certificateRegisterPrint').addEventListener('click', () => window.print());
    el('certificateIssueDate').value = new Date().toLocaleDateString('en-CA');
    el('certificateIssueDate').max = el('certificateIssueDate').value;
    templates(); load();
})();
