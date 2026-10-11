(() => {
    'use strict';

    const stringsNode = document.getElementById('generalSettingsStrings');
    const i18n = stringsNode ? JSON.parse(stringsNode.textContent || '{}') : {};
    const currencySymbols = {
        BDT: '৳', USD: '$', INR: '₹', GBP: '£', EUR: '€', AED: 'د.إ'
    };
    const alertContainer = document.getElementById('alertContainer');
    const saveButton = document.getElementById('saveSettingsBtn');
    const skipButton = document.getElementById('skipBtn');
    let onboarding = null;
    let currentRowVersion = '';

    document.addEventListener('DOMContentLoaded', async () => {
        document.getElementById('currency')?.addEventListener('change', event => {
            updateCurrencySymbol(event.currentTarget.value);
        });
        document.getElementById('settingsForm')?.addEventListener('submit', saveSettings);
        skipButton?.addEventListener('click', () => advance(true));
        await Promise.all([loadSettings(), loadOnboardingState()]);
        updateActions();
    }, { once: true });

    async function loadSettings() {
        try {
            const response = await fetch('/api/tenant-profile', {
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data) throw new Error('settings');
            const settings = payload.data;
            if (!settings.rowVersion) throw new Error('Missing tenant row version.');
            currentRowVersion = settings.rowVersion;
            setSelect('currency', settings.currencyCode || 'BDT');
            updateCurrencySymbol(valueOf('currency'));
            setSelect('timeZone', settings.timeZoneId || 'Asia/Dhaka');
            setSelect('language', normalizeLanguage(settings.defaultLanguage));
            setSelect('dateFormat', settings.dateFormat || 'dd-MM-yyyy');
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

    async function saveSettings(event) {
        event.preventDefault();
        if (!currentRowVersion) {
            showAlert('danger', i18n.loadFailed || 'Reload regional preferences before saving.');
            return;
        }
        setButtonLoading(saveButton, true, i18n.saving);
        try {
            const response = await fetch('/api/tenant-profile/regional-settings', {
                method: 'PUT', cache: 'no-store', credentials: 'same-origin',
                headers: {
                    'Accept': 'application/json', 'Content-Type': 'application/json',
                    'RequestVerificationToken': antiforgeryToken()
                },
                body: JSON.stringify({
                    currencyCode: valueOf('currency'),
                    timeZoneId: valueOf('timeZone'),
                    defaultLanguage: valueOf('language'),
                    dateFormat: valueOf('dateFormat'),
                    rowVersion: currentRowVersion
                })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) {
                showAlert('danger', responseError(payload, i18n.saveFailed || i18n.networkError));
                if (response.status === 409) await loadSettings();
                return;
            }
            currentRowVersion = payload.data?.rowVersion || currentRowVersion;
            await advance(false);
        } catch (error) {
            console.error('Regional preference request failed.', error);
            showAlert('danger', error.message || i18n.networkError);
        } finally {
            setButtonLoading(saveButton, false, i18n.saveAndContinue);
        }
    }

    async function advance(skipped) {
        if (onboarding?.isComplete) {
            window.location.assign('/Dashboard/Index');
            return;
        }
        if (!onboarding) {
            showAlert('danger', i18n.stepFailed);
            return;
        }
        if (Number(onboarding.currentStage) !== 9) {
            window.location.assign(safeLocalUrl(window.eduosOnboardingStageUrl(onboarding.currentStage)));
            return;
        }

        setButtonLoading(skipped ? skipButton : saveButton, true, i18n.continuing);
        try {
            const response = await fetch('/api/onboarding/complete-step', {
                method: 'POST',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: {
                    'Accept': 'application/json', 'Content-Type': 'application/json',
                    'RequestVerificationToken': antiforgeryToken()
                },
                body: JSON.stringify({ stage: 9, skipped })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error(responseError(payload, i18n.stepFailed));
            window.location.assign('/Account/GatewaySetup');
        } catch (error) {
            showAlert('danger', error.message || i18n.stepFailed);
            setButtonLoading(skipButton, false, i18n.skipForNow);
        }
    }

    function updateActions() {
        if (onboarding?.isComplete) {
            if (saveButton) saveButton.textContent = i18n.dashboard || '';
            if (skipButton) skipButton.hidden = true;
        } else if (onboarding && Number(onboarding.currentStage) !== 9) {
            if (saveButton) saveButton.textContent = i18n.continueSetup || '';
            if (skipButton) skipButton.hidden = true;
        }
    }

    function normalizeLanguage(value) {
        return String(value || '').toLowerCase().startsWith('bn') ? 'bn-BD' : 'en';
    }

    function updateCurrencySymbol(currency) {
        const field = document.getElementById('currencySymbol');
        if (field) field.value = currencySymbols[currency] || currency || '';
    }

    function antiforgeryToken() {
        const token = document.querySelector('#settingsForm input[name="__RequestVerificationToken"]')?.value;
        if (!token) throw new Error('Session verification failed. Reload and retry.');
        return token;
    }

    function responseError(payload, fallback) {
        if (payload?.errors && typeof payload.errors === 'object') {
            const message = Object.values(payload.errors).flat().find(x => typeof x === 'string' && x.trim());
            if (message) return message;
        }
        return typeof payload?.message === 'string' && payload.message.trim()
            ? payload.message.trim() : fallback;
    }

    function setSelect(id, value) {
        const element = document.getElementById(id);
        if (element && Array.from(element.options).some(option => option.value === value)) {
            element.value = value;
        }
    }

    function setValue(id, value) {
        const element = document.getElementById(id);
        if (element) element.value = value || '';
    }

    function valueOf(id) {
        return document.getElementById(id)?.value?.trim() || '';
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

    function safeLocalUrl(value) {
        const candidate = String(value || '');
        return candidate.startsWith('/') && !candidate.startsWith('//')
            ? candidate
            : '/Dashboard/Index';
    }
})();
