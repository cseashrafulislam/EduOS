/* Read-only TenantAdmin institution branding summary on the existing Workspace page.
 * Uses existing tenant profile/settings and session-bound Workspace contracts. */
(() => {
    'use strict';
    const defaults = { primary: '#1E40AF', secondary: '#64748B', accent: '#F59E0B' };
    function color(value, fallback) {
        return typeof value === 'string' && /^#[0-9a-f]{6}$/i.test(value)
            ? value.toUpperCase() : fallback;
    }
    function safeLogoPath(value) {
        return typeof value === 'string' && value.length <= 512 &&
            /^\/uploads\/[a-z0-9/_\-.]+$/i.test(value) &&
            !value.includes('..') && !value.includes('//') ? value : null;
    }
    function snapshot(profile, settings) {
        const rows = Array.isArray(settings?.items) ? settings.items : [];
        const byKey = new Map(rows.filter(x => x && typeof x.key === 'string')
            .map(x => [x.key, x.value]));
        return {
            name: String(profile?.name || '').trim().slice(0, 200),
            logo: safeLogoPath(profile?.logoUrl),
            colors: {
                primary: color(byKey.get('Branding.PrimaryColor'), defaults.primary),
                secondary: color(byKey.get('Branding.SecondaryColor'), defaults.secondary),
                accent: color(byKey.get('Branding.AccentColor'), defaults.accent)
            }
        };
    }
    function sessionMatches(response, matcher) {
        return !!response?.ok && typeof matcher === 'function' && matcher(response) === true;
    }
    if (typeof module !== 'undefined' && module.exports)
        module.exports = { color, safeLogoPath, snapshot, sessionMatches };
    if (typeof document === 'undefined') return;
    const section = document.getElementById('workspaceInstituteBranding');
    if (!section) return; // No privileged requests outside TenantAdmin markup.
    const status = section.querySelector('[data-branding-status]');
    const retry = section.querySelector('[data-branding-retry]');
    const content = section.querySelector('[data-branding-content]');
    const name = section.querySelector('[data-branding-name]');
    const logo = section.querySelector('[data-branding-logo]');
    let busy = false;
    let invalidSession = false;
    const originalTenant = document.body?.dataset.authTenantId || '';
    const originalSession = document.body?.dataset.authSessionReference || '';
    const shield = document.getElementById('eduosSessionShield');
    function contextReady() {
        return !!originalTenant && !!originalSession &&
            document.body?.dataset.authTenantId === originalTenant &&
            document.body?.dataset.authSessionReference === originalSession &&
            !window.EduOSSessionTransitionPending?.() &&
            (!shield || shield.hidden) &&
            typeof window.EduOSWorkspaceResponseMatchesSession === 'function';
    }
    function invalidate() {
        invalidSession = true;
        clear();
        retry.hidden = true;
        void window.EduOSVerifySessionContext?.();
    }

    function state(message, error = false) {
        status.textContent = message;
        status.setAttribute('role', error ? 'alert' : 'status');
        status.classList.toggle('text-danger', error);
    }
    function clear() {
        content.hidden = true;
        name.textContent = '';
        logo.hidden = true;
        logo.removeAttribute('src');
    }
    function render(data) {
        name.textContent = data.name;
        if (data.logo) { logo.src = data.logo; logo.hidden = false; }
        else { logo.hidden = true; logo.removeAttribute('src'); }
        for (const row of section.querySelectorAll('[data-branding-color]')) {
            const value = data.colors[row.dataset.brandingColor];
            row.querySelector('[data-branding-value]').textContent = value;
            row.querySelector('[data-branding-swatch]').style.backgroundColor = value;
        }
        content.hidden = false;
    }
    async function json(url, signal) {
        const response = await fetch(url, {
            method: 'GET', credentials: 'same-origin', cache: 'no-store', signal,
            headers: { Accept: 'application/json' }
        });
        const body = await response.json().catch(() => null);
        if (!response.ok || !body?.success || !body.data) throw new Error('Branding read failed.');
        return body.data;
    }
    async function proveSession(signal) {
        // Tenant profile/settings do not emit the session-reference headers.
        // Verify via the existing bound Workspace endpoint before rendering.
        if (!contextReady()) {
            invalidate();
            throw new Error('Session changed.');
        }
        const response = await fetch('/api/workspace-preferences', {
            method: 'GET', credentials: 'same-origin', cache: 'no-store', signal,
            headers: { Accept: 'application/json' }
        });
        if (!response.ok) throw new Error('Session verification unavailable.');
        if (!contextReady() || !sessionMatches(response, window.EduOSWorkspaceResponseMatchesSession)) {
            invalidate();
            throw new Error('Session changed.');
        }
    }
    async function load() {
        if (busy || invalidSession) return;
        busy = true;
        retry.disabled = true;
        retry.hidden = true;
        clear();
        state(section.dataset.loading || '');
        const controller = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), 10000);
        try {
            await proveSession(controller.signal);
            const [profile, settings] = await Promise.all([
                json('/api/tenant-profile', controller.signal),
                json('/api/tenant-settings?category=Branding&page=1&pageSize=50', controller.signal)
            ]);
            await proveSession(controller.signal);
            render(snapshot(profile, settings));
            state('');
        } catch {
            clear();
            state(section.dataset.error || '', true);
            retry.hidden = invalidSession;
        } finally {
            window.clearTimeout(timeout);
            busy = false;
            retry.disabled = invalidSession;
        }
    }
    retry.addEventListener('click', load);
    load();
})();
