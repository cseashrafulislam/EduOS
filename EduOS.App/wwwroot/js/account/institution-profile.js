// ============================================================
// INSTITUTION-PROFILE.JS - Onboarding Step 1
// ============================================================

document.addEventListener('DOMContentLoaded', async function () {

    await loadProfile();

    const form = document.getElementById('profileForm');
    if (form) {
        form.addEventListener('submit', async function (e) {
            e.preventDefault();
            clearErrors();

            const dto = {
                institutionName: val('institutionName'),
                institutionType: val('institutionType'),
                ownerName: val('ownerName'),
                ownerPhone: val('ownerPhone'),
                ownerEmail: val('ownerEmail'),
                ownerDesignation: val('ownerDesignation'),
                phone: val('phone'),
                website: val('website'),
                address: val('address'),
                city: val('city'),
                state: val('state'),
                country: val('country'),
                postalCode: val('postalCode')
            };

            // Validation
            let valid = true;
            if (!dto.institutionName) { showError('institutionName', 'Institution name is required'); valid = false; }
            if (!dto.institutionType) { showError('institutionType', 'Institution type is required'); valid = false; }
            if (!dto.ownerName) { showError('ownerName', 'Owner name is required'); valid = false; }
            if (!valid) return;

            const btn = document.getElementById('saveProfileBtn');
            setLoading(btn, true, 'Saving...');

            try {
                // Save profile
                const res = await fetch('/api/institution-onboarding/institution-profile', {
                    method: 'POST',
                    credentials: 'include',
                    body: buildFormData(dto),
                });
                const json = await res.json();

                if (!json.success) {
                    showAlert('danger', json.message || 'Save failed');
                    setLoading(btn, false, 'Save & continue');
                    return;
                }

                // Advance onboarding step
                await fetch('/api/onboarding/complete-step', {
                    method: 'POST',
                    credentials: 'include',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ step: 1, skipped: false })
                });

                window.location.href = '/Account/PlanSelection';
            } catch {
                showAlert('danger', 'Network error. Please try again.');
                setLoading(btn, false, 'Save & continue');
            }
        });
    }

    async function loadProfile() {
        try {
            const res = await fetch('/api/institution-onboarding/institution-profile',
                { credentials: 'include' });
            const json = await res.json();
            if (!json.success || !json.data) return;

            const d = json.data;
            setVal('institutionName', d.institutionName);
            setVal('institutionType', d.institutionType);
            setVal('ownerName', d.ownerName);
            setVal('ownerPhone', d.ownerPhone);
            setVal('ownerEmail', d.ownerEmail);
            setVal('ownerDesignation', d.ownerDesignation);
            setVal('phone', d.phone);
            setVal('website', d.website);
            setVal('address', d.address);
            setVal('city', d.city);
            setVal('state', d.state);
            setVal('country', d.country);
            setVal('postalCode', d.postalCode);
        } catch { /* ignore */ }
    }

    function buildFormData(obj) {
        const fd = new FormData();
        Object.entries(obj).forEach(([k, v]) => { if (v != null) fd.append(k, v); });
        return fd;
    }

    function val(id) { return document.getElementById(id)?.value?.trim() ?? ''; }
    function setVal(id, v) { const el = document.getElementById(id); if (el && v) el.value = v; }

    function showError(id, msg) {
        const el = document.getElementById(id);
        if (!el) return;
        el.classList.add('is-invalid');
        const fb = el.nextElementSibling;
        if (fb?.classList.contains('invalid-feedback')) fb.textContent = msg;
    }

    function clearErrors() {
        document.querySelectorAll('.is-invalid').forEach(el => el.classList.remove('is-invalid'));
    }
});

// Shared helpers
function showAlert(type, msg) {
    const c = document.getElementById('alertContainer');
    if (c) c.innerHTML = `<div class="alert alert-${type} alert-dismissible mb-3">
        ${msg}<button type="button" class="btn-close" data-bs-dismiss="alert"></button></div>`;
    setTimeout(() => { if (c) c.innerHTML = ''; }, 5000);
}

function setLoading(btn, loading, label) {
    if (!btn) return;
    btn.disabled = loading;
    btn.innerHTML = loading
        ? `<span class="spinner-border spinner-border-sm me-2"></span>${label}`
        : label;
}
