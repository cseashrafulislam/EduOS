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
    const committed = document.getElementById('studentImportCommitted');
    const committedSummary = document.getElementById('studentImportCommittedSummary');
    const committedRows = document.getElementById('studentImportCommittedRows');
    const reload = document.getElementById('studentImportReload');
    const more = document.getElementById('studentImportMore');
    let requestId = crypto.randomUUID();
    let validated = null;
    let lastMapping = null;
    let importReference = null;
    let reconciliationPage = 1;
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
        validated = null;
        lastMapping = null;
        requestId = crypto.randomUUID();
        commit.classList.add('d-none');
        committed.classList.add('d-none');
        alert.classList.add('d-none');
    }
    fileInput.addEventListener('change', reset);
    mapping.addEventListener('change', () => {
        validated = null;
        lastMapping = null;
        commit.classList.add('d-none');
        results.classList.add('d-none');
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
            message('Columns inspected. Review each mapping before validation.', false);
        } catch (error) { message(error.message, true); }
        finally { busy = false; inspect.disabled = false; }
    });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (busy || mapping.classList.contains('d-none')) return;
        busy = true; preview.disabled = true;
        try {
            const chosen = {};
            for (const select of fields) {
                if (select.value) chosen[select.dataset.importField] = select.value;
                else if (required.has(select.dataset.importField)) throw new Error('Required mapping missing: ' + select.dataset.importField);
            }
            if (new Set(Object.values(chosen)).size !== Object.keys(chosen).length)
                throw new Error('Each field must use a different source column.');
            const data = new FormData();
            data.append('file', selectedFile());
            data.append('mappingJson', JSON.stringify(chosen));
            const report = await send('/api/student-imports/preview', data);
            lastMapping = JSON.stringify(chosen);
            validated = report;
            requestId = crypto.randomUUID();
            commit.classList.toggle('d-none', report.invalidRows !== 0 || report.validRows < 1 || report.validRows > 100);
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
    commit.addEventListener('click', async () => {
        if (busy || !validated || validated.invalidRows !== 0 || validated.validRows > 100 || !lastMapping) return;
        if (!confirm('Commit ' + validated.validRows + ' student records and their enrollment/guardian links? This cannot be undone by a normal delete.')) return;
        busy = true; commit.disabled = true;
        try {
            const data = new FormData();
            data.append('file', selectedFile());
            data.append('mappingJson', lastMapping);
            data.append('clientRequestId', requestId);
            const result = await send('/api/student-imports/execute', data);
            importReference = result.importReference;
            reconciliationPage = 1;
            await loadReconciliation();
            commit.classList.add('d-none');
            message(result.replayed ? 'Previously committed batch recovered.' : 'Batch committed successfully.', false);
        } catch (error) { message(error.message, true); }
        finally { busy = false; commit.disabled = false; }
    });
    reload.addEventListener('click', async () => {
        reconciliationPage = 1;
        try { await loadReconciliation(); } catch (error) { message(error.message, true); }
    });
    more.addEventListener('click', async () => {
        reconciliationPage++;
        try { await loadReconciliation(true); }
        catch (error) { reconciliationPage--; message(error.message, true); }
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
