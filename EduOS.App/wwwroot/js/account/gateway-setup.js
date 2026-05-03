// ============================================================
// GATEWAY-SETUP.JS - Onboarding Step 8 (Optional)
// ============================================================

document.addEventListener('DOMContentLoaded', async function () {

    await Promise.all([loadSmsSettings(), loadEmailSettings()]);
    loadOnboardingStatus?.();

    // ── SMS Save ──────────────────────────────────────────────
    document.getElementById('saveSmsBtn')?.addEventListener('click', async function () {
        const dto = {
            provider: val('smsProvider'),
            apiUrl: val('smsApiUrl'),
            apiKey: val('smsApiKey'),
            senderId: val('smsSenderId'),
            isEnabled: document.getElementById('smsEnabled')?.checked ?? false
        };
        setLoading(this, true, 'Saving...');
        try {
            const res = await fetch('/api/tenant-settings/sms-gateway', {
                method: 'PUT', credentials: 'include',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(dto)
            });
            const json = await res.json();
            showAlert(json.success ? 'success' : 'danger', json.message);
        } catch { showAlert('danger', 'Network error'); }
        finally { setLoading(this, false, '<i class="bi bi-save"></i> Save SMS settings'); }
    });

    // ── Email Save ────────────────────────────────────────────
    document.getElementById('saveEmailBtn')?.addEventListener('click', async function () {
        const dto = {
            smtpHost: val('smtpHost'),
            smtpPort: parseInt(val('smtpPort')) || null,
            smtpUsername: val('smtpUsername'),
            smtpPassword: val('smtpPassword'),
            fromEmail: val('fromEmail'),
            fromName: val('fromName'),
            useSsl: document.getElementById('useSsl')?.checked ?? true,
            isEnabled: document.getElementById('emailEnabled')?.checked ?? false
        };
        setLoading(this, true, 'Saving...');
        try {
            const res = await fetch('/api/tenant-settings/email-gateway', {
                method: 'PUT', credentials: 'include',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(dto)
            });
            const json = await res.json();
            showAlert(json.success ? 'success' : 'danger', json.message);
        } catch { showAlert('danger', 'Network error'); }
        finally { setLoading(this, false, '<i class="bi bi-save"></i> Save email settings'); }
    });

    // ── Test SMS ──────────────────────────────────────────────
    document.getElementById('testSmsBtn')?.addEventListener('click', async function () {
        const phone = prompt('Enter phone number to test (e.g., 01700000000):');
        if (!phone) return;
        setLoading(this, true, 'Sending...');
        try {
            const res = await fetch('/api/tenant-settings/sms-gateway/test', {
                method: 'POST', credentials: 'include',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ phone })
            });
            const json = await res.json();
            showAlert(json.success ? 'success' : 'danger', json.message || 'Test sent');
        } catch { showAlert('danger', 'Network error'); }
        finally { setLoading(this, false, 'Send test SMS'); }
    });

    // ── Finish / Skip ─────────────────────────────────────────
    document.getElementById('finishBtn')?.addEventListener('click', finishOnboarding);
    document.getElementById('skipBtn')?.addEventListener('click', finishOnboarding);
});

async function loadSmsSettings() {
    try {
        const res = await fetch('/api/tenant-settings/sms-gateway', { credentials: 'include' });
        const json = await res.json();
        if (!json.success || !json.data) return;
        const d = json.data;
        setSelect('smsProvider', d.provider);
        setVal('smsApiUrl', d.apiUrl);
        setVal('smsApiKey', d.apiKey ? '••••••••' : ''); // mask API key
        setVal('smsSenderId', d.senderId);
        const toggle = document.getElementById('smsEnabled');
        if (toggle) toggle.checked = d.isEnabled ?? false;
    } catch { /* ignore */ }
}

async function loadEmailSettings() {
    try {
        const res = await fetch('/api/tenant-settings/email-gateway', { credentials: 'include' });
        const json = await res.json();
        if (!json.success || !json.data) return;
        const d = json.data;
        setVal('smtpHost', d.smtpHost);
        setVal('smtpPort', d.smtpPort);
        setVal('smtpUsername', d.smtpUsername);
        setVal('smtpPassword', d.smtpPassword ? '••••••••' : '');
        setVal('fromEmail', d.fromEmail);
        setVal('fromName', d.fromName);
        const ssl = document.getElementById('useSsl');
        if (ssl) ssl.checked = d.useSsl !== false;
        const toggle = document.getElementById('emailEnabled');
        if (toggle) toggle.checked = d.isEnabled ?? false;
    } catch { /* ignore */ }
}

async function finishOnboarding() {
    const btn = document.getElementById('finishBtn');
    setLoading(btn, true, 'Finishing...');
    try {
        // Mark gateway step complete
        await fetch('/api/onboarding/complete-step', {
            method: 'POST', credentials: 'include',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ step: 8, skipped: true })
        });

        // Complete entire onboarding
        const res = await fetch('/api/onboarding/complete', {
            method: 'POST', credentials: 'include'
        });
        const json = await res.json();

        if (json.success) {
            window.location.href = '/Account/OnboardingComplete';
        } else {
            showAlert('danger', json.message || 'Could not complete onboarding. Please check required steps.');
            setLoading(btn, false, 'Finish setup <i class="bi bi-check-lg ms-1"></i>');
        }
    } catch {
        showAlert('danger', 'Network error. Please try again.');
        setLoading(btn, false, 'Finish setup');
    }
}

function val(id) { return document.getElementById(id)?.value?.trim() ?? ''; }
function setVal(id, v) { const el = document.getElementById(id); if (el) el.value = v ?? ''; }
function setSelect(id, v) { const el = document.getElementById(id); if (el && v) el.value = v; }
function showAlert(type, msg) {
    const c = document.getElementById('alertContainer');
    if (c) c.innerHTML = `<div class="alert alert-${type} alert-dismissible">
        ${msg}<button type="button" class="btn-close" data-bs-dismiss="alert"></button></div>`;
    setTimeout(() => { if (c) c.innerHTML = ''; }, 4000);
}
function setLoading(btn, loading, label) {
    if (!btn) return;
    btn.disabled = loading;
    btn.innerHTML = loading ? `<span class="spinner-border spinner-border-sm me-2"></span>${label}` : label;
}
