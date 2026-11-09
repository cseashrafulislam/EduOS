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

    function notificationData(message, kind) {
        if (typeof message !== 'string' || !message.trim()) return null;
        const types = new Set(['success', 'info', 'warning', 'error']);
        return { message: message.trim().slice(0, 800), kind: types.has(kind) ? kind : 'info' };
    }

    if (typeof module !== 'undefined' && module.exports)
        module.exports = { breadcrumbParts, notificationData };
    if (typeof document === 'undefined') return;

    const crumbs = document.getElementById('pageBreadcrumbs');
    const sidebar = document.getElementById('sidebarMenu');
    function renderBreadcrumbs() {
        if (!crumbs || !sidebar) return;
        const active = sidebar.querySelector('nav[data-sidebar-section] a.nav-link[aria-current="page"]');
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
    renderBreadcrumbs();
    window.addEventListener('hashchange', renderBreadcrumbs);

    const host = document.getElementById('eduToastStack');
    const tenant = document.body?.dataset.authTenantId || '';
    const session = document.body?.dataset.authSessionReference || '';
    const bangla = document.documentElement.lang?.toLowerCase().startsWith('bn');
    function sameSession() {
        return (document.body?.dataset.authTenantId || '') === tenant &&
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
        if (host.children.length >= 3) host.firstElementChild?.remove();
        host.append(toast);
        if (data.kind === 'success' || data.kind === 'info')
            window.setTimeout(() => toast.remove(), 5500);
        return toast;
    };
})();
