(() => {
    'use strict';
    const root = document.getElementById('learnerConsents');
    if (!root) return;

    const pending = document.getElementById('learnerConsentPending');
    const grants = document.getElementById('learnerConsentGrants');
    const history = document.getElementById('learnerConsentHistory');
    const historyPrevious = document.getElementById('learnerConsentHistoryPrevious');
    const historyNext = document.getElementById('learnerConsentHistoryNext');
    const historyPageLabel = document.getElementById('learnerConsentHistoryPage');
    const consentStates = { 1: 'Pending', 2: 'Approved', 3: 'Denied', 4: 'Revoked', 5: 'Expired' };
    let historyPage = 1, historyHasNext = false;
    const alertBox = document.getElementById('learnerConsentAlert');
    const refresh = document.getElementById('learnerConsentRefresh');
    const antiforgery = root.querySelector('input[name="__RequestVerificationToken"]');
    const purposeNames = { 1: 'Admission', 2: 'Transfer', 3: 'Identity verification', 4: 'Continuing education' };
    const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    let busy = false, generation = 0;

    function message(text, type = 'danger') {
        alertBox.className = 'alert alert-' + type + ' mt-2';
        alertBox.textContent = text;
    }
    function clearMessage() {
        alertBox.className = 'd-none';
        alertBox.textContent = '';
    }
    function setBusy(value) {
        busy = value;
        refresh.disabled = value;
        historyPrevious.disabled = value || historyPage <= 1;
        historyNext.disabled = value || !historyHasNext;
        root.querySelectorAll('button[data-consent-action]').forEach(button => { button.disabled = value; });
    }
    async function api(url, options = {}) {
        const response = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store', referrerPolicy: 'no-referrer',
            ...options,
            headers: { Accept: 'application/json', ...(options.headers || {}) }
        });
        const body = await response.json().catch(() => null);
        if (!response.ok || !body?.success) {
            const error = new Error(body?.message || 'The consent request could not be processed.');
            error.status = response.status;
            throw error;
        }
        return body;
    }
    function scopeNames(value) {
        const numeric = Number(value);
        if (!Number.isFinite(numeric)) return String(value || 'Not specified');
        const names = [];
        if (numeric & 1) names.push('Basic identity');
        if (numeric & 2) names.push('Institution membership history');
        if (numeric & 4) names.push('Academic summary');
        return names.join(', ') || 'Not specified';
    }
    function date(value) {
        if (!value) return 'Not specified';
        const parsed = new Date(value);
        return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
    }
    function cell(row, value) {
        const td = document.createElement('td');
        td.textContent = value == null ? '—' : String(value);
        row.append(td);
        return td;
    }
    function action(label, kind, reference, decision) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-sm ' + (kind === 'approve' ? 'btn-success' : 'btn-outline-danger');
        button.textContent = label;
        button.dataset.consentAction = kind;
        button.addEventListener('click', () => mutate(kind, reference, decision));
        return button;
    }
    function table(container, records, headings, mapper, emptyMessage) {
        container.replaceChildren();
        if (!Array.isArray(records) || !records.length) {
            const p = document.createElement('p');
            p.className = 'text-muted mb-0';
            p.textContent = emptyMessage;
            container.append(p);
            return;
        }
        const wrapper = document.createElement('div');
        wrapper.className = 'table-responsive';
        const grid = document.createElement('table');
        grid.className = 'table table-sm table-striped align-middle';
        const thead = document.createElement('thead');
        const headRow = document.createElement('tr');
        headings.forEach(label => {
            const th = document.createElement('th');
            th.scope = 'col';
            th.textContent = label;
            headRow.append(th);
        });
        thead.append(headRow);
        const tbody = document.createElement('tbody');
        records.forEach(record => tbody.append(mapper(record)));
        grid.append(thead, tbody);
        wrapper.append(grid);
        container.append(wrapper);
    }
    function renderPending(records) {
        table(pending, records,
            ['Requesting institution', 'Purpose', 'Data requested', 'Expires', 'Decision'],
            record => {
                const row = document.createElement('tr');
                cell(row, record.requestingInstitution);
                cell(row, purposeNames[record.purpose] || record.purpose);
                cell(row, scopeNames(record.requestedScopes));
                cell(row, date(record.expiresAt));
                const td = cell(row, '');
                if (guid.test(record.reference || '')) {
                    const controls = document.createElement('div');
                    controls.className = 'd-flex flex-wrap gap-2';
                    controls.append(
                        action('Approve', 'approve', record.reference, 1),
                        action('Deny', 'deny', record.reference, 2));
                    td.append(controls);
                }
                return row;
            }, 'No pending data-sharing requests.');
    }
    function renderGrants(records) {
        table(grants, records,
            ['Institution', 'Purpose', 'Data shared', 'Granted', 'Expires', 'Action'],
            record => {
                const row = document.createElement('tr');
                cell(row, record.institution);
                cell(row, purposeNames[record.purpose] || record.purpose);
                cell(row, scopeNames(record.grantedScopes));
                cell(row, date(record.startsAt));
                cell(row, date(record.expiresAt));
                const td = cell(row, '');
                if (guid.test(record.reference || ''))
                    td.append(action('Revoke access', 'revoke', record.reference));
                return row;
            }, 'No active data-sharing grants.');
    }
    function renderHistory(result) {
        const records = Array.isArray(result?.items) ? result.items : [];
        table(history, records,
            ['Institution', 'Purpose', 'Data requested', 'State', 'Requested', 'Resolved'],
            record => {
                const row = document.createElement('tr');
                cell(row, record.requestingInstitution);
                cell(row, purposeNames[record.purpose] || record.purpose);
                cell(row, scopeNames(record.requestedScopes));
                cell(row, consentStates[record.state] || 'Unknown');
                cell(row, date(record.requestedAt));
                cell(row, date(record.resolvedAt));
                return row;
            }, 'No consent decisions or requests to display.');
        historyPage = result?.page || 1;
        historyHasNext = !!result?.hasNextPage;
        historyPrevious.disabled = busy || historyPage <= 1;
        historyNext.disabled = busy || !historyHasNext;
        historyPageLabel.textContent = 'Page ' + historyPage + ' of ' + (result?.totalPages || 1)
            + ' · ' + (result?.totalCount || 0) + ' request(s)';
    }
    async function load(clear = true) {
        if (busy) return;
        const current = ++generation;
        if (clear) clearMessage();
        refresh.disabled = true;
        try {
            const [requests, active, past] = await Promise.all([
                api('/api/learner-consents/pending'),
                api('/api/learner-consents/grants'),
                api('/api/learner-consents/history?page=' + historyPage + '&pageSize=20')
            ]);
            if (current !== generation) return;
            renderPending(requests.data || []);
            renderGrants(active.data || []);
            renderHistory(past.data);
        } catch (error) {
            if (current !== generation) return;
            pending.replaceChildren();
            grants.replaceChildren();
            history.replaceChildren();
            historyPrevious.disabled = true;
            historyNext.disabled = true;
            historyPageLabel.textContent = '';
            message(error.message);
        } finally {
            if (current === generation && !busy) refresh.disabled = false;
        }
    }
    async function mutate(kind, reference, decision) {
        if (busy || !guid.test(reference || '')) return;
        const confirmation = kind === 'approve'
            ? 'Approve sharing the listed learner data with this institution?'
            : kind === 'deny'
                ? 'Deny this data-sharing request?'
                : 'Revoke this institution’s access to the learner data?';
        if (!window.confirm(confirmation)) return;
        if (!antiforgery?.value) {
            message('Security token is missing. Reload this page and try again.');
            return;
        }
        setBusy(true);
        try {
            const url = kind === 'revoke'
                ? '/api/learner-consents/grants/' + encodeURIComponent(reference) + '/revoke'
                : '/api/learner-consents/' + encodeURIComponent(reference) + '/decision';
            const options = {
                method: 'POST',
                headers: { 'RequestVerificationToken': antiforgery.value }
            };
            if (kind !== 'revoke') {
                options.headers['Content-Type'] = 'application/json';
                options.body = JSON.stringify({ decision });
            }
            const result = await api(url, options);
            message(result.message || 'Your choice has been saved.', 'success');
        } catch (error) {
            message(error.message + (error.status === 409 ? ' Refresh to see the latest state.' : ''));
        } finally {
            setBusy(false);
            await load(false);
        }
    }
    historyPrevious.addEventListener('click', () => {
        if (busy || historyPage <= 1) return;
        historyPage--;
        load(false);
    });
    historyNext.addEventListener('click', () => {
        if (busy || !historyHasNext || historyPage >= 500) return;
        historyPage++;
        load(false);
    });
    refresh.addEventListener('click', () => load());
    load();
})();
