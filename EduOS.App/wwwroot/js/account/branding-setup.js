(() => {
    'use strict';

    const stringsNode = document.getElementById('brandingSetupStrings');
    const i18n = stringsNode ? JSON.parse(stringsNode.textContent || '{}') : {};
    const alertContainer = document.getElementById('alertContainer');
    const subdomainInput = document.getElementById('subdomainInput');
    const availabilityMessage = document.getElementById('availabilityMsg');
    const saveSubdomainButton = document.getElementById('saveSubdomainBtn');
    const saveBrandingButton = document.getElementById('saveBrandingBtn');
    let onboarding = null;
    let savedSubdomain = '';
    let availableSubdomain = '';
    let checkTimer = 0;

    document.addEventListener('DOMContentLoaded', async () => {
        initializeColorInputs();
        initializeAssetControls();
        subdomainInput?.addEventListener('input', handleSubdomainInput);
        saveSubdomainButton?.addEventListener('click', saveSubdomain);
        document.getElementById('brandingForm')?.addEventListener('submit', saveBranding);
        await Promise.all([loadProfile(), loadOnboardingState()]);
        updatePrimaryAction();
    }, { once: true });

    function initializeColorInputs() {
        ['primary', 'secondary', 'accent'].forEach(prefix => {
            const picker = document.getElementById(`${prefix}Color`);
            const text = document.getElementById(`${prefix}ColorText`);
            picker?.addEventListener('input', () => {
                if (text) text.value = picker.value.toUpperCase();
            });
            text?.addEventListener('input', () => {
                const value = text.value.trim();
                if (/^#[0-9a-f]{6}$/i.test(value) && picker) picker.value = value;
            });
        });
    }

    function initializeAssetControls() {
        bindUpload('logo', '/api/tenant-profile/logo');
        bindUpload('favicon', '/api/tenant-profile/favicon');
        document.getElementById('removeLogoBtn')?.addEventListener('click', () =>
            removeAsset('logo', '/api/tenant-profile/logo', i18n.removeLogoConfirm));
        document.getElementById('removeFaviconBtn')?.addEventListener('click', () =>
            removeAsset('favicon', '/api/tenant-profile/favicon', i18n.removeFaviconConfirm));
    }

    async function loadProfile() {
        try {
            const response = await fetch('/api/tenant-profile', {
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data) throw new Error('profile');

            const profile = payload.data;
            savedSubdomain = String(profile.subdomain || '').toLowerCase();
            if (subdomainInput) subdomainInput.value = savedSubdomain;
            if (savedSubdomain) setAvailability(i18n.subdomainSaved, 'ok');
            setColor('primary', profile.primaryColor, '#1E40AF');
            setColor('secondary', profile.secondaryColor, '#64748B');
            setColor('accent', profile.accentColor, '#F59E0B');
            renderAsset('logo', profile.logoUrl);
            renderAsset('favicon', profile.faviconUrl);
        } catch {
            showAlert('danger', i18n.loadFailed);
        }
    }

    async function loadOnboardingState() {
        try {
            const response = await fetch('/api/onboarding/status', {
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            onboarding = response.ok && payload?.success ? payload.data : null;
        } catch {
            onboarding = null;
        }
    }

    function handleSubdomainInput() {
        window.clearTimeout(checkTimer);
        availableSubdomain = '';
        if (saveSubdomainButton) saveSubdomainButton.disabled = true;
        const value = normalizedSubdomain();

        if (value === savedSubdomain && value) {
            setAvailability(i18n.subdomainSaved, 'ok');
            return;
        }
        setAvailability('', '');
        if (!subdomainInput?.checkValidity()) return;

        checkTimer = window.setTimeout(() => checkSubdomain(value), 400);
    }

    async function checkSubdomain(value) {
        setAvailability(i18n.checking, '');
        try {
            const response = await fetch(
                `/api/tenant-profile/subdomain/check?subdomain=${encodeURIComponent(value)}`,
                {
                    cache: 'no-store',
                    credentials: 'same-origin',
                    headers: { 'Accept': 'application/json' }
                });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data) throw new Error('check');
            if (normalizedSubdomain() !== value) return;

            availableSubdomain = payload.data.isAvailable ? value : '';
            setAvailability(
                payload.data.isAvailable ? i18n.available : i18n.unavailable,
                payload.data.isAvailable ? 'ok' : 'bad');
            if (saveSubdomainButton) saveSubdomainButton.disabled = !availableSubdomain;
        } catch {
            setAvailability(i18n.checkFailed, 'bad');
        }
    }

    async function saveSubdomain() {
        const subdomain = normalizedSubdomain();
        if (!subdomain || subdomain !== availableSubdomain) return;
        setButtonLoading(saveSubdomainButton, true, i18n.saving);
        try {
            const response = await fetch('/api/tenant-profile/subdomain', {
                method: 'PUT',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json', 'Content-Type': 'application/json' },
                body: JSON.stringify({ subdomain })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error('save');
            savedSubdomain = subdomain;
            availableSubdomain = '';
            setAvailability(i18n.subdomainSaved, 'ok');
            showAlert('success', i18n.subdomainSaved);
        } catch {
            showAlert('danger', i18n.subdomainSaveFailed);
        } finally {
            setButtonLoading(saveSubdomainButton, false, i18n.saveSubdomain);
            if (saveSubdomainButton) saveSubdomainButton.disabled = true;
        }
    }

    function bindUpload(type, endpoint) {
        const capitalized = type[0].toUpperCase() + type.slice(1);
        const input = document.getElementById(`${type}Input`);
        document.getElementById(`upload${capitalized}Btn`)?.addEventListener('click', () => input?.click());
        document.getElementById(`${type}Preview`)?.addEventListener('click', () => input?.click());
        input?.addEventListener('change', async () => {
            const file = input.files?.[0];
            if (!file) return;
            const body = new FormData();
            body.append('file', file);
            try {
                const response = await fetch(endpoint, {
                    method: 'POST',
                    cache: 'no-store',
                    credentials: 'same-origin',
                    headers: { 'Accept': 'application/json' },
                    body
                });
                const payload = await response.json().catch(() => null);
                if (!response.ok || !payload?.success || !payload.data) throw new Error('upload');
                renderAsset(type, payload.data);
                showAlert('success', i18n.uploadSuccess);
            } catch {
                showAlert('danger', i18n.uploadFailed);
            } finally {
                input.value = '';
            }
        });
    }

    async function removeAsset(type, endpoint, confirmation) {
        if (!window.confirm(confirmation || '')) return;
        try {
            const response = await fetch(endpoint, {
                method: 'DELETE',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error('remove');
            renderAsset(type, null);
        } catch {
            showAlert('danger', i18n.removeFailed);
        }
    }

    function renderAsset(type, value) {
        const preview = document.getElementById(`${type}Preview`);
        const capitalized = type[0].toUpperCase() + type.slice(1);
        const removeButton = document.getElementById(`remove${capitalized}Btn`);
        if (!preview) return;

        const safeUrl = safeAssetUrl(value);
        if (safeUrl) {
            const image = document.createElement('img');
            image.src = safeUrl;
            image.alt = type === 'logo' ? i18n.logoAlt : i18n.faviconAlt;
            image.loading = 'lazy';
            preview.replaceChildren(image);
        } else {
            const placeholder = document.createElement('span');
            placeholder.className = 'brand-asset-placeholder';
            placeholder.setAttribute('aria-hidden', 'true');
            placeholder.textContent = type === 'logo' ? 'L' : 'F';
            preview.replaceChildren(placeholder);
        }
        if (removeButton) removeButton.hidden = !safeUrl;
    }

    async function saveBranding(event) {
        event.preventDefault();
        setButtonLoading(saveBrandingButton, true, i18n.saving);
        try {
            const response = await fetch('/api/tenant-profile/branding', {
                method: 'PUT',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json', 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    primaryColor: valueOf('primaryColorText'),
                    secondaryColor: valueOf('secondaryColorText'),
                    accentColor: valueOf('accentColorText')
                })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error('branding');

            if (onboarding?.isComplete) {
                window.location.assign('/Dashboard/Index');
                return;
            }
            if (Number(onboarding?.currentStep) !== 6) {
                window.location.assign(safeLocalUrl(onboarding?.nextStepUrl));
                return;
            }

            const completed = await fetch('/api/onboarding/complete-step', {
                method: 'POST',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json', 'Content-Type': 'application/json' },
                body: JSON.stringify({ step: 6, skipped: false })
            });
            const completionPayload = await completed.json().catch(() => null);
            if (!completed.ok || !completionPayload?.success) {
                showAlert('danger', i18n.stepFailed);
                return;
            }
            window.location.assign('/Account/GeneralSettings');
        } catch {
            showAlert('danger', i18n.saveFailed || i18n.networkError);
        } finally {
            setButtonLoading(saveBrandingButton, false, i18n.saveAndContinue);
        }
    }

    function setColor(prefix, candidate, fallback) {
        const value = /^#[0-9a-f]{6}$/i.test(String(candidate || '')) ? candidate : fallback;
        const picker = document.getElementById(`${prefix}Color`);
        const text = document.getElementById(`${prefix}ColorText`);
        if (picker) picker.value = value;
        if (text) text.value = value.toUpperCase();
    }

    function setAvailability(message, state) {
        if (!availabilityMessage) return;
        availabilityMessage.className = `availability-msg${state ? ` ${state}` : ''}`;
        availabilityMessage.textContent = message || '';
    }

    function showAlert(type, message) {
        if (!alertContainer) return;
        alertContainer.className = `alert alert-${type}`;
        alertContainer.textContent = message || '';
        alertContainer.focus();
    }

    function setButtonLoading(button, loading, label) {
        if (!button) return;
        button.disabled = loading;
        button.textContent = loading
            ? label || button.dataset.loadingLabel || ''
            : button.dataset.idleLabel || label || '';
    }

    function updatePrimaryAction() {
        if (!saveBrandingButton) return;
        if (onboarding?.isComplete) saveBrandingButton.textContent = i18n.dashboard || '';
        else if (onboarding && Number(onboarding.currentStep) !== 6) {
            saveBrandingButton.textContent = i18n.continueSetup || '';
        }
    }

    function normalizedSubdomain() {
        return String(subdomainInput?.value || '').trim().toLowerCase();
    }

    function valueOf(id) {
        return document.getElementById(id)?.value?.trim() || '';
    }

    function safeAssetUrl(value) {
        if (!value) return null;
        try {
            const url = new URL(String(value), window.location.origin);
            return url.origin === window.location.origin && url.pathname.startsWith('/uploads/')
                ? url.href
                : null;
        } catch {
            return null;
        }
    }

    function safeLocalUrl(value) {
        const candidate = String(value || '');
        return candidate.startsWith('/') && !candidate.startsWith('//')
            ? candidate
            : '/Dashboard/Index';
    }
})();
