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
    const yearSelect = document.getElementById('academicYearId');
    const termSelect = document.getElementById('academicTermId');
    const campusSelect = document.getElementById('campusId');
    const unitSelect = document.getElementById('academicUnitId');
    const statusButton = document.getElementById('statusButton');
    const statusMessage = document.getElementById('statusMessage');
    let terms = [];

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

    function escapeHtml(text) {
        return String(text ?? '').replace(/[&<>'"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[c]));
    }

    async function loadOptions() {
        clearMessage(formMessage);
        try {
            const body = await readResponse(await fetch(`${apiBase}/options`, { headers: { Accept: 'application/json' } }));
            const data = body.data || {};
            terms = data.academicTerms || [];
            bindOptions(yearSelect, data.academicYears || [], 'Select academic year');
            bindOptions(campusSelect, data.campuses || [], 'Select campus');
            bindOptions(unitSelect, data.academicUnits || [], 'Select class / level');
            refreshTerms();
        } catch (error) {
            setMessage(formMessage, error.message);
            submitButton.disabled = true;
        }
    }

    yearSelect.addEventListener('change', refreshTerms);

    form.addEventListener('submit', async event => {
        event.preventDefault();
        clearMessage(formMessage);
        receipt.classList.remove('show');
        submitButton.disabled = true;

        const request = {
            clientRequestId: createRequestId(),
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
            setMessage(formMessage, 'Application submitted successfully. Reference টি সংরক্ষণ করুন।', true);
            localStorage.setItem(`eduos:admission:${tenant}`, JSON.stringify({ reference: data.reference, mobile: request.primaryMobile, applicationNumber: data.applicationNumber }));
        } catch (error) {
            setMessage(formMessage, error.message);
        } finally {
            submitButton.disabled = false;
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
            setMessage(statusMessage, `${data.applicationNumber}: ${status}${note}`, true);
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
