(function (root) {
    'use strict';
    function createTracker(createId) {
        if (typeof createId !== 'function') throw new TypeError('An ID factory is required.');
        const entries = new Map();
        return {
            forPayload(key, payload) {
                const fingerprint = JSON.stringify(payload);
                const previous = entries.get(key);
                if (previous?.kind === 'payload' && previous.fingerprint === fingerprint) return previous.id;
                const id = createId();
                entries.set(key, { kind: 'payload', fingerprint, id });
                return id;
            },
            forFile(key, file) {
                if (!file) throw new TypeError('A file is required.');
                const previous = entries.get(key);
                if (previous?.kind === 'file' && previous.file === file) return previous.id;
                const id = createId();
                entries.set(key, { kind: 'file', file, id });
                return id;
            },
            clear(key) { entries.delete(key); },
            clearAll() { entries.clear(); }
        };
    }
    const api = { createTracker };
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    if (root) root.EduOSAdmissionRetry = api;
})(typeof globalThis !== 'undefined' ? globalThis : null);
