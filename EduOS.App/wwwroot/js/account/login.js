(() => {
    'use strict';

    document.addEventListener('DOMContentLoaded', () => {
        const form = document.getElementById('loginForm');
        const submitButton = document.getElementById('loginBtn');
        if (!form || !submitButton) return;

        const parameters = new URLSearchParams(window.location.search);
        const identifierField = document.getElementById('userNameOrEmail');
        const passwordField = document.getElementById('password');
        const institutionPanel = document.getElementById('institutionChoicePanel');
        const institutionChoice = document.getElementById('institutionChoice');
        let requestVersion = 0, loginPending = false, activeRequest = null;
        function clearInstitutionOffer() {
            if (institutionPanel) institutionPanel.hidden = true;
            if (institutionChoice) {
                institutionChoice.required = false;
                institutionChoice.replaceChildren();
            }
        }
        function invalidateCredentials() {
            requestVersion++;
            activeRequest?.abort();
            activeRequest = null;
            loginPending = false;
            clearInstitutionOffer();
            setLoading(submitButton, false);
            const alert = document.getElementById('alertContainer');
            if (alert) { alert.className = 'd-none'; alert.textContent = ''; }
        }
        identifierField?.addEventListener('input', invalidateCredentials);
        passwordField?.addEventListener('input', invalidateCredentials);
        const identifierParameter = parameters.get('userNameOrEmail');
        if (identifierField && identifierParameter) identifierField.value = identifierParameter;

        // Offered only by the server on an explicitly isolated Staging demo.
        // Credentials always pass through the normal rate-limited login/MFA API.
        const demoToggle = form.querySelector('[data-demo-toggle]');
        const demoPanel = document.getElementById('demoLoginPanel');
        demoToggle?.addEventListener('click', () => {
            if (!demoPanel) return;
            demoPanel.hidden = !demoPanel.hidden;
            demoToggle.setAttribute('aria-expanded', String(!demoPanel.hidden));
            if (!demoPanel.hidden) document.getElementById('demoRole')?.focus();
        });
        form.querySelector('[data-demo-login]')?.addEventListener('click', () => {
            const roleSelector = document.getElementById('demoRole');
            const passwordField = document.getElementById('password');
            if (!demoPanel || !identifierField || !passwordField || !roleSelector?.value ||
                !demoPanel.dataset.demoPassword) return;
            invalidateCredentials();
            identifierField.value = roleSelector.value;
            passwordField.value = demoPanel.dataset.demoPassword;
            form.requestSubmit(submitButton);
        });

        if (parameters.get('status') === 'password-reset') {
            showAlert('success', submitButton.dataset.resetSuccess || '');
        }
        if (parameters.get('status') === 'password-changed') {
            showAlert('success', submitButton.dataset.passwordChangedSuccess || 'Password changed. Sign in again.');
        }

        form.addEventListener('submit', async event => {
            event.preventDefault();
            if (loginPending) return;

            const userNameOrEmail = identifierField?.value?.trim();
            const password = passwordField?.value;
            const rememberMe = document.getElementById('rememberMe')?.checked ?? false;
            const selectingInstitution = institutionPanel && !institutionPanel.hidden;
            const selected = institutionChoice?.selectedOptions?.[0];
            const tenantId = selectingInstitution && selected && !selected.disabled &&
                selected.value !== '' && Number.isSafeInteger(Number(selected.value)) &&
                Number(selected.value) > 0 ? Number(selected.value) : null;

            if (!userNameOrEmail || !password) {
                showAlert('danger', form.dataset.requiredMessage);
                return;
            }
            if (selectingInstitution && tenantId === null) {
                showAlert('danger', form.dataset.selectInstitutionMessage);
                institutionChoice?.focus();
                return;
            }

            const submissionVersion = ++requestVersion;
            const controller = new AbortController();
            activeRequest = controller;
            loginPending = true;
            setLoading(submitButton, true);

            try {
                const response = await fetch('/api/auth/login', {
                    method: 'POST',
                    cache: 'no-store',
                    credentials: 'same-origin',
                    signal: controller.signal,
                    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                    body: JSON.stringify({ userNameOrEmail, password, rememberMe, tenantId })
                });
                const payload = await response.json().catch(() => null);
                if (submissionVersion !== requestVersion) return;

                if (response.status === 409 && payload?.success === true &&
                    payload.data?.requiresTenantSelection === true &&
                    Array.isArray(payload.data?.tenants)) {
                    const panel = institutionPanel;
                    const choice = institutionChoice;
                    if (!panel || !choice) return;
                    clearInstitutionOffer();
                    const placeholder = document.createElement('option');
                    placeholder.value = '';
                    placeholder.disabled = true;
                    placeholder.selected = true;
                    placeholder.textContent = form.dataset.selectInstitutionRequired ||
                        form.dataset.institutionLabel || '';
                    choice.appendChild(placeholder);
                    const seen = new Set();
                    for (const tenant of payload.data.tenants) {
                        const id = Number(tenant?.id);
                        if (!Number.isSafeInteger(id) || id <= 0 || seen.has(id)) continue;
                        seen.add(id);
                        const option = document.createElement('option');
                        option.value = String(id);
                        option.textContent = String(tenant.name || form.dataset.institutionLabel || '');
                        choice.appendChild(option);
                    }
                    if (choice.options.length === 1) {
                        clearInstitutionOffer();
                        showAlert('danger', form.dataset.invalidMessage);
                        return;
                    }
                    choice.selectedIndex = 0;
                    panel.hidden = false;
                    choice.required = true;
                    choice.focus();
                    showAlert('info', form.dataset.selectInstitutionMessage);
                    return;
                }

                if (response.status === 202
                    && payload?.success
                    && payload.data?.requiresTwoFactor === true
                    && typeof payload.data?.challengeToken === 'string') {
                    sessionStorage.setItem('eduos.mfaChallenge', payload.data.challengeToken);
                    window.location.assign('/Account/MfaChallenge');
                    return;
                }

                if (response.ok && payload?.success) {
                    window.location.assign(getSafeRedirect(
                        payload.data?.redirectUrl,
                        parameters.get('returnUrl')));
                    return;
                }

                clearInstitutionOffer();
                showAlert('danger', localizeServerFailure(payload?.message));
            } catch (error) {
                if (submissionVersion === requestVersion && error?.name !== 'AbortError')
                    showAlert('danger', form.dataset.networkMessage);
            } finally {
                if (submissionVersion === requestVersion) {
                    loginPending = false;
                    activeRequest = null;
                    setLoading(submitButton, false);
                }
            }
        });

        function localizeServerFailure(message) {
            const normalized = String(message || '').toLowerCase();
            if (normalized.includes('deactivated') || normalized.includes('inactive')) {
                return form.dataset.inactiveMessage;
            }
            if (normalized.includes('verify your email') || normalized.includes('not verified')) {
                return form.dataset.verifyMessage;
            }
            if (normalized.includes('locked')) return form.dataset.lockedMessage;
            return form.dataset.invalidMessage;
        }
    }, { once: true });

    function getSafeRedirect(primary, fallback) {
        for (const candidate of [primary, fallback, '/Dashboard']) {
            if (typeof candidate !== 'string' || !candidate.startsWith('/') || candidate.startsWith('//')) {
                continue;
            }

            const resolved = new URL(candidate, window.location.origin);
            if (resolved.origin === window.location.origin) {
                return `${resolved.pathname}${resolved.search}${resolved.hash}`;
            }
        }
        return '/Dashboard';
    }

    function showAlert(type, message) {
        const container = document.getElementById('alertContainer');
        if (!container) return;
        container.className = `alert alert-${type}`;
        container.textContent = message || '';
    }

    function setLoading(button, loading) {
        button.disabled = loading;
        button.replaceChildren();

        if (loading) {
            const spinner = document.createElement('span');
            spinner.className = 'spinner-border spinner-border-sm me-2';
            spinner.setAttribute('aria-hidden', 'true');
            button.append(spinner, button.dataset.loadingLabel || '');
        } else {
            button.textContent = button.dataset.idleLabel || '';
        }
    }
})();
