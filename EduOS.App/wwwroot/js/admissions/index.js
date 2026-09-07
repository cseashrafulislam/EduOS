(() => {
    'use strict';

    const i18n = JSON.parse(document.getElementById('admissionStrings')?.textContent || '{}');
    const state = { page: 1, pageSize: 20, totalPages: 1, options: null, pendingRequestId: createRequestId() };
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
        document.getElementById('reviewForm')?.addEventListener('submit', saveDecision);
        document.getElementById('previousPage')?.addEventListener('click', () => changePage(-1));
        document.getElementById('nextPage')?.addEventListener('click', () => changePage(1));
        await loadOptions();
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
            refreshTerms();
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
            setValue('reviewReference', payload.data.reference);
            setValue('reviewRowVersion', payload.data.rowVersion);
            setValue('decisionNote', payload.data.decisionNote);
            fillReviewStatuses(Number(payload.data.status));
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
            [i18n.decisionNote, item.decisionNote]
        ];
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
    function isMinor(value) { const dob = new Date(`${value}T00:00:00`); const threshold = new Date(); threshold.setFullYear(threshold.getFullYear() - 18); return !Number.isNaN(dob.valueOf()) && dob > threshold; }
    function createRequestId() { if (crypto.randomUUID) return crypto.randomUUID(); const bytes = crypto.getRandomValues(new Uint8Array(16)); bytes[6] = (bytes[6] & 15) | 64; bytes[8] = (bytes[8] & 63) | 128; const hex = [...bytes].map(x => x.toString(16).padStart(2, '0')).join(''); return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`; }
})();
