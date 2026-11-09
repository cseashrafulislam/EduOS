/* Safe, local-only global page navigation/field locator. No speech provider, AI
 * requests, values, transaction calls, persistence or implicit form submission. */
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (root && root.document) api.mount(root.document, root);
})(typeof window !== 'undefined' ? window : null, function () {
    'use strict';

    function normalizeLabel(label) {
        return String(label || '').normalize('NFKC').toLocaleLowerCase()
            .replace(/[\u200b-\u200d\ufeff]/g, '').replace(/[\s*ঃ:।.]+$/g, '')
            .replace(/\s+/g, ' ').trim();
    }

    function matchLabel(entries, requested) {
        const query = normalizeLabel(requested);
        if (query.length < 2 || query.length > 120) return { kind: 'invalid' };
        const exact = entries.filter(entry => entry.labels.some(label => normalizeLabel(label) === query));
        if (exact.length === 1) return { kind: 'match', entry: exact[0] };
        if (exact.length > 1) return { kind: 'ambiguous', count: exact.length };
        if (query.length < 3) return { kind: 'missing' };
        const partial = entries.filter(entry => entry.labels.some(label => normalizeLabel(label).includes(query)));
        if (partial.length === 1) return { kind: 'match', entry: partial[0] };
        return partial.length ? { kind: 'ambiguous', count: partial.length } : { kind: 'missing' };
    }

    function isAvailableField(field) {
        if (!field || field.disabled || field.type === 'hidden' || field.readOnly ||
            field.closest('[hidden], [inert], [aria-hidden="true"], .d-none')) return false;
        if (typeof field.getClientRects === 'function' && field.getClientRects().length === 0) return false;
        return true;
    }

    function fieldsOnPage(doc) {
        const main = doc.getElementById('main-content');
        if (!main) return [];
        return [...main.querySelectorAll('input,select,textarea')]
            .filter(isAvailableField).map(field => {
                const labels = [...(field.labels || [])].map(item => item.textContent || '');
                const aria = field.getAttribute('aria-label');
                if (aria) labels.push(aria);
                return { node: field, labels: labels.filter(Boolean) };
            }).filter(entry => entry.labels.length > 0);
    }

    function linksInSidebar(doc) {
        const sidebar = doc.getElementById('sidebarMenu');
        if (!sidebar) return [];
        return [...sidebar.querySelectorAll('nav a.nav-link[href]')].filter(link => {
            const href = link.getAttribute('href') || '';
            return href.startsWith('/') && !href.startsWith('//') && !link.closest('[hidden], [inert]');
        }).map(link => ({ node: link, labels: [link.textContent || ''] }));
    }

    function mount(doc, hostWindow) {
        const dialog = doc.getElementById('pageAssistantDialog');
        const open = doc.getElementById('pageAssistantOpen');
        const close = doc.getElementById('pageAssistantClose');
        const query = doc.getElementById('pageAssistantQuery');
        const find = doc.getElementById('pageAssistantFind');
        const result = doc.getElementById('pageAssistantResult');
        if (!dialog || !open || !close || !query || !find || !result) return;
        const bn = (doc.documentElement.lang || '').toLowerCase().startsWith('bn');
        const t = (en, bd) => bn ? bd : en;
        const tenant = doc.body?.dataset.authTenantId || '';
        const session = doc.body?.dataset.authSessionReference || '';
        const currentContext = () => (doc.body?.dataset.authTenantId || '') === tenant &&
            (doc.body?.dataset.authSessionReference || '') === session;
        let pending = null;
        function reset() { pending = null; result.replaceChildren(); }
        function message(value) { const p = doc.createElement('p'); p.textContent = value; result.append(p); }
        function action(label, handler) {
            const button = doc.createElement('button');
            button.type = 'button';
            button.className = 'btn btn-outline-primary btn-sm';
            button.textContent = label;
            button.addEventListener('click', handler);
            result.append(button);
        }
        function lookup() {
            reset();
            if (!currentContext()) { message(t('Your session changed. Reload the page.', 'সেশন পরিবর্তিত হয়েছে। পেজটি রিফ্রেশ করুন।')); return; }
            const requested = query.value.trim();
            const field = matchLabel(fieldsOnPage(doc), requested);
            const nav = matchLabel(linksInSidebar(doc), requested);
            if (field.kind === 'ambiguous' || nav.kind === 'ambiguous' ||
                (field.kind === 'match' && nav.kind === 'match')) {
                message(t('More than one item matches. Enter its full visible label.', 'একাধিক মিল পাওয়া গেছে। সম্পূর্ণ লেবেল লিখুন।'));
                return;
            }
            if (field.kind === 'match') {
                const target = field.entry.node;
                pending = target;
                message(t(`Field found: ${field.entry.labels[0]}. Review before focusing.`,
                    `ঘর পাওয়া গেছে: ${field.entry.labels[0]}। যাচাই করে যান।`));
                action(t('Focus field', 'ঘরে যান'), () => {
                    if (!currentContext() || pending !== target || !isAvailableField(target) || !target.isConnected) {
                        reset(); message(t('Field or session changed. Search again.', 'ঘর বা সেশন বদলেছে। আবার খুঁজুন।')); return;
                    }
                    dialog.close();
                    target.scrollIntoView({ behavior: 'smooth', block: 'center' });
                    target.focus();
                });
                return;
            }
            if (nav.kind === 'match') {
                const link = nav.entry.node;
                const href = link.getAttribute('href');
                pending = link;
                message(t(`Menu found: ${nav.entry.labels[0]}. Review before opening.`,
                    `মেনু পাওয়া গেছে: ${nav.entry.labels[0]}। যাচাই করে খুলুন।`));
                action(t('Open page', 'পেজ খুলুন'), () => {
                    if (!currentContext() || pending !== link || !link.isConnected ||
                        link.getAttribute('href') !== href || !href.startsWith('/') || href.startsWith('//')) {
                        reset(); message(t('Menu or session changed. Search again.', 'মেনু বা সেশন বদলেছে। আবার খুঁজুন।')); return;
                    }
                    hostWindow.location.assign(href);
                });
                return;
            }
            message(t('No unique visible field or authorized menu matched. Try its full label.',
                'একটি নির্দিষ্ট ঘর বা অনুমোদিত মেনু পাওয়া যায়নি। সম্পূর্ণ নাম লিখুন।'));
        }
        open.addEventListener('click', () => {
            if (!currentContext()) return;
            reset(); query.value = '';
            dialog.showModal(); query.focus();
        });
        close.addEventListener('click', () => dialog.close());
        dialog.addEventListener('close', () => { reset(); open.focus(); });
        find.addEventListener('click', lookup);
        query.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); lookup(); } });
        query.addEventListener('input', reset);
        hostWindow.addEventListener('pageshow', () => { if (!currentContext() && dialog.open) dialog.close(); });
    }

    return { normalizeLabel, matchLabel, isAvailableField, mount };
});
