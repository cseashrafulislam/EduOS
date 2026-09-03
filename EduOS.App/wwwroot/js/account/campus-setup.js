(() => {
    'use strict';

    const configElement = document.getElementById('campusSetupStrings');
    const i18n = configElement ? JSON.parse(configElement.textContent || '{}') : {};
    const list = document.getElementById('campusList');
    const continueButton = document.getElementById('continueBtn');
    const modalElement = document.getElementById('campusModal');

    document.addEventListener('DOMContentLoaded', async () => {
        const form = document.getElementById('campusForm');
        if (!form || !list) return;

        await loadCampuses();

        form.addEventListener('submit', saveCampus);
        list.addEventListener('click', handleListAction);
        continueButton?.addEventListener('click', completeStep);
        modalElement?.addEventListener('hidden.bs.modal', resetForm);
    }, { once: true });

    async function saveCampus(event) {
        event.preventDefault();
        clearErrors();

        const data = {
            id: positiveInteger(valueOf('campusId')),
            name: valueOf('campusName'),
            code: valueOf('campusCode'),
            address: valueOf('campusAddress'),
            phone: normalizeBanglaDigits(valueOf('campusPhone')),
            email: valueOf('campusEmail'),
            headName: valueOf('campusHeadName'),
            isHeadOffice: document.getElementById('isHeadOffice')?.checked ?? false
        };
        if (!data.name) {
            showError('campusName', i18n.campusNameRequired);
            return;
        }

        const saveButton = document.getElementById('saveCampusBtn');
        setLoading(saveButton, true);
        try {
            const response = await fetch('/api/institution-onboarding/campus', {
                method: 'POST',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                body: JSON.stringify(data)
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) {
                showAlert('danger', i18n.saveFailed);
                return;
            }

            closeModal();
            await loadCampuses();
            showAlert('success', i18n.saved);
        } catch {
            showAlert('danger', i18n.networkError);
        } finally {
            setLoading(saveButton, false);
        }
    }

    async function loadCampuses() {
        if (!list) return;
        list.setAttribute('aria-busy', 'true');
        try {
            const response = await fetch('/api/institution-onboarding/campus-list', {
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !Array.isArray(payload.data)) {
                throw new Error('Invalid campus response');
            }
            renderCampuses(payload.data);
        } catch (error) {
            console.warn('EduOS campus list was unavailable.', error);
            renderLoadError();
        } finally {
            list.removeAttribute('aria-busy');
        }
    }

    function renderCampuses(campuses) {
        if (!list) return;
        if (!campuses.length) {
            list.replaceChildren(createEmptyState(i18n.empty));
            if (continueButton) continueButton.disabled = true;
            return;
        }

        const records = campuses.map(campus => {
            const article = document.createElement('article');
            article.className = 'setup-record-card';
            article.dataset.recordId = String(campus.id);
            article.dataset.recordName = campus.name || '';

            const content = document.createElement('div');
            content.className = 'setup-record-content';

            const heading = document.createElement('div');
            heading.className = 'setup-record-heading';
            const name = document.createElement('h4');
            name.textContent = campus.name || '';
            heading.append(name);
            if (campus.isHeadOffice) {
                const badge = document.createElement('span');
                badge.className = 'badge text-bg-primary';
                badge.textContent = i18n.headOffice || '';
                heading.append(badge);
            }

            const meta = document.createElement('p');
            const parts = [campus.code, campus.address].filter(Boolean);
            meta.textContent = parts.join(' · ');
            content.append(heading, meta);

            const actions = document.createElement('div');
            actions.className = 'setup-record-actions';
            actions.append(
                createActionButton('edit', i18n.edit, 'btn-outline-secondary'),
                createActionButton('delete', i18n.deleteLabel, 'btn-outline-danger')
            );
            article.append(content, actions);
            return article;
        });
        list.replaceChildren(...records);
        if (continueButton) continueButton.disabled = false;
    }

    function createActionButton(action, label, className) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = `btn btn-sm ${className}`;
        button.dataset.action = action;
        button.textContent = label || '';
        return button;
    }

    function createEmptyState(message) {
        const empty = document.createElement('div');
        empty.className = 'setup-empty-state';
        const icon = document.createElement('span');
        icon.className = 'setup-empty-icon';
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = '⌂';
        const text = document.createElement('p');
        text.textContent = message || '';
        empty.append(icon, text);
        return empty;
    }

    function renderLoadError() {
        if (!list) return;
        const error = createEmptyState(i18n.loadFailed);
        const retry = document.createElement('button');
        retry.type = 'button';
        retry.className = 'btn btn-sm btn-outline-primary';
        retry.textContent = i18n.retry || '';
        retry.addEventListener('click', loadCampuses, { once: true });
        error.append(retry);
        list.replaceChildren(error);
        if (continueButton) continueButton.disabled = true;
    }

    async function handleListAction(event) {
        const button = event.target.closest('button[data-action]');
        const record = button?.closest('[data-record-id]');
        const id = positiveInteger(record?.dataset.recordId);
        if (!button || !record || !id) return;

        if (button.dataset.action === 'edit') await editCampus(id);
        if (button.dataset.action === 'delete') await deleteCampus(id, record.dataset.recordName || '');
    }

    async function editCampus(id) {
        try {
            const response = await fetch(`/api/institution-onboarding/campus/${encodeURIComponent(id)}`, {
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data) throw new Error('Invalid campus response');
            const campus = payload.data;
            setValue('campusId', id);
            setValue('campusName', campus.name);
            setValue('campusCode', campus.code);
            setValue('campusAddress', campus.address);
            setValue('campusPhone', campus.phone);
            setValue('campusEmail', campus.email);
            setValue('campusHeadName', campus.headName);
            const headOffice = document.getElementById('isHeadOffice');
            if (headOffice) headOffice.checked = Boolean(campus.isHeadOffice);
            openModal();
        } catch {
            showAlert('danger', i18n.loadItemFailed);
        }
    }

    async function deleteCampus(id, name) {
        const question = String(i18n.deleteConfirm || '').replace('{name}', name);
        if (!window.confirm(question)) return;
        try {
            const response = await fetch(`/api/institution-onboarding/campus/${encodeURIComponent(id)}`, {
                method: 'DELETE',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) {
                showAlert('danger', i18n.deleteFailed);
                return;
            }
            await loadCampuses();
            showAlert('success', i18n.deleted);
        } catch {
            showAlert('danger', i18n.networkError);
        }
    }

    async function completeStep() {
        setLoading(continueButton, true);
        try {
            const response = await fetch('/api/onboarding/complete-step', {
                method: 'POST',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                body: JSON.stringify({ step: 4, skipped: false })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) {
                showAlert('danger', i18n.stepFailed);
                return;
            }
            window.location.assign('/Account/AcademicSetup');
        } catch {
            showAlert('danger', i18n.networkError);
        } finally {
            setLoading(continueButton, false);
        }
    }

    function openModal() {
        if (modalElement && window.bootstrap) bootstrap.Modal.getOrCreateInstance(modalElement).show();
    }

    function closeModal() {
        if (modalElement && window.bootstrap) bootstrap.Modal.getOrCreateInstance(modalElement).hide();
    }

    function resetForm() {
        document.getElementById('campusForm')?.reset();
        setValue('campusId', '');
        clearErrors();
    }

    function valueOf(id) {
        return document.getElementById(id)?.value?.trim() ?? '';
    }

    function setValue(id, value) {
        const element = document.getElementById(id);
        if (element) element.value = value ?? '';
    }

    function positiveInteger(value) {
        const number = Number.parseInt(value, 10);
        return Number.isSafeInteger(number) && number > 0 ? number : null;
    }

    function normalizeBanglaDigits(value) {
        return value.replace(/[০-৯]/g, digit => String('০১২৩৪৫৬৭৮৯'.indexOf(digit)));
    }

    function showError(fieldId, message) {
        const field = document.getElementById(fieldId);
        if (!field) return;
        field.classList.add('is-invalid');
        const feedback = field.parentElement?.querySelector('.invalid-feedback');
        if (feedback) feedback.textContent = message || '';
        field.focus();
    }

    function clearErrors() {
        document.querySelectorAll('.is-invalid').forEach(element => element.classList.remove('is-invalid'));
        document.querySelectorAll('.invalid-feedback').forEach(element => { element.textContent = ''; });
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
        if (!button) return;
        button.disabled = loading;
        button.replaceChildren();
        if (loading) {
            const spinner = document.createElement('span');
            spinner.className = 'spinner-border spinner-border-sm me-2';
            spinner.setAttribute('aria-hidden', 'true');
            button.append(spinner, button.dataset.loadingLabel || i18n.saving || '');
        } else {
            button.textContent = button.dataset.idleLabel || '';
        }
    }
})();
