/* Shared shell presentation only. It never creates routes, changes permissions,
 * posts transactions or persists user/tenant data. */
(() => {
    'use strict';

    function breadcrumbParts(parts) {
        const result = [];
        for (const value of parts) {
            const label = typeof value === 'string' ? value.trim().slice(0, 100) : '';
            if (label && label.toLocaleLowerCase() !== result[result.length - 1]?.toLocaleLowerCase())
                result.push(label);
        }
        return result.slice(0, 4);
    }

    // Breadcrumbs are derived from authorized sidebar links, never from URL
    // segments (which can contain private IDs or misleading untrusted labels).
    function breadcrumbSelection(links) {
        // A hidden/disabled destination cannot represent the visible page.
        // Only Razor-authorized, currently visible links may become breadcrumbs.
        const candidates = Array.from(links || []).filter(link =>
            link && !link.closest?.('[hidden], [inert]') &&
            link.getAttribute?.('aria-disabled') !== 'true');
        return candidates.find(link => link.getAttribute?.('aria-current') === 'page') ||
            candidates.find(link => link.classList?.contains('active')) || null;
    }

    // Only offer a next-step shortcut if the current server-authorized menu
    // already exposes it. Never infer a destination from URL or role names.
    function contextualSetupLink(links, current) {
        const candidates = Array.from(links || []);
        const setup = candidates.find(link => {
            if (!link || typeof link.getAttribute !== 'function') return false;
            const href = link.getAttribute('href') || '';
            return /^\\/SmartSetup(?:\\/Index)?\\/?(?:[?#].*)?$/i.test(href) &&
                link !== current && !link.closest?.('[hidden], [inert]') &&
                link.getAttribute('aria-disabled') !== 'true';
        });
        return setup || null;
    }

    // A next-task suggestion is opt-in per server-rendered navigation section.
    // The candidate must be an already-authorized, visible same-origin sibling.
    // This is navigation only: it does not imply that a business task is complete.
    function contextualNextLink(current) {
        if (!current || current.getAttribute?.('aria-current') !== 'page') return null;
        const nav = current.closest?.('nav[data-sidebar-section][data-shell-next-task="true"]');
        if (!nav || current.closest?.('[hidden], [inert]') ||
            current.getAttribute?.('aria-disabled') === 'true') return null;
        const siblings = Array.from(nav.querySelectorAll?.('a.nav-link') || []);
        const at = siblings.indexOf(current);
        if (at < 0) return null;
        for (const candidate of siblings.slice(at + 1)) {
            if (candidate.closest?.('[hidden], [inert]') ||
                candidate.getAttribute?.('aria-disabled') === 'true') continue;
            const href = candidate.getAttribute?.('href') || '';
            if (!/^\\/(?!\\/)[a-z0-9/_-]+(?:[?#][^\\s]*)?$/i.test(href) ||
                /(?:^|\\/)\\.{1,2}(?:\\/|$)/.test(href)) continue;
            return candidate;
        }
        return null;
    }

    function notificationData(message, kind) {
        if (typeof message !== 'string' || !message.trim()) return null;
        const types = new Set(['success', 'info', 'warning', 'error']);
        return { message: message.trim().slice(0, 800), kind: types.has(kind) ? kind : 'info' };
    }

    function notificationDismissalMs(data) {
        // Critical/long notices require explicit dismissal so users can read them.
        if (!data || (data.kind !== 'success' && data.kind !== 'info') || data.message.length > 120)
            return null;
        return Math.max(5500, Math.min(10000, data.message.length * 80));
    }

    if (typeof module !== 'undefined' && module.exports)
        module.exports = { breadcrumbParts, breadcrumbSelection, contextualSetupLink, contextualNextLink, notificationData, notificationDismissalMs };
    if (typeof document === 'undefined') return;

    const crumbs = document.getElementById('pageBreadcrumbs');
    const sidebar = document.getElementById('sidebarMenu');
    const shellTenant = document.body?.dataset.authTenantId || '';
    const shellSession = document.body?.dataset.authSessionReference || '';
    const shellUser = document.body?.dataset.authUserId || '';
    function shellSessionValid() {
        return (document.body?.dataset.authTenantId || '') === shellTenant &&
            (document.body?.dataset.authSessionReference || '') === shellSession &&
            (document.body?.dataset.authUserId || '') === shellUser &&
            !window.EduOSSessionTransitionPending?.();
    }
    function renderBreadcrumbs() {
        if (!crumbs || !sidebar) return;
        if (!shellSessionValid()) {
            crumbs.replaceChildren();
            crumbs.hidden = true;
            return;
        }
        const active = breadcrumbSelection(sidebar.querySelectorAll('nav[data-sidebar-section] a.nav-link'));
        const nav = active?.closest('nav[data-sidebar-section]');
        const section = nav?.previousElementSibling?.classList.contains('menu-title')
            ? nav.previousElementSibling.textContent : '';
        const group = active?.closest('.sidebar-group')?.querySelector('.sidebar-group-label')?.textContent;
        const category = active?.closest('.sidebar-category')?.querySelector('.sidebar-category-toggle span')?.textContent;
        const leaf = active?.querySelector('span')?.textContent || active?.getAttribute('aria-label') || active?.textContent;
        const path = breadcrumbParts([section, group, category, leaf]);
        if (path.length < 2) {
            crumbs.replaceChildren();
            crumbs.hidden = true;
            return;
        }
        const items = document.createElement('ol');
        for (const [index, label] of path.entries()) {
            const item = document.createElement('li');
            item.textContent = label;
            if (index === path.length - 1) item.setAttribute('aria-current', 'page');
            items.append(item);
        }
        crumbs.replaceChildren(items);
        crumbs.hidden = false;
    }
    const contextual = document.getElementById('shellContextActions');
    function renderContextActions() {
        if (!contextual || !sidebar) return;
        contextual.replaceChildren();
        contextual.hidden = true;
        if (!shellUser || !shellSessionValid()) return;
        const links = sidebar.querySelectorAll('nav[data-sidebar-section] a.nav-link');
        const current = breadcrumbSelection(links);
        const setup = contextualSetupLink(links, current);
        const next = contextualNextLink(current);
        if (!setup && !next) return;
        if (setup) {
            const shortcut = document.createElement('a');
            shortcut.className = 'shell-context-shortcut';
            shortcut.href = setup.getAttribute('href');
            shortcut.textContent = document.documentElement.lang?.toLowerCase().startsWith('bn')
                ? 'স্মার্ট সেটআপ চালিয়ে যান' : 'Continue Smart Setup';
            shortcut.setAttribute('aria-label', shortcut.textContent);
            contextual.append(shortcut);
        }
        if (next && next !== setup) {
            const shortcut = document.createElement('a');
            shortcut.className = 'shell-context-shortcut shell-context-shortcut--next';
            shortcut.href = next.getAttribute('href');
            const label = (next.querySelector?.('span')?.textContent ||
                next.getAttribute('aria-label') || next.textContent || '').trim().slice(0, 90);
            if (label) {
                shortcut.textContent = (document.documentElement.lang?.toLowerCase().startsWith('bn')
                    ? 'পরবর্তী পেজ: ' : 'Next page: ') + label;
                contextual.append(shortcut);
            }
        }
        contextual.hidden = contextual.childElementCount === 0;
    }
    renderBreadcrumbs();
    renderContextActions();
    window.addEventListener('hashchange', () => {
        renderBreadcrumbs();
        renderContextActions();
    });
    // Sidebar grouping and active selection are progressive enhancements;
    // refresh the breadcrumb after that work without copying route metadata.
    window.addEventListener('eduos:sidebar-navigation-ready', () => { renderBreadcrumbs(); renderContextActions(); });
    // bfcache/tab restores must never retain a previous account's contextual
    // navigation; no client-side route or permission cache is introduced.
    window.addEventListener('pageshow', () => { renderBreadcrumbs(); renderContextActions(); });
    window.addEventListener('focus', () => { renderBreadcrumbs(); renderContextActions(); });
    document.addEventListener('visibilitychange', () => {
        if (!document.hidden) { renderBreadcrumbs(); renderContextActions(); }
    });

    const host = document.getElementById('eduToastStack');
    const tenant = document.body?.dataset.authTenantId || '';
    const session = document.body?.dataset.authSessionReference || '';
    const bangla = document.documentElement.lang?.toLowerCase().startsWith('bn');
    function sameSession() {
        return shellSessionValid() &&
            (document.body?.dataset.authTenantId || '') === tenant &&
            (document.body?.dataset.authSessionReference || '') === session;
    }
    function clearStaleNotifications() {
        if (!sameSession()) host?.replaceChildren();
    }
    window.addEventListener('pageshow', clearStaleNotifications);
    window.addEventListener('focus', clearStaleNotifications);
    document.addEventListener('visibilitychange', clearStaleNotifications);

    window.EduOSNotify = (message, kind = 'info') => {
        const data = notificationData(message, kind);
        if (!host || !data || !sameSession()) return null;
        const toast = document.createElement('div');
        toast.className = `edu-toast edu-toast--${data.kind}`;
        toast.setAttribute('role', data.kind === 'error' ? 'alert' : 'status');
        toast.setAttribute('aria-atomic', 'true');
        const text = document.createElement('span');
        text.textContent = data.message;
        const dismiss = document.createElement('button');
        dismiss.type = 'button';
        dismiss.className = 'edu-toast-dismiss';
        dismiss.textContent = '×';
        dismiss.setAttribute('aria-label', bangla ? 'বিজ্ঞপ্তি বন্ধ করুন' : 'Dismiss notification');
        dismiss.addEventListener('click', () => toast.remove());
        toast.append(text, dismiss);
        // Bounded presentation state: no storage and no hidden stale notifications.
        if (host.children.length >= 3) {
            // Do not silently remove a critical warning/error or the toast
            // currently being read via keyboard focus.
            const removable = Array.from(host.children).find(node =>
                !node.matches('.edu-toast--error, .edu-toast--warning') &&
                !node.contains(document.activeElement));
            if (!removable) return null;
            removable.remove();
        }
        host.append(toast);
        const delay = notificationDismissalMs(data);
        if (delay !== null) {
            let timer = null;
            function pauseTimer() {
                if (timer !== null) window.clearTimeout(timer);
                timer = null;
            }
            function resumeTimer() {
                pauseTimer();
                if (!toast.isConnected) return;
                timer = window.setTimeout(() => {
                    timer = null;
                    if (!toast.isConnected) return;
                    // Never close a notice while it is being read or acted upon.
                    if (document.hidden || toast.matches(':hover') || toast.contains(document.activeElement))
                        return resumeTimer();
                    toast.remove();
                }, delay);
            }
            toast.addEventListener('pointerenter', pauseTimer);
            toast.addEventListener('pointerleave', resumeTimer);
            toast.addEventListener('focusin', pauseTimer);
            toast.addEventListener('focusout', () => window.setTimeout(() => {
                if (!toast.contains(document.activeElement)) resumeTimer();
            }, 0));
            resumeTimer();
        }
        return toast;
    };
})();
