(() => {
    'use strict';

    const apiRoot = '/api/communication-admin/gateways';
    const stringsNode = document.getElementById('gatewaySetupStrings');
    const i18n = stringsNode ? JSON.parse(stringsNode.textContent || '{}') : {};
    const alertContainer = document.getElementById('alertContainer');
    const finishButton = document.getElementById('finishBtn');
    const skipButton = document.getElementById('skipBtn');
    let gateways = [];
    let gatewaysLoaded = false;
    let onboarding = null;
    let saving = false;

    document.addEventListener('DOMContentLoaded', async () => {
        document.getElementById('smsForm')?.addEventListener('submit', saveSms);
        document.getElementById('emailForm')?.addEventListener('submit', saveEmail);
        document.getElementById('smsEnabled')?.addEventListener('change', updateRequiredFields);
        document.getElementById('emailEnabled')?.addEventListener('change', updateRequiredFields);
        document.getElementById('smsProvider')?.addEventListener('change', () => {
            renderSms(gatewayByCode(3, valueOf('smsProvider')));
            updateRequiredFields();
        });
        finishButton?.addEventListener('click', () => finishOnboarding(false));
        skipButton?.addEventListener('click', () => finishOnboarding(true));
        [finishButton, skipButton].forEach(button => {
            if (button) button.dataset.idleLabel = button.textContent.trim();
        });
        setFormEnabled(false);
        await Promise.all([loadGateways(), loadOnboardingState()]);
        updateRequiredFields();
        updateActions();
    }, { once: true });

    async function api(url, method = 'GET', data) {
        const headers = { 'Accept': 'application/json' };
        if (data !== undefined) {
            headers['Content-Type'] = 'application/json';
            headers.RequestVerificationToken =
                document.querySelector('meta[name="request-verification-token"]')?.content || '';
        }
        const response = await fetch(url, {
            method, cache: 'no-store', credentials: 'same-origin', headers,
            ...(data !== undefined ? { body: JSON.stringify(data) } : {})
        });
        const payload = await response.json().catch(() => null);
        if (!response.ok || !payload?.success)
            throw new Error(payload?.message || (response.status === 403
                ? 'Tenant administrator permission is required.' : i18n.networkError || 'Request failed.'));
        return payload.data;
    }

    async function loadGateways() {
        gatewaysLoaded = false;
        setFormEnabled(false);
        try {
            const rows = await api(apiRoot);
            if (!Array.isArray(rows)) throw new Error('Unexpected gateway response.');
            gateways = rows;
            gatewaysLoaded = true;
            const sms = gatewayByChannel(3);
            ensureProviderOption(sms?.providerCode);
            setSelect('smsProvider', sms?.providerCode || '');
            renderSms(sms);
            renderEmail(gatewayByCode(2, 'SMTP'));
            clearAlert();
            return true;
        } catch (error) {
            console.warn('Gateway configuration read failed.', error.message);
            showAlert('danger', i18n.loadFailed);
            return false;
        } finally {
            setFormEnabled(gatewaysLoaded);
            updateRequiredFields();
        }
    }

    function gatewayByCode(channel, code) {
        const normalized = String(code || '').toUpperCase();
        return gateways.find(row => Number(row.channel) === channel &&
            String(row.providerCode || '').toUpperCase() === normalized) || null;
    }

    function gatewayByChannel(channel) {
        const rows = gateways.filter(row => Number(row.channel) === channel);
        return rows.find(row => row.isDefault) || rows.find(row => row.isActive) || rows[0] || null;
    }

    function ensureProviderOption(code) {
        const select = document.getElementById('smsProvider');
        if (!select || !code) return;
        if (!Array.from(select.options).some(option => option.value.toUpperCase() === code.toUpperCase())) {
            const option = document.createElement('option');
            option.value = code;
            option.textContent = code;
            select.append(option);
        }
    }

    function renderSms(row) {
        setValue('smsSenderId', row?.smsSenderId);
        setValue('smsApiUrl', row?.endpoint);
        setValue('smsApiKey', '');
        document.getElementById('smsApiKey').placeholder = row?.hasCredential ? '********' : '';
        setChecked('smsEnabled', row?.isActive === true);
    }

    function renderEmail(row) {
        setValue('smtpHost', row?.smtpHost);
        setSelect('smtpPort', String(row?.smtpPort || 587));
        setValue('smtpUsername', row?.smtpUsername);
        setValue('smtpPassword', '');
        document.getElementById('smtpPassword').placeholder = row?.hasCredential ? '********' : '';
        setValue('fromEmail', row?.fromEmail);
        setValue('fromName', row?.fromName);
        setChecked('useSsl', row ? row.useSsl !== false : true);
        setChecked('emailEnabled', row?.isActive === true);
    }

    async function loadOnboardingState() {
        try {
            const data = await api('/api/onboarding/status');
            onboarding = data || null;
        } catch {
            onboarding = null;
        }
    }

    async function saveSms(event) {
        event.preventDefault();
        if (!gatewaysLoaded || saving) return;
        const provider = valueOf('smsProvider');
        const enabled = isChecked('smsEnabled');
        if (!provider && !enabled) {
            showAlert('success', i18n.smsSaved);
            return;
        }
        if (!provider) {
            showAlert('danger', i18n.saveFailed);
            return;
        }
        const existing = gatewayByCode(3, provider);
        const data = {
            providerCode: provider, channel: 3, endpoint: valueOf('smsApiUrl'),
            smsSenderId: valueOf('smsSenderId'), credential: enteredSecret('smsApiKey'),
            isActive: enabled, isDefault: enabled, clearCredential: false,
            ...(existing ? { rowVersion: existing.rowVersion } : {})
        };
        await saveGateway(existing, data, 'saveSmsBtn', i18n.smsSaved);
    }

    async function saveEmail(event) {
        event.preventDefault();
        if (!gatewaysLoaded || saving) return;
        const existing = gatewayByCode(2, 'SMTP');
        const enabled = isChecked('emailEnabled');
        if (!existing && !enabled && !valueOf('smtpHost') && !valueOf('smtpUsername') &&
            !valueOf('fromEmail') && !valueOf('smtpPassword')) {
            showAlert('success', i18n.emailSaved);
            return;
        }
        const data = {
            providerCode: 'SMTP', channel: 2, endpoint: null,
            smtpHost: valueOf('smtpHost'), smtpPort: Number(valueOf('smtpPort')) || null,
            smtpUsername: valueOf('smtpUsername'), fromEmail: valueOf('fromEmail'),
            fromName: valueOf('fromName'), useSsl: isChecked('useSsl'),
            credential: enteredSecret('smtpPassword'),
            isActive: enabled, isDefault: enabled, clearCredential: false,
            ...(existing ? { rowVersion: existing.rowVersion } : {})
        };
        await saveGateway(existing, data, 'saveEmailBtn', i18n.emailSaved);
    }

    async function saveGateway(existing, data, buttonId, successText) {
        const button = document.getElementById(buttonId);
        saving = true;
        setButtonLoading(button, true, i18n.saving);
        try {
            await api(existing ? apiRoot + '/' + encodeURIComponent(existing.id) : apiRoot,
                existing ? 'PUT' : 'POST', data);
            if (await loadGateways()) showAlert('success', successText);
        } catch (error) {
            if (error.message?.includes('changed') || error.message?.includes('exists'))
                await loadGateways();
            showAlert('danger', error.message || i18n.saveFailed);
        } finally {
            saving = false;
            setButtonLoading(button, false);
            updateRequiredFields();
        }
    }

    async function finishOnboarding(skipped) {
        if (saving) return;
        if (onboarding?.isComplete) {
            window.location.assign('/Dashboard/Index');
            return;
        }
        if (!onboarding) {
            showAlert('danger', i18n.completionFailed);
            return;
        }
        if (Number(onboarding.currentStage) !== 10) {
            window.location.assign(safeLocalUrl(window.eduosOnboardingStageUrl(onboarding.currentStage)));
            return;
        }
        const button = skipped ? skipButton : finishButton;
        setButtonLoading(button, true, i18n.finishing);
        try {
            await api('/api/onboarding/complete-step', 'POST', { stage: 10, skipped });
            window.location.assign('/Account/OnboardingComplete');
        } catch (error) {
            showAlert('danger', error.message || i18n.completionFailed);
            setButtonLoading(skipButton, false);
            setButtonLoading(finishButton, false);
        }
    }

    function updateRequiredFields() {
        const smsRequired = isChecked('smsEnabled');
        ['smsProvider', 'smsSenderId', 'smsApiUrl'].forEach(id => {
            const element = document.getElementById(id);
            if (element) element.required = smsRequired;
        });
        const provider = valueOf('smsProvider');
        const sms = gatewayByCode(3, provider);
        document.getElementById('smsApiKey').required = smsRequired && !sms?.hasCredential;
        const emailRequired = isChecked('emailEnabled');
        ['smtpHost', 'smtpPort', 'smtpUsername', 'fromEmail'].forEach(id => {
            const element = document.getElementById(id);
            if (element) element.required = emailRequired;
        });
        const email = gatewayByCode(2, 'SMTP');
        document.getElementById('smtpPassword').required = emailRequired && !email?.hasCredential;
    }

    function updateActions() {
        if (onboarding?.isComplete) {
            if (finishButton) finishButton.textContent = i18n.dashboard || '';
            if (skipButton) skipButton.hidden = true;
        } else if (onboarding && Number(onboarding.currentStage) !== 10) {
            if (finishButton) finishButton.textContent = i18n.continueSetup || '';
            if (skipButton) skipButton.hidden = true;
        }
    }

    function setFormEnabled(enabled) {
        ['smsForm', 'emailForm'].forEach(id => document.querySelectorAll('#' + id + ' input, #' +
            id + ' select, #' + id + ' button').forEach(element => { element.disabled = !enabled; }));
        ['smsEnabled', 'emailEnabled'].forEach(id => {
            const element = document.getElementById(id);
            if (element) element.disabled = !enabled;
        });
    }

    function enteredSecret(id) {
        const value = valueOf(id);
        return value && value !== '********' ? value : null;
    }

    function valueOf(id) { return document.getElementById(id)?.value?.trim() || ''; }
    function setValue(id, value) {
        const element = document.getElementById(id);
        if (element) element.value = value ?? '';
    }
    function setSelect(id, value) {
        const element = document.getElementById(id);
        const normalized = String(value ?? '');
        if (!element) return;
        const option = Array.from(element.options).find(item =>
            item.value.toUpperCase() === normalized.toUpperCase());
        if (option) element.value = option.value;
    }
    function setChecked(id, checked) {
        const element = document.getElementById(id);
        if (element) element.checked = Boolean(checked);
    }
    function isChecked(id) { return Boolean(document.getElementById(id)?.checked); }

    function showAlert(type, message) {
        if (!alertContainer) return;
        alertContainer.className = 'alert alert-' + type;
        alertContainer.textContent = message || '';
        alertContainer.focus();
    }
    function clearAlert() {
        if (!alertContainer) return;
        alertContainer.className = 'd-none';
        alertContainer.textContent = '';
    }
    function setButtonLoading(button, loading, label) {
        if (!button) return;
        const isSaveButton = button.id === 'saveSmsBtn' || button.id === 'saveEmailBtn';
        button.disabled = loading || (isSaveButton && !gatewaysLoaded);
        button.textContent = loading ? (label || i18n.saving || '') :
            (button.dataset.idleLabel || '');
    }
    function safeLocalUrl(value) {
        const candidate = String(value || '');
        return candidate.startsWith('/') && !candidate.startsWith('//')
            ? candidate : '/Dashboard/Index';
    }
})();