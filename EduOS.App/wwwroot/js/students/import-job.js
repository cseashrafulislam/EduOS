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
        if (!Number.isSafeInteger(total) || total < 1)
            throw new Error('Inspect and preview the source file before resuming.');
        if (checkpoint && preview && checkpoint.totalRows !== preview.totalRows)
            throw new Error('Import row count changed. Re-select and preview the original file.');
        return total;
    }
    function validateCohort(files) {
        if (!Array.isArray(files) || files.length < 1)
            throw new Error('Select at least one source file.');
        const seen = new Set();
        let rows = 0;
        const normalized = files.map(file => {
            if (!file || !/^[a-f0-9]{64}$/i.test(file.fileHash || '') ||
                !/^[a-f0-9]{64}$/i.test(file.mappingHash || '') ||
                !Number.isSafeInteger(file.totalRows) || file.totalRows < 1)
                throw new Error('Invalid source fingerprint or row count.');
            const key = file.fileHash.toLowerCase() + ':' + file.mappingHash.toLowerCase();
            if (seen.has(key)) throw new Error('This source file and mapping is already selected.');
            seen.add(key);
            rows += file.totalRows;
            if (!Number.isSafeInteger(rows)) throw new Error('Source-row total exceeds integer precision.');
            return { fileHash: file.fileHash.toLowerCase(),
                mappingHash: file.mappingHash.toLowerCase(), totalRows: file.totalRows };
        });
        return normalized;
    }
    function cohortSummaryCsv(status) {
        const lines = ['FileHash,MappingHash,ExpectedRows,CommittedRows,LogItemsVerifiedRows,LogItemReviewWindows,MissingWindows,Status'];
        for (const file of status.files || []) {
            if (!/^[a-f0-9]{64}$/.test(file.fileHash) ||
                !/^[a-f0-9]{64}$/.test(file.mappingHash) ||
                ![file.totalRows, file.committedRows, file.logItemsVerifiedRows,
                    file.logItemReviewWindows, file.missingWindows].every(Number.isSafeInteger) ||
                file.logItemsVerifiedRows < 0 || file.logItemsVerifiedRows > file.committedRows ||
                file.logItemReviewWindows < 0)
                throw new Error('Invalid reconciliation response.');
            lines.push([file.fileHash, file.mappingHash, file.totalRows,
                file.committedRows, file.logItemsVerifiedRows, file.logItemReviewWindows,
                file.missingWindows,
                file.isLogItemReconciled ? 'LogItemsVerified' :
                    file.isComplete ? 'Review' : 'Pending'].join(','));
        }
        return '\uFEFF' + lines.join('\r\n');
    }
    function requireVerifiedRecovery(status) {
        if (!status || !Number.isSafeInteger(status.reviewWindows) ||
            status.reviewWindows < 0 ||
            status.requiresReview !== (status.reviewWindows > 0) ||
            !Array.isArray(status.batches))
            throw new Error('Invalid student import recovery response.');
        if (status.requiresReview)
            throw new Error('Committed import audit rows require review. Resume and export are paused.');
        return status;
    }
    function combineStatusPages(pages, totalRows) {
        if (!Array.isArray(pages) || pages.length < 1 ||
            !Number.isSafeInteger(totalRows) || totalRows < 1)
            throw new Error('Invalid recovery page collection.');
        let offset = 0, committedRows = 0, reviewWindows = 0, nextOffset = totalRows;
        const batches = [];
        const seen = new Set();
        for (const page of pages) {
            if (!page || page.totalRows !== totalRows || page.pageOffset !== offset ||
                !Number.isSafeInteger(page.nextPageOffset) || page.nextPageOffset <= offset ||
                page.nextPageOffset > totalRows ||
                !Number.isSafeInteger(page.committedRows) || page.committedRows < 0 ||
                page.committedRows > page.nextPageOffset - offset ||
                !Number.isSafeInteger(page.reviewWindows) || page.reviewWindows < 0 ||
                !Number.isSafeInteger(page.nextOffset) || page.nextOffset < offset ||
                page.nextOffset > totalRows || !Array.isArray(page.batches))
                throw new Error('Invalid or discontinuous recovery page.');
            for (const batch of page.batches) {
                if (!Number.isSafeInteger(batch.rowOffset) || batch.rowOffset < offset ||
                    batch.rowOffset >= page.nextPageOffset || batch.rowOffset % 100 !== 0 ||
                    seen.has(batch.rowOffset))
                    throw new Error('Duplicate or invalid recovery window.');
                seen.add(batch.rowOffset);
                batches.push(batch);
            }
            committedRows += page.committedRows;
            reviewWindows += page.reviewWindows;
            nextOffset = Math.min(nextOffset, page.nextOffset);
            offset = page.nextPageOffset;
        }
        if (offset !== totalRows || !Number.isSafeInteger(committedRows))
            throw new Error('Recovery pages are incomplete.');
        const isComplete = committedRows === totalRows;
        return { totalRows, committedRows, reviewWindows, nextOffset, batches,
            requiresReview: reviewWindows > 0, isComplete,
            isLogItemReconciled: isComplete && reviewWindows === 0 };
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
    const api = { fingerprint, requestId, resumeRowCount, validateCohort, cohortSummaryCsv,
        requireVerifiedRecovery, combineStatusPages, integrityCsv };
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    else root.EduOSStudentImportJob = api;
})(typeof window !== 'undefined' ? window : globalThis);
