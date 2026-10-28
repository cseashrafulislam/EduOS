(() => {
    'use strict';
    // This is a UX/privacy shield, not the authorization boundary. The server
    // enforces tenant membership and session-bound antiforgery on every write.
    const original = document.body?.dataset.authSessionReference;
    const shield = document.getElementById('eduosSessionShield');
    const message = document.getElementById('eduosSessionShieldMessage');
    const retry = document.getElementById('eduosSessionRetry');
    if (!/^[0-9A-F]{64}$/.test(original || '') || !shield || !message || !retry) return;

    const bangla = document.documentElement.lang === 'bn-BD';
    const shell = document.querySelector('.app-shell');
    let channel = null;
    let pending = null;
    let lastChecked = 0;

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
    function check(force = false) {
        if (pending) return pending;
        if (!force && Date.now() - lastChecked < 5000) return Promise.resolve();
        block();
        pending = (async () => {
            try {
                const response = await fetch('/api/auth/session-context', {
                    credentials: 'same-origin', cache: 'no-store',
                    headers: { Accept: 'application/json' }
                });
                if (response.status === 401 || response.status === 403) {
                    window.location.replace('/Account/Login');
                    return;
                }
                if (!response.ok) throw new Error('Session check unavailable');
                const payload = await response.json();
                const current = payload?.data?.sessionReference;
                if (payload?.success !== true || !/^[0-9A-F]{64}$/.test(current || ''))
                    throw new Error('Invalid session response');
                if (current !== original) {
                    // Never reuse an old page/form against the new institution cookie.
                    window.location.replace('/Dashboard/Index');
                    return;
                }
                unblock();
            } catch {
                // Fail closed: do not display the previous institution's data offline.
                failed();
            } finally {
                lastChecked = Date.now();
                pending = null;
            }
        })();
        return pending;
    }

    retry.addEventListener('click', () => check(true));
    window.addEventListener('pageshow', event => {
        if (event.persisted) void check(true);
    });
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') void check(true);
    });
    window.addEventListener('focus', () => { void check(); });

    // Broadcast contains no user ID, tenant ID, SID, or other private data.
    // Never trust the event itself: revalidate against the server before redirect.
    if (typeof BroadcastChannel === 'function') {
        try {
            channel = new BroadcastChannel('eduos-session-context-v1');
            channel.onmessage = event => {
                if (event.data?.type !== 'session-changed') return;
                void check(true);
                // Logout submits before the response clears the cookie.
                window.setTimeout(() => { void check(true); }, 1200);
            };
        } catch { channel = null; }
    }
    window.EduOSNotifySessionChanged = () => {
        try { channel?.postMessage({ type: 'session-changed' }); }
        catch { /* Browser may disallow cross-tab messaging. Focus still rechecks. */ }
    };
})();
