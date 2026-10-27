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

    // Persist only a SHA-256 fingerprint and an opaque request ID, never applicant data.
    // Session storage survives a reload in this tab but not an unrelated browser session.
    function createPersistentTracker(createId, storageProvider, hashPayload, namespace, now = Date.now) {
        if (typeof createId !== 'function' || typeof hashPayload !== 'function')
            throw new TypeError('An ID factory and secure digest are required.');
        const files = createTracker(createId);
        const entries = new Map();
        const prefix = 'eduos:admission:retry:' + encodeURIComponent(namespace);
        const touched = new Set(['application']);
        const storage = () => {
            try { return typeof storageProvider === 'function' ? storageProvider() : storageProvider; }
            catch { return null; }
        };
        return {
            async forPayload(key, payload) {
                const raw = JSON.stringify(payload);
                let fingerprint;
                try { fingerprint = await hashPayload(raw); }
                catch { return files.forPayload(key, payload); }
                if (typeof fingerprint !== 'string' || !fingerprint)
                    return files.forPayload(key, payload);
                touched.add(key);
                const previous = entries.get(key);
                if (previous?.fingerprint === fingerprint) return previous.id;
                const store = storage();
                const storageKey = prefix + ':' + key;
                let saved = null;
                try { saved = JSON.parse(store?.getItem(storageKey) || 'null'); } catch { /* unavailable */ }
                const id = saved?.fingerprint === fingerprint &&
                    typeof saved.id === 'string' && saved.expiresAt > now()
                    ? saved.id : createId();
                entries.set(key, { fingerprint, id });
                try {
                    store?.setItem(storageKey, JSON.stringify({
                        fingerprint, id, expiresAt: now() + 24 * 60 * 60 * 1000
                    }));
                } catch { /* Memory-only fallback. */ }
                return id;
            },
            forFile(key, file) { return files.forFile(key, file); },
            clear(key) {
                files.clear(key);
                entries.delete(key);
                try { storage()?.removeItem(prefix + ':' + key); } catch { }
            },
            clearAll() {
                files.clearAll();
                entries.clear();
                for (const key of touched) {
                    try { storage()?.removeItem(prefix + ':' + key); } catch { }
                }
            }
        };
    }
    const api = { createTracker, createPersistentTracker };
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    if (root) root.EduOSAdmissionRetry = api;
})(typeof globalThis !== 'undefined' ? globalThis : null);
