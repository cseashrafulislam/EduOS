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

        try {
            const response = await fetch('/api/auth/mfa/status', {
                method: 'GET',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (response.ok && payload?.data?.enabled === true) {
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
            showAlert('danger', form.dataset.networkMessage);
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
                    || !payload.data.authenticatorUri.startsWith('otpauth://totp/')) {
                    showAlert('danger', form.dataset.setupFailed);
                    return;
                }

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
                if (!response.ok || !payload?.success || !Array.isArray(payload.data?.recoveryCodes)) {
                    showAlert('danger', form.dataset.enableFailed);
                    return;
                }

                form.hidden = true;
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
