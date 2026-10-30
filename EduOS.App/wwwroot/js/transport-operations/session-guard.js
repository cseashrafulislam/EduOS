(() => {
    'use strict';
    // Shared by dispatcher, assigned driver and family views. The server remains
    // authoritative for every permission; this prevents stale private DOM/exports.
    function create(onInvalidate) {
        const body = document.body;
        const session = body?.dataset.authSessionReference || '';
        const tenant = body?.dataset.authTenantId || '';
        const active = new Set();
        let invalidated = false;
        const matches = () => /^[0-9A-F]{64}$/i.test(session) && !!tenant &&
            body?.dataset.authSessionReference === session &&
            body?.dataset.authTenantId === tenant &&
            window.EduOSSessionTransitionPending?.() !== true;
        function invalidate() {
            if (invalidated) return;
            invalidated = true;
            for (const controller of active) controller.abort();
            active.clear();
            onInvalidate();
        }
        function valid() {
            if (!matches()) invalidate();
            return !invalidated;
        }
        function assert() {
            if (!valid()) throw Error('Session changed / সেশন পরিবর্তিত হয়েছে। আবার লগইন করুন।');
        }
        // Keep the abort controller active through response body consumption.
        async function request(url, options, consume) {
            assert();
            const controller = new AbortController();
            active.add(controller);
            try {
                const response = await fetch(url, {
                    ...options, credentials: 'same-origin', cache: 'no-store',
                    signal: controller.signal
                });
                assert();
                const value = await consume(response);
                assert();
                return value;
            } finally { active.delete(controller); }
        }
        const observer = new MutationObserver(() => { valid(); });
        observer.observe(body, { attributes: true,
            attributeFilter: ['data-auth-session-reference', 'data-auth-tenant-id'] });
        window.addEventListener('pagehide', invalidate);
        window.addEventListener('focus', valid);
        document.addEventListener('visibilitychange', valid);
        window.addEventListener('pageshow', event => {
            if (event.persisted) invalidate(); else valid();
        });
        if (!valid()) invalidate();
        return Object.freeze({ valid, assert, request });
    }
    window.EduOSTransportSession = Object.freeze({ create });
})();
