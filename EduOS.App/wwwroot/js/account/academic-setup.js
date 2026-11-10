(() => {
    'use strict';

    const configElement = document.getElementById('academicSetupStrings');
    const i18n = configElement ? JSON.parse(configElement.textContent || '{}') : {};
    const list = document.getElementById('yearList');
    const continueButton = document.getElementById('continueBtn');
    const yearModal = document.getElementById('academicYearModal');
    const termModal = document.getElementById('termModal');
    const identity = [document.body?.dataset.authTenantId || '', document.body?.dataset.authSessionReference || ''];
    const sameSession = () => identity[0] === (document.body?.dataset.authTenantId || '') &&
        identity[1] === (document.body?.dataset.authSessionReference || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
    let yearGuard, termGuard, savingYear = false, savingTerm = false, completing = false;
    let structureTicket = 0, yearEditTicket = 0, termEditTicket = 0;
    const deleting = new Set();
    let editedYearRowVersion = '';
    let editedTermRowVersion = '';

    document.addEventListener('DOMContentLoaded', async () => {
        if (!list) return;
        document.getElementById('academicYearForm')?.addEventListener('submit', saveYear);
        document.getElementById('termForm')?.addEventListener('submit', saveTerm);
        yearGuard = window.EduOSFormGuard?.create(document.getElementById('academicYearForm'));
        termGuard = window.EduOSFormGuard?.create(document.getElementById('termForm'));
        if (yearGuard) window.EduOSFormGuard.protectModal(yearModal, yearGuard, () => savingYear);
        if (termGuard) window.EduOSFormGuard.protectModal(termModal, termGuard, () => savingTerm);
        list.addEventListener('click', handleListAction);
        continueButton?.addEventListener('click', completeStep);
        yearModal?.addEventListener('hidden.bs.modal', resetYearForm);
        termModal?.addEventListener('hidden.bs.modal', resetTermForm);
        await loadAcademicStructure();
    }, { once: true });

    async function saveYear(event) {
        event.preventDefault();
        if (savingYear || !sameSession()) return;
        clearErrors();
        const data = {
            id: positiveInteger(valueOf('yearId')),
            rowVersion: editedYearRowVersion,
            name: valueOf('yearName'),
            code: valueOf('yearCode'),
            campusId: positiveInteger(valueOf('yearCampusId')),
            cycleType: Number(valueOf('yearCycleType')),
            isActive: document.getElementById('yearIsActive')?.checked ?? true,
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
        savingYear = true;
        let committed = false;
        setLoading(button, true);
        try {
            const response = await sendJson('/api/institution-onboarding/academic-year', 'POST', data);
            if (!sameSession()) return;
            if (!response.ok) {
                showAlert('danger', responseMessage(response, i18n.saveFailed));
                return;
            }
            committed = true;
            yearGuard?.markClean();
            closeModal(yearModal);
            const refreshed = await loadAcademicStructure();
            if (sameSession()) showAlert(refreshed ? 'success' : 'danger', refreshed
                ? i18n.yearSaved : 'Academic year saved, but the list could not refresh. Retry loading.');
        } catch {
            if (sameSession()) showAlert('danger', committed
                ? 'Academic year saved, but the list could not refresh. Retry loading.' : i18n.networkError);
        } finally {
            savingYear = false;
            setLoading(button, false);
        }
    }

    async function saveTerm(event) {
        event.preventDefault();
        if (savingTerm || !sameSession()) return;
        clearErrors();
        const data = {
            id: positiveInteger(valueOf('termId')),
            academicYearId: positiveInteger(valueOf('termYearId')),
            name: valueOf('termName'),
            code: valueOf('termCode'),
            rowVersion: editedTermRowVersion,
            isCurrent: document.getElementById('termIsCurrent')?.checked ?? false,
            isActive: document.getElementById('termIsActive')?.checked ?? true,
            displayOrder: Number(valueOf('termDisplayOrder')),
            startDate: valueOf('termStartDate') || null,
            endDate: valueOf('termEndDate') || null
        };

        let valid = true;
        if (!data.academicYearId) valid = showError('termYearId', i18n.selectYearFirst);
        if (!data.name) valid = showError('termName', i18n.termNameRequired);
        if (!data.startDate) valid = showError('termStartDate', i18n.startDateRequired);
        if (!data.endDate) valid = showError('termEndDate', i18n.endDateRequired);
        if (data.startDate && data.endDate && data.startDate >= data.endDate) {
            valid = showError('termEndDate', i18n.endAfterStart);
        }
        if (!valid) return;

        const button = document.getElementById('saveTermBtn');
        savingTerm = true;
        let committed = false;
        setLoading(button, true);
        try {
            const response = await sendJson('/api/institution-onboarding/academic-term', 'POST', data);
            if (!sameSession()) return;
            if (!response.ok) {
                showAlert('danger', responseMessage(response, i18n.saveFailed));
                return;
            }
            committed = true;
            termGuard?.markClean();
            closeModal(termModal);
            const refreshed = await loadAcademicStructure();
            if (sameSession()) showAlert(refreshed ? 'success' : 'danger', refreshed
                ? i18n.termSaved : 'Academic term saved, but the list could not refresh. Retry loading.');
        } catch {
            if (sameSession()) showAlert('danger', committed
                ? 'Academic term saved, but the list could not refresh. Retry loading.' : i18n.networkError);
        } finally {
            savingTerm = false;
            setLoading(button, false);
        }
    }

    async function loadAcademicStructure() {
        if (!list || !sameSession()) return false;
        const ticket = ++structureTicket;
        list.setAttribute('aria-busy', 'true');
        try {
            const [yearsResponse, termsResponse, campusesResponse] = await Promise.all([
                fetchJson('/api/institution-onboarding/academic-years'),
                fetchJson('/api/institution-onboarding/academic-terms'),
                fetchJson('/api/institution-onboarding/campus-list')
            ]);
            if (!yearsResponse.ok || !termsResponse.ok || !campusesResponse.ok
                || !Array.isArray(yearsResponse.data) || !Array.isArray(termsResponse.data)
                || !Array.isArray(campusesResponse.data)) {
                throw new Error('Invalid academic structure response');
            }
            if (ticket !== structureTicket || !sameSession()) return false;
            renderYears(yearsResponse.data, termsResponse.data, campusesResponse.data);
            populateYearSelect(yearsResponse.data);
            populateCampusSelect(campusesResponse.data);
            if (continueButton) continueButton.disabled = yearsResponse.data.length === 0;
            return true;
        } catch (error) {
            if (ticket !== structureTicket || !sameSession()) return false;
            console.warn('EduOS academic structure was unavailable.', error);
            renderLoadError();
            if (continueButton) continueButton.disabled = true;
            return false;
        } finally {
            if (ticket === structureTicket && sameSession()) list.removeAttribute('aria-busy');
        }
    }

    function renderYears(years, terms, campuses) {
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
            card.dataset.rowVersion = year.rowVersion || '';

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
            const campus = campuses.find(x => Number(x.id) === Number(year.campusId));
            dates.textContent = `${formatDate(year.startDate)} → ${formatDate(year.endDate)} · ${cycleLabel(year.cycleType)}${campus ? ' · ' + campus.name : ''}`;
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
        row.dataset.rowVersion = term.rowVersion || '';
        const text = document.createElement('span');
        const dates = term.startDate && term.endDate
            ? ` (${formatDate(term.startDate)} → ${formatDate(term.endDate)})`
            : '';
        text.textContent = `${term.name || ''}${dates}`;
        const edit = createActionButton('edit-term', i18n.edit, 'btn-link');
        const remove = createActionButton('delete-term', i18n.removeTerm, 'btn-link text-danger');
        if (term.isCurrent) text.textContent += ` · ${i18n.current || 'Current'}`;
        row.append(text, edit, remove);
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
        const selected = select.value;
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
        if (options.some(option => option.value === selected)) select.value = selected;
    }

    function populateCampusSelect(campuses) {
        const select = document.getElementById('yearCampusId');
        if (!select) return;
        const selected = select.value;
        const placeholder = document.createElement('option');
        placeholder.value = '';
        placeholder.textContent = 'All campuses (institution-wide)';
        const options = campuses.filter(x => x.isActive).map(campus => {
            const option = document.createElement('option');
            option.value = String(campus.id);
            option.textContent = campus.name || '';
            return option;
        });
        select.replaceChildren(placeholder, ...options);
        if (options.some(option => option.value === selected)) select.value = selected;
    }

    function cycleLabel(value) {
        return ({ 1: 'Annual', 2: 'Semester', 3: 'Trimester',
            4: 'Quarterly', 5: 'Modular', 6: 'Batch based' })[Number(value)] || 'Academic year';
    }

    async function handleListAction(event) {
        if (!sameSession()) return;
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
        if (button.dataset.action === 'edit-term' && termId) await editTerm(termId);
        if (button.dataset.action === 'delete-term' && termId) await deleteTerm(termId);
    }

    async function editYear(id) {
        if (!sameSession() || (yearGuard?.isDirty() && !yearGuard.confirmDiscard())) return;
        const ticket = ++yearEditTicket;
        try {
            const response = await fetchJson(`/api/institution-onboarding/academic-year/${encodeURIComponent(id)}`);
            if (ticket !== yearEditTicket || !sameSession()) return;
            if (!response.ok || !response.data) throw new Error('Invalid academic year response');
            const year = response.data;
            editedYearRowVersion = year.rowVersion || '';
            setValue('yearId', id);
            setValue('yearName', year.name);
            setValue('yearCode', year.code);
            setValue('yearCampusId', year.campusId);
            setValue('yearCycleType', year.cycleType);
            const active = document.getElementById('yearIsActive');
            if (active) active.checked = Boolean(year.isActive);
            setValue('yearStartDate', dateInputValue(year.startDate));
            setValue('yearEndDate', dateInputValue(year.endDate));
            const current = document.getElementById('isCurrent');
            if (current) current.checked = Boolean(year.isCurrent);
            yearGuard?.markClean();
            openModal(yearModal);
        } catch {
            if (ticket === yearEditTicket && sameSession()) showAlert('danger', i18n.itemLoadFailed);
        }
    }

    async function editTerm(id) {
        if (!sameSession() || (termGuard?.isDirty() && !termGuard.confirmDiscard())) return;
        const ticket = ++termEditTicket;
        try {
            const response = await fetchJson(`/api/institution-onboarding/academic-term/${encodeURIComponent(id)}`);
            if (ticket !== termEditTicket || !sameSession()) return;
            if (!response.ok || !response.data) throw new Error('Invalid academic term response');
            const term = response.data;
            resetTermForm();
            editedTermRowVersion = term.rowVersion || '';
            setValue('termId', term.id);
            setValue('termYearId', term.academicYearId);
            setValue('termName', term.name);
            setValue('termCode', term.code);
            setValue('termStartDate', dateInputValue(term.startDate));
            setValue('termEndDate', dateInputValue(term.endDate));
            setValue('termDisplayOrder', term.displayOrder);
            const current = document.getElementById('termIsCurrent');
            if (current) current.checked = Boolean(term.isCurrent);
            const active = document.getElementById('termIsActive');
            if (active) active.checked = Boolean(term.isActive);
            const label = document.getElementById('termYearLabel');
            if (label) label.textContent = term.academicYearName || '';
            termGuard?.markClean();
            openModal(termModal);
        } catch {
            if (ticket === termEditTicket && sameSession()) showAlert('danger', i18n.itemLoadFailed);
        }
    }

    function openAddTerm(yearId, yearName) {
        if (!sameSession() || (termGuard?.isDirty() && !termGuard.confirmDiscard())) return;
        ++termEditTicket;
        resetTermForm();
        setValue('termYearId', yearId);
        const label = document.getElementById('termYearLabel');
        if (label) label.textContent = String(i18n.forYear || '').replace('{name}', yearName);
        termGuard?.markClean();
        openModal(termModal);
    }

    async function deleteYear(id) {
        if (!sameSession()) return;
        if (!window.confirm(i18n.deleteYearConfirm || '')) return;
        const rowVersion = list?.querySelector(`[data-record-id="${id}"]`)?.dataset.rowVersion || '';
        await deleteRecord(`/api/institution-onboarding/academic-year/${encodeURIComponent(id)}?rowVersion=${encodeURIComponent(rowVersion)}`);
    }

    async function deleteTerm(id) {
        if (!sameSession()) return;
        if (!window.confirm(i18n.deleteTermConfirm || '')) return;
        const rowVersion = list?.querySelector(`[data-term-id="${id}"]`)?.dataset.rowVersion || '';
        await deleteRecord(`/api/institution-onboarding/academic-term/${encodeURIComponent(id)}?rowVersion=${encodeURIComponent(rowVersion)}`);
    }

    async function deleteRecord(url) {
        if (deleting.has(url) || !sameSession()) return;
        deleting.add(url);
        let committed = false;
        try {
            const response = await sendJson(url, 'DELETE');
            if (!sameSession()) return;
            if (!response.ok) {
                showAlert('danger', responseMessage(response, i18n.deleteFailed));
                return;
            }
            committed = true;
            const refreshed = await loadAcademicStructure();
            if (sameSession()) showAlert(refreshed ? 'success' : 'danger', refreshed
                ? i18n.deleted : 'Record removed, but the list could not refresh. Retry loading.');
        } catch {
            if (sameSession()) showAlert('danger', committed
                ? 'Record removed, but the list could not refresh. Retry loading.' : i18n.networkError);
        } finally {
            deleting.delete(url);
        }
    }

    async function completeStep() {
        if (completing || !sameSession()) return;
        completing = true;
        setLoading(continueButton, true);
        try {
            const response = await sendJson('/api/onboarding/complete-step', 'POST', { stage: 6, skipped: false });
            if (!sameSession()) return;
            if (!response.ok) {
                showAlert('danger', responseMessage(response, i18n.stepFailed));
                return;
            }
            window.location.assign('/Account/ModuleSetup');
        } catch {
            if (sameSession()) showAlert('danger', i18n.networkError);
        } finally {
            completing = false;
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
        return { ok: response.ok && Boolean(payload?.success), status: response.status,
            data: payload?.data, message: payload?.message, errors: payload?.errors };
    }

    async function sendJson(url, method, data) {
        const options = {
            method,
            cache: 'no-store',
            credentials: 'same-origin',
            headers: { 'Accept': 'application/json',
                'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || '' }
        };
        if (data !== undefined) {
            options.headers['Content-Type'] = 'application/json';
            options.body = JSON.stringify(data);
        }
        const response = await fetch(url, options);
        const payload = await response.json().catch(() => null);
        return { ok: response.ok && Boolean(payload?.success), status: response.status,
            data: payload?.data, message: payload?.message, errors: payload?.errors };
    }

    function responseMessage(result, fallback) {
        if (result?.errors && typeof result.errors === 'object') {
            const messages = Object.values(result.errors).flat().filter(x => typeof x === 'string' && x.trim());
            if (messages.length) return messages.slice(0, 3).join(' ');
        }
        return typeof result?.message === 'string' && result.message.trim()
            ? result.message.trim() : fallback;
    }

    function openModal(element) {
        if (element && window.bootstrap) bootstrap.Modal.getOrCreateInstance(element).show();
    }

    function closeModal(element) {
        if (element && window.bootstrap) bootstrap.Modal.getOrCreateInstance(element).hide();
    }

    function resetYearForm() {
        ++yearEditTicket;
        editedYearRowVersion = '';
        document.getElementById('academicYearForm')?.reset();
        setValue('yearId', '');
        clearErrors();
        yearGuard?.markClean();
    }

    function resetTermForm() {
        ++termEditTicket;
        editedTermRowVersion = '';
        document.getElementById('termForm')?.reset();
        setValue('termId', '');
        const label = document.getElementById('termYearLabel');
        if (label) label.textContent = '';
        clearErrors();
        termGuard?.markClean();
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
