/* Feature discovery is a view over the server-authorized navigation, never a route/permission registry.
 * No tenant records or query strings are cached. Navigation stays server-authorized. */
(() => {
    'use strict';

    function safeDestination(href) {
        if (typeof href !== 'string' || href.length > 240 ||
            !href.startsWith('/') || href.startsWith('//') ||
            /[\\?\u0000-\u001f]/.test(href)) return null;
        try {
            const parsed = new URL(href, 'https://eduos.invalid');
            if (parsed.origin !== 'https://eduos.invalid' || parsed.pathname === '/') return null;
            return parsed.pathname + parsed.hash;
        } catch { return null; }
    }

    function makeCatalog(links) {
        const seen = new Set();
        const catalog = [];
        for (const link of links) {
            const href = safeDestination(link?.href);
            const label = typeof link?.label === 'string' ? link.label.trim().slice(0, 100) : '';
            if (!href || !label || seen.has(href.toLowerCase())) continue;
            seen.add(href.toLowerCase());
            catalog.push({
                href, label,
                section: String(link.section || '').trim().slice(0, 70),
                group: String(link.group || '').trim().slice(0, 70)
            });
        }
        return catalog;
    }

    function filterCatalog(catalog, query, limit = 24) {
        const words = String(query || '').toLocaleLowerCase().trim().split(/\s+/).filter(Boolean).slice(0, 8);
        if (!words.length) return catalog.slice(0, limit);
        return catalog.map(entry => {
            const label = entry.label.toLocaleLowerCase();
            const searchable = [label, entry.section, entry.group].join(' ').toLocaleLowerCase();
            if (!words.every(word => searchable.includes(word))) return null;
            const score = label === words.join(' ') ? 0 : label.startsWith(words[0]) ? 1 : 2;
            return { entry, score };
        }).filter(Boolean).sort((a, b) => a.score - b.score ||
            a.entry.label.localeCompare(b.entry.label)).slice(0, limit).map(x => x.entry);
    }

    function scopedState(saved, tenant, session, catalog) {
        const allowed = new Set(catalog.map(entry => entry.href));
        const matches = saved && saved.version === 1 && tenant && session &&
            saved.tenant === tenant && saved.session === session;
        const clean = list => matches && Array.isArray(list)
            ? [...new Set(list.filter(value => typeof value === 'string' && allowed.has(value)))].slice(0, 8)
            : [];
        return { version: 1, tenant, session, favorites: clean(saved?.favorites), recent: clean(saved?.recent) };
    }

    function remember(list, href) {
        return [href, ...list.filter(value => value !== href)].slice(0, 8);
    }

    // The catalogue is server-authorized at render time. A changed session or
    // tenant must not leave its stale links, favorites or search dialog usable.
    function contextStatus(tenant, session, currentTenant, currentSession, transitioning, shieldVisible) {
        if (tenant !== currentTenant || session !== currentSession || transitioning) return 'changed';
        return shieldVisible ? 'verifying' : 'ready';
    }

    if (typeof module !== 'undefined' && module.exports) {
        module.exports = { safeDestination, makeCatalog, filterCatalog, scopedState, remember, contextStatus };
    }
    if (typeof document === 'undefined') return;

    const sidebar = document.getElementById('sidebarMenu');
    const trigger = document.getElementById('featureSearchOpen');
    const dialog = document.getElementById('featureSearchDialog');
    const input = document.getElementById('featureSearchInput');
    const results = document.getElementById('featureSearchResults');
    const close = document.getElementById('featureSearchClose');
    if (!sidebar || !trigger || !dialog || !input || !results) return;

    const bangla = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const labels = bangla
        ? { favorites: 'পছন্দের ফিচার', recent: 'সাম্প্রতিক', available: 'অনুমোদিত ফিচার',
            empty: 'এই নামে অনুমোদিত কোনো ফিচার পাওয়া যায়নি।',
            star: 'পছন্দের তালিকায় যোগ করুন', unstar: 'পছন্দের তালিকা থেকে সরান' }
        : { favorites: 'Favorites', recent: 'Recent', available: 'Available features',
            empty: 'No matching feature is available for your access.',
            star: 'Add to favorites', unstar: 'Remove from favorites' };
    const translated = bangla ? {
        '/admissions/index': 'ভর্তি', '/students/index': 'শিক্ষার্থী',
        '/attendance/index': 'উপস্থিতি', '/feeoperations/index': 'ফি ও বিলিং',
        '/payrolloperations/index': 'বেতন ব্যবস্থাপনা',
        '/accountingreports/index': 'হিসাব প্রতিবেদন',
        '/assetlifecycle/index': 'স্থায়ী সম্পদ',
        '/workspace': 'ওয়ার্কস্পেস ডিজাইন'
    } : {};

    const rawLinks = [
        ...sidebar.querySelectorAll('nav[data-sidebar-section] a.nav-link'),
        ...document.querySelectorAll('a[data-feature-discovery-link]')
    ];
    const catalog = makeCatalog(rawLinks.map(link => {
        const nav = link.closest('nav[data-sidebar-section]');
        const group = link.closest('.sidebar-group')?.querySelector('.sidebar-group-label')?.textContent || '';
        const path = safeDestination(link.getAttribute('href'));
        return {
            href: link.getAttribute('href'),
            label: translated[path?.toLowerCase()] || link.textContent || link.getAttribute('aria-label'),
            section: nav?.previousElementSibling?.classList.contains('menu-title')
                ? nav.previousElementSibling.textContent : (link.dataset.featureDiscoverySection || ''),
            group
        };
    }));
    if (!catalog.length) { trigger.hidden = true; return; }

    const byHref = new Map(catalog.map(entry => [entry.href, entry]));
    const key = 'eduos.feature-discovery.v1';
    const tenant = document.body.dataset.authTenantId || '';
    const session = document.body.dataset.authSessionReference || '';
    function load() {
        let saved = null;
        try { saved = JSON.parse(sessionStorage.getItem(key) || 'null'); } catch { /* Optional storage. */ }
        return scopedState(saved, tenant, session, catalog);
    }
    let state = load();
    const shield = document.getElementById('eduosSessionShield');
    let invalidated = false;
    function status() {
        if (invalidated) return 'changed';
        return contextStatus(tenant, session,
            document.body.dataset.authTenantId || '',
            document.body.dataset.authSessionReference || '',
            typeof window.EduOSSessionTransitionPending === 'function' &&
                window.EduOSSessionTransitionPending(),
            !!shield && !shield.hidden);
    }
    function currentContext() {
        const current = status();
        if (current === 'ready') return true;
        input.value = '';
        results.replaceChildren();
        if (dialog.open) {
            if (typeof dialog.close === 'function') dialog.close();
            else dialog.removeAttribute('open');
        }
        if (current === 'changed') {
            invalidated = true;
            state = scopedState(null, tenant, session, catalog);
            trigger.disabled = true;
            try { sessionStorage.removeItem(key); } catch { /* Optional storage. */ }
        }
        return false;
    }
    async function verifiedContext() {
        if (!currentContext()) return false;
        if (typeof window.EduOSVerifySessionContext === 'function') {
            try { await window.EduOSVerifySessionContext(); }
            catch { return false; }
        }
        return currentContext();
    }
    function persist() {
        if (!tenant || !session) return;
        try { sessionStorage.setItem(key, JSON.stringify(state)); } catch { /* Optional storage. */ }
    }
    // Only the server-rendered active destination can enter recent history.
    const active = rawLinks.find(link => link.getAttribute('aria-current') === 'page');
    const current = safeDestination(active?.getAttribute('href'));
    if (current && byHref.has(current)) {
        state.recent = remember(state.recent, current);
        persist();
    }

    function row(entry) {
        const item = document.createElement('div');
        item.className = 'feature-search-row';
        item.setAttribute('role', 'listitem');
        const link = document.createElement('a');
        link.className = 'feature-search-link';
        link.href = entry.href;
        const title = document.createElement('strong');
        title.textContent = entry.label;
        const context = document.createElement('small');
        context.textContent = [entry.section, entry.group].filter(Boolean).join(' › ');
        link.append(title, context);
        link.addEventListener('click', async event => {
            event.preventDefault();
            if (!await verifiedContext() || !byHref.has(entry.href)) return;
            state.recent = remember(state.recent, entry.href);
            persist();
            // Preserve normal Back/Refresh history; never navigate before a
            // fresh session check has completed successfully.
            window.location.assign(entry.href);
        });
        const star = document.createElement('button');
        star.type = 'button';
        star.className = 'feature-search-star';
        star.dataset.href = entry.href;
        const selected = state.favorites.includes(entry.href);
        star.textContent = selected ? '★' : '☆';
        star.setAttribute('aria-pressed', String(selected));
        star.setAttribute('aria-label', selected ? labels.unstar + ': ' + entry.label : labels.star + ': ' + entry.label);
        star.addEventListener('click', () => {
            if (!currentContext()) return;
            const restoreFocus = document.activeElement === star;
            state.favorites = selected
                ? state.favorites.filter(href => href !== entry.href)
                : remember(state.favorites, entry.href);
            persist();
            render();
            if (restoreFocus) {
                const replacement = [...results.querySelectorAll('.feature-search-star')]
                    .find(button => button.dataset.href === entry.href);
                (replacement || input).focus();
            }
        });
        item.append(link, star);
        return item;
    }

    function section(title, entries) {
        if (!entries.length) return;
        const heading = document.createElement('h3');
        heading.className = 'feature-search-section-title';
        heading.textContent = title;
        results.append(heading);
        const list = document.createElement('div');
        list.setAttribute('role', 'list');
        for (const entry of entries) list.append(row(entry));
        results.append(list);
    }

    function render() {
        if (!currentContext()) return;
        results.replaceChildren();
        const query = input.value.trim();
        if (query) {
            const matches = filterCatalog(catalog, query);
            section(labels.available, matches);
            if (!matches.length) {
                const empty = document.createElement('p');
                empty.className = 'feature-search-empty';
                empty.textContent = labels.empty;
                results.append(empty);
            }
            return;
        }
        const favorites = state.favorites.map(href => byHref.get(href)).filter(Boolean);
        const recent = state.recent.filter(href => !state.favorites.includes(href))
            .map(href => byHref.get(href)).filter(Boolean);
        section(labels.favorites, favorites);
        section(labels.recent, recent);
        if (!favorites.length && !recent.length) section(labels.available, catalog.slice(0, 12));
    }

    async function open() {
        if (!await verifiedContext()) return;
        input.value = '';
        render();
        if (typeof dialog.showModal === 'function') dialog.showModal();
        else dialog.setAttribute('open', '');
        input.focus();
    }
    function dismiss() {
        if (typeof dialog.close === 'function') dialog.close();
        else dialog.removeAttribute('open');
        trigger.focus();
    }
    trigger.addEventListener('click', () => { void open(); });
    close?.addEventListener('click', dismiss);
    dialog.addEventListener('close', () => {
        if (status() === 'ready') trigger.focus();
    });
    input.addEventListener('input', render);
    window.addEventListener('pageshow', () => { if (dialog.open) currentContext(); });
    window.addEventListener('focus', () => { if (dialog.open) currentContext(); });
    document.addEventListener('visibilitychange', () => {
        if (dialog.open && document.visibilityState === 'visible') currentContext();
    });
    dialog.addEventListener('click', event => {
        if (event.target === dialog) dismiss();
    });
    document.addEventListener('keydown', event => {
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k' &&
            !event.altKey && !event.shiftKey) {
            event.preventDefault();
            if (!dialog.open) void open();
            else input.focus();
        }
    });
})();
