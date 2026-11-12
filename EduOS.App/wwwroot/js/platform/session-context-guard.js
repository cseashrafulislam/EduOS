(() => {
    'use strict';
    // This is a UX/privacy shield, not the authorization boundary. The server
    // enforces tenant membership and session-bound antiforgery on every write.
    let original = document.body?.dataset.authSessionReference;
    const originalTenant = document.body?.dataset.authTenantId ?? '';
    const shield = document.getElementById('eduosSessionShield');
    const message = document.getElementById('eduosSessionShieldMessage');
    const retry = document.getElementById('eduosSessionRetry');
    if (!/^[0-9A-F]{64}$/.test(original || '') || !shield || !message || !retry) return;

    const bangla = document.documentElement.lang === 'bn-BD';
    const shell = document.querySelector('.app-shell');
    let channel = null;
    let pending = null;
    let recheckRequested = false;
    let recheckQuiet = true;
    let sessionChangePending = false;
    let navigationPending = false;
    let wasHidden = document.visibilityState === 'hidden';
    let lostFocus = false;

    function block(quiet = false) {
        shield.hidden = false;
        // Keep stale tenant data covered while a tab resumes, but do not
        // flash a modal/focus the Retry button on ordinary tab switches.
        shield.classList.toggle('auth-session-shield--quiet', quiet);
        shell?.setAttribute('inert', '');
        if (!quiet) {
            message.textContent = bangla ? 'আপনার অ্যাকাউন্টের নিরাপত্তা যাচাই হচ্ছে…' :
                'Checking your account session…';
            retry.focus();
        }
    }
    function unblock() {
        shield.hidden = true;
        shield.classList.remove('auth-session-shield--quiet');
        shell?.removeAttribute('inert');
    }
    function failed() {
        block();
        message.textContent = bangla ?
            'সেশন যাচাই করা যায়নি। সংযোগ ঠিক করে আবার চেষ্টা করুন।' :
            'Unable to verify your session. Reconnect and retry.';
    }
    function redirectSafely(url) {
        if (navigationPending) return;
        navigationPending = true;
        block();
        window.location.replace(url);
    }
    function check({ quiet = false, forceRecheck = false } = {}) {
        if (navigationPending) return pending || Promise.resolve();
        if (pending) {
            // Explicit operations/session rotations require a fresh snapshot.
            // Duplicated focus + visibility events never create a request loop.
            if (forceRecheck) {
                recheckRequested = true;
                if (!quiet) recheckQuiet = false;
            }
            return pending;
        }
        block(quiet && !sessionChangePending &&
            (shield.hidden || shield.classList.contains('auth-session-shield--quiet')));
        const expectedReference = original;
        pending = (async () => {
            // Retry one transient network/5xx error before displaying recovery UI.
            // Unauthorized sessions and cross-tenant changes never retry or unblock.
            for (let attempt = 0; attempt < 2; attempt++) {
                const controller = new AbortController();
                const timeout = window.setTimeout(() => controller.abort(), 5000);
                try {
                    const response = await fetch('/api/auth/session-context', {
                        credentials: 'same-origin', cache: 'no-store',
                        headers: { Accept: 'application/json' }, signal: controller.signal
                    });
                    if (response.status === 401 || response.status === 403) {
                        redirectSafely('/Account/Login');
                        return;
                    }
                    if (!response.ok) throw new Error('Session check unavailable');
                    const payload = await response.json();
                    const current = payload?.data?.sessionReference;
                    const currentTenant = payload?.data?.tenantId == null ? '' : String(payload.data.tenantId);
                    if (payload?.success !== true || !/^[0-9A-F]{64}$/.test(current || ''))
                        throw new Error('Invalid session response');
                    if (expectedReference !== original) {
                        recheckRequested = true;
                        recheckQuiet = false;
                        return;
                    }
                    if (current !== original || currentTenant !== originalTenant) {
                        // Never reuse an old page/form against the new institution cookie.
                        redirectSafely('/Dashboard/Index');
                        return;
                    }
                    if (sessionChangePending) {
                        message.textContent = bangla ?
                            'অন্য ট্যাবে সেশন পরিবর্তন চলছে। যাচাই করতে আবার চেষ্টা করুন।' :
                            'Another tab is changing your session. Retry to verify access.';
                        return;
                    }
                    if (document.visibilityState !== 'visible') {
                        // The cookie may change again before the tab is reactivated.
                        block(true);
                        return;
                    }
                    if (!recheckRequested) unblock();
                    return;
                } catch {
                    if (navigationPending) return;
                    if (attempt === 0 && window.navigator?.onLine !== false) {
                        await new Promise(resolve => window.setTimeout(resolve, 350));
                        continue;
                    }
                    // Persistent failures remain fail-closed: never expose
                    // previous-tenant private content or enable stale forms.
                    failed();
                } finally {
                    window.clearTimeout(timeout);
                }
            }
        })().finally(() => {
            pending = null;
            if (recheckRequested && !navigationPending) {
                const quietAgain = recheckQuiet && !sessionChangePending;
                recheckRequested = false;
                recheckQuiet = true;
                void check({ quiet: quietAgain });
            }
        });
        return pending;
    }

    // A switch response can be lost after the server commits the new cookie.
    // Reconcile against the server before an uncertain modal can show old data.
    window.EduOSVerifySessionContext = () => check({ forceRecheck: true });
    // Reject institution-specific UI readbacks while cross-tab session changes are pending.
    window.EduOSSessionTransitionPending = () => sessionChangePending || navigationPending;

    // Only a privileged same-institution operation that has fetched a fresh
    // server-issued token should adopt its new session reference on this page.
    // This is a privacy shield, not a substitute for server authorization.
    window.EduOSAdoptRotatedSession = (reference, tenantId) => {
        const tenant = tenantId == null ? '' : String(tenantId);
        if (navigationPending || !/^[0-9A-F]{64}$/.test(reference || '') || tenant !== originalTenant)
            return false;
        original = reference;
        document.body.dataset.authSessionReference = reference;
        // A pending response may describe the old cookie; verify again before
        // showing this page after adopting a same-institution rotated session.
        if (pending) { recheckRequested = true; recheckQuiet = false; }
        else void check();
        return true;
    };

    retry.addEventListener('click', () => {
        if (navigationPending) return;
        // Explicit user retry may resume only after a fresh server check.
        // A broadcast alone can never grant or restore access.
        sessionChangePending = false;
        void check();
    });
    window.addEventListener('pageshow', event => {
        if (event.persisted) {
            block(true);
            void check({ quiet: true, forceRecheck: true });
        }
    });
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden') {
            wasHidden = true;
            block(true);
            return;
        }
        if (!wasHidden) return;
        wasHidden = false;
        lostFocus = false; // Focus immediately after visibility is the same activation.
        void check({ quiet: true, forceRecheck: true });
    });
    window.addEventListener('blur', () => { lostFocus = true; });
    window.addEventListener('focus', () => {
        // Initial focus and ordinary clicks are not session changes. Still
        // verify a real window return even if visibilitychange did not fire.
        if (!lostFocus || wasHidden || document.visibilityState !== 'visible') return;
        lostFocus = false;
        void check({ quiet: true, forceRecheck: true });
    });

    // Broadcast contains no user ID, tenant ID, SID, or other private data.
    // Never trust the event itself: revalidate against the server before redirect.
    if (typeof BroadcastChannel === 'function') {
        try {
            channel = new BroadcastChannel('eduos-session-context-v1');
            channel.onmessage = event => {
                if (event.data?.type !== 'session-changed') return;
                // Shield immediately, even if the first server check still sees
                // the old cookie while logout/institution switching is in flight.
                sessionChangePending = true;
                block();
                void check({ forceRecheck: true });
                // Logout submits before the response clears the cookie.
                window.setTimeout(() => { void check({ forceRecheck: true }); }, 1200);
            };
        } catch { channel = null; }
    }
    window.EduOSNotifySessionChanged = () => {
        try { channel?.postMessage({ type: 'session-changed' }); }
        catch { /* Browser may disallow cross-tab messaging. Focus still rechecks. */ }
    };
})();
