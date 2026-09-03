(() => {
    'use strict';

    document.addEventListener('DOMContentLoaded', () => {
        const form = document.getElementById('signupForm');
        const submitButton = document.getElementById('signupBtn');
        const passwordInput = document.getElementById('password');
        const strengthBar = document.getElementById('strengthBar');
        const institutionType = document.getElementById('institutionType');
        if (!form || !submitButton) return;

        loadInstitutionTypes();

        passwordInput?.addEventListener('input', () => {
            const value = passwordInput.value;
            let score = 0;
            if (value.length >= 6) score++;
            if (value.length >= 10) score++;
            if (/[A-Z]/.test(value)) score++;
            if (/[0-9]/.test(value)) score++;
            if (/[^A-Za-z0-9]/.test(value)) score++;

            const levels = ['danger', 'danger', 'warning', 'info', 'success', 'success'];
            const labels = [
                '',
                form.dataset.veryWeak,
                form.dataset.weak,
                form.dataset.fair,
                form.dataset.strong,
                form.dataset.veryStrong
            ];

            if (strengthBar) {
                strengthBar.style.width = `${score * 20}%`;
                strengthBar.className = `progress-bar bg-${levels[score]}`;
            }
            const strengthLabel = document.getElementById('strengthLabel');
            if (strengthLabel) strengthLabel.textContent = labels[score] || '';
        });

        form.addEventListener('submit', async event => {
            event.preventDefault();
            clearErrors();

            const data = {
                institutionName: valueOf('institutionName'),
                ownerName: valueOf('ownerName'),
                email: valueOf('email'),
                phone: normalizeBanglaDigits(valueOf('phone')),
                password: document.getElementById('password')?.value || '',
                confirmPassword: document.getElementById('confirmPassword')?.value || '',
                institutionType: valueOf('institutionType'),
                agreeTerms: document.getElementById('agreeTerms')?.checked ?? false
            };

            let isValid = true;
            if (!data.institutionName) isValid = showError('institutionName', form.dataset.institutionRequired);
            if (!data.institutionType) isValid = showError('institutionType', form.dataset.institutionTypeRequired);
            if (!data.ownerName) isValid = showError('ownerName', form.dataset.ownerRequired);
            if (!isValidEmail(data.email)) isValid = showError('email', form.dataset.emailRequired);
            if (data.password.length < 6) isValid = showError('password', form.dataset.passwordMin);
            if (data.password !== data.confirmPassword) isValid = showError('confirmPassword', form.dataset.passwordMismatch);
            if (!data.agreeTerms) isValid = showError('agreeTerms', form.dataset.termsRequired);
            if (!isValid) return;

            setLoading(submitButton, true);

            try {
                const response = await fetch('/api/institution-onboarding/signup', {
                    method: 'POST',
                    cache: 'no-store',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                    body: JSON.stringify(data)
                });
                const payload = await response.json().catch(() => null);

                if (response.ok && payload?.success) {
                    window.location.assign('/Account/SignupSuccess');
                    return;
                }

                const normalizedMessage = String(payload?.message || '').toLowerCase();
                showAlert(normalizedMessage.includes('already exists')
                    ? form.dataset.accountExists
                    : form.dataset.signupFailed);
            } catch {
                showAlert(form.dataset.networkMessage);
            } finally {
                setLoading(submitButton, false);
            }
        });

        async function loadInstitutionTypes() {
            if (!institutionType) return;

            try {
                const response = await fetch('/api/platform-catalog/institution-types', {
                    credentials: 'same-origin',
                    headers: { 'Accept': 'application/json' }
                });
                const payload = await response.json().catch(() => null);
                if (!response.ok || !payload?.success || !Array.isArray(payload.data)) {
                    throw new Error('Invalid institution type response');
                }

                const placeholder = document.createElement('option');
                placeholder.value = '';
                placeholder.textContent = form.dataset.selectType || '';
                institutionType.replaceChildren(placeholder);

                const useBangla = document.documentElement.lang.toLowerCase().startsWith('bn');
                payload.data.forEach(type => {
                    const option = document.createElement('option');
                    option.value = type.code;
                    option.textContent = useBangla && type.nameBangla ? type.nameBangla : type.name;
                    institutionType.append(option);
                });
                institutionType.disabled = false;
            } catch (error) {
                console.warn('EduOS institution catalogue was unavailable.', error);
                institutionType.disabled = true;
                showAlert(form.dataset.catalogFailed);
            }
        }
    }, { once: true });

    function valueOf(id) {
        return document.getElementById(id)?.value?.trim() ?? '';
    }

    function normalizeBanglaDigits(value) {
        return value.replace(/[০-৯]/g, digit => String('০১২৩৪৫৬৭৮৯'.indexOf(digit)));
    }

    function isValidEmail(email) {
        return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
    }

    function showError(fieldId, message) {
        const field = document.getElementById(fieldId);
        if (!field) return false;
        field.classList.add('is-invalid');
        const wrapper = field.closest('.form-check') || field.parentElement;
        const feedback = wrapper?.querySelector('.invalid-feedback');
        if (feedback) feedback.textContent = message || '';
        return false;
    }

    function clearErrors() {
        document.querySelectorAll('.is-invalid').forEach(element => element.classList.remove('is-invalid'));
        document.querySelectorAll('.invalid-feedback').forEach(element => { element.textContent = ''; });
        const alert = document.getElementById('alertContainer');
        if (alert) alert.className = 'd-none';
    }

    function showAlert(message) {
        const container = document.getElementById('alertContainer');
        if (!container) return;
        container.className = 'alert alert-danger';
        container.textContent = message || '';
        container.focus();
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
