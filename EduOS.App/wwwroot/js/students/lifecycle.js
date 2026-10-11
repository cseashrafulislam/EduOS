(() => {
    'use strict';
    const root = document.getElementById('studentLifecycle');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { selected: null, requestId: null, saving: false };
    const value = id => el(id).value.trim();
    function alert(message, error = false) {
        const box = el('lifecycleAlert');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = message;
        box.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
    async function api(url, options = {}) {
        const response = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store', ...options,
            headers: {
                Accept: 'application/json',
                ...(options.body ? { 'Content-Type': 'application/json' } : {}),
                ...(options.method && options.method !== 'GET' ? { RequestVerificationToken: token } : {}),
                ...(options.headers || {})
            }
        });
        const json = await response.json().catch(() => null);
        if (!response.ok || json?.success !== true)
            throw new Error(json?.message || 'Request failed. Reload and retry.');
        return json.data;
    }
    function requestId() {
        if (!state.requestId) {
            if (typeof crypto.randomUUID === 'function') state.requestId = crypto.randomUUID();
            else {
                const bytes = new Uint8Array(16);
                crypto.getRandomValues(bytes);
                bytes[6] = bytes[6] & 15 | 64; bytes[8] = bytes[8] & 63 | 128;
                const h = [...bytes].map(x => x.toString(16).padStart(2, '0')).join('');
                state.requestId = [h.slice(0, 8), h.slice(8, 12), h.slice(12, 16),
                    h.slice(16, 20), h.slice(20)].join('-');
            }
        }
        return state.requestId;
    }
    function canProcess() {
        return !state.saving && state.selected?.status === 'Active' &&
            Boolean(state.selected.rowVersion) && Boolean(value('lifecycleReason')) &&
            Boolean(value('lifecycleExitType'));
    }
    function refresh() { el('lifecycleSubmit').disabled = !canProcess(); }
    async function search(event) {
        event?.preventDefault();
        const query = value('lifecycleSearchText');
        if (query.length < 2) return alert('Enter at least two characters to search.', true);
        const box = el('lifecycleMatches');
        box.textContent = 'Searching...';
        try {
            const page = await api('/api/students?' + new URLSearchParams({ search: query, page: 1, pageSize: 20 }));
            box.replaceChildren();
            if (!page?.items?.length) { box.textContent = 'No matching students.'; return; }
            for (const item of page.items) {
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'list-group-item list-group-item-action';
                button.textContent = item.fullName + ' — ' + item.studentCode + ' (' + item.status + ')';
                button.addEventListener('click', () => selectStudent(item.reference));
                box.append(button);
            }
        } catch (e) { box.textContent = ''; alert(e.message, true); }
    }
    async function selectStudent(reference) {
        state.selected = null; state.requestId = null; refresh();
        try {
            const details = await api('/api/students/' + encodeURIComponent(reference));
            const history = await api('/api/students/exit/' + encodeURIComponent(reference) + '/history');
            if (details.reference !== reference) throw new Error('Unexpected student reference returned.');
            state.selected = details;
            el('lifecycleStudent').classList.remove('d-none');
            el('lifecycleName').textContent = details.fullName;
            el('lifecycleCode').textContent = details.studentCode;
            el('lifecycleStatus').textContent = details.status;
            el('lifecycleCurrent').textContent = (details.enrollments || []).length +
                ' enrollment history rows (up to 100).';
            renderHistory(history || []);
            refresh();
        } catch (e) { alert(e.message, true); }
    }
    function renderHistory(items) {
        const body = el('lifecycleHistory');
        body.replaceChildren();
        if (!items.length) {
            const row = document.createElement('tr'), cell = document.createElement('td');
            cell.colSpan = 5; cell.textContent = 'No final exit recorded.';
            row.append(cell); body.append(row); return;
        }
        for (const item of items) {
            const row = document.createElement('tr');
            for (const text of [
                item.processedAtUtc, item.exitType, item.finalStatus,
                item.certificateNo || '—', String(item.dueAtExit ?? 0)
            ]) {
                const cell = document.createElement('td');
                cell.textContent = text ?? ''; row.append(cell);
            }
            body.append(row);
        }
    }
    async function submit(event) {
        event.preventDefault();
        if (!canProcess()) return alert('Select an active student and provide a reason.', true);
        const chosen = state.selected;
        const reason = value('lifecycleReason'), conductRemark = value('lifecycleConduct') || null;
        const exitType = value('lifecycleExitType');
        if (!confirm('Finalize ' + exitType + ' for ' + chosen.studentCode +
            '? This closes the current enrollment and cannot be silently undone.')) return;
        const payload = {
            clientRequestId: requestId(), studentReference: chosen.reference,
            exitType, reason, conductRemark, studentRowVersion: chosen.rowVersion
        };
        state.saving = true; refresh();
        try {
            const result = await api('/api/students/exit', { method: 'POST', body: JSON.stringify(payload) });
            state.requestId = null;
            await selectStudent(chosen.reference);
            alert('Student exit completed.' + (result.certificateNo ? ' Transfer certificate: ' + result.certificateNo : ''));
        } catch (e) { alert(e.message, true); }
        finally { state.saving = false; refresh(); }
    }
    el('lifecycleStudentSearch').addEventListener('submit', search);
    el('lifecycleExitForm').addEventListener('submit', submit);
    for (const id of ['lifecycleExitType', 'lifecycleReason', 'lifecycleConduct']) {
        el(id).addEventListener('input', () => { state.requestId = null; refresh(); });
        el(id).addEventListener('change', () => { state.requestId = null; refresh(); });
    }
    const preselected = new URLSearchParams(location.search).get('studentReference');
    if (preselected && /^[0-9a-f-]{36}$/i.test(preselected)) selectStudent(preselected);
})();
