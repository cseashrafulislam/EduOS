// ============================================================
// GENERAL-SETTINGS.JS - Onboarding Step 7
// ============================================================

const CURRENCY_SYMBOLS = {
    BDT: '৳', USD: '$', INR: '₹', GBP: '£', EUR: '€',
    AUD: 'A$', CAD: 'C$', SGD: 'S$', MYR: 'RM', AED: 'د.إ'
};

document.addEventListener('DOMContentLoaded', async function () {

    await loadSettings();
    loadOnboardingStatus?.();

    // Auto-fill currency symbol on change
    document.getElementById('currency')?.addEventListener('change', function () {
        const sym = CURRENCY_SYMBOLS[this.value];
        const symField = document.getElementById('currencySymbol');
        if (sym && symField) symField.value = sym;
    });

    // Save & continue
    document.getElementById('settingsForm')?.addEventListener('submit', async function (e) {
        e.preventDefault();

        const dto = {
            currency: val('currency'),
            currencySymbol: val('currencySymbol'),
            timeZone: val('timeZone'),
            language: val('language'),
            dateFormat: val('dateFormat')
        };

        if (!dto.currency) { showAlert('danger', 'Currency is required'); return; }
        if (!dto.timeZone) { showAlert('danger', 'Timezone is required'); return; }

        const btn = document.getElementById('saveSettingsBtn');
        setLoading(btn, true, 'Saving...');

        try {
            const res = await fetch('/api/tenant-profile/general-settings', {
                method: 'PUT', credentials: 'include',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(dto)
            });
            const json = await res.json();
            if (json.success) {
                await advanceStep();
            } else {
                showAlert('danger', json.message || 'Save failed');
                setLoading(btn, false, 'Save & continue');
            }
        } catch {
            showAlert('danger', 'Network error');
            setLoading(btn, false, 'Save & continue');
        }
    });

    document.getElementById('skipBtn')?.addEventListener('click', advanceStep);
});

async function loadSettings() {
    try {
        const res = await fetch('/api/tenant-profile', { credentials: 'include' });
        const json = await res.json();
        if (!json.success || !json.data) return;
        const d = json.data;
        setSelect('currency', d.currency || 'BDT');
        setVal('currencySymbol', d.currencySymbol || '৳');
        setSelect('timeZone', d.timeZone || 'Asia/Dhaka');
        setSelect('language', d.language || 'en');
        setSelect('dateFormat', d.dateFormat || 'dd-MM-yyyy');
    } catch { /* ignore */ }
}

async function advanceStep() {
    try {
        await fetch('/api/onboarding/complete-step', {
            method: 'POST', credentials: 'include',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ step: 7, skipped: false })
        });
        window.location.href = '/Account/GatewaySetup';
    } catch { showAlert('danger', 'Error advancing. Please try again.'); }
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
