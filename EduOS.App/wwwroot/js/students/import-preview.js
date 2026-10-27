(() => {
    'use strict';
    const form = document.getElementById('studentImportForm');
    if (!form) return;
    const fileInput = document.getElementById('studentImportFile');
    const inspect = document.getElementById('studentImportInspect');
    const preview = document.getElementById('studentImportPreview');
    const mapping = document.getElementById('studentImportMapping');
    const alert = document.getElementById('studentImportAlert');
    const results = document.getElementById('studentImportResults');
    const summary = document.getElementById('studentImportSummary');
    const errorRows = document.getElementById('studentImportErrorRows');
    const download = document.getElementById('studentImportDownloadErrors');
    const commit = document.getElementById('studentImportCommit');
    const resume = document.getElementById('studentImportResume');
    const progress = document.getElementById('studentImportProgress');
    const jobSummary = document.getElementById('studentImportJobSummary');
    const jobExport = document.getElementById('studentImportJobExport');
    const tenant = document.querySelector('[data-import-tenant]')?.dataset.importTenant;
    const checkpointKey = 'eduos.student-import.v1.' + tenant;
    const jobTools = window.EduOSStudentImportJob;
    const committed = document.getElementById('studentImportCommitted');
    const committedSummary = document.getElementById('studentImportCommittedSummary');
    const committedRows = document.getElementById('studentImportCommittedRows');
    const reload = document.getElementById('studentImportReload');
    const more = document.getElementById('studentImportMore');
    let validated = null;
    let lastMapping = null;
    let importReference = null;
    let reconciliationPage = 1;
    let currentJob = null;
    let currentFingerprint = null;
    const fields = Array.from(mapping.querySelectorAll('[data-import-field]'));
    const required = new Set(['SourceKey', 'StudentCode', 'FullName', 'CampusId', 'AcademicBatchId', 'RollNo']);
    let busy = false;
    let errors = [];

    function message(text, failed) {
        alert.textContent = text || '';
        alert.className = 'alert ' + (failed ? 'alert-danger' : 'alert-info');
    }
    function reset() {
        mapping.classList.add('d-none');
        results.classList.add('d-none');
        errors = [];
        currentJob = null;
        currentFingerprint = null;
        jobSummary.textContent = '';
        jobExport.classList.add('d-none');
        validated = null;
        lastMapping = null;
        commit.classList.add('d-none');
        committed.classList.add('d-none');
        alert.classList.add('d-none');
    }
    fileInput.addEventListener('change', reset);
    mapping.addEventListener('change', () => {
        currentJob = null;
        currentFingerprint = null;
        jobSummary.textContent = '';
        jobExport.classList.add('d-none');
        validated = null;
        lastMapping = null;
        commit.classList.add('d-none');
        results.classList.add('d-none');
        refreshResume().catch(() => resume.classList.add('d-none'));
    });
    async function send(path, data) {
        const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (!token) throw new Error('Session verification token is missing. Refresh the page.');
        const response = await fetch(path, {
            method: 'POST', body: data, credentials: 'same-origin',
            headers: { 'RequestVerificationToken': token, 'Accept': 'application/json' },
            cache: 'no-store'
        });
        const body = await response.json().catch(() => null);
        if (!response.ok || !body?.success) throw new Error(body?.message || 'File validation failed (' + response.status + ').');
        return body.data;
    }
    function selectedFile() {
        const file = fileInput.files?.[0];
        if (!file) throw new Error('Choose a CSV or XLSX file first.');
        if (file.size > 6 * 1024 * 1024) throw new Error('File must be 6 MB or smaller.');
        if (!/\.(csv|xlsx)$/i.test(file.name)) throw new Error('Only CSV and XLSX files are accepted.');
        return file;
    }
    inspect.addEventListener('click', async () => {
        if (busy) return;
        busy = true; inspect.disabled = true;
        try {
            const data = new FormData();
            data.append('file', selectedFile());
            const headers = await send('/api/student-imports/headers', data);
            for (const select of fields) {
                select.replaceChildren();
                const optional = !required.has(select.dataset.importField);
                if (optional) select.add(new Option('Not mapped', ''));
                for (const header of headers) select.add(new Option(header, header));
                const match = headers.find(h => h.toLowerCase() === select.dataset.importField.toLowerCase());
                select.value = match || (optional ? '' : headers[0]);
            }
            mapping.classList.remove('d-none');
            results.classList.add('d-none');
            await refreshResume();
            message('Columns inspected. Review each mapping before validation.', false);
        } catch (error) { message(error.message, true); }
        finally { busy = false; inspect.disabled = false; }
    });
    function chosenMapping() {
        const chosen = {};
        for (const select of fields) {
            if (select.value) chosen[select.dataset.importField] = select.value;
            else if (required.has(select.dataset.importField))
                throw new Error('Required mapping missing: ' + select.dataset.importField);
        }
        if (new Set(Object.values(chosen)).size !== Object.keys(chosen).length)
            throw new Error('Each field must use a different source column.');
        return JSON.stringify(chosen);
    }
    function readCheckpoint() {
        if (!tenant) return null;
        try {
            const data = JSON.parse(localStorage.getItem(checkpointKey) || 'null');
            return data && Number.isInteger(data.offset) && Number.isInteger(data.totalRows) ? data : null;
        } catch { return null; }
    }
    async function matchingCheckpoint() {
        const saved = readCheckpoint();
        if (!saved || !jobTools) return null;
        const fp = await jobTools.fingerprint(selectedFile(), chosenMapping());
        return fp.fileHash === saved.fileHash && fp.mappingHash === saved.mappingHash ? saved : null;
    }

    async function getJobStatus(fp, totalRows) {
        const query = new URLSearchParams({
            fileHash: fp.fileHash, mappingHash: fp.mappingHash, totalRows: String(totalRows)
        });
        const response = await fetch('/api/student-imports/job-status?' + query, {
            credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
        });
        const body = await response.json().catch(() => null);
        if (!response.ok || !body?.success)
            throw new Error(body?.message || 'Server reconciliation failed.');
        return body.data;
    }
    function showJobStatus(fp, status) {
        currentFingerprint = fp;
        currentJob = status;
        jobSummary.textContent = 'Source-file reconciliation: ' + status.committedRows +
            ' / ' + status.totalRows + ' rows committed.';
        jobExport.classList.toggle('d-none', !(status.batches || []).length);
        resume.classList.toggle('d-none', status.isComplete || !status.committedRows);
    }
    async function refreshResume() {
        resume.classList.add('d-none');
        const saved = await matchingCheckpoint();
        if (saved && saved.totalRows > 0 && saved.totalRows <= 5000)
            showJobStatus(saved, await getJobStatus(saved, saved.totalRows));
    }
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (busy || mapping.classList.contains('d-none')) return;
        busy = true; preview.disabled = true;
        try {
            const data = new FormData();
            data.append('file', selectedFile());
            data.append('mappingJson', chosenMapping());
            const report = await send('/api/student-imports/preview', data);
            lastMapping = chosenMapping();
            validated = report;
            const fp = await jobTools.fingerprint(selectedFile(), lastMapping);
            showJobStatus(fp, await getJobStatus(fp, report.totalRows));
            commit.classList.toggle('d-none', report.invalidRows !== 0 || report.validRows < 1 || report.validRows > 5000);
            errors = Array.isArray(report.errors) ? report.errors : [];
            summary.textContent = 'Rows: ' + report.totalRows + ' | Valid: ' + report.validRows +
                ' | Invalid: ' + report.invalidRows + (report.hasMoreErrors ? ' | More errors not shown' : '');
            errorRows.replaceChildren();
            for (const row of errors) {
                const tr = document.createElement('tr');
                for (const value of [row.rowNumber, row.sourceKey, row.studentCode, (row.issues || []).join('; ')]) {
                    const td = document.createElement('td');
                    td.textContent = String(value ?? '');
                    tr.appendChild(td);
                }
                errorRows.appendChild(tr);
            }
            results.classList.remove('d-none');
            message('Read-only preflight completed. No student records were created.', false);
         } catch (error) {
            results.classList.add('d-none'); commit.classList.add('d-none');
            validated = null; message(error.message, true);
        }
        finally { busy = false; preview.disabled = false; }
    });
    async function loadReconciliation(append = false) {
        if (!importReference) return;
        const response = await fetch('/api/student-imports/' + encodeURIComponent(importReference) +
            '?page=' + reconciliationPage + '&pageSize=50', { credentials: 'same-origin', cache: 'no-store' });
        const body = await response.json().catch(() => null);
        if (!response.ok || !body?.success) throw new Error(body?.message || 'Reconciliation could not be loaded.');
        const report = body.data;
        if (!append) committedRows.replaceChildren();
        committedSummary.textContent = 'Import: ' + report.importReference + ' | Total: ' +
            report.totalRows + ' | Committed: ' + report.successRows + ' | Failed: ' + report.failedRows;
        for (const row of report.items || []) {
            const tr = document.createElement('tr');
            for (const value of [row.rowNumber, row.sourceKey, row.isSuccess ? 'Imported' : (row.errorMessage || 'Failed')]) {
                const td = document.createElement('td'); td.textContent = String(value ?? ''); tr.appendChild(td);
            }
            committedRows.appendChild(tr);
        }
        more.classList.toggle('d-none', reconciliationPage * 50 >= report.totalItems);
        committed.classList.remove('d-none');
    }
    async function runImport(continuing) {
        if (busy || !jobTools) return;
        busy = true; commit.disabled = true; resume.disabled = true;
        try {
            const file = selectedFile();
            const mappingJson = chosenMapping();
            const fp = await jobTools.fingerprint(file, mappingJson);
            const saved = await matchingCheckpoint();
            if (continuing && (!saved || saved.totalRows < 1 || saved.totalRows > 5000))
                throw new Error('No matching unfinished import found.');
            if (!continuing && (!validated || validated.invalidRows !== 0 ||
                validated.totalRows < 1 || validated.totalRows > 5000))
                throw new Error('Complete a valid dry-run before starting.');
            const totalRows = continuing ? saved.totalRows : validated.totalRows;
            const status = await getJobStatus(fp, totalRows);
            showJobStatus(fp, status);
            if (status.isComplete) { message('This file is fully reconciled.', false); return; }
            if (!confirm('Commit or resume ' + totalRows + ' rows in 100-row atomic transactions?')) return;
            const completed = new Set((status.batches || []).map(x => x.rowOffset));
            let committedRows = status.committedRows;
            const state = { ...fp, totalRows, offset: status.nextOffset, lastReference: null };
            localStorage.setItem(checkpointKey, JSON.stringify(state));
            for (let offset = 0; offset < totalRows; offset += 100) {
                if (completed.has(offset)) continue;
                progress.textContent = 'Committed ' + committedRows + ' of ' + totalRows + ' rows.';
                const data = new FormData();
                data.append('file', file);
                data.append('mappingJson', mappingJson);
                data.append('rowOffset', String(offset));
                data.append('clientRequestId',
                    await jobTools.requestId(fp.fileHash, fp.mappingHash, offset));
                const result = await send('/api/student-imports/execute', data);
                if (result.importedRows !== Math.min(100, totalRows - offset))
                    throw new Error('Import row count mismatch; stopped safely.');
                committedRows += result.importedRows;
                state.offset = offset + result.importedRows;
                state.lastReference = result.importReference;
                localStorage.setItem(checkpointKey, JSON.stringify(state));
                importReference = result.importReference;
            }
            const finalStatus = await getJobStatus(fp, totalRows);
            showJobStatus(fp, finalStatus);
            if (!finalStatus.isComplete) throw new Error('Some source windows remain unreconciled.');
            progress.textContent = 'Committed ' + finalStatus.committedRows + ' of ' + totalRows + ' rows.';
            reconciliationPage = 1;
            await loadReconciliation();
            commit.classList.add('d-none'); resume.classList.add('d-none');
            message('All source windows committed and reconciled.', false);
        } catch (error) {
            message(error.message + ' Re-select the same file and mapping to resume.', true);
            await refreshResume().catch(() => {});
        } finally { busy = false; commit.disabled = false; resume.disabled = false; }
    }
    commit.addEventListener('click', () => runImport(false));
    resume.addEventListener('click', () => runImport(true));
    reload.addEventListener('click', async () => {
        reconciliationPage = 1;
        try { await loadReconciliation(); } catch (error) { message(error.message, true); }
    });
    more.addEventListener('click', async () => {
        reconciliationPage++;
        try { await loadReconciliation(true); }
        catch (error) { reconciliationPage--; message(error.message, true); }
    });

    jobExport.addEventListener('click', async () => {
        if (busy || !currentJob || !currentFingerprint) return;
        busy = true; jobExport.disabled = true;
        try {
            const status = await getJobStatus(currentFingerprint, currentJob.totalRows);
            const escape = value => {
                let s = String(value ?? '');
                if (/^[\s\x00-\x1f]*[=+\-@]/.test(s)) s = "'" + s;
                return '"' + s.replaceAll('"', '""') + '"';
            };
            const lines = ['RowNumber,SourceKey,Status'];
            for (const batch of status.batches || []) {
                const response = await fetch('/api/student-imports/' +
                    encodeURIComponent(batch.importReference) + '?page=1&pageSize=100',
                    { credentials: 'same-origin', cache: 'no-store' });
                const body = await response.json().catch(() => null);
                if (!response.ok || !body?.success || body.data?.totalItems !== batch.importedRows)
                    throw new Error('A batch changed during reconciliation. Export stopped.');
                for (const row of body.data.items || [])
                    lines.push([row.rowNumber, row.sourceKey,
                        row.isSuccess ? 'Imported' : (row.errorMessage || 'Failed')].map(escape).join(','));
            }
            const url = URL.createObjectURL(new Blob([lines.join('\r\n')],
                { type: 'text/csv;charset=utf-8' }));
            const a = document.createElement('a');
            a.href = url; a.download = 'student-import-reconciliation.csv'; a.click();
            URL.revokeObjectURL(url);
            showJobStatus(currentFingerprint, status);
        } catch (error) { message(error.message, true); }
        finally { busy = false; jobExport.disabled = false; }
    });
    download.addEventListener('click', () => {
        const escape = value => {
            let s = String(value ?? '');
            if (/^[\s\x00-\x1f]*[=+\-@]/.test(s)) s = "'" + s;
            return '"' + s.replaceAll('"', '""') + '"';
        };
        const lines = ['RowNumber,SourceKey,StudentCode,Issues', ...errors.map(row =>
            [row.rowNumber, row.sourceKey, row.studentCode, (row.issues || []).join('; ')].map(escape).join(','))];
        const url = URL.createObjectURL(new Blob([lines.join('\r\n')], { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a');
        a.href = url; a.download = 'student-import-preview-errors.csv'; a.click();
        URL.revokeObjectURL(url);
    });
})();
