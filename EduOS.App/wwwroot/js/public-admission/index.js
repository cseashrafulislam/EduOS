(() => {
    'use strict';

    const root = document.getElementById('publicAdmissionApp');
    if (!root) return;

    const tenant = root.dataset.tenant;
    const apiBase = `/api/public/admissions/${encodeURIComponent(tenant)}`;
    const form = document.getElementById('admissionForm');
    const formMessage = document.getElementById('formMessage');
    const submitButton = document.getElementById('submitButton');
    const receipt = document.getElementById('receipt');
    const intakeFormSelect = document.getElementById('intakeFormReference');
    const customFields = document.getElementById('customFields');
    const yearSelect = document.getElementById('academicYearId');
    const termSelect = document.getElementById('academicTermId');
    const campusSelect = document.getElementById('campusId');
    const unitSelect = document.getElementById('academicUnitId');
    const statusButton = document.getElementById('statusButton');
    const statusMessage = document.getElementById('statusMessage');
    const documentPanel = document.getElementById('documentPanel');
    const documentFields = document.getElementById('documentFields');
    const documentMessage = document.getElementById('documentMessage');
    const uploadDocumentsButton = document.getElementById('uploadDocumentsButton');
    let terms = [];
    let openForms = [];
    let activeSubmission = null;

    const statusNames = { 1: 'Submitted', 2: 'Under Review', 3: 'Waitlisted', 4: 'Approved', 5: 'Rejected', 6: 'Withdrawn', 7: 'Admitted' };
    const value = id => document.getElementById(id).value.trim();
    const optional = id => value(id) || null;
    const setMessage = (element, text, success = false) => { element.className = `message ${success ? 'ok' : 'error'}`; element.textContent = text; };
    const clearMessage = element => { element.className = 'message'; element.textContent = ''; };
    const createRequestId = () => crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-0000-4000-8000-${Math.random().toString(16).slice(2).padEnd(12, '0').slice(0, 12)}`;

    async function readResponse(response) {
        const body = await response.json().catch(() => ({}));
        if (response.ok && body.success !== false) return body;
        const validation = body.errors ? Object.values(body.errors).flat().join(' ') : '';
        throw new Error(body.message || body.title || validation || 'Request failed. Please try again.');
    }

    function bindOptions(select, items, placeholder) {
        select.innerHTML = `<option value="">${placeholder}</option>` + items.map(x => `<option value="${x.id}">${escapeHtml(x.name)}</option>`).join('');
        if (items.length === 1) select.value = String(items[0].id);
    }

    function refreshTerms() {
        const yearId = Number(yearSelect.value || 0);
        const filtered = yearId ? terms.filter(x => Number(x.parentId) === yearId) : [];
        bindOptions(termSelect, filtered, 'Optional');
    }

    const selectedIntakeForm = () => openForms.find(x => x.reference === intakeFormSelect.value) || null;

    function renderCustomFields(fields) {
        customFields.replaceChildren();
        for (const field of [...fields].sort((a, b) => (a.displayOrder - b.displayOrder) || a.key.localeCompare(b.key))) {
            const wrapper = document.createElement('div');
            wrapper.className = `field ${field.type === 2 ? 'full' : ''}`;
            const label = document.createElement('label');
            label.htmlFor = `custom_${field.key}`;
            label.textContent = `${field.label}${field.labelBangla ? ` / ${field.labelBangla}` : ''}`;
            let control;
            if (field.type === 2) control = document.createElement('textarea');
            else if (field.type === 5) {
                control = document.createElement('select');
                const placeholder = document.createElement('option');
                placeholder.value = '';
                placeholder.textContent = 'Select';
                control.append(placeholder);
                for (const optionValue of field.options || []) {
                    const option = document.createElement('option');
                    option.value = optionValue;
                    option.textContent = optionValue;
                    control.append(option);
                }
            } else {
                control = document.createElement('input');
                control.type = field.type === 3 ? 'number' : field.type === 4 ? 'date' : field.type === 6 ? 'checkbox' : 'text';
            }
            control.id = `custom_${field.key}`;
            control.dataset.customKey = field.key;
            control.dataset.fieldType = String(field.type);
            control.required = Boolean(field.isRequired);
            if (field.maxLength && field.type !== 3 && field.type !== 4 && field.type !== 6) control.maxLength = field.maxLength;
            wrapper.append(label, control);
            customFields.append(wrapper);
        }
    }

    function renderDocumentFields(requirements) {
        documentFields.replaceChildren();
        for (const requirement of requirements) {
            const wrapper = document.createElement('div');
            wrapper.className = 'field';
            const label = document.createElement('label');
            label.textContent = `${requirement.label}${requirement.labelBangla ? ` / ${requirement.labelBangla}` : ''}${requirement.isRequired ? ' *' : ''}`;
            const input = document.createElement('input');
            input.type = 'file';
            input.dataset.documentType = requirement.documentType;
            input.dataset.requiredDocument = String(Boolean(requirement.isRequired));
            input.accept = (requirement.allowedExtensions || []).join(',');
            label.htmlFor = `document_${requirement.documentType}`;
            input.id = label.htmlFor;
            wrapper.append(label, input);
            const help = document.createElement('span');
            help.className = 'muted';
            help.textContent = `Maximum ${requirement.maxFileSizeMb} MB`;
            wrapper.append(help);
            documentFields.append(wrapper);
        }
    }

    function applySelectedForm() {
        const selected = selectedIntakeForm();
        const locked = Boolean(selected);
        for (const control of [yearSelect, termSelect, campusSelect, unitSelect]) control.disabled = locked;
        if (!selected) {
            renderCustomFields([]);
            renderDocumentFields([]);
            documentPanel.hidden = true;
            return;
        }
        yearSelect.value = String(selected.academicYearId);
        refreshTerms();
        termSelect.value = selected.academicTermId ? String(selected.academicTermId) : '';
        campusSelect.value = String(selected.campusId);
        unitSelect.value = String(selected.academicUnitId);
        renderCustomFields(selected.fields || []);
        renderDocumentFields(selected.documentRequirements || []);
        documentPanel.hidden = !activeSubmission || !(selected.documentRequirements || []).length;
    }

    function readCustomResponses() {
        const responses = {};
        for (const control of customFields.querySelectorAll('[data-custom-key]')) {
            const raw = control.dataset.fieldType === '6' ? String(control.checked) : control.value.trim();
            if (raw || control.required || control.dataset.fieldType === '6') responses[control.dataset.customKey] = raw || null;
        }
        return responses;
    }

    function escapeHtml(text) {
        return String(text ?? '').replace(/[&<>'"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[c]));
    }

    async function loadOptions() {
        clearMessage(formMessage);
        try {
            const body = await readResponse(await fetch(`${apiBase}/options`, { headers: { Accept: 'application/json' } }));
            const data = body.data || {};
            terms = data.academicTerms || [];
            openForms = data.openForms || [];
            bindOptions(yearSelect, data.academicYears || [], 'Select academic year');
            bindOptions(campusSelect, data.campuses || [], 'Select campus');
            bindOptions(unitSelect, data.academicUnits || [], 'Select class / level');
            intakeFormSelect.innerHTML = openForms.length
                ? '<option value="">Select admission form</option>' + openForms.map(x => `<option value="${x.reference}">${escapeHtml(x.title)}${x.applicationFee ? ` — ${escapeHtml(x.currency)} ${x.applicationFee}` : ''}</option>`).join('')
                : '<option value="">General application</option>';
            intakeFormSelect.required = openForms.length > 0;
            intakeFormSelect.disabled = openForms.length === 0;
            if (openForms.length === 1) intakeFormSelect.value = openForms[0].reference;
            refreshTerms();
            applySelectedForm();
        } catch (error) {
            setMessage(formMessage, error.message);
            submitButton.disabled = true;
        }
    }

    yearSelect.addEventListener('change', refreshTerms);
    intakeFormSelect.addEventListener('change', applySelectedForm);

    form.addEventListener('submit', async event => {
        event.preventDefault();
        clearMessage(formMessage);
        receipt.classList.remove('show');
        submitButton.disabled = true;

        const request = {
            clientRequestId: createRequestId(),
            admissionFormReference: intakeFormSelect.value || null,
            customResponses: readCustomResponses(),
            academicYearId: Number(value('academicYearId')),
            academicTermId: value('academicTermId') ? Number(value('academicTermId')) : null,
            campusId: Number(value('campusId')),
            academicUnitId: Number(value('academicUnitId')),
            applicantName: value('applicantName'),
            applicantNameBangla: optional('applicantNameBangla'),
            dateOfBirth: value('dateOfBirth'),
            gender: Number(value('gender')),
            primaryMobile: value('primaryMobile'),
            email: optional('email'),
            guardianName: optional('guardianName'),
            guardianRelation: optional('guardianRelation'),
            guardianMobile: optional('guardianMobile'),
            presentAddress: optional('presentAddress'),
            permanentAddress: optional('permanentAddress'),
            previousInstitution: optional('previousInstitution'),
            preferredLanguage: value('preferredLanguage') || 'bn-BD'
        };

        try {
            const response = await fetch(`${apiBase}/applications`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
                body: JSON.stringify(request)
            });
            const body = await readResponse(response);
            const data = body.data;
            document.getElementById('applicationNumber').textContent = data.applicationNumber;
            document.getElementById('applicationReference').textContent = data.reference;
            document.getElementById('statusReference').value = data.reference;
            document.getElementById('statusMobile').value = request.primaryMobile;
            receipt.classList.add('show');
            activeSubmission = { reference: data.reference, mobile: request.primaryMobile };
            applySelectedForm();
            setMessage(formMessage, 'Application submitted successfully. Reference টি সংরক্ষণ করুন।', true);
            localStorage.setItem(`eduos:admission:${tenant}`, JSON.stringify({ reference: data.reference, mobile: request.primaryMobile, applicationNumber: data.applicationNumber }));
        } catch (error) {
            setMessage(formMessage, error.message);
        } finally {
            submitButton.disabled = false;
        }
    });

    uploadDocumentsButton.addEventListener('click', async () => {
        clearMessage(documentMessage);
        if (!activeSubmission) { setMessage(documentMessage, 'Submit the application before uploading documents.'); return; }
        const inputs = [...documentFields.querySelectorAll('input[type="file"]')];
        const missing = inputs.find(x => x.dataset.requiredDocument === 'true' && !x.files?.length);
        if (missing) { setMessage(documentMessage, 'Select every required document.'); missing.focus(); return; }
        const selected = inputs.filter(x => x.files?.length);
        if (!selected.length) { setMessage(documentMessage, 'Select at least one document.'); return; }
        uploadDocumentsButton.disabled = true;
        try {
            for (const input of selected) {
                const data = new FormData();
                data.append('clientRequestId', createRequestId());
                data.append('mobile', activeSubmission.mobile);
                data.append('documentType', input.dataset.documentType);
                data.append('file', input.files[0]);
                await readResponse(await fetch(`${apiBase}/applications/${encodeURIComponent(activeSubmission.reference)}/documents`, { method: 'POST', body: data, headers: { Accept: 'application/json' } }));
            }
            setMessage(documentMessage, 'Documents uploaded and awaiting verification.', true);
        } catch (error) {
            setMessage(documentMessage, error.message);
        } finally {
            uploadDocumentsButton.disabled = false;
        }
    });

    statusButton.addEventListener('click', async () => {
        clearMessage(statusMessage);
        const reference = value('statusReference');
        const mobile = value('statusMobile');
        if (!reference || !mobile) { setMessage(statusMessage, 'Application reference এবং mobile number দিন।'); return; }
        statusButton.disabled = true;
        try {
            const body = await readResponse(await fetch(`${apiBase}/applications/${encodeURIComponent(reference)}/status?mobile=${encodeURIComponent(mobile)}`, { headers: { Accept: 'application/json' } }));
            const data = body.data;
            const status = statusNames[data.status] || `Status ${data.status}`;
            const note = data.decisionNote ? ` — ${data.decisionNote}` : '';
            const assessment = data.assessment;
            const merit = assessment?.meritPosition ? `, Merit #${assessment.meritPosition}` : '';
            const assessmentText = assessment
                ? ` — ${assessment.testName}: ${assessment.resultStatus} (${assessment.obtainedMarks}/${assessment.totalMarks}${merit})`
                : '';
            const documentText = (data.documents || []).length
                ? ` — Documents: ${(data.documents || []).map(x => `${x.documentType} (${x.verificationStatus === 2 ? 'Verified' : x.verificationStatus === 3 ? 'Rejected' : 'Pending'})`).join(', ')}`
                : '';
            setMessage(statusMessage, `${data.applicationNumber}: ${status}${note}${assessmentText}${documentText}`, true);
        } catch (error) {
            setMessage(statusMessage, error.message);
        } finally {
            statusButton.disabled = false;
        }
    });

    try {
        const saved = JSON.parse(localStorage.getItem(`eduos:admission:${tenant}`) || 'null');
        if (saved?.reference) document.getElementById('statusReference').value = saved.reference;
        if (saved?.mobile) document.getElementById('statusMobile').value = saved.mobile;
    } catch { }

    loadOptions();
})();
