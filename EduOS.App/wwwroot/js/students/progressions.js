(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const val = id => el(id)?.value?.trim() || '';
    const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    const state = {
        options: null, requestId: null, saving: false, reference: null,
        lookupPending: 0, selectionPending: 0, historyPending: 0,
        historyPage: 1, historyPages: 0
    };

    document.addEventListener('DOMContentLoaded', () => {
        el('promotionLookup')?.addEventListener('submit', searchStudents);
        el('promotionResults')?.addEventListener('click', event => {
            const button = event.target.closest('button[data-reference]');
            if (button && guid.test(button.dataset.reference)) selectStudent(button.dataset.reference);
        });
        el('promotionHistoryPrevious')?.addEventListener('click', () => {
            if (state.historyPage > 1) loadHistory(state.historyPage - 1);
        });
        el('promotionHistoryNext')?.addEventListener('click', () => {
            if (state.historyPage < state.historyPages) loadHistory(state.historyPage + 1);
        });
        el('promotionForm')?.addEventListener('submit', submitProgression);
        const reference = new URLSearchParams(location.search).get('studentReference');
        if (reference && guid.test(reference)) selectStudent(reference);
    }, { once: true });

    function status(id, message, error = false) {
        const node = el(id);
        if (!node) return;
        node.textContent = message;
        node.classList.toggle('text-danger', error);
    }

    function notice(message, success = false) {
        const box = el('promotionAlert');
        box.className = 'alert alert-' + (success ? 'success' : 'danger');
        box.textContent = message;
        box.focus();
    }

    async function api(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        if (method !== 'GET' && token) headers.RequestVerificationToken = token;
        const response = await fetch(url, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers, ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        const payload = await response.json().catch(() => null);
        if (!response.ok || payload?.success !== true)
            throw Error(payload?.message || 'Request failed. Reload and retry.');
        return payload.data;
    }

    async function searchStudents(event) {
        event?.preventDefault();
        const search = val('promotionSearch');
        if (search.length < 2 || search.length > 100) {
            status('promotionSearchStatus', 'Enter 2–100 characters.', true);
            return;
        }
        const request = ++state.lookupPending;
        el('promotionResults').replaceChildren();
        status('promotionSearchStatus', 'Searching students…');
        try {
            const query = new URLSearchParams({ search, page: '1', pageSize: '20' });
            const data = await api('/api/students?' + query);
            if (request !== state.lookupPending) return;
            if (!Array.isArray(data?.items)) throw Error('Student search returned an invalid result.');
            for (const item of data.items) {
                if (!guid.test(item.reference)) continue;
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'list-group-item list-group-item-action';
                button.dataset.reference = item.reference;
                button.textContent = [item.studentCode, item.fullName, item.rollNo, item.status]
                    .filter(Boolean).join(' · ');
                el('promotionResults').append(button);
            }
            status('promotionSearchStatus', data.items.length
                ? 'Select a student. Showing up to 20 matches; refine your search if needed.'
                : 'No matching students found.');
        } catch (error) {
            if (request === state.lookupPending)
                status('promotionSearchStatus', error.message || 'Student search failed.', true);
        }
    }

    async function selectStudent(reference) {
        if (!guid.test(reference)) return;
        const request = ++state.selectionPending;
        state.reference = reference;
        state.options = null;
        state.requestId = null;
        ++state.historyPending;
        el('promotionEditor').classList.add('d-none');
        el('promotionHistoryPanel').classList.remove('d-none');
        el('promotionHistoryRows').replaceChildren();
        el('promotionHistoryPrevious').disabled = true;
        el('promotionHistoryNext').disabled = true;
        status('promotionSelectionStatus', 'Loading student…');
        loadHistory(1);
        try {
            const student = await api('/api/students/' + encodeURIComponent(reference));
            if (request !== state.selectionPending || reference !== state.reference) return;
            status('promotionSelectionStatus', [student.studentCode, student.fullName, student.status]
                .filter(Boolean).join(' · '));
            if (student.status !== 'Active') {
                status('promotionSelectionStatus', 'Historical student: ' + student.studentCode +
                    ' (' + student.status + '). History is available; new promotion is disabled.');
                return;
            }
            try {
                const data = await api('/api/students/' + encodeURIComponent(reference) + '/promotions/options');
                if (request !== state.selectionPending || reference !== state.reference) return;
                if (!data || !Array.isArray(data.targets)) throw Error('Promotion options unavailable.');
                state.options = data;
                state.requestId = crypto.randomUUID();
                el('promotionStudentSummary').textContent =
                    'Student: ' + (data.studentCode || '') + ' · Current enrollment #' + data.sourceEnrollmentId;
                const select = el('promotionTarget');
                select.replaceChildren();
                for (const target of data.targets) {
                    const option = document.createElement('option');
                    option.value = String(target.academicBatchId);
                    option.textContent = target.name;
                    select.append(option);
                }
                if (!data.targets.length) {
                    status('promotionSelectionStatus', 'No eligible next-year batch. Existing history is available.');
                    return;
                }
                el('promotionRoll').value = '';
                el('promotionNote').value = '';
                el('promotionEditor').classList.remove('d-none');
            } catch (error) {
                if (request === state.selectionPending)
                    status('promotionSelectionStatus', error.message || 'Promotion options unavailable.', true);
            }
        } catch (error) {
            if (request === state.selectionPending)
                status('promotionSelectionStatus', error.message || 'Student unavailable.', true);
        }
    }

    function renderHistory(data) {
        const body = el('promotionHistoryRows');
        body.replaceChildren();
        state.historyPage = data.page;
        state.historyPages = data.totalPages || 0;
        el('promotionHistoryPage').textContent =
            'Page ' + data.page + ' / ' + Math.max(1, state.historyPages) +
            ' (' + data.totalCount + ' records)';
        el('promotionHistoryPrevious').disabled = !data.hasPreviousPage;
        el('promotionHistoryNext').disabled = !data.hasNextPage;
        if (!data.items?.length) {
            const row = document.createElement('tr');
            const cell = document.createElement('td');
            cell.colSpan = 6;
            cell.textContent = 'No progression records on this page.';
            row.append(cell);
            body.append(row);
            return;
        }
        for (const item of data.items) {
            const row = document.createElement('tr');
            const placement = [item.fromAcademicYearId + '/' + item.fromAcademicLevelId +
                '/' + item.fromAcademicBatchId, item.toAcademicYearId + '/' +
                item.toAcademicLevelId + '/' + item.toAcademicBatchId].join(' → ');
            const fields = [
                item.decision === 1 ? 'Promoted' : item.decision === 2 ? 'Repeated' : 'Unknown',
                item.fromRoll, item.toRoll, placement, item.processedAt, item.note
            ];
            for (const value of fields) {
                const cell = document.createElement('td');
                cell.textContent = value == null || value === '' ? '—' : String(value);
                row.append(cell);
            }
            body.append(row);
        }
    }

    async function loadHistory(page) {
        const reference = state.reference;
        if (!reference) return;
        const request = ++state.historyPending;
        status('promotionHistoryStatus', 'Loading progression history…');
        try {
            const query = new URLSearchParams({ page: String(page), pageSize: '20' });
            const data = await api('/api/students/' + encodeURIComponent(reference) +
                '/promotions/history?' + query);
            if (request !== state.historyPending || reference !== state.reference) return;
            if (!data || !Array.isArray(data.items)) throw Error('History returned an invalid result.');
            renderHistory(data);
            status('promotionHistoryStatus', 'Rows show the original enrollment rolls and placement IDs.');
        } catch (error) {
            if (request !== state.historyPending || reference !== state.reference) return;
            el('promotionHistoryRows').replaceChildren();
            el('promotionHistoryPrevious').disabled = true;
            el('promotionHistoryNext').disabled = true;
            status('promotionHistoryStatus', error.message || 'History unavailable.', true);
        }
    }

    async function submitProgression(event) {
        event.preventDefault();
        if (state.saving || !state.options || !state.requestId || !state.reference) return;
        const selected = state.options.targets.find(x =>
            String(x.academicBatchId) === val('promotionTarget'));
        const roll = val('promotionRoll');
        if (!selected || !roll || roll.length > 50) {
            notice('Select a batch and enter a valid roll.');
            return;
        }
        const reference = state.reference;
        const request = {
            clientRequestId: state.requestId,
            sourceEnrollmentId: state.options.sourceEnrollmentId,
            targetAcademicYearId: selected.academicYearId,
            targetAcademicLevelId: selected.academicLevelId,
            targetAcademicBatchId: selected.academicBatchId,
            targetAcademicTrackId: selected.academicTrackId,
            targetRoll: roll, decision: selected.decision,
            studentRowVersion: state.options.studentRowVersion,
            sourceEnrollmentRowVersion: state.options.sourceEnrollmentRowVersion,
            note: val('promotionNote') || null
        };
        state.saving = true;
        el('promotionSubmit').disabled = true;
        try {
            const data = await api('/api/students/' + encodeURIComponent(reference) +
                '/promotions', 'POST', request);
            if (reference !== state.reference) return;
            notice(data?.alreadyProcessed
                ? 'Progression already recorded; no duplicate created.'
                : 'Student progression recorded.', true);
            el('promotionEditor').classList.add('d-none');
            state.options = null;
            state.requestId = null;
            await loadHistory(1);
            await selectStudent(reference);
        } catch (error) {
            if (reference === state.reference)
                notice(error.message || 'Progression failed. Retry with the same request.');
        } finally {
            state.saving = false;
            el('promotionSubmit').disabled = false;
        }
    }
})();
