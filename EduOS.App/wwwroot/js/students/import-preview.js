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
        alert.classList.add('d-none');
    }
    fileInput.addEventListener('change', reset);
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
        } catch (error) { results.classList.add('d-none'); message(error.message, true); }
        finally { busy = false; preview.disabled = false; }
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
