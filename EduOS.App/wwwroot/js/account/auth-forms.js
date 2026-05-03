// ============================================================
// FORGOT-PASSWORD.JS
// ============================================================

(function forgotPassword() {
    const form = document.getElementById('forgotPasswordForm');
    const btn = document.getElementById('forgotBtn');
    if (!form) return;

    form.addEventListener('submit', async function (e) {
        e.preventDefault();
        const email = document.getElementById('email')?.value?.trim();
        if (!email) { showAlert('danger', 'Email is required.'); return; }

        setLoading(btn, true, 'Sending...');

        try {
            const res = await fetch('/api/auth/forgot-password', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email })
            });
            const json = await res.json();
            if (json.success) {
                form.innerHTML = `
                    <div class="text-center py-4">
                        <div class="mb-3" style="font-size:48px">📧</div>
                        <h5>Check your email</h5>
                        <p class="text-muted">If an account exists with <strong>${escHtml(email)}</strong>,
                        a password reset link has been sent.</p>
                        <a href="/Account/Login" class="btn btn-outline-primary">Back to login</a>
                    </div>`;
            } else {
                showAlert('danger', json.message || 'Request failed.');
                setLoading(btn, false, 'Send reset link');
            }
        } catch {
            showAlert('danger', 'Network error. Please try again.');
            setLoading(btn, false, 'Send reset link');
        }
    });
})();


// ============================================================
// RESET-PASSWORD.JS
// ============================================================

(function resetPassword() {
    const form = document.getElementById('resetPasswordForm');
    const btn = document.getElementById('resetBtn');
    if (!form) return;

    form.addEventListener('submit', async function (e) {
        e.preventDefault();

        const email = document.getElementById('resetEmail')?.value?.trim()
            || new URLSearchParams(window.location.search).get('email') || '';
        const token = document.getElementById('resetToken')?.value?.trim()
            || new URLSearchParams(window.location.search).get('token') || '';
        const newPassword = document.getElementById('newPassword')?.value;
        const confirmPassword = document.getElementById('confirmPassword')?.value;

        if (!newPassword || newPassword.length < 6) {
            showFieldError('newPassword', 'Password must be at least 6 characters.'); return;
        }
        if (newPassword !== confirmPassword) {
            showFieldError('confirmPassword', 'Passwords do not match.'); return;
        }

        setLoading(btn, true, 'Resetting...');

        try {
            const res = await fetch('/api/auth/reset-password', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email, token, newPassword, confirmPassword })
            });
            const json = await res.json();

            if (json.success) {
                window.location.href = '/Account/Login?message='
                    + encodeURIComponent('Password reset successful. Please sign in.');
            } else {
                showAlert('danger', json.message || 'Reset failed.');
                setLoading(btn, false, 'Reset password');
            }
        } catch {
            showAlert('danger', 'Network error. Please try again.');
            setLoading(btn, false, 'Reset password');
        }
    });

    function showFieldError(id, msg) {
        const el = document.getElementById(id);
        if (!el) return;
        el.classList.add('is-invalid');
        const fb = el.nextElementSibling;
        if (fb?.classList.contains('invalid-feedback')) fb.textContent = msg;
    }
})();


// ============================================================
// SHARED HELPERS (used by both above IIFEs)
// ============================================================

function showAlert(type, msg) {
    const c = document.getElementById('alertContainer');
    if (!c) return;
    c.innerHTML = `<div class="alert alert-${type} alert-dismissible">
        ${msg}<button type="button" class="btn-close" data-bs-dismiss="alert"></button></div>`;
}

function setLoading(btn, loading, label) {
    if (!btn) return;
    btn.disabled = loading;
    btn.innerHTML = loading
        ? `<span class="spinner-border spinner-border-sm me-2"></span>${label}`
        : label;
}

function escHtml(s) {
    return (s || '').replace(/[&<>"']/g, c =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}
