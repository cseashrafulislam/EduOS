(() => {
    'use strict';

    const i18n = JSON.parse(document.getElementById('admissionStrings')?.textContent || '{}');
    const bangla = document.documentElement.lang?.toLowerCase().startsWith('bn') === true;
    const state = { page: 1, pageSize: 20, totalPages: 1, options: null, optionsRequest: 0, intakeForms: [], currentApplication: null, pendingRequestId: createRequestId(), pendingFingerprint: null, submitting: false, listRequest: 0, intakeRequest: 0, reviewRequest: 0, documentRequest: 0, reviewingDocument: false, documents: [], documentReference: null, documentPage: 1, savingIntake: false, intakeFingerprint: null, listPageRows: null };
    const guards = { application: null, intake: null, review: null, enrollment: null };
    const identity = [document.body?.dataset?.authTenantId || '', document.body?.dataset?.authSessionReference || '', document.body?.dataset?.authUserId || ''];
    const sameSession = () => identity[0] === (document.body?.dataset?.authTenantId || '') &&
        identity[1] === (document.body?.dataset?.authSessionReference || '') &&
        identity[2] === (document.body?.dataset?.authUserId || '');
    // Canonical AdmissionApplicantState (FinalDomainEnums), not the retired 1-7 UI status map.
    const statuses = {
        1: bangla ? 'খসড়া' : 'Draft',
        2: i18n.submitted,
        3: i18n.underReview,
        4: bangla ? 'কাগজপত্র যাচাই বাকি' : 'Documents pending',
        5: bangla ? 'পরীক্ষা বাকি' : 'Assessment pending',
        6: bangla ? 'যোগ্য' : 'Qualified',
        7: i18n.rejected,
        8: i18n.admitted,
        9: bangla ? 'প্রত্যাহার' : 'Withdrawn'
    };

    document.addEventListener('DOMContentLoaded', async () => {
        const form = document.getElementById('admissionForm');
        if (!form) return;
        for (const [name, id, modalId, isSaving] of [
            ['application', 'admissionForm', null, null],
            ['intake', 'intakeFormEditor', 'intakeFormModal', () => state.savingIntake],
            ['review', 'reviewForm', 'reviewModal', () => state.savingDecision || state.preparingEnrollment],
            ['enrollment', 'enrollmentForm', 'enrollmentModal', () => state.enrolling]
        ]) {
            guards[name] = window.EduOSFormGuard?.create(document.getElementById(id)) || null;
            if (modalId && guards[name])
                window.EduOSFormGuard.protectModal(document.getElementById(modalId), guards[name], isSaving);
        }
        const feature = document.querySelector('section[data-admission-feature]')?.dataset.admissionFeature || 'all';
        form.addEventListener('submit', submitApplication);
        form.addEventListener('input', () => clearAdmissionErrors(form));
        form.addEventListener('change', () => clearAdmissionErrors(form));
        document.getElementById('admissionFilters')?.addEventListener('submit', applyFilters);
        document.getElementById('admissionPageSize')?.addEventListener('change', changePageSize);
        document.getElementById('academicYearId')?.addEventListener('change', refreshTerms);
        document.getElementById('admissionFormReference')?.addEventListener('change', changeAdmissionForm);
        document.getElementById('applicationList')?.addEventListener('click', openReview);
        document.getElementById('applicantDocumentSearch')?.addEventListener('input', resetApplicantDocumentPage);
        document.getElementById('applicantDocumentPageSize')?.addEventListener('change', resetApplicantDocumentPage);
        document.getElementById('applicantDocumentPrevious')?.addEventListener('click', () => changeApplicantDocumentPage(-1));
        document.getElementById('applicantDocumentNext')?.addEventListener('click', () => changeApplicantDocumentPage(1));

        document.getElementById('intakeFormList')?.addEventListener('click', handleIntakeFormAction);
        document.getElementById('intakeFormSearch')?.addEventListener('input', resetIntakeCataloguePage);
        document.getElementById('intakeFormState')?.addEventListener('change', resetIntakeCataloguePage);
        document.getElementById('intakeFormSort')?.addEventListener('change', resetIntakeCataloguePage);
        document.getElementById('intakeFormPageSize')?.addEventListener('change', resetIntakeCataloguePage);
        document.getElementById('intakePreviousPage')?.addEventListener('click', () => changeIntakeCataloguePage(-1));
        document.getElementById('intakeNextPage')?.addEventListener('click', () => changeIntakeCataloguePage(1));
        document.getElementById('newIntakeFormButton')?.addEventListener('click', openNewIntakeForm);
        document.getElementById('intakeFormEditor')?.addEventListener('submit', saveIntakeForm);
        document.getElementById('intakeFormEditor')?.addEventListener('input', clearIntakeValidation);
        document.getElementById('intakeFormEditor')?.addEventListener('change', clearIntakeValidation);
        document.getElementById('intakeYearId')?.addEventListener('change', refreshIntakeTerms);
        document.getElementById('addIntakeFieldButton')?.addEventListener('click', () => addIntakeFieldRow());
        document.getElementById('addDocumentRequirementButton')?.addEventListener('click', () => addDocumentRequirementRow());
        document.getElementById('intakeFieldRows')?.addEventListener('click', removeBuilderRow);
        document.getElementById('documentRequirementRows')?.addEventListener('click', removeBuilderRow);
        document.getElementById('applicantDocumentList')?.addEventListener('click', reviewApplicantDocument);
        document.getElementById('applicantDocumentStatus')?.addEventListener('change', renderApplicantDocuments);
        document.getElementById('applicantDocumentVersions')?.addEventListener('change', renderApplicantDocuments);
        document.getElementById('reviewForm')?.addEventListener('submit', saveDecision);
        document.getElementById('admitApplicantButton')?.addEventListener('click', prepareEnrollment);
        document.getElementById('enrollmentForm')?.addEventListener('submit', completeEnrollment);
        initializeAdmissionOfferUi();
        document.getElementById('previousPage')?.addEventListener('click', () => changePage(-1));
        document.getElementById('nextPage')?.addEventListener('click', () => changePage(1));
        document.getElementById('applicationExportPage')?.addEventListener('click', exportApplicationPage);
        for (const id of ['applicationSearch', 'statusFilter']) {
            const element = document.getElementById(id);
            element?.addEventListener('input', () => { state.listPageRows = null; updatePagination(); });
            element?.addEventListener('change', () => { state.listPageRows = null; updatePagination(); });
        }
        await loadOptions();
        if (feature === 'all' || feature === 'forms') await loadIntakeForms();
        if (feature === 'all' || feature === 'applications') await loadApplications();
    }, { once: true });

    async function loadOptions() {
        if (!sameSession()) return;
        const request = ++state.optionsRequest;
        try {
            const response = await fetch('/api/admission-applications/options', apiOptions());
            const payload = await response.json().catch(() => null);
            if (request !== state.optionsRequest || !sameSession()) return;
            if (!response.ok || !payload?.success || !payload.data) throw new Error('Invalid options');
            // A user can start editing while the refresh is in flight. Only a
            // genuinely pristine editor may acquire a new clean baseline.
            const wasDirty = guards.application?.isDirty();
            const intakeWasDirty = guards.intake?.isDirty();
            // A publish/close refresh can occur while an applicant is typing.
            // Snapshot dependent selections before rebuilding the option controls.
            const previousSelections = {
                year: valueOf('academicYearId'),
                term: valueOf('academicTermId'),
                campus: valueOf('campusId'),
                level: valueOf('academicUnitId'),
                intakeYear: valueOf('intakeYearId'),
                intakeTerm: valueOf('intakeTermId'),
                intakeCampus: valueOf('intakeCampusId'),
                intakeLevel: valueOf('intakeUnitId')
            };
            state.options = payload.data;
            fillSelect('academicYearId', state.options.academicYears, true);
            fillSelect('campusId', state.options.campuses, true);
            fillSelect('academicUnitId', state.options.academicLevels, true);
            restoreAvailableOption('academicYearId', previousSelections.year);
            restoreAvailableOption('campusId', previousSelections.campus);
            restoreAvailableOption('academicUnitId', previousSelections.level);
            fillSelect('intakeYearId', state.options.academicYears, true);
            fillSelect('intakeCampusId', state.options.campuses, true);
            fillSelect('intakeUnitId', state.options.academicLevels, true);
            restoreAvailableOption('intakeYearId', previousSelections.intakeYear);
            restoreAvailableOption('intakeCampusId', previousSelections.intakeCampus);
            restoreAvailableOption('intakeUnitId', previousSelections.intakeLevel);
            // Refreshing published intakes must not silently discard a partially
            // completed application. Only retain a reference still authorized/open.
            const previousReference = valueOf('admissionFormReference');
            const previousCustom = snapshotAdmissionFields();
            fillSelect('admissionFormReference', (state.options.openForms || []).map(x => ({
                id: x.reference, name: x.title + ' (' + x.code + ')'
            })), true);
            if (previousReference && state.options.openForms?.some(x => x.reference === previousReference))
                setValue('admissionFormReference', previousReference);
            selectAdmissionForm(previousReference === valueOf('admissionFormReference') ? previousCustom : null);
            state.activeIntakeReference = valueOf('admissionFormReference');
            // A published intake owns its academic term. Do not overwrite that
            // server-provided term with a stale selection from another intake.
            if (!state.options.openForms?.some(x => x.reference === state.activeIntakeReference)) {
                refreshTerms();
                restoreAvailableOption('academicTermId', previousSelections.term);
            }
            refreshIntakeTerms(previousSelections.intakeTerm);
            if (!wasDirty) guards.application?.markClean();
            if (!intakeWasDirty) guards.intake?.markClean();
        } catch {
            if (request === state.optionsRequest && sameSession()) showAlert('danger', i18n.optionsFailed);
        }
    }

    function restoreAvailableOption(id, previousValue) {
        const select = document.getElementById(id);
        if (!select || !previousValue) return;
        // Never restore an option removed by current tenant/campus authorization.
        if ([...select.options].some(option => option.value === String(previousValue)))
            select.value = String(previousValue);
        else select.value = ''; // Never silently replace a removed authorized choice.
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
        if (selectedValue) restoreAvailableOption('intakeTermId', selectedValue);
    }

    function changeAdmissionForm() {
        if (!sameSession()) return;
        const next = valueOf('admissionFormReference');
        const previous = state.activeIntakeReference || '';
        if (next === previous) return;
        // The changed intake selector itself is not evidence of an edited draft.
        setValue('admissionFormReference', previous);
        if (guards.application?.isDirty() && !guards.application.confirmDiscard()) return;
        const form = document.getElementById('admissionForm');
        form.reset();
        setValue('admissionFormReference', next);
        selectAdmissionForm();
        state.activeIntakeReference = next;
        state.pendingFingerprint = null;
        state.pendingRequestId = createRequestId();
        guards.application?.markClean();
    }


    function selectAdmissionForm(previousCustom = null) {
        const form = state.options?.openForms?.find(x => x.reference === valueOf('admissionFormReference'));
        ['academicYearId', 'academicTermId', 'campusId', 'academicUnitId'].forEach(id => {
            const select = document.getElementById(id);
            if (select) select.disabled = Boolean(form);
        });
        if (form) {
            setValue('academicYearId', form.academicYearId);
            setValue('campusId', form.campusId);
            setValue('academicUnitId', form.academicLevelId);
            refreshTerms();
            setValue('academicTermId', form.academicTermId);
        }
        renderAdmissionFields(form?.fields || []);
        if (previousCustom) restoreAdmissionFields(previousCustom);
    }

    function snapshotAdmissionFields() {
        const values = new Map();
        document.querySelectorAll('[data-admission-field]').forEach(control => {
            values.set(control.dataset.admissionField, control.multiple
                ? [...control.selectedOptions].map(option => option.value) : control.value);
        });
        return values;
    }

    function restoreAdmissionFields(values) {
        document.querySelectorAll('[data-admission-field]').forEach(control => {
            if (!values.has(control.dataset.admissionField)) return;
            const previous = values.get(control.dataset.admissionField);
            if (control.multiple) {
                const selected = new Set(Array.isArray(previous) ? previous : []);
                [...control.options].forEach(option => { option.selected = selected.has(option.value); });
            } else if (control.tagName !== 'SELECT' || [...control.options].some(option => option.value === previous)) {
                control.value = previous;
            }
        });
    }

    function renderAdmissionFields(fields) {
        const container = document.getElementById('admissionCustomFields');
        if (!container) return;
        container.replaceChildren();
        for (const field of fields) {
            const type = Number(field.dataType);
            const wrapper = document.createElement('div');
            wrapper.className = 'col-md-6';
            const label = document.createElement('label');
            label.className = 'form-label';
            label.textContent = (field.label || field.fieldKey) + (field.isRequired ? ' *' : '');
            let control;
            if ([6, 7, 8].includes(type)) {
                control = document.createElement('select');
                if (type === 8) control.multiple = true;
                if (type !== 8) {
                    const empty = document.createElement('option');
                    empty.value = ''; empty.textContent = i18n.select || 'Select';
                    control.append(empty);
                }
                let choices = [];
                if (type === 6) choices = ['true', 'false'];
                else {
                    try { choices = JSON.parse(field.optionsJson || '[]'); } catch { choices = []; }
                    if (!Array.isArray(choices)) choices = [];
                }
                for (const choice of choices) {
                    const option = document.createElement('option');
                    option.value = String(choice);
                    option.textContent = String(choice);
                    control.append(option);
                }
            } else if (type === 9) {
                control = document.createElement('textarea');
                control.rows = 2;
            } else {
                control = document.createElement('input');
                control.type = ({ 2: 'number', 3: 'number', 4: 'date', 5: 'datetime-local' })[type] || 'text';
                if (type === 2) control.step = '1';
                if (type === 3) control.step = 'any';
            }
            control.className = control.tagName === 'SELECT' ? 'form-select' : 'form-control';
            control.required = Boolean(field.isRequired);
            control.dataset.admissionField = field.fieldKey;
            control.dataset.dataType = String(type);
            if (control.tagName !== 'SELECT') control.maxLength = 4000;
            label.append(control);
            wrapper.append(label);
            container.append(wrapper);
        }
    }

    function collectAdmissionResponses() {
        const values = {};
        for (const input of document.querySelectorAll('[data-admission-field]')) {
            let value = input.multiple
                ? (input.selectedOptions.length ? JSON.stringify([...input.selectedOptions].map(x => x.value)) : '')
                : input.value.trim();
            if (value && input.dataset.dataType === '5') {
                const parsed = new Date(value);
                value = Number.isFinite(parsed.valueOf()) ? parsed.toISOString() : '';
            }
            if (value || input.required) values[input.dataset.admissionField] = value || null;
        }
        return values;
    }

    async function loadIntakeForms() {
        if (!sameSession()) return;
        const list = document.getElementById('intakeFormList');
        if (!list) return;
        const request = ++state.intakeRequest;
        list.setAttribute('aria-busy', 'true');
        list.replaceChildren(statusMessage(i18n.loading));
        try {
            const response = await fetch('/api/admission-intake/forms', apiOptions());
            const payload = await response.json().catch(() => null);
            if (request !== state.intakeRequest || !sameSession()) return;
            if (!response.ok || !payload?.success || !Array.isArray(payload.data)) throw new Error(payload?.message || 'Admission forms could not be loaded.');
            state.intakeForms = payload.data;
            renderIntakeForms();
        } catch (error) {
            if (request === state.intakeRequest && sameSession())
                list.replaceChildren(statusMessage(error.message || 'Admission forms could not be loaded.'));
        } finally {
            if (request === state.intakeRequest) list.removeAttribute('aria-busy');
        }
    }

    function intakeText(english, bengali) { return bangla ? bengali : english; }

    // Catalogue pagination is presentation-only: it never changes tenant-authorized API data.
    const intakePaging = { page: 1, size: 10 };

    function changeIntakeCataloguePage(delta) {
        intakePaging.page = Math.max(1, intakePaging.page + delta);
        renderIntakeForms();
        document.getElementById('intakeFormCount')?.focus();
    }

    function resetIntakeCataloguePage() {
        intakePaging.page = 1;
        renderIntakeForms();
    }

    function renderIntakeForms() {
        if (!sameSession()) return;
        const list = document.getElementById('intakeFormList');
        if (!list) return;
        const query = (document.getElementById('intakeFormSearch')?.value || '').trim().toLocaleLowerCase();
        const stateFilter = document.getElementById('intakeFormState')?.value || '';
        const visible = state.intakeForms.filter(item => {
            const matchesText = !query || [item.code, item.title].some(value => String(value || '').toLocaleLowerCase().includes(query));
            return matchesText && (!stateFilter || String(item.state) === stateFilter);
        });
        const sort = document.getElementById('intakeFormSort')?.value || 'recent';
        const byTitle = (a, b) => String(a.title || '').localeCompare(String(b.title || ''), bangla ? 'bn-BD' : 'en-BD');
        visible.sort((a, b) => {
            if (sort === 'title') return byTitle(a, b) || Number(a.id) - Number(b.id);
            if (sort === 'opening') return String(a.opensAt || '').localeCompare(String(b.opensAt || '')) || byTitle(a, b);
            return Number(b.id) - Number(a.id);
        });
        const pageSize = Number(document.getElementById('intakeFormPageSize')?.value || 10);
        intakePaging.size = [10, 20, 50].includes(pageSize) ? pageSize : 10;
        const totalPages = Math.max(1, Math.ceil(visible.length / intakePaging.size));
        intakePaging.page = Math.min(Math.max(1, intakePaging.page), totalPages);
        const offset = (intakePaging.page - 1) * intakePaging.size;
        const pageItems = visible.slice(offset, offset + intakePaging.size);
        const count = document.getElementById('intakeFormCount');
        if (count) {
            count.tabIndex = -1;
            count.textContent = intakeText(
                `Showing ${pageItems.length ? offset + 1 : 0}–${Math.min(offset + pageItems.length, visible.length)} of ${visible.length} matching forms (${state.intakeForms.length} total)`,
                `মোট ${state.intakeForms.length}টি ফরমের মধ্যে ${visible.length}টি মিলেছে; দেখানো হচ্ছে ${pageItems.length ? offset + 1 : 0}–${Math.min(offset + pageItems.length, visible.length)}`
            );
        }
        const pageStatus = document.getElementById('intakePageStatus');
        if (pageStatus) pageStatus.textContent = intakeText(
            `Page ${intakePaging.page} of ${totalPages}`,
            `পৃষ্ঠা ${intakePaging.page} / ${totalPages}`
        );
        const previous = document.getElementById('intakePreviousPage');
        const next = document.getElementById('intakeNextPage');
        if (previous) previous.disabled = intakePaging.page <= 1 || !visible.length;
        if (next) next.disabled = intakePaging.page >= totalPages || !visible.length;
        if (!visible.length) {
            list.replaceChildren(statusMessage(state.intakeForms.length
                ? intakeText('No forms match these filters.', 'এই অনুসন্ধানে কোনো ফরম পাওয়া যায়নি।')
                : intakeText('No admission forms yet. Create a draft to begin.', 'এখনো কোনো ভর্তি ফরম নেই। শুরু করতে নতুন খসড়া তৈরি করুন।')));
            return;
        }
        list.replaceChildren(...pageItems.map(item => {
            const card = document.createElement('article');
            card.className = 'intake-form-card';
            const summary = document.createElement('div');
            const title = document.createElement('h4');
            title.textContent = item.title || '';
            const detail = document.createElement('p');
            detail.textContent = `${item.code || ''} · ${formatDate(item.opensAt)} — ${formatDate(item.closesAt)} · ${item.currencyCode || ''} ${Number(item.applicationFee || 0).toFixed(2)}`;
            const counts = document.createElement('p');
            counts.textContent = intakeText(
                `${item.fields?.length || 0} custom fields · document policies managed separately`,
                `${item.fields?.length || 0}টি অতিরিক্ত তথ্যের ঘর · নথির নিয়ম আলাদাভাবে পরিচালিত`
            );
            summary.append(title, detail, counts);
            const controls = document.createElement('div');
            controls.className = 'intake-form-actions';
            const badge = document.createElement('span');
            badge.className = `admission-status status-${Number(item.state) === 2 ? 4 : Number(item.state) === 3 ? 6 : 1}`;
            badge.textContent = ({
                1: intakeText('Draft', 'খসড়া'),
                2: intakeText('Published', 'প্রকাশিত'),
                3: intakeText('Closed', 'বন্ধ'),
                4: intakeText('Archived', 'সংরক্ষিত')
            })[Number(item.state)] || intakeText('Unknown', 'অজানা');
            controls.append(badge);
            if (Number(item.state) === 1) {
                controls.append(actionButton(intakeText('Edit', 'সম্পাদনা'), 'edit', item.id, 'btn-outline-secondary'),
                    actionButton(intakeText('Publish', 'প্রকাশ করুন'), 'publish', item.id, 'btn-success'));
            } else if (Number(item.state) === 2) {
                controls.append(actionButton(intakeText('Close', 'বন্ধ করুন'), 'close', item.id, 'btn-outline-danger'));
            }
            if (list.dataset.mutatingIntake)
                controls.querySelectorAll('button[data-intake-action]').forEach(button => { button.disabled = true; });
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
        if (!sameSession()) return;
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
        const controls = document.getElementById('intakeFormList');
        if (controls?.dataset.mutatingIntake) return;
        if (controls) {
            controls.dataset.mutatingIntake = String(item.id);
            controls.querySelectorAll('button[data-intake-action]').forEach(control => { control.disabled = true; });
            controls.setAttribute('aria-busy', 'true');
        }
        try {
            const response = await fetch(`/api/admission-intake/forms/${encodeURIComponent(item.id)}/${action}`, apiOptions('POST', { rowVersion: item.rowVersion }));
            const payload = await response.json().catch(() => null);
            if (!sameSession()) return;
            if (!response.ok || !payload?.success) {
                if (response.status === 409 || response.status === 412) {
                    await loadIntakeForms();
                    throw new Error(intakeText('This form was changed elsewhere. The latest version has been loaded; review it before retrying.',
                        'ফরমটি অন্য জায়গায় পরিবর্তিত হয়েছে। সর্বশেষ সংস্করণ আনা হয়েছে; আবার চেষ্টা করার আগে যাচাই করুন।'));
                }
                throw new Error(payload?.message || intakeText('Unable to change the form status.', 'ফরমের অবস্থা পরিবর্তন করা যায়নি।'));
            }
            showAlert('success', payload.message || intakeText('Admission form updated.', 'ভর্তি ফরম হালনাগাদ হয়েছে।'));
            await loadIntakeForms();
            // Refresh the published-form selector only after the authoritative mutation succeeds.
            await loadOptions();
        } catch (error) {
            if (sameSession()) showAlert('danger', error.message || intakeText('Unable to change the form status.', 'ফরমের অবস্থা পরিবর্তন করা যায়নি।'));
        } finally {
            if (controls) {
                delete controls.dataset.mutatingIntake;
                controls.querySelectorAll('button[data-intake-action]').forEach(control => { control.disabled = false; });
                controls.removeAttribute('aria-busy');
            }
        }
    }

    function openNewIntakeForm() {
        openIntakeFormEditor(null);
    }

    function openIntakeFormEditor(item) {
        if (!sameSession()) return;
        const editor = document.getElementById('intakeFormEditor');
        if (!editor || !state.options) return;
        if (state.savingIntake || (guards.intake?.isDirty() && !guards.intake.confirmDiscard())) return;
        editor.reset();
        state.intakeFingerprint = null;
        clearIntakeValidation();
        const modalTitle = document.getElementById('intakeFormModalTitle');
        if (modalTitle) modalTitle.textContent = item
            ? intakeText('Edit admission form', 'ভর্তি ফরম সম্পাদনা')
            : intakeText('New admission form', 'নতুন ভর্তি ফরম');
        setValue('intakeFormId', item?.id);
        setValue('intakeFormRowVersion', item?.rowVersion);
        setValue('intakeFormRequestId', item ? '' : createRequestId());
        setValue('intakeCode', item?.code);
        setValue('intakeTitle', item?.title);
        fillSelect('intakeYearId', state.options.academicYears, true);
        fillSelect('intakeCampusId', state.options.campuses, true);
        fillSelect('intakeUnitId', state.options.academicLevels, true);
        if (item) {
            setValue('intakeYearId', item.academicYearId);
            setValue('intakeCampusId', item.campusId);
            setValue('intakeUnitId', item.academicLevelId);
        }
        refreshIntakeTerms(item?.academicTermId);
        const now = new Date();
        const closes = new Date(now.valueOf() + 30 * 24 * 60 * 60 * 1000);
        setValue('intakeOpensAt', toLocalDateTime(item?.opensAt || now));
        setValue('intakeClosesAt', toLocalDateTime(item?.closesAt || closes));
        setValue('intakeFee', item?.applicationFee ?? 0);
        setValue('intakeCurrency', item?.currencyCode || 'BDT');
        const fields = document.getElementById('intakeFieldRows');
        const requirements = document.getElementById('documentRequirementRows');
        fields?.replaceChildren();
        requirements?.replaceChildren();
        (item?.fields || []).forEach(addIntakeFieldRow);
        (item?.documentRequirements || []).forEach(addDocumentRequirementRow);
        guards.intake?.markClean();
        bootstrap.Modal.getOrCreateInstance(document.getElementById('intakeFormModal')).show();
    }

    function addIntakeFieldRow(field = null) {
        const container = document.getElementById('intakeFieldRows');
        if (!container) return;
        if (container.querySelectorAll('.intake-field-row').length >= 50) {
            showIntakeValidation([{ message: intakeText('A form can have at most 50 custom questions.',
                'একটি ফরমে সর্বোচ্চ ৫০টি অতিরিক্ত প্রশ্ন রাখা যাবে।') }]);
            return;
        }
        const row = document.createElement('div');
        row.className = 'intake-builder-row intake-field-row';
        row.dataset.fieldId = String(field?.id || '');
        row.dataset.rowVersion = field?.rowVersion || '';
        const key = builderInput('Key', 'intake-field-key', 'text', field?.fieldKey || '');
        key.input.required = true;
        key.input.pattern = '[A-Za-z][A-Za-z0-9_]*';
        const label = builderInput('Label', 'intake-field-label', 'text', field?.label || '');
        label.input.required = true;
        const type = builderSelect('Type', 'intake-field-type', [
            [1, 'Text'], [2, 'Number'], [3, 'Decimal'], [4, 'Date'], [5, 'Date & time'],
            [6, 'Yes / No'], [7, 'Choice'], [8, 'Multiple choice'], [9, 'JSON']
        ], field?.dataType || 1);
        const validation = parseBuilderJson(field?.validationJson, {});
        const maximum = builderInput('Max length (metadata)', 'intake-field-max', 'number', validation.maxLength || '');
        maximum.input.min = '1'; maximum.input.max = '4000';
        const options = builderInput('Choices (comma separated)', 'intake-field-options', 'text',
            parseBuilderJson(field?.optionsJson, []).join(', '));
        const required = builderCheckbox('Required', 'intake-field-required', Boolean(field?.isRequired));
        row.append(key.wrapper, label.wrapper, type.wrapper, maximum.wrapper, options.wrapper, required.wrapper, removeBuilderButton());
        container.append(row);
    }

    function parseBuilderJson(value, fallback) {
        if (!value) return fallback;
        try {
            const parsed = JSON.parse(value);
            return Array.isArray(fallback) ? (Array.isArray(parsed) ? parsed : fallback)
                : (parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : fallback);
        } catch { return fallback; }
    }

    function addDocumentRequirementRow(requirement = null) {
        const container = document.getElementById('documentRequirementRows');
        if (!container) return;
        if (container.querySelectorAll('.intake-document-row').length >= 20) {
            showIntakeValidation([{ message: intakeText('A form can have at most 20 document requirements.',
                'একটি ফরমে সর্বোচ্চ ২০ ধরনের প্রয়োজনীয় নথি রাখা যাবে।') }]);
            return;
        }
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
        if (state.savingIntake || !sameSession()) return;
        const editor = event.currentTarget;
        if (!editor.checkValidity()) {
            const invalid = [...editor.querySelectorAll('input,select,textarea')].filter(x => x.willValidate && !x.checkValidity());
            showIntakeValidation(invalid.map(x => ({
                row: x,
                message: editor.querySelector('label[for="' + x.id + '"]')?.textContent?.trim()
                    || x.closest('label')?.textContent?.trim() || x.id || intakeText('Invalid field', 'ভুল তথ্য')
            })));
            return;
        }
        const id = positiveInteger(valueOf('intakeFormId'));
        const fields = [...document.querySelectorAll('.intake-field-row')].map((row, index) => {
            const type = positiveInteger(row.querySelector('.intake-field-type')?.value) || 1;
            const choices = row.querySelector('.intake-field-options')?.value.split(',').map(x => x.trim()).filter(Boolean) || [];
            const maximum = positiveInteger(row.querySelector('.intake-field-max')?.value);
            return {
                id: positiveInteger(row.dataset.fieldId) || 0,
                rowVersion: row.dataset.rowVersion || '',
                fieldKey: row.querySelector('.intake-field-key')?.value.trim(),
                label: row.querySelector('.intake-field-label')?.value.trim(),
                dataType: type,
                isRequired: Boolean(row.querySelector('.intake-field-required')?.checked),
                displayOrder: index,
                optionsJson: [7, 8].includes(type) ? JSON.stringify(choices) : null,
                validationJson: maximum ? JSON.stringify({ maxLength: maximum }) : null,
                isActive: true
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
        // Validate the complete intake definition before any write; keep the editor open
        // so staff can correct the form without losing configured fields/documents.
        const invalid = [];
        const fieldKeys = new Set();
        for (const [index, field] of fields.entries()) {
            const key = String(field.fieldKey || '').toLowerCase();
            if (fieldKeys.has(key)) invalid.push({ row: document.querySelectorAll('.intake-field-row')[index], message: bangla ? 'একই ফিল্ড কী একাধিকবার ব্যবহার করা যাবে না।' : 'Field keys must be unique.' });
            fieldKeys.add(key);
            if ([7, 8].includes(field.dataType) && !JSON.parse(field.optionsJson || '[]').length)
                invalid.push({ row: document.querySelectorAll('.intake-field-row')[index], message: bangla ? 'চয়েস ফিল্ডে অন্তত একটি অপশন দিন।' : 'Add at least one choice.' });
        }
        const documentTypes = new Set();
        for (const [index, requirement] of documentRequirements.entries()) {
            const key = String(requirement.documentType || '').toLowerCase();
            if (documentTypes.has(key)) invalid.push({ row: document.querySelectorAll('.intake-document-row')[index], message: bangla ? 'একই নথির ধরন একাধিকবার দেওয়া যাবে না।' : 'Document types must be unique.' });
            documentTypes.add(key);
            if (!requirement.allowedExtensions.length)
                invalid.push({ row: document.querySelectorAll('.intake-document-row')[index], message: bangla ? 'অনুমোদিত ফাইল এক্সটেনশন দিন।' : 'Specify allowed file extensions.' });
        }
        const opens = new Date(valueOf('intakeOpensAt'));
        const closes = new Date(valueOf('intakeClosesAt'));
        if (!Number.isFinite(opens.valueOf()) || !Number.isFinite(closes.valueOf()) || closes <= opens)
            invalid.unshift({ row: document.getElementById('intakeClosesAt'), message: bangla ? 'শেষের সময় শুরুর সময়ের পরে হতে হবে।' : 'Closing time must be after opening time.' });
        if (invalid.length) {
            showIntakeValidation(invalid);
            return;
        }
        const request = {
            code: valueOf('intakeCode'), title: valueOf('intakeTitle'), description: null,
            academicYearId: positiveInteger(valueOf('intakeYearId')), academicTermId: positiveInteger(valueOf('intakeTermId')),
            campusId: positiveInteger(valueOf('intakeCampusId')), academicLevelId: positiveInteger(valueOf('intakeUnitId')),
            opensAtUtc: opens.toISOString(), closesAtUtc: closes.toISOString(),
            applicationFee: Number(valueOf('intakeFee') || 0), currency: valueOf('intakeCurrency'), fields, documentRequirements
        };
        if (id) request.rowVersion = valueOf('intakeFormRowVersion');
        else {
            const fingerprint = JSON.stringify(request);
            if (state.intakeFingerprint !== null && state.intakeFingerprint !== fingerprint)
                setValue('intakeFormRequestId', createRequestId());
            state.intakeFingerprint = fingerprint;
            request.clientRequestId = valueOf('intakeFormRequestId');
        }
        const button = document.getElementById('saveIntakeFormButton');
        state.savingIntake = true;
        editor.inert = true;
        setLoading(button, true);
        try {
            const response = await fetch(id ? `/api/admission-intake/forms/${encodeURIComponent(id)}` : '/api/admission-intake/forms', apiOptions(id ? 'PUT' : 'POST', request));
            const payload = await response.json().catch(() => null);
            if (!sameSession()) return;
            if (response.status === 409 || response.status === 412) {
                // Duplicate code and stale edits are different conflicts; never
                // discard a draft or mislabel a unique-code violation as a race.
                editor.inert = false;
                showIntakeValidation(intakeServerErrors(response, payload));
                return;
            }
            if (!response.ok || !payload?.success) {
                editor.inert = false;
                showIntakeValidation(intakeServerErrors(response, payload));
                return;
            }
            state.intakeFingerprint = null;
            guards.intake?.markClean();
            state.savingIntake = false;
            bootstrap.Modal.getInstance(document.getElementById('intakeFormModal'))?.hide();
            showAlert('success', payload.message || 'Admission form saved.');
            await loadIntakeForms();
        } catch (error) {
            if (sameSession()) {
                editor.inert = false;
                showIntakeValidation([{
                    message: error?.message?.startsWith('Security token') || error?.message?.startsWith('Session changed')
                        ? error.message
                        : intakeText('The request could not be confirmed. Your entries are intact. Check the form list for this code before retrying.',
                            'অনুরোধটি নিশ্চিত করা যায়নি। আপনার তথ্য অক্ষত আছে। আবার চেষ্টা করার আগে এই কোডটি ফরমের তালিকায় আছে কি না দেখুন।')
                }]);
            }
        } finally {
            state.savingIntake = false;
            editor.inert = false;
            setLoading(button, false);
        }
    }

    function intakeServerErrors(response, payload) {
        const status = Number(response?.status || 0);
        const message = typeof payload?.message === 'string' ? payload.message : '';
        const fieldMap = {
            Code: 'intakeCode', Title: 'intakeTitle', AcademicYearId: 'intakeYearId',
            AcademicTermId: 'intakeTermId', CampusId: 'intakeCampusId',
            AcademicLevelId: 'intakeUnitId', OpensAtUtc: 'intakeOpensAt',
            ClosesAtUtc: 'intakeClosesAt', ApplicationFee: 'intakeFee',
            Currency: 'intakeCurrency'
        };
        const errors = [];
        // ASP.NET ApiController returns ValidationProblemDetails on model-binding
        // failure; display each field error instead of a generic failed POST.
        if (payload?.errors && typeof payload.errors === 'object') {
            for (const [key, messages] of Object.entries(payload.errors).slice(0, 30)) {
                const fieldName = key.split('.').pop();
                const row = document.getElementById(fieldMap[fieldName]) || null;
                for (const item of (Array.isArray(messages) ? messages : [messages]).slice(0, 3)) {
                    if (typeof item === 'string' && item.trim())
                        errors.push({ row, message: item.slice(0, 300) });
                }
            }
        }
        if (errors.length) return errors;
        if (status === 409 || status === 412) {
            if (/\bcode\b/i.test(message) && /exist|duplicate|unique/i.test(message))
                return [{ row: document.getElementById('intakeCode'),
                    message: intakeText('This form code already exists. Choose a different code.',
                        'এই ফরম কোডটি আগে থেকেই আছে। অন্য কোড দিন।') }];
            if (/year|campus|term|academic level|reference/i.test(message))
                return [{ row: document.getElementById('intakeYearId'),
                    message: intakeText('An academic selection is no longer valid. Review year, term, campus and class before saving.',
                        'একাডেমিক কোনো নির্বাচন এখন আর বৈধ নয়। বছর, টার্ম, ক্যাম্পাস ও ক্লাস যাচাই করুন।') }];
            if (/document type/i.test(message))
                return [{ message: intakeText('A document type is not configured. Choose an active type in Document Administration.',
                    'একটি নথির ধরন সেটআপ করা নেই। Document Administration-এ সক্রিয় ধরন নির্বাচন করুন।') }];
            return [{ message: intakeText('The form has changed since you opened it. Copy any edits you need, reload the latest form, and review before saving.',
                'ফরমটি অন্য জায়গায় পরিবর্তিত হয়েছে। প্রয়োজনীয় পরিবর্তন রেখে সর্বশেষ তথ্য রিলোড করে যাচাই করুন।') }];
        }
        if (status === 401 || status === 403)
            return [{ message: intakeText('Your session or permission does not allow this save. Sign in again or contact your administrator.',
                'সেশন বা অনুমতি দিয়ে এই ফরম সংরক্ষণ করা যাচ্ছে না। আবার লগইন করুন অথবা প্রশাসকের সঙ্গে যোগাযোগ করুন।') }];
        if (status === 429)
            return [{ message: intakeText('Too many requests. Leave this draft open and try again after a short interval.',
                'অনেকবার অনুরোধ হয়েছে। তথ্য অপরিবর্তিত রেখে কিছুক্ষণ পর আবার চেষ্টা করুন।') }];
        if (status >= 500) {
            // Never echo an unexpected stack trace or database exception from an
            // upstream error body. Only the whitelisted support-reference token
            // is safe to share with the institution administrator.
            const reference = /Support reference:\s*([a-f0-9]{12})\b/i.exec(message)?.[1];
            return [{ message: intakeText('Server error (HTTP ' + status + '). Save was not confirmed; check the list for this form code before retrying. ',
                'সার্ভার সমস্যা (HTTP ' + status + ')। সংরক্ষণ নিশ্চিত হয়নি; আবার চেষ্টা করার আগে ফরম তালিকায় কোডটি দেখুন। ') +
                (reference
                    ? intakeText('Support reference: ', 'সাপোর্ট রেফারেন্স: ') + reference
                    : intakeText('Ask an administrator to review the server logs.',
                        'সার্ভার লগ পরীক্ষা করতে প্রশাসককে জানান।')) }];
        }
        return [{ message: message || intakeText('The form could not be saved. Review your entries and try again.',
            'ফরম সংরক্ষণ করা যায়নি। তথ্য যাচাই করে আবার চেষ্টা করুন।') }];
    }

    function clearIntakeValidation() {
        document.getElementById('intakeValidationSummary')?.remove();
        document.getElementById('intakeFormEditor')?.querySelectorAll('[aria-invalid="true"]')
            .forEach(field => field.removeAttribute('aria-invalid'));
    }

    function showIntakeValidation(entries) {
        const editor = document.getElementById('intakeFormEditor');
        if (!editor || !entries.length) return;
        clearIntakeValidation();
        const summary = document.createElement('div');
        summary.id = 'intakeValidationSummary';
        summary.className = 'alert alert-danger';
        summary.setAttribute('role', 'alert');
        summary.tabIndex = -1;
        const title = document.createElement('p');
        title.className = 'fw-semibold mb-2';
        title.textContent = bangla ? 'ফরম সংরক্ষণের আগে সংশোধন করুন:' : 'Correct these items before saving:';
        const list = document.createElement('ul');
        list.className = 'mb-0';
        for (const { row, message } of entries) {
            const field = row?.matches?.('input,select,textarea') ? row
                : row?.querySelector?.('input,select,textarea');
            const item = document.createElement('li');
            if (field) {
                const link = document.createElement('button');
                link.type = 'button';
                link.className = 'btn btn-link p-0 text-start';
                link.textContent = message;
                link.addEventListener('click', () => field?.focus());
                field.setAttribute('aria-invalid', 'true');
                item.append(link);
            } else item.textContent = message;
            list.append(item);
        }
        summary.append(title, list);
        const body = editor.querySelector('.modal-body');
        body?.prepend(summary);
        if (body) body.scrollTop = 0;
        summary.focus();
    }

    // Accessible, in-form error summary. Keep existing API and browser validation authoritative.
    function admissionErrors(form, extra = []) {
        const invalid = [...form.querySelectorAll('input, select, textarea')]
            .filter(field => field.willValidate && !field.checkValidity());
        const entries = invalid.map(field => ({
            field,
            label: form.querySelector('label[for="' + field.id + '"]')?.textContent?.trim()
                || field.getAttribute('aria-label') || field.name || field.id
                || (bangla ? 'তথ্য' : 'Field')
        }));
        extra.forEach(field => {
            if (field && !entries.some(item => item.field === field))
                entries.push({ field, label: form.querySelector('label[for="' + field.id + '"]')?.textContent?.trim() || field.id });
        });
        let summary = document.getElementById('admissionValidationSummary');
        if (!summary) {
            summary = document.createElement('div');
            summary.id = 'admissionValidationSummary';
            summary.className = 'alert alert-danger col-12';
            summary.tabIndex = -1;
            summary.setAttribute('role', 'alert');
            form.prepend(summary);
        }
        const heading = document.createElement('p');
        heading.className = 'fw-semibold mb-2';
        heading.textContent = bangla ? 'নিচের তথ্যগুলো সংশোধন করুন:' : 'Please correct these fields:';
        const list = document.createElement('ul');
        list.className = 'mb-0';
        entries.forEach(({ field, label }) => {
            const item = document.createElement('li');
            const link = document.createElement('button');
            link.type = 'button';
            link.className = 'btn btn-link p-0 text-start';
            link.textContent = label;
            link.addEventListener('click', () => field.focus());
            item.append(link);
            list.append(item);
            field.setAttribute('aria-invalid', 'true');
        });
        summary.replaceChildren(heading, list);
        summary.focus();
    }

    function clearAdmissionErrors(form) {
        document.getElementById('admissionValidationSummary')?.remove();
        form.querySelectorAll('[aria-invalid="true"]').forEach(field => field.removeAttribute('aria-invalid'));
    }

    async function submitApplication(event) {
        event.preventDefault();
        const form = event.currentTarget;
        if (state.submitting || !sameSession()) return;
        // Required multi-selects need an explicit check: native constraint
        // validation does not consistently enforce required on multiple selects.
        const missingChoices = [...form.querySelectorAll('select[multiple][required]')]
            .filter(control => control.selectedOptions.length === 0);
        if (!form.checkValidity() || missingChoices.length) {
            admissionErrors(form, missingChoices);
            return;
        }
        clearAdmissionErrors(form);
        const selectedForm = state.options?.openForms?.find(x => x.reference === valueOf('admissionFormReference'));
        if (!selectedForm) {
            showAlert('danger', bangla ? 'আবেদন জমা দেওয়ার আগে চালু ভর্তি ফরম নির্বাচন করুন।' : 'Select an open admission intake before submitting.');
            admissionErrors(form, [document.getElementById('admissionFormReference')]);
            return;
        }
        const dateOfBirth = valueOf('dateOfBirth');
        if (isMinor(dateOfBirth) && (!valueOf('guardianName') || !valueOf('guardianRelation') || !valueOf('guardianMobile'))) {
            showAlert('danger', i18n.minorGuardianRequired);
            admissionErrors(form, ['guardianName', 'guardianRelation', 'guardianMobile'].map(id => document.getElementById(id)).filter(field => field && !field.value.trim()));
            return;
        }

        const request = {
            clientRequestId: state.pendingRequestId,
            admissionFormReference: selectedForm.reference,
            customResponses: collectAdmissionResponses(),
            academicYearId: positiveInteger(valueOf('academicYearId')),
            academicTermId: positiveInteger(valueOf('academicTermId')),
            campusId: positiveInteger(valueOf('campusId')),
            academicLevelId: positiveInteger(valueOf('academicUnitId')),
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
            permanentAddress: null,
            previousInstitution: null,
            preferredLanguage: valueOf('preferredLanguage') || 'bn-BD'
        };
        const fingerprint = JSON.stringify({ ...request, clientRequestId: null });
        if (state.pendingFingerprint !== null && state.pendingFingerprint !== fingerprint)
            state.pendingRequestId = createRequestId();
        state.pendingFingerprint = fingerprint;
        request.clientRequestId = state.pendingRequestId;
        state.submitting = true;
        const button = document.getElementById('submitApplicationButton');
        form.inert = true;
        setLoading(button, true);
        try {
            const response = await fetch('/api/admission-applications', apiOptions('POST', request));
            const payload = await response.json().catch(() => null);
            if (!sameSession()) return;
            if (!response.ok || !payload?.success) {
                showAlert('danger', i18n.createFailed);
                // Preserve every entered field and the idempotency key for a safe retry.
                document.getElementById('submitApplicationButton')?.focus();
                return;
            }
            // Display a privacy-minimal receipt before clearing the form. A network
            // retry retains the original idempotency key until success is confirmed.
            showApplicationReceipt({
                number: payload.data?.applicationNumber,
                name: request.applicantName,
                intake: selectedForm.title,
                submittedAt: new Date()
            });
            form.reset();
            state.pendingRequestId = createRequestId();
            state.pendingFingerprint = null;
            fillSelect('academicYearId', state.options?.academicYears, true);
            fillSelect('campusId', state.options?.campuses, true);
            fillSelect('academicUnitId', state.options?.academicLevels, true);
            fillSelect('admissionFormReference', (state.options?.openForms || []).map(x => ({
                id: x.reference, name: x.title + ' (' + x.code + ')'
            })), true);
            selectAdmissionForm();
            refreshTerms();
            state.activeIntakeReference = valueOf('admissionFormReference');
            guards.application?.markClean();
            showAlert('success', `${i18n.created} ${payload.data?.applicationNumber || ''}`.trim());
            state.page = 1;
            await loadApplications();
        } catch {
            if (sameSession()) {
                showAlert('danger', i18n.networkError);
                document.getElementById('submitApplicationButton')?.focus();
            }
        } finally {
            state.submitting = false;
            form.inert = false;
            setLoading(button, false);
        }
    }

    function showApplicationReceipt(receipt) {
        const host = document.getElementById('admissionSubmissionReceipt');
        if (!host) return;
        const number = String(receipt.number || '').trim();
        host.replaceChildren();
        const title = document.createElement('h4');
        title.className = 'h5';
        title.textContent = bangla ? 'আবেদন গ্রহণ করা হয়েছে' : 'Application submitted';
        const details = document.createElement('dl');
        details.className = 'row mb-2';
        const entries = [
            [bangla ? 'আবেদন নম্বর' : 'Application number', number || (bangla ? 'নম্বর পাওয়া যায়নি' : 'Number unavailable')],
            [bangla ? 'আবেদনকারী' : 'Applicant', receipt.name || ''],
            [bangla ? 'ভর্তি ফরম' : 'Intake', receipt.intake || ''],
            [bangla ? 'সময়' : 'Submitted', formatDate(receipt.submittedAt)]
        ];
        for (const [label, value] of entries) {
            const term = document.createElement('dt');
            term.className = 'col-sm-4';
            term.textContent = label;
            const description = document.createElement('dd');
            description.className = 'col-sm-8';
            description.textContent = value;
            details.append(term, description);
        }
        const print = document.createElement('button');
        print.type = 'button';
        print.className = 'btn btn-sm btn-outline-primary';
        print.textContent = bangla ? 'রসিদ প্রিন্ট করুন' : 'Print receipt';
        print.addEventListener('click', () => window.print());
        host.append(title, details, print);
        host.hidden = false;
        host.focus();
    }

    async function loadApplications() {
        if (!sameSession()) return;
        const list = document.getElementById('applicationList');
        if (!list) return;
        const requestNumber = ++state.listRequest;
        state.listPageRows = null;
        updatePagination(true);
        list.setAttribute('aria-busy', 'true');
        list.replaceChildren(statusMessage(i18n.loading));
        const params = new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize) });
        const search = valueOf('applicationSearch');
        const status = positiveInteger(valueOf('statusFilter'));
        if (search) params.set('search', search);
        if (status) params.set('state', String(status));
        try {
            const response = await fetch(`/api/admission-applications?${params}`, apiOptions());
            const payload = await response.json().catch(() => null);
            if (requestNumber !== state.listRequest || !sameSession()) return;
            if (!response.ok || !payload?.success || !Array.isArray(payload.data?.items)) throw new Error('Invalid list');
            state.totalPages = Math.max(1, Number(payload.data.totalPages) || 1);
            state.totalItems = Math.max(0, Number(payload.data.totalCount ?? payload.data.totalItems ?? payload.data.total) || 0);
            if (state.page > state.totalPages) {
                state.page = state.totalPages;
                void loadApplications();
                return;
            }
            renderApplications(payload.data.items);
            // Export only the exact authorized page returned for unchanged filters.
            if (search === valueOf('applicationSearch') && status === positiveInteger(valueOf('statusFilter')))
                state.listPageRows = payload.data.items;
            updatePagination();
        } catch {
            if (requestNumber !== state.listRequest || !sameSession()) return;
            const error = statusMessage(i18n.loadFailed);
            const retry = document.createElement('button');
            retry.type = 'button';
            retry.className = 'btn btn-sm btn-outline-primary';
            retry.textContent = i18n.retry || '';
            retry.addEventListener('click', loadApplications, { once: true });
            error.append(retry);
            list.replaceChildren(error);
            updatePagination();
        } finally {
            if (requestNumber === state.listRequest) list.removeAttribute('aria-busy');
        }
    }

    function exportApplicationPage() {
        if (!sameSession()) return;
        const items = state.listPageRows;
        if (!Array.isArray(items) || !items.length) return;
        const rows = items.map(item => [item.applicationNumber, item.applicantName,
            item.academicLevelName, item.campusName, item.maskedMobile,
            formatDate(item.submittedAtUtc), statusName(item.state)]);
        if (!window.EduOSPageCsv) return showAlert('danger', 'Export is unavailable. Reload this page.');
        window.EduOSPageCsv.download(`admissions-page-${state.page}.csv`,
            ['Application no', 'Applicant', 'Academic level', 'Campus', 'Masked mobile', 'Submitted', 'Status'], rows);
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
            badge.className = `admission-status status-${Number(item.state)}`;
            badge.textContent = statusName(item.state);
            heading.append(title, badge);
            const number = detailLine(i18n.applicationNo, item.applicationNumber);
            const unit = detailLine(i18n.academicUnit, [item.academicLevelName, item.campusName].filter(Boolean).join(' · '));
            const contact = detailLine(i18n.fullContact, item.maskedMobile);
            const submitted = detailLine(i18n.submittedAt, formatDate(item.submittedAtUtc));
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'btn btn-sm btn-outline-primary';
            button.dataset.reference = item.reference;
            button.textContent = i18n.review || '';
            card.append(heading, number, unit, contact, submitted, button);
            if (Number(item.state) === 6 && list.dataset.canOffer === 'true') {
                const link = document.createElement('a');
                link.className = 'btn btn-sm btn-outline-success';
                link.textContent = bangla ? 'অফার ও ভর্তি সম্পন্ন' : 'Offer & enroll';
                link.href = '/Admissions/Offers?reference=' + encodeURIComponent(item.reference);
                card.append(link);
            }
            return card;
        }));
    }

    async function openReview(event) {
        if (!sameSession()) return;
        const button = event.target.closest('button[data-reference]');
        if (!button) return;
        const requestNumber = ++state.reviewRequest;
        ++state.documentRequest; // Invalidate documents from the previously selected applicant.
        state.documents = [];
        state.documentReference = null;
        state.documentPage = 1;
        setValue('applicantDocumentSearch', '');
        const documentSummary = document.getElementById('applicantDocumentSummary');
        if (documentSummary) documentSummary.textContent = '';
        const documentList = document.getElementById('applicantDocumentList');
        documentList?.replaceChildren(statusMessage(i18n.loading));
        const reference = button.dataset.reference;
        // A later review selection always wins, even if an earlier response arrives last.
        document.querySelectorAll('#applicationList button[data-reference]').forEach(control => {
            control.disabled = true;
            control.setAttribute('aria-busy', 'true');
        });
        try {
            const response = await fetch(`/api/admission-applications/${encodeURIComponent(reference)}`, apiOptions());
            const payload = await response.json().catch(() => null);
            if (requestNumber !== state.reviewRequest || !sameSession()) return;
            if (!response.ok || !payload?.success || !payload.data) throw new Error('Invalid details');
            // Show the application promptly; private documents load independently.
            // A slow document endpoint must not block review of the canonical record.
            renderDetails(payload.data);
            state.currentApplication = payload.data;
            setValue('reviewReference', payload.data.reference);
            setValue('reviewRowVersion', payload.data.rowVersion);
            setValue('decisionNote', payload.data.decisionNote);
            fillReviewStatuses(Number(payload.data.state));
            guards.review?.markClean();
            document.getElementById('admitApplicantButton')?.classList.toggle('d-none', Number(payload.data.state) !== 6);
            bootstrap.Modal.getOrCreateInstance(document.getElementById('reviewModal')).show();
            // Do not await here: loadApplicantDocuments already protects the active
            // application and rejects stale responses when selection changes.
            void loadApplicantDocuments(payload.data.reference);
        } catch {
            if (requestNumber === state.reviewRequest && sameSession()) showAlert('danger', i18n.detailsFailed);
        } finally {
            if (requestNumber === state.reviewRequest) {
                document.querySelectorAll('#applicationList button[data-reference]').forEach(control => {
                    control.disabled = false;
                    control.removeAttribute('aria-busy');
                });
            }
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
            [i18n.academicUnit, [item.academicLevelName, item.academicYearName, item.campusName].filter(Boolean).join(' · ')],
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
        if (!sameSession()) return;
        const list = document.getElementById('applicantDocumentList');
        if (!list) return;
        const requestNumber = ++state.documentRequest;
        list.setAttribute('aria-busy', 'true');
        list.replaceChildren(statusMessage(i18n.loading));
        const summary = document.getElementById('applicantDocumentSummary');
        if (summary) summary.textContent = '';
        try {
            const response = await fetch(`/api/admission-intake/applications/${encodeURIComponent(reference)}/documents`, apiOptions());
            const payload = await response.json().catch(() => null);
            if (requestNumber !== state.documentRequest || state.currentApplication?.reference !== reference || !sameSession()) return;
            if (!response.ok || !payload?.success || !Array.isArray(payload.data)) throw new Error(payload?.message || 'Documents could not be loaded.');
            state.documents = payload.data;
            state.documentReference = reference;
            renderApplicantDocuments();
        } catch (error) {
            if (requestNumber !== state.documentRequest || state.currentApplication?.reference !== reference || !sameSession()) return;
            const message = statusMessage(bangla ? 'নথি লোড করা যায়নি।' : (error.message || 'Documents could not be loaded.'));
            const retry = document.createElement('button');
            retry.type = 'button';
            retry.className = 'btn btn-sm btn-outline-primary';
            retry.textContent = i18n.retry || (bangla ? 'আবার চেষ্টা করুন' : 'Retry');
            retry.addEventListener('click', () => loadApplicantDocuments(reference), { once: true });
            message.append(retry);
            list.replaceChildren(message);
            state.documents = [];
            state.documentReference = null;
        } finally {
            if (requestNumber === state.documentRequest) list.removeAttribute('aria-busy');
        }
    }

    // Document triage is read-only until a reviewer explicitly selects a latest,
    // pending version. The server remains authoritative for every decision.
    function resetApplicantDocumentPage() {
        state.documentPage = 1;
        renderApplicantDocuments();
    }

    function changeApplicantDocumentPage(delta) {
        state.documentPage += delta;
        renderApplicantDocuments();
    }

    function renderApplicantDocuments() {
        if (!sameSession()) return;
        const list = document.getElementById('applicantDocumentList');
        const summary = document.getElementById('applicantDocumentSummary');
        if (!list || state.documentReference !== state.currentApplication?.reference) return;
        const documents = state.documents;
        const latestVersions = new Map();
        for (const item of documents) {
            const key = String(item.documentTypeCode || '');
            const previous = latestVersions.get(key);
            if (!previous || Number(item.versionNo) > Number(previous.versionNo))
                latestVersions.set(key, item);
        }
        const latest = [...latestVersions.values()];
        const counts = { pending: 0, verified: 0, rejected: 0 };
        for (const item of latest) {
            const status = Number(item.verificationStatus);
            if (status === 2) counts.verified++;
            else if (status === 3) counts.rejected++;
            else counts.pending++;
        }
        const filter = valueOf('applicantDocumentStatus') || 'all';
        const showHistory = valueOf('applicantDocumentVersions') === 'history';
        const candidates = showHistory ? documents : latest;
        const visible = candidates.filter(item => {
            const status = Number(item.verificationStatus);
            return filter === 'all' || (filter === 'verified' && status === 2)
                || (filter === 'rejected' && status === 3)
                || (filter === 'pending' && status !== 2 && status !== 3);
        }).sort((a, b) => String(a.documentTypeCode || '').localeCompare(String(b.documentTypeCode || ''))
            || Number(b.versionNo) - Number(a.versionNo));
        const search = valueOf('applicantDocumentSearch').toLocaleLowerCase();
        const matched = visible.filter(item => !search || [item.documentTypeCode, item.originalFileName].some(value => String(value || '').toLocaleLowerCase().includes(search)));
        const pageSize = [10, 20, 50].includes(Number(valueOf('applicantDocumentPageSize'))) ? Number(valueOf('applicantDocumentPageSize')) : 10;
        const totalPages = Math.max(1, Math.ceil(matched.length / pageSize));
        state.documentPage = Math.min(totalPages, Math.max(1, state.documentPage));
        const pageItems = matched.slice((state.documentPage - 1) * pageSize, state.documentPage * pageSize);
        const previous = document.getElementById('applicantDocumentPrevious');
        const next = document.getElementById('applicantDocumentNext');
        if (previous) previous.disabled = state.documentPage <= 1;
        if (next) next.disabled = state.documentPage >= totalPages;
        const pageStatus = document.getElementById('applicantDocumentPageStatus');
        if (pageStatus) pageStatus.textContent = (bangla ? 'পৃষ্ঠা ' : 'Page ') + new Intl.NumberFormat(bangla ? 'bn-BD' : 'en-BD').format(state.documentPage) + ' / ' + new Intl.NumberFormat(bangla ? 'bn-BD' : 'en-BD').format(totalPages);
        const number = value => new Intl.NumberFormat(bangla ? 'bn-BD' : 'en-BD').format(value);
        if (summary) summary.textContent = bangla
            ? `সর্বশেষ নথি ${number(latest.length)} · যাচাই বাকি ${number(counts.pending)} · যাচাইকৃত ${number(counts.verified)} · বাতিল ${number(counts.rejected)} · মিলেছে ${number(matched.length)} · দেখানো হচ্ছে ${number(pageItems.length)}`
            : `Latest documents ${number(latest.length)} · Pending ${number(counts.pending)} · Verified ${number(counts.verified)} · Rejected ${number(counts.rejected)} · Matching ${number(matched.length)} · Showing ${number(pageItems.length)}`;
        if (!pageItems.length) {
            list.replaceChildren(statusMessage(documents.length
                ? (bangla ? 'এই ফিল্টারে কোনো নথি নেই।' : 'No documents match these filters.')
                : (bangla ? 'আবেদনকারীর কোনো নথি আপলোড করা হয়নি।' : 'No applicant documents have been uploaded.')));
            return;
        }
        list.replaceChildren(...pageItems.map(documentItem => {
            const card = document.createElement('article');
            card.className = 'applicant-document-card';
            const documentSummary = document.createElement('div');
            const title = document.createElement('strong');
            title.textContent = documentItem.documentTypeCode || '';
            const detail = document.createElement('span');
            const decision = Number(documentItem.verificationStatus);
            const label = decision === 3 ? (bangla ? 'বাতিল' : 'Rejected') : decision === 2 ? (bangla ? 'যাচাইকৃত' : 'Verified') : (bangla ? 'অপেক্ষমাণ' : 'Pending');
            detail.textContent = `${documentItem.originalFileName || 'Private document'} · v${documentItem.versionNo} · ${formatFileSize(documentItem.fileSizeBytes)} · ${label}`;
            documentSummary.append(title, detail);
            if (documentItem.verificationNote) {
                const note = document.createElement('span');
                note.textContent = documentItem.verificationNote;
                documentSummary.append(note);
            }
            const controls = document.createElement('div');
            controls.className = 'intake-form-actions';
            const download = document.createElement('a');
            download.className = 'btn btn-sm btn-outline-secondary';
            download.href = `/api/admission-intake/applications/${encodeURIComponent(state.documentReference)}/documents/${encodeURIComponent(documentItem.id)}/content`;
            download.textContent = bangla ? 'দেখুন / ডাউনলোড' : 'View / Download';
            controls.append(download);
            if (decision === 1 && !documentItem.isVerified &&
                latestVersions.get(String(documentItem.documentTypeCode || ''))?.id === documentItem.id) {
                const reason = document.createElement('textarea');
                reason.className = 'form-control form-control-sm';
                reason.maxLength = 1000;
                reason.rows = 2;
                reason.placeholder = bangla ? 'বাতিলের কারণ লিখুন' : 'Reason required to reject this document';
                reason.setAttribute('aria-label', bangla ? 'নথি বাতিলের কারণ' : 'Document rejection reason');
                controls.append(documentActionButton(bangla ? 'যাচাই করুন' : 'Verify', 2, documentItem), reason,
                    documentActionButton(bangla ? 'বাতিল করুন' : 'Reject', 3, documentItem));
            }
            card.append(documentSummary, controls);
            return card;
        }));
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
        if (!sameSession()) return;
        const button = event.target.closest('button[data-document-id]');
        if (!button || !state.currentApplication?.reference || state.reviewingDocument) return;
        const status = Number(button.dataset.documentStatus);
        if (status !== 2 && status !== 3) return;
        const reference = state.currentApplication.reference;
        const noteField = button.parentElement?.querySelector('textarea');
        const note = status === 3 ? noteField?.value.trim() : null;
        if (status === 3 && !note) {
            showAlert('danger', bangla ? 'নথি বাতিলের কারণ লিখুন।' : 'Enter a rejection reason before rejecting this document.');
            noteField?.focus();
            return;
        }
        state.reviewingDocument = true;
        const controls = [...button.parentElement.querySelectorAll('button[data-document-id]')];
        controls.forEach(control => { control.disabled = true; control.setAttribute('aria-busy', 'true'); });
        try {
            const response = await fetch(`/api/admission-intake/applications/${encodeURIComponent(reference)}/documents/${encodeURIComponent(button.dataset.documentId)}/review`, apiOptions('POST', {
                status, note: note || null, rowVersion: button.dataset.documentRowVersion
            }));
            const payload = await response.json().catch(() => null);
            if (state.currentApplication?.reference !== reference || !sameSession()) return;
            if (!response.ok || !payload?.success) {
                if (response.status === 409 || response.status === 412) {
                    showAlert('danger', bangla
                        ? 'অন্য ব্যবহারকারী নথিটি পরিবর্তন করেছেন। সর্বশেষ তথ্য আবার দেখুন।'
                        : 'Another reviewer changed this document. Review its latest version.');
                    await loadApplicantDocuments(reference);
                    return;
                }
                throw new Error(payload?.message || (bangla ? 'নথির সিদ্ধান্ত সংরক্ষণ করা যায়নি।' : 'Document decision could not be saved.'));
            }
            showAlert('success', payload.message || (bangla ? 'নথির সিদ্ধান্ত সংরক্ষিত হয়েছে।' : 'Document decision saved.'));
            await loadApplicantDocuments(reference);
        } catch (error) {
            if (state.currentApplication?.reference === reference) {
                showAlert('danger', error.message || i18n.networkError);
                button.focus();
            }
        } finally {
            state.reviewingDocument = false;
            controls.forEach(control => { control.disabled = false; control.removeAttribute('aria-busy'); });
        }
    }

    function fillReviewStatuses(current) {
        const transitions = { 2: [3, 6, 7], 3: [6, 7], 4: [3], 5: [6, 7] };
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

    // Keep a rejected review editable. A stale row version must be reloaded rather
    // than silently overwriting another reviewer, and double-clicks must not post twice.
    async function saveDecision(event) {
        event.preventDefault();
        const form = event.currentTarget;
        if (state.savingDecision || !sameSession() || !form.reportValidity()) return;
        const reference = valueOf('reviewReference');
        const status = positiveInteger(valueOf('reviewStatus'));
        if (!reference || !status) return;
        const button = document.getElementById('saveDecisionButton');
        state.savingDecision = true;
        document.getElementById('reviewModal').inert = true;
        setLoading(button, true);
        try {
            const response = await fetch(`/api/admission-applications/${encodeURIComponent(reference)}/status`, apiOptions('PUT', {
                state: status,
                rowVersion: valueOf('reviewRowVersion'),
                decisionNote: valueOf('decisionNote') || null
            }));
            const payload = await response.json().catch(() => null);
            if (!sameSession()) return;
            if (!response.ok || !payload?.success) {
                const conflict = response.status === 409 || response.status === 412;
                showAlert('danger', conflict
                    ? (bangla ? 'অন্য ব্যবহারকারী আবেদনটি পরিবর্তন করেছেন। আবার তালিকা থেকে খুলে সর্বশেষ তথ্য যাচাই করুন।'
                        : 'Another reviewer changed this application. Reopen it from the list to review the latest details.')
                    : (payload?.message || i18n.decisionFailed));
                if (conflict) {
                    guards.review?.markClean();
                    state.savingDecision = false;
                    bootstrap.Modal.getInstance(document.getElementById('reviewModal'))?.hide();
                    await loadApplications();
                } else button?.focus();
                return;
            }
            guards.review?.markClean();
            state.savingDecision = false;
            bootstrap.Modal.getInstance(document.getElementById('reviewModal'))?.hide();
            showAlert('success', i18n.decisionSaved);
            await loadApplications();
        } catch {
            if (sameSession()) {
                showAlert('danger', i18n.networkError);
                button?.focus();
            }
        } finally {
            state.savingDecision = false;
            document.getElementById('reviewModal').inert = false;
            setLoading(button, false);
        }
    }

    async function prepareEnrollment() {
        if (!sameSession()) return;
        const item = state.currentApplication;
        if (!item?.reference || Number(item.state) !== 6 || state.preparingEnrollment) return;
        if (guards.review?.isDirty() && !guards.review.confirmDiscard()) return;
        const selectedReview = state.reviewRequest;
        state.preparingEnrollment = true;
        const trigger = document.getElementById('admitApplicantButton');
        setLoading(trigger, true);
        try {
            const response = await fetch(`/api/admission-applications/${encodeURIComponent(item.reference)}/enrollment-options`, apiOptions());
            const payload = await response.json().catch(() => null);
            if (selectedReview !== state.reviewRequest || state.currentApplication?.reference !== item.reference || !sameSession()) return;
            if (!response.ok || !payload?.success || !payload.data) throw new Error('Invalid enrollment options');
            fillSelect('enrollmentSectionId', payload.data.academicBatches, true);
            fillSelect('enrollmentGroupId', payload.data.academicTracks, false);
            setValue('enrollmentReference', item.reference);
            setValue('enrollmentRowVersion', item.rowVersion);
            setValue('enrollmentRoll', '');
            guards.review?.markClean();
            guards.enrollment?.markClean();
            state.preparingEnrollment = false;
            bootstrap.Modal.getInstance(document.getElementById('reviewModal'))?.hide();
            bootstrap.Modal.getOrCreateInstance(document.getElementById('enrollmentModal')).show();
        } catch (error) {
            if (selectedReview === state.reviewRequest && state.currentApplication?.reference === item.reference)
                showAlert('danger', error.message || i18n.enrollmentFailed);
        } finally {
            state.preparingEnrollment = false;
            setLoading(trigger, false);
        }
    }


    function initializeAdmissionOfferUi() {
        const modal = document.getElementById('enrollmentModal');
        const footer = modal?.querySelector('.modal-footer');
        const body = modal?.querySelector('.modal-body');
        if (!footer || !body) return;
        const status = document.createElement('p');
        status.id = 'offerStatusMessage';
        status.className = 'col-12 alert alert-info';
        status.setAttribute('role', 'status');
        body.prepend(status);
        const expiryLabel = document.createElement('label');
        expiryLabel.className = 'form-label col-12';
        expiryLabel.textContent = 'Offer expiry (local date/time) / অফারের মেয়াদ';
        const expiry = document.createElement('input');
        expiry.type = 'datetime-local'; expiry.id = 'offerExpiry'; expiry.className = 'form-control';
        expiryLabel.append(expiry); body.append(expiryLabel);
        const button = document.createElement('button');
        button.type = 'button'; button.id = 'issueAdmissionOffer';
        button.className = 'btn btn-outline-primary';
        button.textContent = 'Issue Offer / অফার দিন';
        footer.insertBefore(button, document.getElementById('completeEnrollmentButton'));
        const signer = document.createElement('select');
        signer.id = 'acceptanceSigner'; signer.className = 'form-select';
        [['Guardian', 'Verified guardian / অভিভাবক'], ['Applicant', 'Verified adult applicant']].forEach(([value, label]) => {
            const option = document.createElement('option'); option.value = value; option.textContent = label; signer.append(option);
        });
        const signerLabel = document.createElement('label');
        signerLabel.className = 'form-label col-12'; signerLabel.textContent = 'Signed by / স্বাক্ষরকারী';
        signerLabel.append(signer); body.append(signerLabel);
        const attestationLabel = document.createElement('label');
        attestationLabel.className = 'form-check-label col-12';
        const attestation = document.createElement('input');
        attestation.type = 'checkbox'; attestation.id = 'acceptanceIdentityChecked'; attestation.className = 'form-check-input me-2';
        attestationLabel.append(attestation, document.createTextNode('I verified signer identity and signed document against the applicant/guardian record.'));
        body.append(attestationLabel);
        const acceptButton = document.createElement('button');
        acceptButton.type = 'button'; acceptButton.id = 'recordAdmissionAcceptance';
        acceptButton.className = 'btn btn-success'; acceptButton.textContent = 'Record Verified Acceptance';
        footer.insertBefore(acceptButton, document.getElementById('completeEnrollmentButton'));
        let verifiedDocumentId = null, decisionVersion = null, currentOfferState = 0;
        const refreshAcceptanceButton = () => { acceptButton.disabled = currentOfferState !== 2 || !verifiedDocumentId || !attestation.checked; };
        attestation.addEventListener('change', refreshAcceptanceButton);
        const offerRetry = window.EduOSAdmissionOffers.createRetryTracker(
            window.EduOSAdmissionRetry, window.crypto, () => window.sessionStorage);
        let issuingOffer = false;
        modal.addEventListener('shown.bs.modal', async () => {
            verifiedDocumentId = null; decisionVersion = null; currentOfferState = 0;
            attestation.checked = false; refreshAcceptanceButton();
            const future = new Date(Date.now() + 7 * 86400000);
            expiry.value = toLocalDateTime(future);
            const reference = valueOf('enrollmentReference');
            const admit = document.getElementById('completeEnrollmentButton');
            if (admit) admit.disabled = true;
            button.disabled = true;
            status.textContent = 'Checking admission offer…';
            try {
                const response = await fetch(`/api/admission-applications/${encodeURIComponent(reference)}/offer`, apiOptions());
                const payload = await response.json().catch(() => null);
                if (!response.ok || !payload?.success) throw Error(payload?.message || 'Offer could not be loaded.');
                const decision = payload.data;
                const state = Number(decision?.state || 0);
                currentOfferState = state; decisionVersion = decision?.rowVersion || null;
                if (state === 2) {
                    const docsResponse = await fetch(`/api/admission-intake/applications/${encodeURIComponent(reference)}/documents`, apiOptions());
                    const docsPayload = await docsResponse.json().catch(() => null);
                    if (docsResponse.ok && docsPayload?.success && Array.isArray(docsPayload.data)) {
                        const evidence = (window.EduOSAdmissionOffers?.eligibleEvidence(
                            docsPayload.data, decision?.offeredAt) || [])[0];
                        verifiedDocumentId = evidence?.id || null;
                    }
                }
                refreshAcceptanceButton();
                status.textContent = state === 3 ? 'Offer accepted. Enrollment may proceed.' :
                    state === 2 ? 'Offer issued. Waiting for verified applicant/guardian acceptance.' :
                    state ? 'An existing admission decision requires review.' : 'Issue an offer before enrollment.';
                if (decision?.offeredAcademicBatchId) setValue('enrollmentSectionId', decision.offeredAcademicBatchId);
                button.disabled = state !== 0;
                if (state === 2 && !verifiedDocumentId) status.textContent += ' Upload and verify a signed OFFER_ACCEPTANCE document first.';
                if (admit) admit.disabled = state !== 3;
            } catch (error) {
                status.textContent = error.message || 'Offer status could not be loaded.';
            }
        });
        acceptButton.addEventListener('click', async () => {
            const reference = valueOf('enrollmentReference');
            if (!reference || !verifiedDocumentId || !decisionVersion || !attestation.checked) return;
            acceptButton.disabled = true;
            try {
                const response = await fetch(`/api/admission-applications/${encodeURIComponent(reference)}/offer/verified-acceptance`,
                    apiOptions('POST', { acceptanceDocumentId: verifiedDocumentId,
                        decisionRowVersion: decisionVersion, signerRole: signer.value, identityChecked: true }));
                const payload = await response.json().catch(() => null);
                if (!response.ok || !payload?.success) throw Error(payload?.message || 'Acceptance could not be recorded.');
                currentOfferState = 3; refreshAcceptanceButton();
                status.textContent = 'Verified signed acceptance recorded. Enrollment may proceed.';
                const admit = document.getElementById('completeEnrollmentButton');
                if (admit) admit.disabled = false;
                showAlert('success', payload.message || 'Acceptance recorded.');
            } catch (error) { refreshAcceptanceButton(); showAlert('danger', error.message || 'Acceptance could not be recorded.'); }
        });
        button.addEventListener('click', async () => {
            if (issuingOffer) return;
            const reference = valueOf('enrollmentReference');
            const batch = positiveInteger(valueOf('enrollmentSectionId'));
            const date = new Date(expiry.value);
            if (!reference || !batch || !Number.isFinite(date.getTime()) || date.getTime() <= Date.now()) {
                showAlert('danger', 'Choose an eligible batch and future expiry.'); return;
            }
            issuingOffer = true;
            button.disabled = true;
            try {
                const expiresAt = date.toISOString();
                const key = 'offer:' + reference;
                const requestId = await offerRetry.forPayload(key,
                    window.EduOSAdmissionOffers.offerFingerprint(reference, batch, expiresAt));
                const response = await fetch(`/api/admission-applications/${encodeURIComponent(reference)}/offer`,
                    apiOptions('POST', {
                        clientRequestId: requestId, admissionApplicantReference: reference,
                        state: 2, offeredAcademicBatchId: batch, expiresAt,
                        applicantRowVersion: valueOf('enrollmentRowVersion')
                    }));
                const payload = await response.json().catch(() => null);
                if (!response.ok || !payload?.success) throw Error(payload?.message || 'Offer could not be issued.');
                offerRetry.clear(key);
                status.textContent = 'Offer issued. Verified applicant/guardian acceptance is required before enrollment.';
                showAlert('success', payload.message || 'Offer issued.');
                await loadApplications();
            } catch (error) {
                button.disabled = false;
                showAlert('danger', error.message || 'Offer could not be issued.');
            } finally {
                issuingOffer = false;
            }
        });
    }

    // Enrollment is a server-authorized state transition. Preserve the entered roll
    // and selection on failure; never infer success after an ambiguous network error.
    async function completeEnrollment(event) {
        event.preventDefault();
        if (state.enrolling || !sameSession() || !event.currentTarget.reportValidity()) return;
        const reference = valueOf('enrollmentReference');
        if (!reference) return;
        const button = document.getElementById('completeEnrollmentButton');
        state.enrolling = true;
        document.getElementById('enrollmentModal').inert = true;
        setLoading(button, true);
        try {
            const response = await fetch(`/api/admission-applications/${encodeURIComponent(reference)}/admit`, apiOptions('POST', {
                academicBatchId: positiveInteger(valueOf('enrollmentSectionId')),
                academicTrackId: positiveInteger(valueOf('enrollmentGroupId')),
                roll: valueOf('enrollmentRoll'),
                rowVersion: valueOf('enrollmentRowVersion')
            }));
            const payload = await response.json().catch(() => null);
            if (!sameSession()) return;
            if (!response.ok || !payload?.success) {
                const conflict = response.status === 409 || response.status === 412;
                showAlert('danger', conflict
                    ? (bangla ? 'ভর্তির তথ্য পরিবর্তিত হয়েছে। তালিকা থেকে আবেদনটি আবার খুলে যাচাই করুন।'
                        : 'Enrollment data changed. Reopen the application from the list before trying again.')
                    : (payload?.message || i18n.enrollmentFailed));
                if (conflict) {
                    guards.enrollment?.markClean();
                    state.enrolling = false;
                    bootstrap.Modal.getInstance(document.getElementById('enrollmentModal'))?.hide();
                    state.currentApplication = null;
                    await loadApplications();
                } else button?.focus();
                return;
            }
            guards.enrollment?.markClean();
            state.enrolling = false;
            bootstrap.Modal.getInstance(document.getElementById('enrollmentModal'))?.hide();
            showAlert('success', `${i18n.admittedSuccess} ${payload.data?.studentCode || ''}`.trim());
            state.currentApplication = null;
            await loadApplications();
        } catch {
            if (sameSession()) {
                showAlert('danger', bangla
                    ? 'সংযোগ বিচ্ছিন্ন হয়েছে। আবার চেষ্টা করার আগে আবেদনটির বর্তমান অবস্থা যাচাই করুন।'
                    : 'Connection lost. Check the current application status before retrying enrollment.');
                button?.focus();
            }
        } finally {
            state.enrolling = false;
            document.getElementById('enrollmentModal').inert = false;
            setLoading(button, false);
        }
    }

    function applyFilters(event) {
        event.preventDefault();
        state.page = 1;
        loadApplications();
    }

    function changePageSize() {
        const selected = Number(valueOf('admissionPageSize'));
        if (![10, 20, 50].includes(selected) || selected === state.pageSize) return;
        state.pageSize = selected;
        state.page = 1;
        loadApplications();
    }

    function changePage(change) {
        const next = state.page + change;
        if (next < 1 || next > state.totalPages) return;
        state.page = next;
        loadApplications();
    }

    function updatePagination(loading = false) {
        const previous = document.getElementById('previousPage');
        const next = document.getElementById('nextPage');
        if (previous) previous.disabled = loading || state.page <= 1;
        if (next) next.disabled = loading || state.page >= state.totalPages;
        const exportButton = document.getElementById('applicationExportPage');
        if (exportButton) exportButton.disabled = loading || !state.listPageRows?.length;
        const label = document.getElementById('pageStatus');
        if (label) label.textContent = loading
            ? (i18n.loading || (bangla ? 'লোড হচ্ছে' : 'Loading'))
            : `${i18n.pageLabel || (bangla ? 'পৃষ্ঠা' : 'Page')} ${new Intl.NumberFormat(bangla ? 'bn-BD' : 'en-BD').format(state.page)} / ${new Intl.NumberFormat(bangla ? 'bn-BD' : 'en-BD').format(state.totalPages)}`;
    }

    function apiOptions(method = 'GET', body = null) {
        if (!sameSession()) throw new Error('Session changed. Reload the page.');
        const options = { method, cache: 'no-store', credentials: 'same-origin', headers: { Accept: 'application/json' } };
        if (method !== 'GET') {
            const csrf = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (!csrf) throw new Error('Security token missing. Reload the page.');
            options.headers.RequestVerificationToken = csrf;
        }
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
        if (!sameSession()) return;
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
