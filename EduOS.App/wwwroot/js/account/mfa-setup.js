(() => {
    'use strict';

    document.addEventListener('DOMContentLoaded', async () => {
        const form = document.getElementById('mfaSetupForm');
        const setupButton = document.getElementById('mfaSetupButton');
        const enableButton = document.getElementById('mfaEnableButton');
        const passwordField = document.getElementById('currentPassword');
        const codeField = document.getElementById('mfaEnableCode');
        const keyPanel = document.getElementById('mfaKeyPanel');
        if (!form || !setupButton || !enableButton || !passwordField || !codeField || !keyPanel) return;

        // This public-layout page is authenticated but has no private-shell guard.
        // Bind the initial status read to the cookie that rendered the page.
        const initialReference = form.dataset.authSessionReference;
        const originalTenant = document.body.dataset.authTenantId ?? '';
        try {
            const response = await fetch('/api/auth/mfa/status', {
                method: 'GET',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            const bound = /^[0-9A-F]{64}$/.test(initialReference || '') &&
                response.headers?.get('X-EduOS-Session-Reference') === initialReference &&
                response.headers?.get('X-EduOS-Tenant-Id') === originalTenant;
            if (!response.ok || payload?.success !== true ||
                typeof payload.data?.enabled !== 'boolean' || !bound) {
                form.hidden = true;
                showAlert('danger', form.dataset.networkMessage);
                return;
            }
            if (payload.data.enabled === true) {
                form.hidden = true;
                const enabledPanel = document.getElementById('mfaAlreadyEnabled');
                const enabledAction = document.getElementById('mfaEnabledAction');
                if (enabledAction && payload.data?.sessionVerified !== true) {
                    enabledAction.setAttribute('href', '/Account/Login');
                    enabledAction.textContent = enabledAction.dataset.loginLabel || '';
                } else if (enabledAction) {
                    enabledAction.textContent = enabledAction.dataset.dashboardLabel || '';
                }
                if (enabledPanel) enabledPanel.className = '';
                return;
            }
        } catch {
            form.hidden = true;
            showAlert('danger', form.dataset.networkMessage);
            return;
        }

        setupButton.addEventListener('click', async () => {
            const currentPassword = passwordField.value;
            if (!currentPassword) {
                showAlert('danger', form.dataset.requiredMessage);
                return;
            }

            setLoading(setupButton, true);
            try {
                const response = await fetch('/api/auth/mfa/setup', {
                    method: 'POST',
                    cache: 'no-store',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                    body: JSON.stringify({ currentPassword })
                });
                const payload = await response.json().catch(() => null);
                if (!response.ok
                    || !payload?.success
                    || typeof payload.data?.sharedKey !== 'string'
                    || typeof payload.data?.authenticatorUri !== 'string'
                    || !payload.data.authenticatorUri.startsWith('otpauth://totp/')
                    || !/^[0-9A-F]{64}$/.test(payload.data.sessionReference || '')) {
                    showAlert('danger', form.dataset.setupFailed);
                    return;
                }

                // Setup may rotate the cookie SID. Fetch a fresh session-bound CSRF token
                // before allowing the enable POST; never reuse the old page token.
                if (!await refreshRotatedSession(payload.data.sessionReference)) {
                    showAlert('danger', form.dataset.networkMessage);
                    return;
                }
                window.EduOSNotifySessionChanged?.();
                const keyOutput = document.getElementById('mfaSharedKey');
                const appLink = document.getElementById('openAuthenticatorLink');
                if (keyOutput) keyOutput.textContent = payload.data.sharedKey;
                if (appLink) appLink.setAttribute('href', payload.data.authenticatorUri);
                keyPanel.hidden = false;
                codeField.focus();
            } catch {
                showAlert('danger', form.dataset.networkMessage);
            } finally {
                setLoading(setupButton, false);
            }
        });

        form.addEventListener('submit', async event => {
            event.preventDefault();
            const currentPassword = passwordField.value;
            const code = codeField.value.trim();
            if (!currentPassword || !code) {
                showAlert('danger', form.dataset.requiredMessage);
                return;
            }

            setLoading(enableButton, true);
            try {
                const response = await fetch('/api/auth/mfa/enable', {
                    method: 'POST',
                    cache: 'no-store',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                    body: JSON.stringify({ currentPassword, code })
                });
                const payload = await response.json().catch(() => null);
                if (!response.ok || !payload?.success || !Array.isArray(payload.data?.recoveryCodes) ||
                    !/^[0-9A-F]{64}$/.test(payload.data.sessionReference || '')) {
                    showAlert('danger', form.dataset.enableFailed);
                    return;
                }

                // MFA activation issues a second new cookie. Keep this page's
                // recovery-code view bound to that verified same-institution session.
                const refreshed = await refreshRotatedSession(payload.data.sessionReference);
                window.EduOSNotifySessionChanged?.();
                form.hidden = true;
                if (!refreshed) {
                    // Never expose recovery codes after an unverified cookie switch.
                    showAlert('danger', form.dataset.networkMessage);
                    return;
                }
                const recoveryList = document.getElementById('mfaRecoveryCodes');
                const recoveryPanel = document.getElementById('mfaRecoveryPanel');
                recoveryList?.replaceChildren();
                for (const recoveryCode of payload.data.recoveryCodes) {
                    if (typeof recoveryCode !== 'string') continue;
                    const item = document.createElement('li');
                    item.textContent = recoveryCode;
                    recoveryList?.appendChild(item);
                }
                if (recoveryPanel) recoveryPanel.hidden = false;
            } catch {
                showAlert('danger', form.dataset.networkMessage);
            } finally {
                setLoading(enableButton, false);
            }
        });

        async function refreshRotatedSession(expectedReference) {
            if (!/^[0-9A-F]{64}$/.test(expectedReference || '')) return false;
            try {
                const response = await fetch('/api/auth/antiforgery', {
                    method: 'GET', cache: 'no-store', credentials: 'same-origin',
                    headers: { Accept: 'application/json' }
                });
                const payload = await response.json().catch(() => null);
                const data = payload?.data;
                const meta = document.querySelector('meta[name="request-verification-token"]');
                // MFA setup uses the public layout, which intentionally has no
                // private-shell session guard. Verify the original institution
                // there; on private pages adopt through the shared guard.
                const tenant = data?.tenantId == null ? '' : String(data.tenantId);
                const sameTenant = tenant === (document.body.dataset.authTenantId ?? '');
                // A matching tenant is insufficient: another tab may have signed in
                // as a different user in the same institute during this request.
                if (!response.ok || payload?.success !== true || !meta || !sameTenant ||
                    data?.sessionReference !== expectedReference ||
                    typeof data?.requestToken !== 'string' || !data.requestToken)
                    return false;
                const adopted = typeof window.EduOSAdoptRotatedSession === 'function'
                    ? window.EduOSAdoptRotatedSession(expectedReference, data.tenantId)
                    : true;
                if (!adopted) return false;
                form.dataset.authSessionReference = expectedReference;
                meta.content = data.requestToken;
                document.querySelectorAll('input[name="__RequestVerificationToken"]')
                    .forEach(input => { input.value = data.requestToken; });
                return true;
            } catch { return false; }
        }

        function showAlert(type, message) {
            const alert = document.getElementById('mfaSetupAlert');
            if (!alert) return;
            alert.className = `alert alert-${type}`;
            alert.textContent = message || '';
            alert.focus();
        }
    }, { once: true });

    function setLoading(button, loading) {
        button.disabled = loading;
        button.textContent = loading
            ? button.dataset.loadingLabel || ''
            : button.dataset.idleLabel || '';
    }
})();
