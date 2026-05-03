// ============================================================
// SIGNUP.JS - Institution Signup
// ============================================================

document.addEventListener('DOMContentLoaded', function () {

    const form = document.getElementById('signupForm');
    const submitBtn = document.getElementById('signupBtn');
    const passwordInput = document.getElementById('password');
    const confirmInput = document.getElementById('confirmPassword');
    const strengthBar = document.getElementById('strengthBar');

    // ── Password strength indicator ──────────────────────────
    if (passwordInput && strengthBar) {
        passwordInput.addEventListener('input', function () {
            const val = passwordInput.value;
            let score = 0;
            if (val.length >= 6) score++;
            if (val.length >= 10) score++;
            if (/[A-Z]/.test(val)) score++;
            if (/[0-9]/.test(val)) score++;
            if (/[^A-Za-z0-9]/.test(val)) score++;

            const levels = ['', 'danger', 'warning', 'info', 'success', 'success'];
            const labels = ['', 'Very weak', 'Weak', 'Fair', 'Strong', 'Very strong'];

            strengthBar.style.width = (score * 20) + '%';
            strengthBar.className = 'progress-bar bg-' + (levels[score] || 'danger');
            const label = document.getElementById('strengthLabel');
            if (label) label.textContent = labels[score] || '';
        });
    }

    // ── Form submit ───────────────────────────────────────────
    if (form) {
        form.addEventListener('submit', async function (e) {
            e.preventDefault();
            clearErrors();

            const data = {
                institutionName: val('institutionName'),
                ownerName: val('ownerName'),
                email: val('email'),
                phone: val('phone'),
                password: val('password'),
                confirmPassword: val('confirmPassword'),
                institutionType: val('institutionType'),
                agreeTerms: document.getElementById('agreeTerms')?.checked
            };

            // Client-side validation
            let valid = true;
            if (!data.institutionName) { showError('institutionName', 'Institution name is required'); valid = false; }
            if (!data.ownerName) { showError('ownerName', 'Owner name is required'); valid = false; }
            if (!data.email || !isValidEmail(data.email)) { showError('email', 'Valid email is required'); valid = false; }
            if (!data.password || data.password.length < 6) { showError('password', 'Password must be at least 6 characters'); valid = false; }
            if (data.password !== data.confirmPassword) { showError('confirmPassword', 'Passwords do not match'); valid = false; }
            if (!data.agreeTerms) { showError('agreeTerms', 'You must agree to the terms'); valid = false; }
            if (!valid) return;

            setLoading(submitBtn, true, 'Creating account...');

            try {
                const res = await fetch('/api/institution-onboarding/signup', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(data)
                });

                const json = await res.json();

                if (json.success) {
                    window.location.href = '/Account/SignupSuccess';
                } else {
                    showAlert('danger', json.message || 'Signup failed. Please try again.');
                }
            } catch {
                showAlert('danger', 'Network error. Please try again.');
            } finally {
                setLoading(submitBtn, false, 'Create account');
            }
        });
    }

    // ── Helpers ───────────────────────────────────────────────
    function val(id) {
        return document.getElementById(id)?.value?.trim() ?? '';
    }

    function isValidEmail(email) {
        return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
    }

    function showError(fieldId, msg) {
        const field = document.getElementById(fieldId);
        if (!field) return;
        field.classList.add('is-invalid');
        const fb = field.nextElementSibling;
        if (fb && fb.classList.contains('invalid-feedback')) fb.textContent = msg;
    }

    function clearErrors() {
        document.querySelectorAll('.is-invalid').forEach(el => el.classList.remove('is-invalid'));
    }

    function showAlert(type, msg) {
        let container = document.getElementById('alertContainer');
        if (!container) {
            container = document.createElement('div');
            container.id = 'alertContainer';
            form?.prepend(container);
        }
        container.innerHTML = `<div class="alert alert-${type} alert-dismissible">
            ${msg}<button type="button" class="btn-close" data-bs-dismiss="alert"></button></div>`;
    }

    function setLoading(btn, loading, label) {
        if (!btn) return;
        btn.disabled = loading;
        btn.innerHTML = loading
            ? `<span class="spinner-border spinner-border-sm me-2"></span>${label}`
            : label;
    }
});
