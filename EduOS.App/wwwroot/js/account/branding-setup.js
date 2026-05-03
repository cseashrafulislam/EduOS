// ============================================================
// BRANDING-SETUP.JS - Onboarding Step 6
// ============================================================

document.addEventListener('DOMContentLoaded', async function () {

    await loadProfile();
    loadOnboardingStatus?.();
    initColorPickers();

    // ── Subdomain live check ──────────────────────────────────
    let checkTimer;
    document.getElementById('subdomainInput')?.addEventListener('input', function () {
        clearTimeout(checkTimer);
        const val = this.value.trim().toLowerCase();
        const msg = document.getElementById('availabilityMsg');
        const btn = document.getElementById('saveSubdomainBtn');
        if (msg) { msg.className = 'availability-msg'; msg.textContent = ''; }
        if (btn) btn.disabled = true;
        if (val.length < 3) return;

        checkTimer = setTimeout(async () => {
            if (msg) msg.textContent = 'Checking...';
            try {
                const res = await fetch(
                    `/api/tenant-profile/subdomain/check?subdomain=${encodeURIComponent(val)}`,
                    { credentials: 'include' });
                const json = await res.json();
                if (json.success && json.data) {
                    if (msg) {
                        msg.textContent = json.data.message;
                        msg.className = 'availability-msg ' + (json.data.isAvailable ? 'ok' : 'bad');
                    }
                    if (btn) btn.disabled = !json.data.isAvailable;
                }
            } catch { if (msg) msg.textContent = 'Check failed'; }
        }, 400);
    });

    // ── Save subdomain ────────────────────────────────────────
    document.getElementById('saveSubdomainBtn')?.addEventListener('click', async function () {
        const subdomain = document.getElementById('subdomainInput')?.value.trim().toLowerCase();
        if (!subdomain) return;
        setLoading(this, true, 'Saving...');
        try {
            const res = await fetch('/api/tenant-profile/subdomain', {
                method: 'PUT', credentials: 'include',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ subdomain })
            });
            const json = await res.json();
            showAlert(json.success ? 'success' : 'danger', json.message);
        } catch { showAlert('danger', 'Network error.'); }
        finally { setLoading(this, false, 'Save subdomain'); }
    });

    // ── Logo upload ───────────────────────────────────────────
    setupUpload('uploadLogoBtn', 'logoInput', 'logoPreview',
        '/api/tenant-profile/logo', 'removeLogoBtn', 'bi bi-cloud-upload');

    // ── Favicon upload ────────────────────────────────────────
    setupUpload('uploadFaviconBtn', 'faviconInput', 'faviconPreview',
        '/api/tenant-profile/favicon', 'removeFaviconBtn', 'bi bi-window');

    // ── Remove logo/favicon ───────────────────────────────────
    document.getElementById('removeLogoBtn')?.addEventListener('click', async () => {
        if (!confirm('Remove logo?')) return;
        await fetch('/api/tenant-profile/logo', { method: 'DELETE', credentials: 'include' });
        clearPreview('logoPreview', 'bi bi-cloud-upload');
        document.getElementById('removeLogoBtn').style.display = 'none';
    });

    document.getElementById('removeFaviconBtn')?.addEventListener('click', async () => {
        if (!confirm('Remove favicon?')) return;
        await fetch('/api/tenant-profile/favicon', { method: 'DELETE', credentials: 'include' });
        clearPreview('faviconPreview', 'bi bi-window');
        document.getElementById('removeFaviconBtn').style.display = 'none';
    });

    // ── Save colors + advance ─────────────────────────────────
    document.getElementById('brandingForm')?.addEventListener('submit', async function (e) {
        e.preventDefault();
        const dto = {
            primaryColor: document.getElementById('primaryColorText')?.value,
            secondaryColor: document.getElementById('secondaryColorText')?.value,
            accentColor: document.getElementById('accentColorText')?.value
        };
        const btn = document.getElementById('saveBrandingBtn');
        setLoading(btn, true, 'Saving...');
        try {
            const res = await fetch('/api/tenant-profile/branding', {
                method: 'PUT', credentials: 'include',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(dto)
            });
            const json = await res.json();
            if (json.success) {
                await advanceStep(6);
            } else {
                showAlert('danger', json.message || 'Save failed');
                setLoading(btn, false, 'Save & continue');
            }
        } catch {
            showAlert('danger', 'Network error');
            setLoading(btn, false, 'Save & continue');
        }
    });

    document.getElementById('skipBtn')?.addEventListener('click', () => advanceStep(6, true));
});

// ── Init color pickers ────────────────────────────────────────
function initColorPickers() {
    ['primary', 'secondary', 'accent'].forEach(prefix => {
        const picker = document.getElementById(prefix + 'Color');
        const text = document.getElementById(prefix + 'ColorText');
        if (!picker || !text) return;
        picker.addEventListener('input', () => text.value = picker.value.toUpperCase());
        text.addEventListener('input', () => {
            if (/^#[0-9A-Fa-f]{6}$/.test(text.value)) picker.value = text.value;
        });
    });
}

// ── Load profile ──────────────────────────────────────────────
async function loadProfile() {
    try {
        const res = await fetch('/api/tenant-profile', { credentials: 'include' });
        const json = await res.json();
        if (!json.success || !json.data) return;
        const d = json.data;

        const sub = document.getElementById('subdomainInput');
        if (sub) sub.value = d.subdomain || '';

        setColor('primary', d.primaryColor || '#1E40AF');
        setColor('secondary', d.secondaryColor || '#64748B');
        setColor('accent', d.accentColor || '#F59E0B');

        if (d.logoUrl) { showPreview('logoPreview', d.logoUrl); document.getElementById('removeLogoBtn').style.display = 'inline-block'; }
        if (d.faviconUrl) { showPreview('faviconPreview', d.faviconUrl); document.getElementById('removeFaviconBtn').style.display = 'inline-block'; }
    } catch { /* ignore */ }
}

function setColor(prefix, value) {
    const picker = document.getElementById(prefix + 'Color');
    const text = document.getElementById(prefix + 'ColorText');
    if (picker) picker.value = value;
    if (text) text.value = value;
}

// ── Upload helper ─────────────────────────────────────────────
function setupUpload(btnId, inputId, previewId, endpoint, removeBtnId, defaultIcon) {
    const btn = document.getElementById(btnId);
    const input = document.getElementById(inputId);
    if (!btn || !input) return;

    btn.addEventListener('click', () => input.click());
    input.addEventListener('change', async function () {
        if (!this.files?.length) return;
        const fd = new FormData();
        fd.append('file', this.files[0]);
        try {
            const res = await fetch(endpoint, { method: 'POST', credentials: 'include', body: fd });
            const json = await res.json();
            if (json.success) {
                showPreview(previewId, json.data);
                const rb = document.getElementById(removeBtnId);
                if (rb) rb.style.display = 'inline-block';
                showAlert('success', 'Uploaded successfully');
            } else {
                showAlert('danger', json.message || 'Upload failed');
            }
        } catch { showAlert('danger', 'Upload error'); }
    });
}

function showPreview(previewId, url) {
    const el = document.getElementById(previewId);
    if (el) el.innerHTML = `<img src="${url}" alt="preview" style="width:100%;height:100%;object-fit:contain" />`;
}
function clearPreview(previewId, iconClass) {
    const el = document.getElementById(previewId);
    if (el) el.innerHTML = `<i class="${iconClass}" style="font-size:24px;color:#94a3b8"></i>`;
}

async function advanceStep(step, skipped = false) {
    await fetch('/api/onboarding/complete-step', {
        method: 'POST', credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ step, skipped })
    });
    window.location.href = '/Account/GeneralSettings';
}

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
