(() => {
    'use strict';

    const nativeFetch = window.fetch.bind(window);
    const verificationToken = document.querySelector(
        'meta[name="request-verification-token"]')?.content;
    const safeMethods = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE']);

    // Cookie-authenticated API writes require the server-issued anti-forgery token.
    // Centralizing this keeps every current and future same-origin fetch protected.
    window.fetch = (input, init) => {
        const request = new Request(input, init);
        const url = new URL(request.url, window.location.href);
        if (!verificationToken
            || url.origin !== window.location.origin
            || safeMethods.has(request.method.toUpperCase())) {
            return nativeFetch(request);
        }

        const headers = new Headers(request.headers);
        headers.set('RequestVerificationToken', verificationToken);
        return nativeFetch(new Request(request, { headers }));
    };

    // Clear only public runtime PWA assets when signing out; never delay or replace server logout.
    document.querySelectorAll('form[method="post"]').forEach(form => {
        const action = new URL(form.action, window.location.href);
        if (action.origin !== window.location.origin || action.pathname.toLowerCase() !== '/account/logout') return;
        form.addEventListener('submit', () => {
            navigator.serviceWorker?.controller?.postMessage({ type: 'EDUOS_CLEAR_RUNTIME_STATIC' });
        });
    });

    const sidebar = document.getElementById('sidebarMenu');
    const sidebarToggle = document.querySelector('[data-sidebar-toggle], #btnSidebarToggle');
    const sidebarClosers = document.querySelectorAll('[data-sidebar-close]');
    const desktopMedia = window.matchMedia('(min-width: 992px)');
    let lastFocusedElement = null;

    function setSidebarOpen(isOpen) {
        if (!sidebar || !sidebarToggle) return;

        sidebar.classList.toggle('show', isOpen);
        document.body.classList.toggle('sidebar-open', isOpen);
        sidebarToggle.setAttribute('aria-expanded', String(isOpen));

        if (isOpen) {
            lastFocusedElement = document.activeElement;
            sidebar.querySelector('a, button')?.focus();
        } else if (lastFocusedElement instanceof HTMLElement) {
            lastFocusedElement.focus();
        }
    }

    sidebarToggle?.addEventListener('click', () => {
        setSidebarOpen(!sidebar?.classList.contains('show'));
    });

    sidebarClosers.forEach(element => {
        element.addEventListener('click', () => setSidebarOpen(false));
    });

    sidebar?.querySelectorAll('a').forEach(link => {
        link.addEventListener('click', () => {
            if (!desktopMedia.matches) setSidebarOpen(false);
        });
    });

    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && sidebar?.classList.contains('show')) {
            setSidebarOpen(false);
        }
    });

    desktopMedia.addEventListener('change', event => {
        if (event.matches) setSidebarOpen(false);
    });

    document.querySelector('[data-language-selector]')?.addEventListener('change', event => {
        event.currentTarget.form?.requestSubmit();
    });

    // Connectivity is advisory: never queue authenticated academic or financial writes offline.
    const connectivityNotice = document.querySelector('[data-connectivity-notice]');
    function updateConnectivityNotice() {
        if (connectivityNotice) connectivityNotice.hidden = navigator.onLine !== false;
    }
    window.addEventListener('offline', updateConnectivityNotice);
    window.addEventListener('online', updateConnectivityNotice);
    updateConnectivityNotice();

    let installPrompt = null;
    const installButton = document.querySelector('[data-install-app]');

    window.addEventListener('beforeinstallprompt', event => {
        event.preventDefault();
        installPrompt = event;
        if (installButton) installButton.hidden = false;
    });

    installButton?.addEventListener('click', async () => {
        if (!installPrompt) return;
        installPrompt.prompt();
        await installPrompt.userChoice;
        installPrompt = null;
        installButton.hidden = true;
    });

    window.addEventListener('appinstalled', () => {
        installPrompt = null;
        if (installButton) installButton.hidden = true;
    });

    if ('serviceWorker' in navigator) {
        window.addEventListener('load', () => {
            navigator.serviceWorker.register('/service-worker.js', { scope: '/' })
                .catch(error => console.warn('EduOS service worker registration failed.', error));
        });
    }
    // Preferences are fetched only for the server-resolved active institution.
    // No localStorage/cross-tenant cache is used as a source of authorization or appearance.
    const workspaceChoices = {
        theme: new Set(['system', 'light', 'dark']),
        palette: new Set(['blue', 'teal', 'violet']),
        navigation: new Set(['left', 'right', 'top']),
        density: new Set(['comfortable', 'compact']),
        fontScale: new Set(['normal', 'large'])
    };
    const dashboardPanelIds = ['capacity', 'quick-actions', 'setup-checklist'];
    function safeDashboardPanels(panels) {
        if (!Array.isArray(panels) || panels.length !== 3
            || !panels.every(p => p && dashboardPanelIds.includes(p.id) && typeof p.visible === 'boolean')
            || new Set(panels.map(p => p.id)).size !== 3)
            return dashboardPanelIds.map(id => ({ id, visible: true }));
        return panels.map(p => ({ id: p.id, visible: p.visible }));
    }
    const tableDefinitions = {
        'institution-users': { ids: ['user', 'membership', 'roles', 'action'], required: ['user', 'action'] },
        'institution-invitations': { ids: ['email', 'role', 'campus', 'expires', 'status', 'action'], required: ['email', 'status', 'action'] }
    };
    function safeTablePreferences(tables) {
        const defaults = () => Object.entries(tableDefinitions).map(([id, definition]) => ({
            id, columns: definition.ids.map(column => ({ id: column, visible: true }))
        }));
        if (!Array.isArray(tables) || tables.length !== 2 ||
            new Set(tables.map(table => table?.id)).size !== 2 ||
            !tables.every(table => {
                const def = tableDefinitions[table?.id];
                return def && Array.isArray(table.columns) && table.columns.length === def.ids.length &&
                    new Set(table.columns.map(column => column?.id)).size === def.ids.length &&
                    table.columns.every(column => column && def.ids.includes(column.id) && typeof column.visible === 'boolean') &&
                    def.required.every(id => table.columns.find(column => column.id === id).visible);
            })) return defaults();
        return tables.map(table => ({
            id: table.id, columns: table.columns.map(column => ({ id: column.id, visible: column.visible }))
        }));
    }
    window.EduOSApplyWorkspacePreferences = preferences => {
        const html = document.documentElement;
        for (const [name, allowed] of Object.entries(workspaceChoices)) {
            const value = preferences?.[name];
            const safe = allowed.has(value) ? value : [...allowed][0];
            const attr = name === 'fontScale' ? 'workspaceFontScale' : 'workspace' + name[0].toUpperCase() + name.slice(1);
            html.dataset[attr] = safe;
        }
        // Ephemeral page-only state: never persist or share across institution sessions.
        const detail = {
            dashboardPanels: safeDashboardPanels(preferences?.dashboardPanels),
            tablePreferences: safeTablePreferences(preferences?.tablePreferences)
        };
        window.EduOSWorkspacePreferences = detail;
        window.dispatchEvent(new CustomEvent('eduos:workspace-preferences', { detail }));
    };
    if (document.body.dataset.workspacePreferences === 'enabled') {
        fetch('/api/workspace-preferences', { credentials: 'same-origin', cache: 'no-store' })
            .then(response => response.ok ? response.json() : null)
            .then(result => {
                if (result?.success && result.data)
                    window.EduOSApplyWorkspacePreferences(result.data);
            })
            .catch(() => { /* Server defaults remain usable when offline. */ });
    }
})();
