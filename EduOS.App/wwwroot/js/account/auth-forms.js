(() => {
    'use strict';

    document.addEventListener('DOMContentLoaded', () => {
        setupForgotPassword();
        setupResetPassword();
    }, { once: true });

    function setupForgotPassword() {
        const form = document.getElementById('forgotPasswordForm');
        const button = document.getElementById('forgotBtn');
        if (!form || !button) return;

        form.addEventListener('submit', async event => {
            event.preventDefault();
            const email = document.getElementById('email')?.value?.trim();
            if (!email) {
                showAlert('danger', form.dataset.emailRequired);
                return;
            }

            setLoading(button, true);
            try {
                const response = await fetch('/api/auth/forgot-password', {
                    method: 'POST',
                    cache: 'no-store',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                    body: JSON.stringify({ email })
                });

                if (!response.ok) throw new Error(`HTTP ${response.status}`);
                showResetEmailSent(form);
            } catch (error) {
                console.warn('EduOS password reset request was unavailable.', error);
                showAlert('danger', form.dataset.networkMessage || form.dataset.requestFailed);
                setLoading(button, false);
            }
        });
    }

    function showResetEmailSent(form) {
        const wrapper = document.createElement('div');
        wrapper.className = 'text-center py-3';

        const mark = document.createElement('div');
        mark.className = 'status-mark';
        mark.setAttribute('aria-hidden', 'true');
        mark.textContent = '✉';

        const title = document.createElement('h2');
        title.className = 'h5';
        title.textContent = form.dataset.sentTitle || '';

        const message = document.createElement('p');
        message.className = 'auth-meta';
        message.textContent = form.dataset.sentMessage || '';

        const link = document.createElement('a');
        link.href = '/Account/Login';
        link.className = 'btn btn-outline-primary mt-2';
        link.textContent = form.dataset.backLabel || '';

        wrapper.append(mark, title, message, link);
        form.replaceWith(wrapper);
    }

    function setupResetPassword() {
        const form = document.getElementById('resetPasswordForm');
        const button = document.getElementById('resetBtn');
        if (!form || !button) return;

        form.addEventListener('submit', async event => {
            event.preventDefault();
            clearFieldErrors(form);

            const email = document.getElementById('resetEmail')?.value?.trim() || '';
            const token = document.getElementById('resetToken')?.value || '';
            const newPassword = document.getElementById('newPassword')?.value || '';
            const confirmPassword = document.getElementById('confirmPassword')?.value || '';

            if (newPassword.length < 6) {
                showFieldError('newPassword', form.dataset.minLengthMessage);
                return;
            }
            if (newPassword !== confirmPassword) {
                showFieldError('confirmPassword', form.dataset.mismatchMessage);
                return;
            }

            setLoading(button, true);
            try {
                const response = await fetch('/api/auth/reset-password', {
                    method: 'POST',
                    cache: 'no-store',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                    body: JSON.stringify({ email, token, newPassword, confirmPassword })
                });
                const payload = await response.json().catch(() => null);

                if (response.ok && payload?.success) {
                    window.location.assign('/Account/Login?status=password-reset');
                    return;
                }
                showAlert('danger', form.dataset.resetFailed);
            } catch {
                showAlert('danger', form.dataset.networkMessage);
            } finally {
                setLoading(button, false);
            }
        });
    }

    function showFieldError(id, message) {
        const field = document.getElementById(id);
        if (!field) return;
        field.classList.add('is-invalid');
        const feedback = field.parentElement?.querySelector('.invalid-feedback');
        if (feedback) feedback.textContent = message || '';
    }

    function clearFieldErrors(form) {
        form.querySelectorAll('.is-invalid').forEach(element => element.classList.remove('is-invalid'));
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
