(() => {
    'use strict';

    const stringsNode = document.getElementById('brandingSetupStrings');
    const i18n = stringsNode ? JSON.parse(stringsNode.textContent || '{}') : {};
    // All branding mutations are protected by the server's antiforgery filter.
    // Never send a write without the current page's request-verification token.
    function mutationHeaders(headers = {}) {
        const token = document.querySelector('#brandingForm input[name="__RequestVerificationToken"]')?.value;
        if (!token) throw new Error('Security token missing. Reload this page.');
        return { ...headers, RequestVerificationToken: token };
    }

    const alertContainer = document.getElementById('alertContainer');
    const subdomainInput = document.getElementById('subdomainInput');
    const availabilityMessage = document.getElementById('availabilityMsg');
    const saveSubdomainButton = document.getElementById('saveSubdomainBtn');
    const saveBrandingButton = document.getElementById('saveBrandingBtn');
    let onboarding = null;
    let savedSubdomain = '';
    let rowVersion = '';
    const colorSettings = new Map();
    let availableSubdomain = '';
    let checkTimer = 0;
    let ready = false;
    let saving = false;
    let loading = false;
    let invalidSession = false;
    // A restored BFCache document must not expose the previous tenant's branding,
    // reuse its antiforgery token, or resume a pending privileged operation.
    let pageActive = true;
    window.addEventListener('pagehide', () => {
        pageActive = false;
        clearLoadedBranding();
        if (alertContainer) {
            alertContainer.className = 'd-none';
            alertContainer.textContent = '';
        }
        if (retryLoadButton) retryLoadButton.hidden = true;
    });
    window.addEventListener('pageshow', event => {
        if (event.persisted) window.location.reload();
    });
    const retryLoadButton = document.getElementById('retryBrandingLoadBtn');
    const originalTenant = document.body?.dataset.authTenantId || '';
    const originalSession = document.body?.dataset.authSessionReference || '';
    const shield = document.getElementById('eduosSessionShield');
    function contextReady() {
        return pageActive && !!originalTenant && !!originalSession &&
            document.body?.dataset.authTenantId === originalTenant &&
            document.body?.dataset.authSessionReference === originalSession &&
            !window.EduOSSessionTransitionPending?.() &&
            (!shield || shield.hidden) &&
            typeof window.EduOSWorkspaceResponseMatchesSession === 'function';
    }
    function invalidateSession() {
        invalidSession = true;
        clearLoadedBranding();
        void window.EduOSVerifySessionContext?.();
    }

    function updateControls() {
        const disabled = !ready || saving || invalidSession;
        ['subdomainInput', 'saveBrandingBtn', 'uploadLogoBtn', 'uploadFaviconBtn',
            'removeLogoBtn', 'removeFaviconBtn', 'logoInput', 'faviconInput',
            'primaryColor', 'primaryColorText', 'secondaryColor', 'secondaryColorText',
            'accentColor', 'accentColorText'].forEach(id => {
            const node = document.getElementById(id);
            if (node) node.disabled = disabled;
        });
        if (saveSubdomainButton) saveSubdomainButton.disabled =
            disabled || availableSubdomain !== normalizedSubdomain();
    }

    function clearLoadedBranding() {
        window.clearTimeout(checkTimer);
        checkTimer = 0;
        ready = false;
        onboarding = null;
        savedSubdomain = '';
        availableSubdomain = '';
        rowVersion = '';
        colorSettings.clear();
        if (subdomainInput) subdomainInput.value = '';
        setAvailability('', '');
        ['primary', 'secondary', 'accent'].forEach((name, index) =>
            setColor(name, null, ['#1E40AF', '#64748B', '#F59E0B'][index]));
        renderAsset('logo', null);
        renderAsset('favicon', null);
        updateControls();
    }

    async function verifySession() {
        if (invalidSession || !contextReady()) {
            invalidateSession();
            throw new Error('Institution session changed.');
        }
        let response;
        try {
            response = await fetch('/api/workspace-preferences', {
                method: 'GET', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json' }
            });
        } catch {
            clearLoadedBranding();
            throw new Error('Session verification unavailable.');
        }
        if (!response.ok) {
            clearLoadedBranding();
            throw new Error('Session verification unavailable.');
        }
        if (!contextReady() || window.EduOSWorkspaceResponseMatchesSession(response) !== true) {
            invalidateSession();
            throw new Error('Institution session changed.');
        }
    }

    async function reloadBranding() {
        if (loading || invalidSession || !pageActive) return false;
        loading = true;
        clearLoadedBranding();
        updateControls();
        if (retryLoadButton) retryLoadButton.hidden = true;
        try {
            await verifySession();
            const [data, status] = await Promise.all([loadProfile(), loadOnboardingState()]);
            await verifySession();
            const profile = data.profile;
            rowVersion = profile.rowVersion || '';
            savedSubdomain = String(profile.subdomain || '').toLowerCase();
            if (subdomainInput) subdomainInput.value = savedSubdomain;
            if (savedSubdomain) setAvailability(i18n.subdomainSaved, 'ok');
            colorSettings.clear();
            for (const row of data.colors) colorSettings.set(row.key, row);
            const value = name => colorSettings.get('Branding.' + name)?.value;
            setColor('primary', value('PrimaryColor'), '#1E40AF');
            setColor('secondary', value('SecondaryColor'), '#64748B');
            setColor('accent', value('AccentColor'), '#F59E0B');
            renderAsset('logo', profile.logoUrl);
            renderAsset('favicon', profile.faviconUrl);
            onboarding = status;
            ready = true;
            if (alertContainer) {
                alertContainer.className = 'd-none';
                alertContainer.textContent = '';
            }
            updatePrimaryAction();
            updateControls();
            return true;
        } catch {
            clearLoadedBranding();
            showAlert('danger', i18n.loadFailed);
            if (retryLoadButton) retryLoadButton.hidden = invalidSession;
            return false;
        } finally {
            loading = false;
        }
    }

    document.addEventListener('DOMContentLoaded', async () => {
        initializeColorInputs();
        initializeAssetControls();
        subdomainInput?.addEventListener('input', handleSubdomainInput);
        saveSubdomainButton?.addEventListener('click', saveSubdomain);
        document.getElementById('brandingForm')?.addEventListener('submit', saveBranding);
        retryLoadButton?.addEventListener('click', reloadBranding);
        await reloadBranding();
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
        const [response, colorResponse] = await Promise.all([
            fetch('/api/tenant-profile', {
                cache: 'no-store', credentials: 'same-origin',
                headers: { Accept: 'application/json' }
            }),
            fetch('/api/tenant-settings?category=Branding&page=1&pageSize=50', {
                cache: 'no-store', credentials: 'same-origin',
                headers: { Accept: 'application/json' }
            })
        ]);
        const [payload, colors] = await Promise.all([
            response.json().catch(() => null), colorResponse.json().catch(() => null)
        ]);
        if (!response.ok || !payload?.success || !payload.data ||
            !colorResponse.ok || !colors?.success || !Array.isArray(colors.data?.items))
            throw new Error('Branding profile or colors unavailable.');
        return { profile: payload.data, colors: colors.data.items };
    }

    async function loadOnboardingState() {
        const response = await fetch('/api/onboarding/status', {
            cache: 'no-store', credentials: 'same-origin',
            headers: { 'Accept': 'application/json' }
        });
        const payload = await response.json().catch(() => null);
        if (!response.ok || !payload?.success || !payload.data)
            throw new Error('Onboarding status unavailable.');
        return payload.data;
    }

    function handleSubdomainInput() {
        if (!ready || saving) return;
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
        if (!ready) return;
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
            await verifySession();
            if (!ready || normalizedSubdomain() !== value) return;

            availableSubdomain = payload.data.isAvailable ? value : '';
            setAvailability(
                payload.data.isAvailable ? i18n.available : i18n.unavailable,
                payload.data.isAvailable ? 'ok' : 'bad');
            updateControls();
        } catch {
            if (ready && normalizedSubdomain() === value)
                setAvailability(i18n.checkFailed, 'bad');
        }
    }

    async function saveSubdomain() {
        const subdomain = normalizedSubdomain();
        if (!ready || saving || !subdomain || subdomain !== availableSubdomain) return;
        saving = true;
        updateControls();
        setButtonLoading(saveSubdomainButton, true, i18n.saving);
        try {
            await verifySession();
            const response = await fetch('/api/tenant-profile/subdomain', {
                method: 'PUT',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: mutationHeaders({ 'Accept': 'application/json', 'Content-Type': 'application/json' }),
                body: JSON.stringify({ subdomain, rowVersion })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error('save');
            await verifySession();
            savedSubdomain = subdomain;
            rowVersion = payload.data?.rowVersion || rowVersion;
            availableSubdomain = '';
            setAvailability(i18n.subdomainSaved, 'ok');
            showAlert('success', i18n.subdomainSaved);
        } catch {
            showAlert('danger', i18n.subdomainSaveFailed);
        } finally {
            saving = false;
            availableSubdomain = '';
            setButtonLoading(saveSubdomainButton, false, i18n.saveSubdomain);
            updateControls();
        }
    }

    function bindUpload(type, endpoint) {
        const capitalized = type[0].toUpperCase() + type.slice(1);
        const input = document.getElementById(`${type}Input`);
        const openPicker = () => { if (pageActive && ready && !saving && !invalidSession) input?.click(); };
        document.getElementById(`upload${capitalized}Btn`)?.addEventListener('click', openPicker);
        document.getElementById(`${type}Preview`)?.addEventListener('click', openPicker);
        input?.addEventListener('change', async () => {
            const file = input.files?.[0];
            if (!file || !ready || saving) return;
            saving = true;
            updateControls();
            const body = new FormData();
            body.append('file', file);
            try {
                await verifySession();
                const response = await fetch(endpoint, {
                    method: 'POST',
                    cache: 'no-store',
                    credentials: 'same-origin',
                    headers: mutationHeaders({ 'Accept': 'application/json' }),
                    body
                });
                const payload = await response.json().catch(() => null);
                if (!response.ok || !payload?.success || !payload.data) throw new Error('upload');
                if (await reloadBranding())
                    showAlert('success', i18n.uploadSuccess);
            } catch {
                showAlert('danger', i18n.uploadFailed);
            } finally {
                saving = false;
                updateControls();
                input.value = '';
            }
        });
    }

    async function removeAsset(type, endpoint, confirmation) {
        if (!ready || saving || !window.confirm(confirmation || '')) return;
        saving = true;
        updateControls();
        try {
            await verifySession();
            const response = await fetch(endpoint + '?rowVersion=' + encodeURIComponent(rowVersion), {
                method: 'DELETE',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: mutationHeaders({ 'Accept': 'application/json' })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error('remove');
            await reloadBranding();
        } catch {
            showAlert('danger', i18n.removeFailed);
        } finally {
            saving = false;
            updateControls();
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

    async function saveColor(name, value) {
        if (!/^#[0-9a-f]{6}$/i.test(value)) throw new Error('Invalid color value.');
        await verifySession();
        const key = 'Branding.' + name;
        const existing = colorSettings.get(key);
        const response = await fetch(existing?.id ? '/api/tenant-settings/' + existing.id : '/api/tenant-settings', {
            method: existing?.id ? 'PUT' : 'POST',
            cache: 'no-store', credentials: 'same-origin',
            headers: mutationHeaders({ Accept: 'application/json', 'Content-Type': 'application/json' }),
            body: JSON.stringify({ key, value, category: 'Branding', rowVersion: existing?.rowVersion || null, isSensitive: false })
        });
        const payload = await response.json().catch(() => null);
        if (!response.ok || !payload?.success || !payload.data) throw new Error(payload?.message || 'Color could not be saved.');
        colorSettings.set(key, payload.data);
    }

    async function saveBranding(event) {
        event.preventDefault();
        if (!ready || saving) return;
        saving = true;
        updateControls();
        setButtonLoading(saveBrandingButton, true, i18n.saving);
        let attemptedWrite = false;
        try {
            const colors = ['primary', 'secondary', 'accent'].map(prefix => valueOf(prefix + 'ColorText'));
            if (colors.some(value => !/^#[0-9a-f]{6}$/i.test(value)))
                throw new Error('Invalid brand color.');
            mutationHeaders();
            await verifySession();
            attemptedWrite = true;
            await saveColor('PrimaryColor', colors[0]);
            await saveColor('SecondaryColor', colors[1]);
            await saveColor('AccentColor', colors[2]);
            await verifySession();

            if (onboarding?.isComplete) {
                window.location.assign('/Dashboard/Index');
                return;
            }
            if (Number(onboarding?.currentStage) !== 8) {
                window.location.assign(safeLocalUrl(window.eduosOnboardingStageUrl(onboarding?.currentStage)));
                return;
            }

            const completed = await fetch('/api/onboarding/complete-step', {
                method: 'POST', cache: 'no-store', credentials: 'same-origin',
                headers: mutationHeaders({ 'Accept': 'application/json', 'Content-Type': 'application/json' }),
                body: JSON.stringify({ stage: 8, skipped: false })
            });
            const completionPayload = await completed.json().catch(() => null);
            if (!completed.ok || !completionPayload?.success) {
                showAlert('danger', i18n.stepFailed);
                return;
            }
            await verifySession();
            window.location.assign('/Account/GeneralSettings');
        } catch {
            if (attemptedWrite && !invalidSession) {
                // Separate canonical settings rows may have partially committed.
                // Reconcile the current saved values/versions before any retry.
                await reloadBranding();
                showAlert('danger', i18n.partialSaveRecovery);
            } else {
                showAlert('danger', i18n.saveFailed || i18n.networkError);
            }
        } finally {
            saving = false;
            setButtonLoading(saveBrandingButton, false, i18n.saveAndContinue);
            updatePrimaryAction();
            updateControls();
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
        if (!alertContainer || !pageActive) return;
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
        const label = onboarding?.isComplete ? i18n.dashboard :
            onboarding && Number(onboarding.currentStage) !== 8 ? i18n.continueSetup :
            i18n.saveAndContinue;
        saveBrandingButton.dataset.idleLabel = label || '';
        if (!saving) saveBrandingButton.textContent = label || '';
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
