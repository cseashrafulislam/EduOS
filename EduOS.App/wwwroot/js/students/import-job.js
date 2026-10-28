(function (root) {
    'use strict';
    const toHex = bytes => Array.from(new Uint8Array(bytes), b => b.toString(16).padStart(2, '0')).join('');
    async function fingerprint(file, mappingJson, cryptoApi) {
        const cryptoProvider = cryptoApi || root.crypto;
        const fileHash = toHex(await cryptoProvider.subtle.digest('SHA-256', await file.arrayBuffer()));
        const mappingHash = toHex(await cryptoProvider.subtle.digest('SHA-256', new TextEncoder().encode(mappingJson)));
        return { fileHash, mappingHash };
    }
    async function requestId(fileHash, mappingHash, offset, cryptoApi) {
        if (!Number.isInteger(offset) || offset < 0 || offset % 100 !== 0)
            throw new Error('Invalid import window offset.');
        const cryptoProvider = cryptoApi || root.crypto;
        const digest = new Uint8Array(await cryptoProvider.subtle.digest('SHA-256',
            new TextEncoder().encode(fileHash + ':' + mappingHash + ':' + offset))).slice(0, 16);
        digest[6] = (digest[6] & 0x0f) | 0x50;
        digest[8] = (digest[8] & 0x3f) | 0x80;
        const hex = toHex(digest);
        return hex.slice(0, 8) + '-' + hex.slice(8, 12) + '-' + hex.slice(12, 16) +
            '-' + hex.slice(16, 20) + '-' + hex.slice(20);
    }
    function resumeRowCount(checkpoint, preview) {
        const total = checkpoint?.totalRows ?? preview?.totalRows;
        if (!Number.isInteger(total) || total < 1 || total > 5000)
            throw new Error('Inspect and preview the source file before resuming.');
        if (checkpoint && preview && checkpoint.totalRows !== preview.totalRows)
            throw new Error('Import row count changed. Re-select and preview the original file.');
        return total;
    }
    function validateCohort(files) {
        if (!Array.isArray(files) || files.length < 1 || files.length > 8)
            throw new Error('Select between one and eight source files.');
        const seen = new Set();
        let rows = 0;
        const normalized = files.map(file => {
            if (!file || !/^[a-f0-9]{64}$/i.test(file.fileHash || '') ||
                !/^[a-f0-9]{64}$/i.test(file.mappingHash || '') ||
                !Number.isInteger(file.totalRows) || file.totalRows < 1 || file.totalRows > 5000)
                throw new Error('Invalid source fingerprint or row count.');
            const key = file.fileHash.toLowerCase() + ':' + file.mappingHash.toLowerCase();
            if (seen.has(key)) throw new Error('This source file and mapping is already selected.');
            seen.add(key);
            rows += file.totalRows;
            return { fileHash: file.fileHash.toLowerCase(),
                mappingHash: file.mappingHash.toLowerCase(), totalRows: file.totalRows };
        });
        if (rows > 40000) throw new Error('Cohort is limited to 40,000 source rows.');
        return normalized;
    }
    function cohortSummaryCsv(status) {
        const lines = ['FileHash,MappingHash,ExpectedRows,CommittedRows,MissingWindows,Status'];
        for (const file of status.files || []) {
            if (!/^[a-f0-9]{64}$/.test(file.fileHash) ||
                !/^[a-f0-9]{64}$/.test(file.mappingHash) ||
                ![file.totalRows, file.committedRows, file.missingWindows].every(Number.isSafeInteger))
                throw new Error('Invalid reconciliation response.');
            lines.push([file.fileHash, file.mappingHash, file.totalRows,
                file.committedRows, file.missingWindows,
                file.isComplete ? 'Complete' : 'Pending'].join(','));
        }
        return '\uFEFF' + lines.join('\r\n');
    }
    function integrityCsv(report) {
        if (!report || !Number.isSafeInteger(report.expectedRows) ||
            report.expectedRows < 1 || report.expectedRows > 100 ||
            !Number.isSafeInteger(report.checkedRows) || report.checkedRows < 0 ||
            report.checkedRows > 100 || !Array.isArray(report.issues) ||
            report.issues.length > 2001)
            throw new Error('Invalid import integrity report.');
        const cell = value => {
            let s = String(value ?? '');
            if (/^[\s\x00-\x1f]*[=+\-@]/.test(s)) s = "'" + s;
            return '"' + s.replaceAll('"', '""') + '"';
        };
        const lines = ['RowNumber,IssueCode,Details'];
        for (const issue of report.issues) {
            if (!Number.isSafeInteger(issue.rowNumber) || issue.rowNumber < 0 ||
                typeof issue.code !== 'string' || issue.code.length > 80 ||
                typeof issue.message !== 'string' || issue.message.length > 250)
                throw new Error('Invalid integrity issue.');
            lines.push([issue.rowNumber, issue.code, issue.message].map(cell).join(','));
        }
        return '\uFEFF' + lines.join('\r\n');
    }
    const api = { fingerprint, requestId, resumeRowCount, validateCohort, cohortSummaryCsv, integrityCsv };
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    else root.EduOSStudentImportJob = api;
})(typeof window !== 'undefined' ? window : globalThis);
