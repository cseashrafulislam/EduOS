// ============================================================
// LOGIN.JS - Login page
// ============================================================

document.addEventListener('DOMContentLoaded', function () {

    const form = document.getElementById('loginForm');
    const submitBtn = document.getElementById('loginBtn');

    // Pre-fill email from query string (e.g., after password reset)
    const params = new URLSearchParams(window.location.search);
    const emailParam = params.get('email');
    if (emailParam) {
        const emailField = document.getElementById('email');
        if (emailField) emailField.value = emailParam;
    }

    // Show success message if redirected from reset/verify
    const msg = params.get('message');
    if (msg) showAlert('success', decodeURIComponent(msg));

    if (form) {
        form.addEventListener('submit', async function (e) {
            e.preventDefault();

            const email = document.getElementById('email')?.value?.trim();
            const password = document.getElementById('password')?.value;
            const rememberMe = document.getElementById('rememberMe')?.checked ?? false;

            if (!email || !password) {
                showAlert('danger', 'Email and password are required.');
                return;
            }

            setLoading(submitBtn, true, 'Signing in...');

            try {
                const res = await fetch('/api/auth/login', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    credentials: 'include',
                    body: JSON.stringify({ email, password, rememberMe })
                });

                const json = await res.json();

                if (json.success) {
                    // Redirect to the URL returned by server (handles onboarding vs dashboard)
                    const redirectUrl = json.data?.redirectUrl
                        || params.get('returnUrl')
                        || '/Dashboard';
                    window.location.href = redirectUrl;
                } else {
                    showAlert('danger', json.message || 'Invalid email or password.');
                    setLoading(submitBtn, false, 'Sign in');
                }
            } catch {
                showAlert('danger', 'Network error. Please try again.');
                setLoading(submitBtn, false, 'Sign in');
            }
        });
    }

    // Toggle password visibility
    document.getElementById('togglePassword')?.addEventListener('click', function () {
        const pwField = document.getElementById('password');
        if (!pwField) return;
        const isText = pwField.type === 'text';
        pwField.type = isText ? 'password' : 'text';
        this.querySelector('i')?.classList.toggle('bi-eye', isText);
        this.querySelector('i')?.classList.toggle('bi-eye-slash', !isText);
    });

    function showAlert(type, msg) {
        const c = document.getElementById('alertContainer');
        if (!c) return;
        c.innerHTML = `<div class="alert alert-${type} alert-dismissible mb-3">
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
