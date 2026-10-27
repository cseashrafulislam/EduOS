(() => {
    'use strict';

    const root = document.getElementById('publicAdmissionApp');
    if (!root) return;

    const tenant = root.dataset.tenant;
    const apiBase = `/api/public/admissions/${encodeURIComponent(tenant)}`;
    const form = document.getElementById('admissionForm');
    const formMessage = document.getElementById('formMessage');
    const submitButton = document.getElementById('submitButton');
    const newApplicationButton = document.getElementById('newApplicationButton');
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

    const statusNames = { 1: 'Draft', 2: 'Submitted', 3: 'Under Review', 4: 'Documents Pending', 5: 'Assessment Pending', 6: 'Qualified', 7: 'Rejected', 8: 'Admitted', 9: 'Withdrawn' };
    const value = id => document.getElementById(id).value.trim();
    const optional = id => value(id) || null;
    const setMessage = (element, text, success = false) => { element.className = `message ${success ? 'ok' : 'error'}`; element.textContent = text; };
    const clearMessage = element => { element.className = 'message'; element.textContent = ''; };
    const createRequestId = () => crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-0000-4000-8000-${Math.random().toString(16).slice(2).padEnd(12, '0').slice(0, 12)}`;
    const retry = window.EduOSAdmissionRetry.createPersistentTracker(
        createRequestId,
        () => window.sessionStorage,
        async raw => {
            // Never persist a raw form or a reversible/non-cryptographic fingerprint.
            const bytes = new TextEncoder().encode(raw);
            const hash = await crypto.subtle.digest('SHA-256', bytes);
            return Array.from(new Uint8Array(hash), x => x.toString(16).padStart(2, '0')).join('');
        },
        tenant
    );
    let applicationSubmitted = false;
    let submissionInFlight = false;

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
        unitSelect.value = String(selected.academicLevelId);
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
            bindOptions(unitSelect, data.academicLevels || [], 'Select class / level');
            intakeFormSelect.innerHTML = openForms.length
                ? '<option value="">Select admission form</option>' + openForms.map(x => `<option value="${x.reference}">${escapeHtml(x.title)}${x.applicationFee ? ` — ${escapeHtml(x.currency)} ${x.applicationFee}` : ''}</option>`).join('')
                : '<option value="">General application</option>';
            intakeFormSelect.required = openForms.length > 0;
            intakeFormSelect.disabled = openForms.length === 0;
            if (openForms.length === 1) intakeFormSelect.value = openForms[0].reference;
            refreshTerms();
            applySelectedForm();
            submitButton.disabled = !openForms.length || applicationSubmitted;
            if (!openForms.length) setMessage(formMessage, 'No admission intake is open. Please return when applications are available.');
        } catch (error) {
            setMessage(formMessage, error.message);
            submitButton.disabled = true;
        }
    }

    yearSelect.addEventListener('change', refreshTerms);
    intakeFormSelect.addEventListener('change', applySelectedForm);

    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (applicationSubmitted || submissionInFlight || !openForms.length) return;
        submissionInFlight = true;
        clearMessage(formMessage);
        receipt.classList.remove('show');
        submitButton.disabled = true;

        const request = {
            admissionFormReference: intakeFormSelect.value || null,
            customResponses: readCustomResponses(),
            academicYearId: Number(value('academicYearId')),
            academicTermId: value('academicTermId') ? Number(value('academicTermId')) : null,
            campusId: Number(value('campusId')),
            academicLevelId: Number(value('academicUnitId')),
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
        // Reuse the same key after a timeout; changed form data gets a fresh key.
        request.clientRequestId = await retry.forPayload('application', request);

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
            applicationSubmitted = true;
            newApplicationButton.hidden = false;
            setMessage(formMessage, 'Application submitted successfully. Reference টি সংরক্ষণ করুন।', true);
            try {
                localStorage.setItem(`eduos:admission:${tenant}`, JSON.stringify({ reference: data.reference, mobile: request.primaryMobile, applicationNumber: data.applicationNumber }));
            } catch { /* Browser storage is optional; a successful server write remains successful. */ }
        } catch (error) {
            setMessage(formMessage, error.message);
        } finally {
            submissionInFlight = false;
            submitButton.disabled = applicationSubmitted || !openForms.length;
        }
    });

    newApplicationButton.addEventListener('click', () => {
        retry.clearAll();
        applicationSubmitted = false;
        activeSubmission = null;
        form.reset();
        receipt.classList.remove('show');
        documentPanel.hidden = true;
        document.getElementById('offerAcceptancePanel').hidden = true;
        resubmissionPanel.hidden = true;
        clearMessage(formMessage);
        clearMessage(documentMessage);
        newApplicationButton.hidden = true;
        applySelectedForm();
        submitButton.disabled = !openForms.length;
        form.querySelector('select, input')?.focus();
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
                const file = input.files[0];
                const key = `document:${activeSubmission.reference}:${input.dataset.documentType}`;
                data.append('clientRequestId', retry.forFile(key, file));
                data.append('mobile', activeSubmission.mobile);
                data.append('documentType', input.dataset.documentType);
                data.append('file', input.files[0]);
                await readResponse(await fetch(`${apiBase}/applications/${encodeURIComponent(activeSubmission.reference)}/documents`, { method: 'POST', body: data, headers: { Accept: 'application/json' } }));
                retry.clear(key);
                input.value = ''; // Completed files are not uploaded again if a later file fails.
            }
            setMessage(documentMessage, 'Documents uploaded and awaiting verification.', true);
        } catch (error) {
            setMessage(documentMessage, error.message);
        } finally {
            uploadDocumentsButton.disabled = false;
        }
    });

    const resubmissionPanel = document.createElement('section');
    resubmissionPanel.id = 'rejectedDocumentResubmission';
    resubmissionPanel.setAttribute('aria-label', 'Rejected document replacement');
    resubmissionPanel.hidden = true;
    statusMessage.after(resubmissionPanel);

    function showRejectedDocuments(data, reference, mobile) {
        resubmissionPanel.replaceChildren();
        const finalState = [6, 7, 8, 9].includes(Number(data.state));
        const latest = new Map();
        for (const doc of data.documents || []) {
            const previous = latest.get(doc.documentTypeCode);
            if (!previous || Number(doc.versionNo) > Number(previous.versionNo))
                latest.set(doc.documentTypeCode, doc);
        }
        const rejected = [...latest.values()].filter(doc => Number(doc.verificationStatus) === 3 &&
            (!finalState || Number(data.state) === 6 && doc.documentTypeCode === 'OFFER_ACCEPTANCE'));
        resubmissionPanel.hidden = rejected.length === 0;
        for (const doc of rejected) {
            const row = document.createElement('div');
            row.className = 'field';
            const label = document.createElement('label');
            label.textContent = `Replace rejected ${doc.documentTypeCode} (version ${doc.versionNo})`;
            const reason = document.createElement('p');
            reason.className = 'muted';
            reason.textContent = doc.verificationNote || 'Please upload a corrected document.';
            const input = document.createElement('input');
            input.type = 'file';
            input.setAttribute('aria-label', `Replacement for ${doc.documentTypeCode}`);
            const button = document.createElement('button');
            button.type = 'button';
            button.textContent = 'Upload replacement';
            button.disabled = true;
            input.addEventListener('change', () => {
                button.disabled = !input.files?.length;
                // The selected File object itself is the retry identity until upload succeeds.
            });
            button.addEventListener('click', async () => {
                if (!input.files?.length) return;
                button.disabled = true;
                try {
                    const payload = new FormData();
                    const key = `replacement:${reference}:${doc.documentTypeCode}`;
                    payload.append('clientRequestId', retry.forFile(key, input.files[0]));
                    payload.append('mobile', mobile);
                    payload.append('documentType', doc.documentTypeCode);
                    payload.append('file', input.files[0]);
                    await readResponse(await fetch(`${apiBase}/applications/${encodeURIComponent(reference)}/documents`,
                        { method: 'POST', body: payload, headers: { Accept: 'application/json' } }));
                    retry.clear(key);
                    input.value = '';
                    setMessage(statusMessage, 'Replacement uploaded for verification.', true);
                    resubmissionPanel.hidden = true;
                } catch (error) {
                    setMessage(statusMessage, error.message);
                    button.disabled = false;
                }
            });
            row.append(label, reason, input, button);
            resubmissionPanel.append(row);
        }
    }

    statusButton.addEventListener('click', async () => {
        resubmissionPanel.hidden = true;
        clearMessage(statusMessage);
        document.getElementById('offerAcceptancePanel').hidden = true;
        const reference = value('statusReference');
        const mobile = value('statusMobile');
        if (!reference || !mobile) { setMessage(statusMessage, 'Application reference এবং mobile number দিন।'); return; }
        statusButton.disabled = true;
        try {
            const body = await readResponse(await fetch(`${apiBase}/applications/${encodeURIComponent(reference)}/status?mobile=${encodeURIComponent(mobile)}`, { headers: { Accept: 'application/json' } }));
            const data = body.data;
            const status = statusNames[data.state] || `Status ${data.state}`;
            const note = data.decisionNote ? ` — ${data.decisionNote}` : '';
            const assessment = data.assessment;
            const merit = assessment?.meritPosition ? `, Merit #${assessment.meritPosition}` : '';
            const assessmentText = assessment
                ? ` — ${assessment.testName}: ${assessment.resultStatus} (${assessment.obtainedMarks}/${assessment.totalMarks}${merit})`
                : '';
            const documentText = (data.documents || []).length
                ? ` — Documents: ${(data.documents || []).map(x => `${x.documentTypeCode} v${x.versionNo} (${Number(x.verificationStatus) === 3 ? 'Rejected' : x.isVerified ? 'Verified' : 'Pending'})`).join(', ')}`
                : '';
            setMessage(statusMessage, `${data.applicationNumber}: ${status}${note}${assessmentText}${documentText}`, true);
            document.getElementById('offerAcceptancePanel').hidden = !data.canUploadOfferAcceptance;
            showRejectedDocuments(data, reference, mobile);
        } catch (error) {
            setMessage(statusMessage, error.message);
        } finally {
            statusButton.disabled = false;
        }
    });

    document.getElementById('uploadOfferAcceptanceButton').addEventListener('click', async () => {
        const button = document.getElementById('uploadOfferAcceptanceButton');
        const message = document.getElementById('offerAcceptanceMessage');
        const file = document.getElementById('offerAcceptanceFile').files?.[0];
        const reference = value('statusReference'), mobile = value('statusMobile');
        clearMessage(message);
        if (!file || !reference || !mobile) { setMessage(message, 'Check status and select a signed acceptance document.'); return; }
        const key = `offer:${reference}:${mobile}`;
        const body = new FormData();
        body.append('clientRequestId', retry.forFile(key, file)); body.append('mobile', mobile);
        body.append('documentType', 'OFFER_ACCEPTANCE'); body.append('file', file);
        button.disabled = true;
        try {
            await readResponse(await fetch(`${apiBase}/applications/${encodeURIComponent(reference)}/documents`,
                { method: 'POST', body, headers: { Accept: 'application/json' } }));
            retry.clear(key);
            document.getElementById('offerAcceptanceFile').value = '';
            setMessage(message, 'Signed acceptance submitted. The admission office must verify identity and signature before enrollment.', true);
            document.getElementById('offerAcceptancePanel').hidden = true;
        } catch (error) { setMessage(message, error.message); }
        finally { button.disabled = false; }
    });

    try {
        const saved = JSON.parse(localStorage.getItem(`eduos:admission:${tenant}`) || 'null');
        if (saved?.reference) document.getElementById('statusReference').value = saved.reference;
        if (saved?.mobile) document.getElementById('statusMobile').value = saved.mobile;
    } catch { }

    loadOptions();
})();
