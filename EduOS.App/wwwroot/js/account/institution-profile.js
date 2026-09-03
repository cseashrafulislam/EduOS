(() => {
    'use strict';

    const configElement = document.getElementById('institutionProfileStrings');
    const i18n = configElement ? JSON.parse(configElement.textContent || '{}') : {};

    document.addEventListener('DOMContentLoaded', async () => {
        const form = document.getElementById('profileForm');
        const submitButton = document.getElementById('saveProfileBtn');
        const institutionType = document.getElementById('institutionType');
        if (!form || !submitButton || !institutionType) return;

        const [profile, institutionTypes] = await Promise.all([
            loadProfile(),
            loadInstitutionTypes(institutionType)
        ]);
        if (profile) populateProfile(profile);
        if (profile?.institutionType && institutionTypes) {
            institutionType.value = profile.institutionType;
        }

        form.addEventListener('submit', async event => {
            event.preventDefault();
            clearErrors();

            const data = {
                institutionName: valueOf('institutionName'),
                institutionType: valueOf('institutionType'),
                ownerName: valueOf('ownerName'),
                ownerPhone: normalizeBanglaDigits(valueOf('ownerPhone')),
                ownerEmail: valueOf('ownerEmail'),
                ownerDesignation: valueOf('ownerDesignation'),
                phone: normalizeBanglaDigits(valueOf('phone')),
                website: valueOf('website'),
                address: valueOf('address'),
                city: valueOf('city'),
                state: valueOf('state'),
                country: valueOf('country'),
                postalCode: normalizeBanglaDigits(valueOf('postalCode'))
            };

            let valid = true;
            if (!data.institutionName) valid = showError('institutionName', i18n.institutionRequired);
            if (!data.institutionType) valid = showError('institutionType', i18n.institutionTypeRequired);
            if (!data.ownerName) valid = showError('ownerName', i18n.ownerRequired);
            if (data.ownerEmail && !isValidEmail(data.ownerEmail)) valid = showError('ownerEmail', i18n.invalidEmail);
            if (data.website && !isValidHttpUrl(data.website)) valid = showError('website', i18n.invalidWebsite);
            if (!valid) return;

            setLoading(submitButton, true);
            try {
                const response = await fetch('/api/institution-onboarding/institution-profile', {
                    method: 'POST',
                    cache: 'no-store',
                    credentials: 'same-origin',
                    headers: { 'Accept': 'application/json' },
                    body: toFormData(data)
                });
                const payload = await response.json().catch(() => null);
                if (!response.ok || !payload?.success) {
                    showAlert('danger', i18n.saveFailed);
                    return;
                }

                const stepResponse = await fetch('/api/onboarding/complete-step', {
                    method: 'POST',
                    cache: 'no-store',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                    body: JSON.stringify({ step: 1, skipped: false })
                });
                if (!stepResponse.ok) {
                    showAlert('danger', i18n.saveFailed);
                    return;
                }

                window.location.assign('/Account/PlanSelection');
            } catch {
                showAlert('danger', i18n.networkError);
            } finally {
                setLoading(submitButton, false);
            }
        });
    }, { once: true });

    async function loadProfile() {
        try {
            const response = await fetch('/api/institution-onboarding/institution-profile', {
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error('Invalid profile response');
            return payload.data || null;
        } catch (error) {
            console.warn('EduOS institution profile was unavailable.', error);
            showAlert('danger', i18n.loadFailed);
            return null;
        }
    }

    async function loadInstitutionTypes(select) {
        const selectedValue = select.value;
        try {
            const response = await fetch('/api/platform-catalog/institution-types', {
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !Array.isArray(payload.data)) {
                throw new Error('Invalid institution type response');
            }

            const placeholder = document.createElement('option');
            placeholder.value = '';
            placeholder.textContent = i18n.selectType || '';
            const options = payload.data.map(type => {
                const option = document.createElement('option');
                option.value = String(type.code || '');
                option.textContent = i18n.useBangla && type.nameBangla
                    ? type.nameBangla
                    : type.name || type.code || '';
                return option;
            });
            select.replaceChildren(placeholder, ...options);
            select.disabled = false;
            if (selectedValue) select.value = selectedValue;
            return true;
        } catch (error) {
            console.warn('EduOS institution catalogue was unavailable.', error);
            select.replaceChildren();
            const unavailable = document.createElement('option');
            unavailable.value = '';
            unavailable.textContent = i18n.loadingTypes || '';
            select.append(unavailable);
            select.disabled = true;
            showAlert('danger', i18n.catalogFailed);
            return false;
        }
    }

    function populateProfile(profile) {
        const fields = [
            'institutionName', 'ownerName', 'ownerPhone', 'ownerEmail',
            'ownerDesignation', 'phone', 'website', 'address', 'city',
            'state', 'country', 'postalCode'
        ];
        fields.forEach(id => setValue(id, profile[id]));
    }

    function valueOf(id) {
        return document.getElementById(id)?.value?.trim() ?? '';
    }

    function setValue(id, value) {
        const element = document.getElementById(id);
        if (element && value != null) element.value = value;
    }

    function normalizeBanglaDigits(value) {
        return value.replace(/[০-৯]/g, digit => String('০১২৩৪৫৬৭৮৯'.indexOf(digit)));
    }

    function isValidEmail(value) {
        return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);
    }

    function isValidHttpUrl(value) {
        try {
            const url = new URL(value);
            return url.protocol === 'https:' || url.protocol === 'http:';
        } catch {
            return false;
        }
    }

    function toFormData(data) {
        const formData = new FormData();
        Object.entries(data).forEach(([key, value]) => formData.append(key, value ?? ''));
        return formData;
    }

    function showError(fieldId, message) {
        const field = document.getElementById(fieldId);
        if (!field) return false;
        field.classList.add('is-invalid');
        const feedback = field.parentElement?.querySelector('.invalid-feedback');
        if (feedback) feedback.textContent = message || '';
        return false;
    }

    function clearErrors() {
        document.querySelectorAll('.is-invalid').forEach(element => element.classList.remove('is-invalid'));
        document.querySelectorAll('.invalid-feedback').forEach(element => { element.textContent = ''; });
        const alert = document.getElementById('alertContainer');
        if (alert) alert.className = 'd-none';
    }

    function showAlert(type, message) {
        const container = document.getElementById('alertContainer');
        if (!container) return;
        const safeType = type === 'success' ? 'success' : 'danger';
        container.className = `alert alert-${safeType}`;
        container.textContent = message || '';
        container.focus();
    }

    function setLoading(button, loading) {
        button.disabled = loading;
        button.replaceChildren();
        if (loading) {
            const spinner = document.createElement('span');
            spinner.className = 'spinner-border spinner-border-sm me-2';
            spinner.setAttribute('aria-hidden', 'true');
            button.append(spinner, button.dataset.loadingLabel || i18n.saving || '');
        } else {
            button.textContent = button.dataset.idleLabel || i18n.saveContinue || '';
        }
    }
})();
