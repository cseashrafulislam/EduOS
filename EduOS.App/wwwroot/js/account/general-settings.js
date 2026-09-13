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

    document.addEventListener('DOMContentLoaded', async () => {
        document.getElementById('currency')?.addEventListener('change', event => {
            const symbol = currencySymbols[event.currentTarget.value];
            const field = document.getElementById('currencySymbol');
            if (field && symbol) field.value = symbol;
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
            setSelect('currency', settings.currency || 'BDT');
            setValue('currencySymbol', settings.currencySymbol || '৳');
            setSelect('timeZone', settings.timeZone || 'Asia/Dhaka');
            setSelect('language', normalizeLanguage(settings.language));
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
        setButtonLoading(saveButton, true, i18n.saving);
        try {
            const response = await fetch('/api/tenant-profile/general-settings', {
                method: 'PUT',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json', 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    currency: valueOf('currency'),
                    currencySymbol: valueOf('currencySymbol'),
                    timeZone: valueOf('timeZone'),
                    language: valueOf('language'),
                    dateFormat: valueOf('dateFormat')
                })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error('save');
            await advance(false);
        } catch {
            showAlert('danger', i18n.saveFailed || i18n.networkError);
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
        if (Number(onboarding.currentStep) !== 7) {
            window.location.assign(safeLocalUrl(onboarding.nextStepUrl));
            return;
        }

        setButtonLoading(skipped ? skipButton : saveButton, true, i18n.continuing);
        try {
            const response = await fetch('/api/onboarding/complete-step', {
                method: 'POST',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json', 'Content-Type': 'application/json' },
                body: JSON.stringify({ step: 7, skipped })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error('step');
            window.location.assign('/Account/GatewaySetup');
        } catch {
            showAlert('danger', i18n.stepFailed);
            setButtonLoading(skipButton, false, i18n.skipForNow);
        }
    }

    function updateActions() {
        if (onboarding?.isComplete) {
            if (saveButton) saveButton.textContent = i18n.dashboard || '';
            if (skipButton) skipButton.hidden = true;
        } else if (onboarding && Number(onboarding.currentStep) !== 7) {
            if (saveButton) saveButton.textContent = i18n.continueSetup || '';
            if (skipButton) skipButton.hidden = true;
        }
    }

    function normalizeLanguage(value) {
        return String(value || '').toLowerCase() === 'bn' ? 'bn-BD' : value || 'en';
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
