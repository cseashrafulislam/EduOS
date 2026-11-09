/* Load the large bilingual page-manual catalogue only after its Info button is
 * requested. The server fingerprints the same-origin asset for safe caching.
 * The manual remains the sole dialog/session/authorization implementation. */
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (root && root.document) api.mount(root.document, root);
})(typeof window !== 'undefined' ? window : null, function () {
    'use strict';

    function approvedManualUrl(path, base) {
        if (!path || !base) return null;
        try {
            const url = new URL(path, base);
            const origin = new URL(base).origin;
            if (url.origin !== origin || !url.pathname.endsWith('/js/platform/page-context-help.js') ||
                url.hash || [...url.searchParams.keys()].some(key => key !== 'v')) return null;
            return url.href;
        } catch { return null; }
    }

    function mount(doc, host) {
        const trigger = doc.getElementById('pageContextHelp');
        const status = doc.getElementById('pageHelpLoadStatus');
        if (!trigger || !status) return;
        const scriptUrl = approvedManualUrl(trigger.dataset.manualSrc, host.location.href);
        const tenant = doc.body?.dataset.authTenantId || '';
        const session = doc.body?.dataset.authSessionReference || '';
        let loading = false;
        let loaded = false;
        const validContext = () => (doc.body?.dataset.authTenantId || '') === tenant &&
            (doc.body?.dataset.authSessionReference || '') === session;
        const bn = () => (doc.documentElement.lang || '').toLowerCase().startsWith('bn');
        function announce(en, bd) { status.textContent = bn() ? bd : en; }
        function requested(event) {
            if (loaded) return;
            event.preventDefault();
            event.stopImmediatePropagation();
            if (!scriptUrl || !validContext()) {
                announce('Reload the page to open Help.', 'নির্দেশিকা খুলতে পৃষ্ঠা রিফ্রেশ করুন।');
                return;
            }
            if (loading) return;
            loading = true;
            trigger.setAttribute('aria-busy', 'true');
            announce('Loading page guide…', 'পৃষ্ঠার নির্দেশিকা লোড হচ্ছে…');
            const script = doc.createElement('script');
            script.src = scriptUrl;
            script.async = true;
            script.onload = () => {
                loading = false;
                trigger.removeAttribute('aria-busy');
                if (!validContext()) {
                    announce('Session changed. Reload the page.', 'সেশন বদলেছে। পৃষ্ঠা রিফ্রেশ করুন।');
                    return;
                }
                loaded = true;
                trigger.removeEventListener('click', requested, true);
                status.textContent = '';
                trigger.click(); // The reviewed manual validates session again before display.
            };
            script.onerror = () => {
                script.remove();
                loading = false;
                trigger.removeAttribute('aria-busy');
                announce('Could not load Help. Select Info to retry.',
                    'নির্দেশিকা লোড হয়নি। আবার Info চাপুন।');
            };
            doc.head.append(script);
        }
        trigger.addEventListener('click', requested, true);
    }

    return { approvedManualUrl, mount };
});
