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
    const api = { fingerprint, requestId, resumeRowCount };
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    else root.EduOSStudentImportJob = api;
})(typeof window !== 'undefined' ? window : globalThis);
