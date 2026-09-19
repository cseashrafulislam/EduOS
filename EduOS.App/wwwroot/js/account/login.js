(() => {
    'use strict';

    document.addEventListener('DOMContentLoaded', () => {
        const form = document.getElementById('loginForm');
        const submitButton = document.getElementById('loginBtn');
        if (!form || !submitButton) return;

        const parameters = new URLSearchParams(window.location.search);
        const emailField = document.getElementById('email');
        const emailParameter = parameters.get('email');
        if (emailField && emailParameter) emailField.value = emailParameter;

        if (parameters.get('status') === 'password-reset') {
            showAlert('success', submitButton.dataset.resetSuccess || '');
        }

        form.addEventListener('submit', async event => {
            event.preventDefault();

            const email = emailField?.value?.trim();
            const password = document.getElementById('password')?.value;
            const rememberMe = document.getElementById('rememberMe')?.checked ?? false;

            if (!email || !password) {
                showAlert('danger', form.dataset.requiredMessage);
                return;
            }

            setLoading(submitButton, true);

            try {
                const response = await fetch('/api/auth/login', {
                    method: 'POST',
                    cache: 'no-store',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                    body: JSON.stringify({ email, password, rememberMe })
                });
                const payload = await response.json().catch(() => null);

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

                showAlert('danger', localizeServerFailure(payload?.message));
            } catch {
                showAlert('danger', form.dataset.networkMessage);
            } finally {
                setLoading(submitButton, false);
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
