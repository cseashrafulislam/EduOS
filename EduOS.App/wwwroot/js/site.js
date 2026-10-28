(() => {
    'use strict';

    const nativeFetch = window.fetch.bind(window);
    const safeMethods = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE']);

    // Cookie-authenticated API writes require the server-issued anti-forgery token.
    // Centralizing this keeps every current and future same-origin fetch protected.
    window.fetch = (input, init) => {
        const request = new Request(input, init);
        const url = new URL(request.url, window.location.href);
        // A privileged action can replace the cookie and its session-bound token
        // without a full page reload. Read the current token for every write.
        const verificationToken = document.querySelector(
            'meta[name="request-verification-token"]')?.content;
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
            window.EduOSNotifySessionChanged?.();
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
        'institution-invitations': { ids: ['email', 'role', 'campus', 'expires', 'status', 'action'], required: ['email', 'status', 'action'] },
        'institution-feature-audit': { ids: ['feature', 'code', 'plan', 'override', 'restriction', 'reason'], required: ['feature', 'code', 'restriction'] },
        'institution-activity-audit': { ids: ['time', 'user', 'action', 'entity', 'record', 'ip', 'outcome'], required: ['time', 'user', 'action', 'outcome'] },
        'institution-campuses': { ids: ['campus', 'code', 'contact', 'type', 'action'], required: ['campus', 'code', 'action'] },
        'institution-change-history': { ids: ['when', 'action', 'fields', 'actor'], required: ['when', 'action', 'actor'] },
        'institution-settings': { ids: ['key', 'category', 'value', 'protection', 'action'], required: ['key', 'protection', 'action'] },
        'institution-permission-exceptions': { ids: ['permission', 'mode', 'reason', 'expires', 'action'], required: ['permission', 'mode', 'expires', 'action'] }
    };
    function safeTablePreferences(tables) {
        const defaults = () => Object.entries(tableDefinitions).map(([id, definition]) => ({
            id, columns: definition.ids.map(column => ({ id: column, visible: true }))
        }));
        if (!Array.isArray(tables) || tables.length !== Object.keys(tableDefinitions).length ||
            new Set(tables.map(table => table?.id)).size !== Object.keys(tableDefinitions).length ||
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
    // Server response identity must match this rendered page, not just the current cookie.
    // A tab opened under institute A must never apply preferences returned under B.
    window.EduOSWorkspaceResponseMatchesSession = response => {
        const reference = document.body.dataset.authSessionReference || '';
        const tenant = document.body.dataset.authTenantId || '';
        return /^[0-9A-F]{64}$/.test(reference) &&
            response?.headers?.get('X-EduOS-Session-Reference') === reference &&
            response.headers.get('X-EduOS-Tenant-Id') === tenant;
    };
    // The editor owns its own load/preview. A second shell request can otherwise
    // overwrite an unsaved preview after the editor has already rendered.
    if (document.body.dataset.workspacePreferences === 'enabled' &&
        !document.getElementById?.('workspacePreferencesForm')) {
        const initialReference = document.body.dataset.authSessionReference;
        const initialTenant = document.body.dataset.authTenantId;
        fetch('/api/workspace-preferences', { credentials: 'same-origin', cache: 'no-store' })
            .then(async response => {
                if (!window.EduOSWorkspaceResponseMatchesSession(response)) {
                    window.EduOSVerifySessionContext?.();
                    return null;
                }
                if (!response.ok) return null;
                return { response, body: await response.json() };
            })
            .then(result => {
                if (!result) return;
                // Headers can match when received, then the user switches
                // institutions before a delayed JSON body finishes parsing.
                if (document.body.dataset.authSessionReference !== initialReference ||
                    document.body.dataset.authTenantId !== initialTenant ||
                    !window.EduOSWorkspaceResponseMatchesSession(result.response)) {
                    window.EduOSVerifySessionContext?.();
                    return;
                }
                if (result.body?.success && result.body.data)
                    window.EduOSApplyWorkspacePreferences(result.body.data);
            })
            .catch(() => { /* Server defaults remain usable when offline. */ });
    }
})();
