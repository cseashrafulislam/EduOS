(() => {
    'use strict';

    const configElement = document.getElementById('academicSetupStrings');
    const i18n = configElement ? JSON.parse(configElement.textContent || '{}') : {};
    const list = document.getElementById('yearList');
    const continueButton = document.getElementById('continueBtn');
    const yearModal = document.getElementById('academicYearModal');
    const termModal = document.getElementById('termModal');

    document.addEventListener('DOMContentLoaded', async () => {
        if (!list) return;
        document.getElementById('academicYearForm')?.addEventListener('submit', saveYear);
        document.getElementById('termForm')?.addEventListener('submit', saveTerm);
        list.addEventListener('click', handleListAction);
        continueButton?.addEventListener('click', completeStep);
        yearModal?.addEventListener('hidden.bs.modal', resetYearForm);
        termModal?.addEventListener('hidden.bs.modal', resetTermForm);
        await loadAcademicStructure();
    }, { once: true });

    async function saveYear(event) {
        event.preventDefault();
        clearErrors();
        const data = {
            id: positiveInteger(valueOf('yearId')),
            name: valueOf('yearName'),
            startDate: valueOf('yearStartDate'),
            endDate: valueOf('yearEndDate'),
            isCurrent: document.getElementById('isCurrent')?.checked ?? false
        };

        let valid = true;
        if (!data.name) valid = showError('yearName', i18n.yearNameRequired);
        if (!data.startDate) valid = showError('yearStartDate', i18n.startDateRequired);
        if (!data.endDate) valid = showError('yearEndDate', i18n.endDateRequired);
        if (data.startDate && data.endDate && data.startDate >= data.endDate) {
            valid = showError('yearEndDate', i18n.endAfterStart);
        }
        if (!valid) return;

        const button = document.getElementById('saveYearBtn');
        setLoading(button, true);
        try {
            const response = await sendJson('/api/institution-onboarding/academic-year', 'POST', data);
            if (!response.ok) {
                showAlert('danger', i18n.saveFailed);
                return;
            }
            closeModal(yearModal);
            await loadAcademicStructure();
            showAlert('success', i18n.yearSaved);
        } catch {
            showAlert('danger', i18n.networkError);
        } finally {
            setLoading(button, false);
        }
    }

    async function saveTerm(event) {
        event.preventDefault();
        clearErrors();
        const data = {
            id: positiveInteger(valueOf('termId')),
            academicYearId: positiveInteger(valueOf('termYearId')),
            name: valueOf('termName'),
            startDate: valueOf('termStartDate') || null,
            endDate: valueOf('termEndDate') || null
        };

        let valid = true;
        if (!data.academicYearId) valid = showError('termYearId', i18n.selectYearFirst);
        if (!data.name) valid = showError('termName', i18n.termNameRequired);
        if (Boolean(data.startDate) !== Boolean(data.endDate)) {
            valid = showError(data.startDate ? 'termEndDate' : 'termStartDate', i18n.bothTermDates);
        }
        if (data.startDate && data.endDate && data.startDate >= data.endDate) {
            valid = showError('termEndDate', i18n.endAfterStart);
        }
        if (!valid) return;

        const button = document.getElementById('saveTermBtn');
        setLoading(button, true);
        try {
            const response = await sendJson('/api/institution-onboarding/academic-term', 'POST', data);
            if (!response.ok) {
                showAlert('danger', i18n.saveFailed);
                return;
            }
            closeModal(termModal);
            await loadAcademicStructure();
            showAlert('success', i18n.termSaved);
        } catch {
            showAlert('danger', i18n.networkError);
        } finally {
            setLoading(button, false);
        }
    }

    async function loadAcademicStructure() {
        if (!list) return;
        list.setAttribute('aria-busy', 'true');
        try {
            const [yearsResponse, termsResponse] = await Promise.all([
                fetchJson('/api/institution-onboarding/academic-years'),
                fetchJson('/api/institution-onboarding/academic-terms')
            ]);
            if (!yearsResponse.ok || !termsResponse.ok
                || !Array.isArray(yearsResponse.data) || !Array.isArray(termsResponse.data)) {
                throw new Error('Invalid academic structure response');
            }
            renderYears(yearsResponse.data, termsResponse.data);
            populateYearSelect(yearsResponse.data);
            if (continueButton) continueButton.disabled = yearsResponse.data.length === 0;
        } catch (error) {
            console.warn('EduOS academic structure was unavailable.', error);
            renderLoadError();
            if (continueButton) continueButton.disabled = true;
        } finally {
            list.removeAttribute('aria-busy');
        }
    }

    function renderYears(years, terms) {
        if (!list) return;
        if (!years.length) {
            list.replaceChildren(createEmptyState(i18n.noYears));
            return;
        }

        const cards = years.map(year => {
            const card = document.createElement('article');
            card.className = 'setup-record-card academic-record-card';
            card.dataset.recordId = String(year.id);
            card.dataset.recordName = year.name || '';

            const body = document.createElement('div');
            body.className = 'setup-record-content';
            const heading = document.createElement('div');
            heading.className = 'setup-record-heading';
            const name = document.createElement('h4');
            name.textContent = year.name || '';
            heading.append(name);
            if (year.isCurrent) {
                const badge = document.createElement('span');
                badge.className = 'badge text-bg-success';
                badge.textContent = i18n.current || '';
                heading.append(badge);
            }
            const dates = document.createElement('p');
            dates.textContent = `${formatDate(year.startDate)} → ${formatDate(year.endDate)}`;
            body.append(heading, dates);

            const actions = document.createElement('div');
            actions.className = 'setup-record-actions';
            actions.append(
                createActionButton('add-term', i18n.addTerm, 'btn-outline-primary'),
                createActionButton('edit-year', i18n.edit, 'btn-outline-secondary'),
                createActionButton('delete-year', i18n.deleteLabel, 'btn-outline-danger')
            );

            const yearTerms = terms.filter(term => Number(term.academicYearId) === Number(year.id));
            const termList = document.createElement('div');
            termList.className = 'academic-term-list';
            if (yearTerms.length) {
                yearTerms.forEach(term => termList.append(createTermRow(term)));
            } else {
                const empty = document.createElement('p');
                empty.className = 'academic-term-empty';
                empty.textContent = i18n.noTerms || '';
                termList.append(empty);
            }

            card.append(body, actions, termList);
            return card;
        });
        list.replaceChildren(...cards);
    }

    function createTermRow(term) {
        const row = document.createElement('div');
        row.className = 'academic-term-row';
        row.dataset.termId = String(term.id);
        const text = document.createElement('span');
        const dates = term.startDate && term.endDate
            ? ` (${formatDate(term.startDate)} → ${formatDate(term.endDate)})`
            : '';
        text.textContent = `${term.name || ''}${dates}`;
        const remove = createActionButton('delete-term', i18n.removeTerm, 'btn-link text-danger');
        row.append(text, remove);
        return row;
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
        icon.textContent = '▣';
        const text = document.createElement('p');
        text.textContent = message || '';
        empty.append(icon, text);
        return empty;
    }

    function renderLoadError() {
        if (!list) return;
        const error = createEmptyState(i18n.listLoadFailed);
        const retry = document.createElement('button');
        retry.type = 'button';
        retry.className = 'btn btn-sm btn-outline-primary';
        retry.textContent = i18n.retry || '';
        retry.addEventListener('click', loadAcademicStructure, { once: true });
        error.append(retry);
        list.replaceChildren(error);
    }

    function populateYearSelect(years) {
        const select = document.getElementById('termYearId');
        if (!select) return;
        const placeholder = document.createElement('option');
        placeholder.value = '';
        placeholder.textContent = i18n.selectAcademicYear || '';
        const options = years.map(year => {
            const option = document.createElement('option');
            option.value = String(year.id);
            option.textContent = year.name || '';
            return option;
        });
        select.replaceChildren(placeholder, ...options);
    }

    async function handleListAction(event) {
        const button = event.target.closest('button[data-action]');
        if (!button) return;
        const yearCard = button.closest('[data-record-id]');
        const yearId = positiveInteger(yearCard?.dataset.recordId);
        const termId = positiveInteger(button.closest('[data-term-id]')?.dataset.termId);

        if (button.dataset.action === 'add-term' && yearId) {
            openAddTerm(yearId, yearCard?.dataset.recordName || '');
        }
        if (button.dataset.action === 'edit-year' && yearId) await editYear(yearId);
        if (button.dataset.action === 'delete-year' && yearId) await deleteYear(yearId);
        if (button.dataset.action === 'delete-term' && termId) await deleteTerm(termId);
    }

    async function editYear(id) {
        try {
            const response = await fetchJson(`/api/institution-onboarding/academic-year/${encodeURIComponent(id)}`);
            if (!response.ok || !response.data) throw new Error('Invalid academic year response');
            const year = response.data;
            setValue('yearId', id);
            setValue('yearName', year.name);
            setValue('yearStartDate', dateInputValue(year.startDate));
            setValue('yearEndDate', dateInputValue(year.endDate));
            const current = document.getElementById('isCurrent');
            if (current) current.checked = Boolean(year.isCurrent);
            openModal(yearModal);
        } catch {
            showAlert('danger', i18n.itemLoadFailed);
        }
    }

    function openAddTerm(yearId, yearName) {
        resetTermForm();
        setValue('termYearId', yearId);
        const label = document.getElementById('termYearLabel');
        if (label) label.textContent = String(i18n.forYear || '').replace('{name}', yearName);
        openModal(termModal);
    }

    async function deleteYear(id) {
        if (!window.confirm(i18n.deleteYearConfirm || '')) return;
        await deleteRecord(`/api/institution-onboarding/academic-year/${encodeURIComponent(id)}`);
    }

    async function deleteTerm(id) {
        if (!window.confirm(i18n.deleteTermConfirm || '')) return;
        await deleteRecord(`/api/institution-onboarding/academic-term/${encodeURIComponent(id)}`);
    }

    async function deleteRecord(url) {
        try {
            const response = await sendJson(url, 'DELETE');
            if (!response.ok) {
                showAlert('danger', i18n.deleteFailed);
                return;
            }
            await loadAcademicStructure();
            showAlert('success', i18n.deleted);
        } catch {
            showAlert('danger', i18n.networkError);
        }
    }

    async function completeStep() {
        setLoading(continueButton, true);
        try {
            const response = await sendJson('/api/onboarding/complete-step', 'POST', { step: 5, skipped: false });
            if (!response.ok) {
                showAlert('danger', i18n.stepFailed);
                return;
            }
            window.location.assign('/Account/BrandingSetup');
        } catch {
            showAlert('danger', i18n.networkError);
        } finally {
            setLoading(continueButton, false);
        }
    }

    async function fetchJson(url) {
        const response = await fetch(url, {
            cache: 'no-store',
            credentials: 'same-origin',
            headers: { 'Accept': 'application/json' }
        });
        const payload = await response.json().catch(() => null);
        return { ok: response.ok && Boolean(payload?.success), data: payload?.data };
    }

    async function sendJson(url, method, data) {
        const options = {
            method,
            cache: 'no-store',
            credentials: 'same-origin',
            headers: { 'Accept': 'application/json' }
        };
        if (data !== undefined) {
            options.headers['Content-Type'] = 'application/json';
            options.body = JSON.stringify(data);
        }
        const response = await fetch(url, options);
        const payload = await response.json().catch(() => null);
        return { ok: response.ok && Boolean(payload?.success), data: payload?.data };
    }

    function openModal(element) {
        if (element && window.bootstrap) bootstrap.Modal.getOrCreateInstance(element).show();
    }

    function closeModal(element) {
        if (element && window.bootstrap) bootstrap.Modal.getOrCreateInstance(element).hide();
    }

    function resetYearForm() {
        document.getElementById('academicYearForm')?.reset();
        setValue('yearId', '');
        clearErrors();
    }

    function resetTermForm() {
        document.getElementById('termForm')?.reset();
        setValue('termId', '');
        const label = document.getElementById('termYearLabel');
        if (label) label.textContent = '';
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

    function dateInputValue(value) {
        return value ? String(value).slice(0, 10) : '';
    }

    function formatDate(value) {
        if (!value) return '';
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? '' : date.toLocaleDateString(i18n.culture || 'en-BD');
    }

    function showError(fieldId, message) {
        const field = document.getElementById(fieldId);
        if (!field) return false;
        field.classList.add('is-invalid');
        const feedback = field.parentElement?.querySelector('.invalid-feedback');
        if (feedback) feedback.textContent = message || '';
        field.focus();
        return false;
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
