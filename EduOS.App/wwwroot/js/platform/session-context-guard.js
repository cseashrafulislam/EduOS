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

    function block() {
        shield.hidden = false;
        shell?.setAttribute('inert', '');
        message.textContent = bangla ? 'আপনার অ্যাকাউন্টের নিরাপত্তা যাচাই হচ্ছে…' :
            'Checking your account session…';
        retry.focus();
    }
    function unblock() {
        shield.hidden = true;
        shell?.removeAttribute('inert');
    }
    function failed() {
        block();
        message.textContent = bangla ?
            'সেশন যাচাই করা যায়নি। সংযোগ ঠিক করে আবার চেষ্টা করুন।' :
            'Unable to verify your session. Reconnect and retry.';
    }
    function check() {
        // Focus/visibility events must never skip verification for a time-based
        // cooldown: a different tab may have switched institutions moments ago.
        // If the cookie changes during an in-flight request, perform another
        // server check before ever unshielding the previous institution page.
        if (pending) {
            recheckRequested = true;
            return pending;
        }
        block();
        const expectedReference = original;
        const controller = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), 10000);
        pending = (async () => {
            try {
                const response = await fetch('/api/auth/session-context', {
                    credentials: 'same-origin', cache: 'no-store',
                    headers: { Accept: 'application/json' }, signal: controller.signal
                });
                if (response.status === 401 || response.status === 403) {
                    window.location.replace('/Account/Login');
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
                    return;
                }
                if (current !== original || currentTenant !== originalTenant) {
                    // Never reuse an old page/form against the new institution cookie.
                    window.location.replace('/Dashboard/Index');
                    return;
                }
                if (!recheckRequested) unblock();
            } catch {
                // Fail closed: do not display the previous institution's data offline.
                failed();
            } finally {
                window.clearTimeout(timeout);
            }
        })().finally(() => {
            pending = null;
            if (recheckRequested) {
                recheckRequested = false;
                void check();
            }
        });
        return pending;
    }

    // A switch response can be lost after the server commits the new cookie.
    // Reconcile against the server before an uncertain modal can show old data.
    window.EduOSVerifySessionContext = () => check();

    // Only a privileged same-institution operation that has fetched a fresh
    // server-issued token should adopt its new session reference on this page.
    // This is a privacy shield, not a substitute for server authorization.
    window.EduOSAdoptRotatedSession = (reference, tenantId) => {
        const tenant = tenantId == null ? '' : String(tenantId);
        if (!/^[0-9A-F]{64}$/.test(reference || '') || tenant !== originalTenant)
            return false;
        original = reference;
        document.body.dataset.authSessionReference = reference;
        // A pending response may describe the old cookie; verify again before
        // showing this page after adopting a same-institution rotated session.
        if (pending) recheckRequested = true;
        else void check();
        return true;
    };

    retry.addEventListener('click', () => check());
    window.addEventListener('pageshow', event => {
        if (event.persisted) void check();
    });
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') void check();
    });
    window.addEventListener('focus', () => { void check(); });

    // Broadcast contains no user ID, tenant ID, SID, or other private data.
    // Never trust the event itself: revalidate against the server before redirect.
    if (typeof BroadcastChannel === 'function') {
        try {
            channel = new BroadcastChannel('eduos-session-context-v1');
            channel.onmessage = event => {
                if (event.data?.type !== 'session-changed') return;
                void check();
                // Logout submits before the response clears the cookie.
                window.setTimeout(() => { void check(); }, 1200);
            };
        } catch { channel = null; }
    }
    window.EduOSNotifySessionChanged = () => {
        try { channel?.postMessage({ type: 'session-changed' }); }
        catch { /* Browser may disallow cross-tab messaging. Focus still rechecks. */ }
    };
})();
