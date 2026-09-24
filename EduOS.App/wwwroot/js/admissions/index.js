(() => {
    'use strict';

    const i18n = JSON.parse(document.getElementById('admissionStrings')?.textContent || '{}');
    const state = { page: 1, pageSize: 20, totalPages: 1, options: null, intakeForms: [], currentApplication: null, pendingRequestId: createRequestId() };
    const statuses = {
        1: i18n.submitted,
        2: i18n.underReview,
        3: i18n.waitlisted,
        4: i18n.approved,
        5: i18n.rejected,
        6: i18n.withdrawn,
        7: i18n.admitted
    };

    document.addEventListener('DOMContentLoaded', async () => {
        const form = document.getElementById('admissionForm');
        if (!form) return;
        form.addEventListener('submit', submitApplication);
        document.getElementById('admissionFilters')?.addEventListener('submit', applyFilters);
        document.getElementById('academicYearId')?.addEventListener('change', refreshTerms);
        document.getElementById('sameAddress')?.addEventListener('change', copyAddress);
        document.getElementById('applicationList')?.addEventListener('click', openReview);
        document.getElementById('intakeFormList')?.addEventListener('click', handleIntakeFormAction);
        document.getElementById('newIntakeFormButton')?.addEventListener('click', openNewIntakeForm);
        document.getElementById('intakeFormEditor')?.addEventListener('submit', saveIntakeForm);
        document.getElementById('intakeYearId')?.addEventListener('change', refreshIntakeTerms);
        document.getElementById('addIntakeFieldButton')?.addEventListener('click', () => addIntakeFieldRow());
        document.getElementById('addDocumentRequirementButton')?.addEventListener('click', () => addDocumentRequirementRow());
        document.getElementById('intakeFieldRows')?.addEventListener('click', removeBuilderRow);
        document.getElementById('documentRequirementRows')?.addEventListener('click', removeBuilderRow);
        document.getElementById('applicantDocumentList')?.addEventListener('click', reviewApplicantDocument);
        document.getElementById('reviewForm')?.addEventListener('submit', saveDecision);
        document.getElementById('admitApplicantButton')?.addEventListener('click', prepareEnrollment);
        document.getElementById('enrollmentForm')?.addEventListener('submit', completeEnrollment);
        document.getElementById('previousPage')?.addEventListener('click', () => changePage(-1));
        document.getElementById('nextPage')?.addEventListener('click', () => changePage(1));
        await loadOptions();
        await loadIntakeForms();
        await loadApplications();
    }, { once: true });

    async function loadOptions() {
        try {
            const response = await fetch('/api/admission-applications/options', apiOptions());
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data) throw new Error('Invalid options');
            state.options = payload.data;
            fillSelect('academicYearId', state.options.academicYears, true);
            fillSelect('campusId', state.options.campuses, true);
            fillSelect('academicUnitId', state.options.academicUnits, true);
            fillSelect('intakeYearId', state.options.academicYears, true);
            fillSelect('intakeCampusId', state.options.campuses, true);
            fillSelect('intakeUnitId', state.options.academicUnits, true);
            refreshTerms();
            refreshIntakeTerms();
        } catch {
            showAlert('danger', i18n.optionsFailed);
        }
    }

    function fillSelect(id, items, selectFirst) {
        const select = document.getElementById(id);
        if (!select) return;
        const placeholder = document.createElement('option');
        placeholder.value = '';
        placeholder.textContent = i18n.select || '';
        const options = [placeholder, ...(Array.isArray(items) ? items.map(item => {
            const option = document.createElement('option');
            option.value = String(item.id);
            option.textContent = item.name || '';
            return option;
        }) : [])];
        select.replaceChildren(...options);
        if (selectFirst && options.length === 2) select.selectedIndex = 1;
    }

    function refreshTerms() {
        const yearId = positiveInteger(valueOf('academicYearId'));
        const terms = state.options?.academicTerms?.filter(item => Number(item.parentId) === yearId) || [];
        fillSelect('academicTermId', terms, false);
    }

    function refreshIntakeTerms(selectedValue = null) {
        const yearId = positiveInteger(valueOf('intakeYearId'));
        const terms = state.options?.academicTerms?.filter(item => Number(item.parentId) === yearId) || [];
        fillSelect('intakeTermId', terms, false);
        if (selectedValue) setValue('intakeTermId', selectedValue);
    }

    async function loadIntakeForms() {
        const list = document.getElementById('intakeFormList');
        if (!list) return;
        list.setAttribute('aria-busy', 'true');
        list.replaceChildren(statusMessage(i18n.loading));
        try {
            const response = await fetch('/api/admission-intake/forms', apiOptions());
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !Array.isArray(payload.data)) throw new Error(payload?.message || 'Admission forms could not be loaded.');
            state.intakeForms = payload.data;
            renderIntakeForms();
        } catch (error) {
            list.replaceChildren(statusMessage(error.message || 'Admission forms could not be loaded.'));
        } finally {
            list.removeAttribute('aria-busy');
        }
    }

    function renderIntakeForms() {
        const list = document.getElementById('intakeFormList');
        if (!list) return;
        if (!state.intakeForms.length) {
            list.replaceChildren(statusMessage('No configured admission forms. Create a draft to open public applications.'));
            return;
        }
        list.replaceChildren(...state.intakeForms.map(item => {
            const card = document.createElement('article');
            card.className = 'intake-form-card';
            const summary = document.createElement('div');
            const title = document.createElement('h4');
            title.textContent = item.title || '';
            const detail = document.createElement('p');
            detail.textContent = `${item.code || ''} · ${formatDate(item.opensAtUtc)} — ${formatDate(item.closesAtUtc)} · ${item.currency || ''} ${Number(item.applicationFee || 0).toFixed(2)}`;
            const counts = document.createElement('p');
            counts.textContent = `${item.fields?.length || 0} custom fields · ${item.documentRequirements?.length || 0} document requirements`;
            summary.append(title, detail, counts);
            const controls = document.createElement('div');
            controls.className = 'intake-form-actions';
            const badge = document.createElement('span');
            badge.className = `admission-status status-${Number(item.status) === 2 ? 4 : Number(item.status) === 3 ? 6 : 1}`;
            badge.textContent = ({ 1: 'Draft', 2: 'Published', 3: 'Closed', 4: 'Archived' })[Number(item.status)] || 'Unknown';
            controls.append(badge);
            if (Number(item.status) === 1) {
                controls.append(actionButton('Edit', 'edit', item.id, 'btn-outline-secondary'), actionButton('Publish', 'publish', item.id, 'btn-success'));
            } else if (Number(item.status) === 2) {
                controls.append(actionButton('Close', 'close', item.id, 'btn-outline-danger'));
            }
            card.append(summary, controls);
            return card;
        }));
    }

    function actionButton(label, action, id, style) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = `btn btn-sm ${style}`;
        button.dataset.intakeAction = action;
        button.dataset.intakeId = String(id);
        button.textContent = label;
        return button;
    }

    async function handleIntakeFormAction(event) {
        const button = event.target.closest('button[data-intake-action]');
        if (!button) return;
        const item = state.intakeForms.find(x => Number(x.id) === Number(button.dataset.intakeId));
        if (!item) return;
        if (button.dataset.intakeAction === 'edit') {
            openIntakeFormEditor(item);
            return;
        }
        const action = button.dataset.intakeAction;
        if (action !== 'publish' && action !== 'close') return;
        button.disabled = true;
        try {
            const response = await fetch(`/api/admission-intake/forms/${encodeURIComponent(item.id)}/${action}`, apiOptions('POST', { rowVersion: item.rowVersion }));
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error(payload?.message || `Admission form could not be ${action}ed.`);
            showAlert('success', payload.message || `Admission form ${action}ed.`);
            await loadIntakeForms();
        } catch (error) {
            showAlert('danger', error.message || `Admission form could not be ${action}ed.`);
        } finally {
            button.disabled = false;
        }
    }

    function openNewIntakeForm() {
        openIntakeFormEditor(null);
    }

    function openIntakeFormEditor(item) {
        const editor = document.getElementById('intakeFormEditor');
        if (!editor || !state.options) return;
        editor.reset();
        setValue('intakeFormId', item?.id);
        setValue('intakeFormRowVersion', item?.rowVersion);
        setValue('intakeFormRequestId', item ? '' : createRequestId());
        setValue('intakeCode', item?.code);
        setValue('intakeTitle', item?.title);
        setValue('intakeDescription', item?.description);
        fillSelect('intakeYearId', state.options.academicYears, true);
        fillSelect('intakeCampusId', state.options.campuses, true);
        fillSelect('intakeUnitId', state.options.academicUnits, true);
        if (item) {
            setValue('intakeYearId', item.academicYearId);
            setValue('intakeCampusId', item.campusId);
            setValue('intakeUnitId', item.academicUnitId);
        }
        refreshIntakeTerms(item?.academicTermId);
        const now = new Date();
        const closes = new Date(now.valueOf() + 30 * 24 * 60 * 60 * 1000);
        setValue('intakeOpensAt', toLocalDateTime(item?.opensAtUtc || now));
        setValue('intakeClosesAt', toLocalDateTime(item?.closesAtUtc || closes));
        setValue('intakeFee', item?.applicationFee ?? 0);
        setValue('intakeCurrency', item?.currency || 'BDT');
        const fields = document.getElementById('intakeFieldRows');
        const requirements = document.getElementById('documentRequirementRows');
        fields?.replaceChildren();
        requirements?.replaceChildren();
        (item?.fields || []).forEach(addIntakeFieldRow);
        (item?.documentRequirements || []).forEach(addDocumentRequirementRow);
        bootstrap.Modal.getOrCreateInstance(document.getElementById('intakeFormModal')).show();
    }

    function addIntakeFieldRow(field = null) {
        const container = document.getElementById('intakeFieldRows');
        if (!container) return;
        const row = document.createElement('div');
        row.className = 'intake-builder-row intake-field-row';
        const key = builderInput('Key', 'intake-field-key', 'text', field?.key || '');
        key.input.required = true;
        key.input.pattern = '[A-Za-z][A-Za-z0-9_]*';
        const label = builderInput('Label', 'intake-field-label', 'text', field?.label || '');
        label.input.required = true;
        const type = builderSelect('Type', 'intake-field-type', [
            [1, 'Text'], [2, 'Text area'], [3, 'Number'], [4, 'Date'], [5, 'Choice'], [6, 'Yes / No']
        ], field?.type || 1);
        const maximum = builderInput('Max length', 'intake-field-max', 'number', field?.maxLength || '');
        maximum.input.min = '1'; maximum.input.max = '4000';
        const options = builderInput('Choices (comma separated)', 'intake-field-options', 'text', (field?.options || []).join(', '));
        const required = builderCheckbox('Required', 'intake-field-required', Boolean(field?.isRequired));
        row.append(key.wrapper, label.wrapper, type.wrapper, maximum.wrapper, options.wrapper, required.wrapper, removeBuilderButton());
        container.append(row);
    }

    function addDocumentRequirementRow(requirement = null) {
        const container = document.getElementById('documentRequirementRows');
        if (!container) return;
        const row = document.createElement('div');
        row.className = 'intake-builder-row intake-document-row';
        const type = builderInput('Document type', 'intake-document-type', 'text', requirement?.documentType || '');
        type.input.required = true; type.input.pattern = '[A-Za-z][A-Za-z0-9_-]*';
        const label = builderInput('Label', 'intake-document-label', 'text', requirement?.label || '');
        label.input.required = true;
        const size = builderInput('Maximum MB', 'intake-document-size', 'number', requirement?.maxFileSizeMb || 5);
        size.input.required = true; size.input.min = '1'; size.input.max = '10';
        const extensions = builderInput('Extensions', 'intake-document-extensions', 'text', (requirement?.allowedExtensions || ['.pdf', '.jpg', '.png']).join(', '));
        extensions.input.required = true;
        const required = builderCheckbox('Required', 'intake-document-required', Boolean(requirement?.isRequired));
        row.append(type.wrapper, label.wrapper, size.wrapper, extensions.wrapper, required.wrapper, removeBuilderButton());
        container.append(row);
    }

    function builderInput(labelText, className, type, initialValue) {
        const wrapper = document.createElement('label');
        wrapper.className = 'form-label intake-builder-control';
        wrapper.append(document.createTextNode(labelText));
        const input = document.createElement('input');
        input.className = `form-control ${className}`;
        input.type = type;
        input.value = initialValue;
        wrapper.append(input);
        return { wrapper, input };
    }

    function builderSelect(labelText, className, choices, selected) {
        const wrapper = document.createElement('label');
        wrapper.className = 'form-label intake-builder-control';
        wrapper.append(document.createTextNode(labelText));
        const select = document.createElement('select');
        select.className = `form-select ${className}`;
        for (const [value, text] of choices) {
            const option = document.createElement('option');
            option.value = String(value); option.textContent = text; option.selected = Number(value) === Number(selected);
            select.append(option);
        }
        wrapper.append(select);
        return { wrapper, input: select };
    }

    function builderCheckbox(labelText, className, checked) {
        const wrapper = document.createElement('label');
        wrapper.className = 'form-check intake-builder-check';
        const input = document.createElement('input');
        input.className = `form-check-input ${className}`;
        input.type = 'checkbox'; input.checked = checked;
        const text = document.createElement('span');
        text.className = 'form-check-label'; text.textContent = labelText;
        wrapper.append(input, text);
        return { wrapper, input };
    }

    function removeBuilderButton() {
        const button = document.createElement('button');
        button.type = 'button'; button.className = 'btn btn-sm btn-outline-danger intake-remove-row'; button.textContent = 'Remove';
        return button;
    }

    function removeBuilderRow(event) {
        event.target.closest('.intake-remove-row')?.closest('.intake-builder-row')?.remove();
    }

    async function saveIntakeForm(event) {
        event.preventDefault();
        if (!event.currentTarget.reportValidity()) return;
        const id = positiveInteger(valueOf('intakeFormId'));
        const fields = [...document.querySelectorAll('.intake-field-row')].map((row, index) => {
            const type = positiveInteger(row.querySelector('.intake-field-type')?.value) || 1;
            const choices = row.querySelector('.intake-field-options')?.value.split(',').map(x => x.trim()).filter(Boolean) || [];
            return {
                key: row.querySelector('.intake-field-key')?.value.trim(),
                label: row.querySelector('.intake-field-label')?.value.trim(),
                labelBangla: null,
                type,
                isRequired: Boolean(row.querySelector('.intake-field-required')?.checked),
                maxLength: positiveInteger(row.querySelector('.intake-field-max')?.value),
                displayOrder: index,
                options: type === 5 ? choices : []
            };
        });
        const documentRequirements = [...document.querySelectorAll('.intake-document-row')].map(row => ({
            documentType: row.querySelector('.intake-document-type')?.value.trim(),
            label: row.querySelector('.intake-document-label')?.value.trim(),
            labelBangla: null,
            isRequired: Boolean(row.querySelector('.intake-document-required')?.checked),
            maxFileSizeMb: positiveInteger(row.querySelector('.intake-document-size')?.value),
            allowedExtensions: row.querySelector('.intake-document-extensions')?.value.split(/[\s,]+/).map(x => x.trim()).filter(Boolean) || []
        }));
        const request = {
            code: valueOf('intakeCode'), title: valueOf('intakeTitle'), description: valueOf('intakeDescription') || null,
            academicYearId: positiveInteger(valueOf('intakeYearId')), academicTermId: positiveInteger(valueOf('intakeTermId')),
            campusId: positiveInteger(valueOf('intakeCampusId')), academicUnitId: positiveInteger(valueOf('intakeUnitId')),
            opensAtUtc: new Date(valueOf('intakeOpensAt')).toISOString(), closesAtUtc: new Date(valueOf('intakeClosesAt')).toISOString(),
            applicationFee: Number(valueOf('intakeFee') || 0), currency: valueOf('intakeCurrency'), fields, documentRequirements
        };
        if (id) request.rowVersion = valueOf('intakeFormRowVersion');
        else request.clientRequestId = valueOf('intakeFormRequestId');
        const button = document.getElementById('saveIntakeFormButton');
        setLoading(button, true);
        try {
            const response = await fetch(id ? `/api/admission-intake/forms/${encodeURIComponent(id)}` : '/api/admission-intake/forms', apiOptions(id ? 'PUT' : 'POST', request));
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error(payload?.message || 'Admission form could not be saved.');
            bootstrap.Modal.getInstance(document.getElementById('intakeFormModal'))?.hide();
            showAlert('success', payload.message || 'Admission form saved.');
            await loadIntakeForms();
        } catch (error) {
            showAlert('danger', error.message || 'Admission form could not be saved.');
        } finally {
            setLoading(button, false);
        }
    }

    async function submitApplication(event) {
        event.preventDefault();
        const form = event.currentTarget;
        if (!form.reportValidity()) return;
        const dateOfBirth = valueOf('dateOfBirth');
        if (isMinor(dateOfBirth) && (!valueOf('guardianName') || !valueOf('guardianRelation') || !valueOf('guardianMobile'))) {
            showAlert('danger', i18n.minorGuardianRequired);
            document.getElementById('guardianName')?.focus();
            return;
        }

        const request = {
            clientRequestId: state.pendingRequestId,
            academicYearId: positiveInteger(valueOf('academicYearId')),
            academicTermId: positiveInteger(valueOf('academicTermId')),
            campusId: positiveInteger(valueOf('campusId')),
            academicUnitId: positiveInteger(valueOf('academicUnitId')),
            applicantName: valueOf('applicantName'),
            applicantNameBangla: valueOf('applicantNameBangla') || null,
            dateOfBirth,
            gender: positiveInteger(valueOf('gender')),
            primaryMobile: normalizeBanglaDigits(valueOf('primaryMobile')),
            email: valueOf('email') || null,
            guardianName: valueOf('guardianName') || null,
            guardianRelation: valueOf('guardianRelation') || null,
            guardianMobile: normalizeBanglaDigits(valueOf('guardianMobile')) || null,
            presentAddress: valueOf('presentAddress') || null,
            permanentAddress: valueOf('permanentAddress') || null,
            previousInstitution: valueOf('previousInstitution') || null,
            preferredLanguage: valueOf('preferredLanguage') || 'bn-BD'
        };
        const button = document.getElementById('submitApplicationButton');
        setLoading(button, true);
        try {
            const response = await fetch('/api/admission-applications', apiOptions('POST', request));
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) {
                showAlert('danger', i18n.createFailed);
                return;
            }
            form.reset();
            state.pendingRequestId = createRequestId();
            fillSelect('academicYearId', state.options?.academicYears, true);
            fillSelect('campusId', state.options?.campuses, true);
            fillSelect('academicUnitId', state.options?.academicUnits, true);
            refreshTerms();
            showAlert('success', `${i18n.created} ${payload.data?.applicationNumber || ''}`.trim());
            state.page = 1;
            await loadApplications();
        } catch {
            showAlert('danger', i18n.networkError);
        } finally {
            setLoading(button, false);
        }
    }

    async function loadApplications() {
        const list = document.getElementById('applicationList');
        if (!list) return;
        list.setAttribute('aria-busy', 'true');
        list.replaceChildren(statusMessage(i18n.loading));
        const params = new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize) });
        const search = valueOf('applicationSearch');
        const status = positiveInteger(valueOf('statusFilter'));
        if (search) params.set('search', search);
        if (status) params.set('status', String(status));
        try {
            const response = await fetch(`/api/admission-applications?${params}`, apiOptions());
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !Array.isArray(payload.data?.items)) throw new Error('Invalid list');
            state.totalPages = Math.max(1, Number(payload.data.totalPages) || 1);
            renderApplications(payload.data.items);
            updatePagination();
        } catch {
            const error = statusMessage(i18n.loadFailed);
            const retry = document.createElement('button');
            retry.type = 'button';
            retry.className = 'btn btn-sm btn-outline-primary';
            retry.textContent = i18n.retry || '';
            retry.addEventListener('click', loadApplications, { once: true });
            error.append(retry);
            list.replaceChildren(error);
        } finally {
            list.removeAttribute('aria-busy');
        }
    }

    function renderApplications(items) {
        const list = document.getElementById('applicationList');
        if (!list) return;
        if (!items.length) {
            list.replaceChildren(statusMessage(i18n.empty));
            return;
        }
        list.replaceChildren(...items.map(item => {
            const card = document.createElement('article');
            card.className = 'admission-card';
            const heading = document.createElement('div');
            heading.className = 'admission-card-heading';
            const title = document.createElement('h4');
            title.textContent = item.applicantName || '';
            const badge = document.createElement('span');
            badge.className = `admission-status status-${Number(item.status)}`;
            badge.textContent = statusName(item.status);
            heading.append(title, badge);
            const number = detailLine(i18n.applicationNo, item.applicationNumber);
            const unit = detailLine(i18n.academicUnit, [item.academicUnitName, item.campusName].filter(Boolean).join(' · '));
            const contact = detailLine(i18n.fullContact, item.maskedMobile);
            const submitted = detailLine(i18n.submittedAt, formatDate(item.submittedAtUtc));
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'btn btn-sm btn-outline-primary';
            button.dataset.reference = item.reference;
            button.textContent = i18n.review || '';
            card.append(heading, number, unit, contact, submitted, button);
            return card;
        }));
    }

    async function openReview(event) {
        const button = event.target.closest('button[data-reference]');
        if (!button) return;
        try {
            const response = await fetch(`/api/admission-applications/${encodeURIComponent(button.dataset.reference)}`, apiOptions());
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data) throw new Error('Invalid details');
            renderDetails(payload.data);
            state.currentApplication = payload.data;
            await loadApplicantDocuments(payload.data.reference);
            setValue('reviewReference', payload.data.reference);
            setValue('reviewRowVersion', payload.data.rowVersion);
            setValue('decisionNote', payload.data.decisionNote);
            fillReviewStatuses(Number(payload.data.status));
            document.getElementById('admitApplicantButton')?.classList.toggle('d-none', Number(payload.data.status) !== 4);
            bootstrap.Modal.getOrCreateInstance(document.getElementById('reviewModal')).show();
        } catch {
            showAlert('danger', i18n.detailsFailed);
        }
    }

    function renderDetails(item) {
        const details = document.getElementById('applicationDetails');
        if (!details) return;
        const rows = [
            [i18n.applicationNo, item.applicationNumber],
            [i18n.applicant, item.applicantName],
            [i18n.dateOfBirth, formatDateOnly(item.dateOfBirth)],
            [i18n.gender, genderName(item.gender)],
            [i18n.fullContact, [item.primaryMobile, item.email].filter(Boolean).join(' · ')],
            [i18n.guardian, [item.guardianName, item.guardianRelation, item.guardianMobile].filter(Boolean).join(' · ')],
            [i18n.academicUnit, [item.academicUnitName, item.academicYearName, item.campusName].filter(Boolean).join(' · ')],
            [i18n.academicTerm, item.academicTermName],
            [i18n.previousInstitution, item.previousInstitution],
            [i18n.presentAddress, item.presentAddress],
            [i18n.permanentAddress, item.permanentAddress],
            [i18n.preferredLanguage, item.preferredLanguage],
            ['Admission form', item.admissionFormTitle],
            [i18n.decisionNote, item.decisionNote]
        ];
        Object.entries(item.customResponses || {}).forEach(([key, value]) => rows.push([key.replaceAll('_', ' '), value]));
        const nodes = [];
        rows.filter(row => row[1]).forEach(row => {
            const term = document.createElement('dt');
            term.textContent = row[0] || '';
            const value = document.createElement('dd');
            value.textContent = row[1] || '';
            nodes.push(term, value);
        });
        details.replaceChildren(...nodes);
    }

    async function loadApplicantDocuments(reference) {
        const list = document.getElementById('applicantDocumentList');
        if (!list) return;
        list.replaceChildren(statusMessage(i18n.loading));
        try {
            const response = await fetch(`/api/admission-intake/applications/${encodeURIComponent(reference)}/documents`, apiOptions());
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !Array.isArray(payload.data)) throw new Error(payload?.message || 'Documents could not be loaded.');
            if (!payload.data.length) {
                list.replaceChildren(statusMessage('No applicant documents have been uploaded.'));
                return;
            }
            list.replaceChildren(...payload.data.map(documentItem => {
                const card = document.createElement('article');
                card.className = 'applicant-document-card';
                const summary = document.createElement('div');
                const title = document.createElement('strong');
                title.textContent = documentItem.documentType || '';
                const detail = document.createElement('span');
                detail.textContent = `${documentItem.originalFileName || ''} · ${formatFileSize(documentItem.fileSizeBytes)} · ${{ 1: 'Pending', 2: 'Verified', 3: 'Rejected' }[Number(documentItem.verificationStatus)] || 'Unknown'}`;
                summary.append(title, detail);
                if (documentItem.reviewNote) {
                    const note = document.createElement('span');
                    note.textContent = documentItem.reviewNote;
                    summary.append(note);
                }
                const controls = document.createElement('div');
                controls.className = 'intake-form-actions';
                const download = document.createElement('a');
                download.className = 'btn btn-sm btn-outline-secondary';
                download.href = `/api/admission-intake/applications/${encodeURIComponent(reference)}/documents/${encodeURIComponent(documentItem.id)}/content`;
                download.textContent = 'View / Download';
                controls.append(download);
                if (Number(documentItem.verificationStatus) === 1) {
                    controls.append(documentActionButton('Verify', 2, documentItem), documentActionButton('Reject', 3, documentItem));
                }
                card.append(summary, controls);
                return card;
            }));
        } catch (error) {
            list.replaceChildren(statusMessage(error.message || 'Documents could not be loaded.'));
        }
    }

    function documentActionButton(label, status, item) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = `btn btn-sm ${status === 2 ? 'btn-success' : 'btn-outline-danger'}`;
        button.dataset.documentId = String(item.id);
        button.dataset.documentStatus = String(status);
        button.dataset.documentRowVersion = item.rowVersion;
        button.textContent = label;
        return button;
    }

    async function reviewApplicantDocument(event) {
        const button = event.target.closest('button[data-document-id]');
        if (!button || !state.currentApplication?.reference) return;
        const status = Number(button.dataset.documentStatus);
        const note = status === 3 ? window.prompt('Rejection reason / প্রত্যাখ্যানের কারণ') : null;
        if (status === 3 && note === null) return;
        if (status === 3 && !note.trim()) {
            showAlert('danger', 'A rejection reason is required.');
            return;
        }
        button.disabled = true;
        try {
            const response = await fetch(`/api/admission-intake/applications/${encodeURIComponent(state.currentApplication.reference)}/documents/${encodeURIComponent(button.dataset.documentId)}/review`, apiOptions('POST', {
                status, note: note?.trim() || null, rowVersion: button.dataset.documentRowVersion
            }));
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error(payload?.message || 'Document decision could not be saved.');
            showAlert('success', payload.message || 'Document decision saved.');
            await loadApplicantDocuments(state.currentApplication.reference);
        } catch (error) {
            showAlert('danger', error.message || 'Document decision could not be saved.');
        } finally {
            button.disabled = false;
        }
    }

    function fillReviewStatuses(current) {
        const transitions = { 1: [2, 6], 2: [3, 4, 5], 3: [4, 5] };
        const select = document.getElementById('reviewStatus');
        const choices = transitions[current] || [];
        const placeholder = document.createElement('option');
        placeholder.value = '';
        placeholder.textContent = i18n.select || '';
        const options = choices.map(status => {
            const option = document.createElement('option');
            option.value = String(status);
            option.textContent = statusName(status);
            return option;
        });
        select?.replaceChildren(placeholder, ...options);
        const button = document.getElementById('saveDecisionButton');
        if (button) button.disabled = choices.length === 0;
    }

    async function saveDecision(event) {
        event.preventDefault();
        const reference = valueOf('reviewReference');
        const status = positiveInteger(valueOf('reviewStatus'));
        if (!reference || !status) return;
        const button = document.getElementById('saveDecisionButton');
        setLoading(button, true);
        try {
            const response = await fetch(`/api/admission-applications/${encodeURIComponent(reference)}/status`, apiOptions('PUT', {
                status,
                rowVersion: valueOf('reviewRowVersion'),
                decisionNote: valueOf('decisionNote') || null
            }));
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) {
                showAlert('danger', i18n.decisionFailed);
                return;
            }
            bootstrap.Modal.getInstance(document.getElementById('reviewModal'))?.hide();
            showAlert('success', i18n.decisionSaved);
            await loadApplications();
        } catch {
            showAlert('danger', i18n.networkError);
        } finally {
            setLoading(button, false);
        }
    }

    async function prepareEnrollment() {
        const item = state.currentApplication;
        if (!item?.reference || Number(item.status) !== 4) return;
        try {
            const response = await fetch(`/api/admission-applications/${encodeURIComponent(item.reference)}/enrollment-options`, apiOptions());
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data) throw new Error('Invalid enrollment options');
            fillSelect('enrollmentSectionId', payload.data.sections, true);
            fillSelect('enrollmentGroupId', payload.data.groups, false);
            setValue('enrollmentReference', item.reference);
            setValue('enrollmentRowVersion', item.rowVersion);
            setValue('enrollmentRoll', '');
            bootstrap.Modal.getInstance(document.getElementById('reviewModal'))?.hide();
            bootstrap.Modal.getOrCreateInstance(document.getElementById('enrollmentModal')).show();
        } catch {
            showAlert('danger', i18n.enrollmentFailed);
        }
    }

    async function completeEnrollment(event) {
        event.preventDefault();
        if (!event.currentTarget.reportValidity()) return;
        const reference = valueOf('enrollmentReference');
        const button = document.getElementById('completeEnrollmentButton');
        setLoading(button, true);
        try {
            const response = await fetch(`/api/admission-applications/${encodeURIComponent(reference)}/admit`, apiOptions('POST', {
                sectionId: positiveInteger(valueOf('enrollmentSectionId')),
                groupId: positiveInteger(valueOf('enrollmentGroupId')),
                roll: valueOf('enrollmentRoll'),
                rowVersion: valueOf('enrollmentRowVersion')
            }));
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) {
                showAlert('danger', i18n.enrollmentFailed);
                return;
            }
            bootstrap.Modal.getInstance(document.getElementById('enrollmentModal'))?.hide();
            showAlert('success', `${i18n.admittedSuccess} ${payload.data?.studentCode || ''}`.trim());
            state.currentApplication = null;
            await loadApplications();
        } catch {
            showAlert('danger', i18n.networkError);
        } finally {
            setLoading(button, false);
        }
    }

    function applyFilters(event) {
        event.preventDefault();
        state.page = 1;
        loadApplications();
    }

    function changePage(change) {
        const next = state.page + change;
        if (next < 1 || next > state.totalPages) return;
        state.page = next;
        loadApplications();
    }

    function updatePagination() {
        const previous = document.getElementById('previousPage');
        const next = document.getElementById('nextPage');
        if (previous) previous.disabled = state.page <= 1;
        if (next) next.disabled = state.page >= state.totalPages;
        const label = document.getElementById('pageStatus');
        if (label) label.textContent = `${i18n.pageLabel || ''} ${state.page} / ${state.totalPages}`;
    }

    function copyAddress(event) {
        const permanent = document.getElementById('permanentAddress');
        if (!permanent) return;
        permanent.disabled = event.currentTarget.checked;
        if (event.currentTarget.checked) permanent.value = valueOf('presentAddress');
    }

    function apiOptions(method = 'GET', body = null) {
        const options = { method, cache: 'no-store', credentials: 'same-origin', headers: { Accept: 'application/json' } };
        if (body !== null) {
            options.headers['Content-Type'] = 'application/json';
            options.body = JSON.stringify(body);
        }
        return options;
    }

    function detailLine(label, value) {
        const line = document.createElement('p');
        const name = document.createElement('strong');
        name.textContent = `${label || ''}: `;
        line.append(name, value || '—');
        return line;
    }

    function statusMessage(message) {
        const box = document.createElement('div');
        box.className = 'admission-empty';
        const text = document.createElement('p');
        text.textContent = message || '';
        box.append(text);
        return box;
    }

    function showAlert(type, message) {
        const alert = document.getElementById('admissionAlert');
        if (!alert) return;
        alert.className = `alert alert-${type === 'success' ? 'success' : 'danger'}`;
        alert.textContent = message || '';
        alert.focus();
    }

    function setLoading(button, loading) {
        if (!button) return;
        button.disabled = loading;
        button.textContent = loading ? button.dataset.loadingLabel || i18n.saving : button.dataset.idleLabel || '';
    }

    function valueOf(id) { return document.getElementById(id)?.value?.trim() || ''; }
    function setValue(id, value) { const element = document.getElementById(id); if (element) element.value = value || ''; }
    function positiveInteger(value) { const number = Number.parseInt(value, 10); return Number.isSafeInteger(number) && number > 0 ? number : null; }
    function normalizeBanglaDigits(value) { return value.replace(/[০-৯]/g, digit => String('০১২৩৪৫৬৭৮৯'.indexOf(digit))); }
    function statusName(status) { return statuses[Number(status)] || ''; }
    function genderName(gender) { return ({ 1: i18n.male, 2: i18n.female, 3: i18n.other })[Number(gender)] || ''; }
    function formatDate(value) { const date = new Date(value); return Number.isNaN(date.valueOf()) ? '' : new Intl.DateTimeFormat(i18n.culture || 'en-BD', { dateStyle: 'medium', timeStyle: 'short' }).format(date); }
    function formatDateOnly(value) { const date = new Date(`${String(value || '').slice(0, 10)}T00:00:00`); return Number.isNaN(date.valueOf()) ? '' : new Intl.DateTimeFormat(i18n.culture || 'en-BD', { dateStyle: 'medium' }).format(date); }
    function toLocalDateTime(value) { const date = value instanceof Date ? value : new Date(value); if (Number.isNaN(date.valueOf())) return ''; const local = new Date(date.valueOf() - date.getTimezoneOffset() * 60000); return local.toISOString().slice(0, 16); }
    function formatFileSize(value) { const bytes = Number(value) || 0; return bytes < 1024 ? `${bytes} B` : bytes < 1024 * 1024 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`; }
    function isMinor(value) { const dob = new Date(`${value}T00:00:00`); const threshold = new Date(); threshold.setFullYear(threshold.getFullYear() - 18); return !Number.isNaN(dob.valueOf()) && dob > threshold; }
    function createRequestId() { if (crypto.randomUUID) return crypto.randomUUID(); const bytes = crypto.getRandomValues(new Uint8Array(16)); bytes[6] = (bytes[6] & 15) | 64; bytes[8] = (bytes[8] & 63) | 128; const hex = [...bytes].map(x => x.toString(16).padStart(2, '0')).join(''); return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`; }
})();
