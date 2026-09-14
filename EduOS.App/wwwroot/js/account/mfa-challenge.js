(() => {
    'use strict';

    document.addEventListener('DOMContentLoaded', () => {
        const form = document.getElementById('mfaChallengeForm');
        const button = document.getElementById('mfaVerifyButton');
        const codeField = document.getElementById('mfaCode');
        const recoveryField = document.getElementById('useRecoveryCode');
        if (!form || !button || !codeField || !recoveryField) return;

        const challengeToken = sessionStorage.getItem('eduos.mfaChallenge');
        if (!challengeToken) {
            showAlert('danger', form.dataset.expiredMessage);
            button.disabled = true;
            return;
        }

        recoveryField.addEventListener('change', () => {
            codeField.inputMode = recoveryField.checked ? 'text' : 'numeric';
            codeField.maxLength = recoveryField.checked ? 32 : 12;
            codeField.value = '';
            codeField.focus();
        });

        form.addEventListener('submit', async event => {
            event.preventDefault();
            let challengeExpired = false;
            const code = codeField.value.trim();
            if (!code) {
                showAlert('danger', form.dataset.requiredMessage);
                return;
            }

            setLoading(button, true);
            try {
                const response = await fetch('/api/auth/mfa/login', {
                    method: 'POST',
                    cache: 'no-store',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                    body: JSON.stringify({
                        challengeToken,
                        code,
                        useRecoveryCode: recoveryField.checked
                    })
                });
                const payload = await response.json().catch(() => null);
                if (response.ok && payload?.success) {
                    sessionStorage.removeItem('eduos.mfaChallenge');
                    window.location.assign(safeLocalUrl(payload.data?.redirectUrl, '/Dashboard'));
                    return;
                }

                const message = String(payload?.message || '').toLowerCase();
                if (message.includes('expired') || message.includes('request')) {
                    sessionStorage.removeItem('eduos.mfaChallenge');
                    showAlert('danger', form.dataset.expiredMessage);
                    challengeExpired = true;
                    button.disabled = true;
                    return;
                }
                showAlert('danger', form.dataset.invalidMessage);
            } catch {
                showAlert('danger', form.dataset.networkMessage);
            } finally {
                if (!challengeExpired) setLoading(button, false);
            }
        });

        function showAlert(type, message) {
            const alert = document.getElementById('mfaAlert');
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

    function safeLocalUrl(value, fallback) {
        if (typeof value !== 'string' || !value.startsWith('/') || value.startsWith('//')) return fallback;
        const url = new URL(value, window.location.origin);
        return url.origin === window.location.origin
            ? `${url.pathname}${url.search}${url.hash}`
            : fallback;
    }
})();
