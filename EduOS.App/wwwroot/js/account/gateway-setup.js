(() => {
    'use strict';

    const stringsNode = document.getElementById('gatewaySetupStrings');
    const i18n = stringsNode ? JSON.parse(stringsNode.textContent || '{}') : {};
    const alertContainer = document.getElementById('alertContainer');
    const finishButton = document.getElementById('finishBtn');
    const skipButton = document.getElementById('skipBtn');
    let onboarding = null;

    document.addEventListener('DOMContentLoaded', async () => {
        document.getElementById('smsForm')?.addEventListener('submit', saveSms);
        document.getElementById('emailForm')?.addEventListener('submit', saveEmail);
        document.getElementById('smsEnabled')?.addEventListener('change', updateRequiredFields);
        document.getElementById('emailEnabled')?.addEventListener('change', updateRequiredFields);
        finishButton?.addEventListener('click', () => finishOnboarding(false));
        skipButton?.addEventListener('click', () => finishOnboarding(true));
        await Promise.all([loadSms(), loadEmail(), loadOnboardingState()]);
        updateRequiredFields();
        updateActions();
    }, { once: true });

    async function loadSms() {
        try {
            const payload = await getJson('/api/tenant-settings/sms-gateway');
            if (!payload?.success || !payload.data) throw new Error('sms');
            const settings = payload.data;
            setSelect('smsProvider', settings.provider);
            setValue('smsSenderId', settings.senderId);
            setValue('smsApiUrl', settings.apiUrl);
            setValue('smsApiKey', settings.apiKey);
            setChecked('smsEnabled', settings.isEnabled);
        } catch {
            showAlert('danger', i18n.loadFailed);
        }
    }

    async function loadEmail() {
        try {
            const payload = await getJson('/api/tenant-settings/email-gateway');
            if (!payload?.success || !payload.data) throw new Error('email');
            const settings = payload.data;
            setValue('smtpHost', settings.smtpHost);
            setSelect('smtpPort', String(settings.smtpPort || 587));
            setValue('smtpUsername', settings.smtpUsername);
            setValue('smtpPassword', settings.smtpPassword);
            setValue('fromEmail', settings.fromEmail);
            setValue('fromName', settings.fromName);
            setChecked('useSsl', settings.useSsl !== false);
            setChecked('emailEnabled', settings.isEnabled);
        } catch {
            showAlert('danger', i18n.loadFailed);
        }
    }

    async function loadOnboardingState() {
        try {
            const payload = await getJson('/api/onboarding/status');
            onboarding = payload?.success ? payload.data : null;
        } catch {
            onboarding = null;
        }
    }

    async function saveSms(event) {
        event.preventDefault();
        const button = document.getElementById('saveSmsBtn');
        setButtonLoading(button, true, i18n.saving);
        try {
            const response = await fetch('/api/tenant-settings/sms-gateway', {
                method: 'PUT',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json', 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    provider: valueOf('smsProvider'),
                    senderId: valueOf('smsSenderId'),
                    apiUrl: valueOf('smsApiUrl'),
                    apiKey: valueOf('smsApiKey'),
                    isEnabled: isChecked('smsEnabled')
                })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error('sms-save');
            if (valueOf('smsApiKey')) setValue('smsApiKey', '********');
            showAlert('success', i18n.smsSaved);
        } catch {
            showAlert('danger', i18n.saveFailed);
        } finally {
            setButtonLoading(button, false, '');
        }
    }

    async function saveEmail(event) {
        event.preventDefault();
        const button = document.getElementById('saveEmailBtn');
        setButtonLoading(button, true, i18n.saving);
        try {
            const response = await fetch('/api/tenant-settings/email-gateway', {
                method: 'PUT',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json', 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    smtpHost: valueOf('smtpHost'),
                    smtpPort: Number.parseInt(valueOf('smtpPort'), 10) || null,
                    smtpUsername: valueOf('smtpUsername'),
                    smtpPassword: valueOf('smtpPassword'),
                    fromEmail: valueOf('fromEmail'),
                    fromName: valueOf('fromName'),
                    useSsl: isChecked('useSsl'),
                    isEnabled: isChecked('emailEnabled')
                })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error('email-save');
            if (valueOf('smtpPassword')) setValue('smtpPassword', '********');
            showAlert('success', i18n.emailSaved);
        } catch {
            showAlert('danger', i18n.saveFailed);
        } finally {
            setButtonLoading(button, false, '');
        }
    }

    async function finishOnboarding(skipped) {
        if (onboarding?.isComplete) {
            window.location.assign('/Dashboard/Index');
            return;
        }
        if (!onboarding) {
            showAlert('danger', i18n.completionFailed);
            return;
        }
        if (Number(onboarding.currentStep) !== 8) {
            window.location.assign(safeLocalUrl(onboarding.nextStepUrl));
            return;
        }

        setButtonLoading(skipped ? skipButton : finishButton, true, i18n.finishing);
        try {
            const response = await fetch('/api/onboarding/complete-step', {
                method: 'POST',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json', 'Content-Type': 'application/json' },
                body: JSON.stringify({ step: 8, skipped })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error('complete');
            window.location.assign('/Account/OnboardingComplete');
        } catch {
            showAlert('danger', i18n.completionFailed || i18n.networkError);
            setButtonLoading(skipButton, false, '');
            setButtonLoading(finishButton, false, '');
        }
    }

    function updateRequiredFields() {
        const smsRequired = isChecked('smsEnabled');
        ['smsProvider', 'smsSenderId', 'smsApiUrl', 'smsApiKey'].forEach(id => {
            const element = document.getElementById(id);
            if (element) element.required = smsRequired;
        });

        const emailRequired = isChecked('emailEnabled');
        ['smtpHost', 'smtpPort', 'fromEmail'].forEach(id => {
            const element = document.getElementById(id);
            if (element) element.required = emailRequired;
        });
    }

    function updateActions() {
        if (onboarding?.isComplete) {
            if (finishButton) finishButton.textContent = i18n.dashboard || '';
            if (skipButton) skipButton.hidden = true;
        } else if (onboarding && Number(onboarding.currentStep) !== 8) {
            if (finishButton) finishButton.textContent = i18n.continueSetup || '';
            if (skipButton) skipButton.hidden = true;
        }
    }

    async function getJson(url) {
        const response = await fetch(url, {
            cache: 'no-store',
            credentials: 'same-origin',
            headers: { 'Accept': 'application/json' }
        });
        if (!response.ok) throw new Error(String(response.status));
        return response.json();
    }

    function valueOf(id) {
        return document.getElementById(id)?.value?.trim() || '';
    }

    function setValue(id, value) {
        const element = document.getElementById(id);
        if (element) element.value = value || '';
    }

    function setSelect(id, value) {
        const element = document.getElementById(id);
        const normalized = String(value || '');
        if (element && Array.from(element.options).some(option => option.value === normalized)) {
            element.value = normalized;
        }
    }

    function setChecked(id, checked) {
        const element = document.getElementById(id);
        if (element) element.checked = Boolean(checked);
    }

    function isChecked(id) {
        return Boolean(document.getElementById(id)?.checked);
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
            ? label || i18n.saving || ''
            : button.dataset.idleLabel || label || '';
    }

    function safeLocalUrl(value) {
        const candidate = String(value || '');
        return candidate.startsWith('/') && !candidate.startsWith('//')
            ? candidate
            : '/Dashboard/Index';
    }
})();
