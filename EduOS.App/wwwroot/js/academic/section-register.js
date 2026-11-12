(() => {
    'use strict';
    const root = document.getElementById('academicSectionsPage');
    if (!root) return;
    const el = id => document.getElementById(id);
    const form = el('sectionCreateForm');
    const guard = window.EduOSFormGuard?.create(form);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const identity = [document.body?.dataset?.authTenantId || '', document.body?.dataset?.authSessionReference || ''];
    const sameSession = () => identity[0] === (document.body?.dataset?.authTenantId || '') &&
        identity[1] === (document.body?.dataset?.authSessionReference || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
    const state = { page: 1, hasPrevious: false, hasNext: false, busy: false,
        requestId: null, fingerprint: null, sequence: 0, batches: [] };
    const selectedBatch = () => state.batches.find(x => String(x.id) === el('sectionBatch').value);
    const text = id => el(id).value.trim();
    function notice(message, danger = false) {
        const node = el('sectionNotice');
        node.className = 'alert ' + (danger ? 'alert-danger' : 'alert-success');
        node.textContent = message;
    }
    async function api(url, options = {}) {
        const response = await fetch(url, { cache: 'no-store', credentials: 'same-origin', ...options,
            headers: { Accept: 'application/json',
                ...(options.body ? { 'Content-Type': 'application/json' } : {}),
                ...(options.method && options.method !== 'GET' ? { RequestVerificationToken: token } : {}),
                ...(options.headers || {}) } });
        const body = await response.json().catch(() => null);
        if (!response.ok || body?.success !== true)
            throw new Error(body?.message || 'Request failed. Reload and retry.');
        return body.data;
    }
    function setBusy(on) {
        state.busy = on;
        el('sectionSave').disabled = on;
        el('sectionBatchSearchButton').disabled = on;
        el('sectionRefresh').disabled = on;
        el('sectionCancel').disabled = on;
    }
    function clearEditor() {
        if (guard?.isDirty() && !guard.confirmDiscard()) return false;
        form.reset();
        el('sectionId').value = '';
        el('sectionVersion').value = '';
        el('sectionCode').readOnly = false;
        el('sectionBatch').disabled = false;
        el('sectionSave').textContent = 'Add Section';
        state.requestId = null;
        state.fingerprint = null;
        guard?.markClean();
        return true;
    }
    async function loadBatches() {
        const query = text('sectionBatchSearch');
        const data = await api('/api/academic-setup/batches?search=' +
            encodeURIComponent(query) + '&pageSize=100&page=1');
        if (!sameSession()) return;
        state.batches = data.items || [];
        const select = el('sectionBatch'), previous = select.value;
        select.replaceChildren(new Option('Select academic batch', ''));
        state.batches.forEach(batch => select.add(new Option(
            [batch.academicYearName, batch.academicProgramName, batch.name].filter(Boolean).join(' / '),
            String(batch.id))));
        if (state.batches.some(x => String(x.id) === previous)) select.value = previous;
        if (!select.value && state.batches.length === 1) select.value = String(state.batches[0].id);
    }
    function render(rows) {
        const body = el('sectionRows');
        body.replaceChildren();
        if (!rows.length) {
            const tr = document.createElement('tr'), td = document.createElement('td');
            td.colSpan = 6; td.className = 'text-muted'; td.textContent = 'No sections match the filters.';
            tr.appendChild(td); body.appendChild(tr); return;
        }
        for (const row of rows) {
            const tr = document.createElement('tr');
            for (const value of [row.name, row.academicBatchName, row.code,
                String(row.capacity), row.isActive ? 'Active' : 'Inactive']) {
                const td = document.createElement('td'); td.textContent = value || '';
                tr.appendChild(td);
            }
            const actions = document.createElement('td');
            if (row.isLegacyDefault) {
                actions.textContent = 'Legacy · Read only';
            } else {
                const edit = document.createElement('button');
                edit.className = 'btn btn-sm btn-outline-primary me-2';
                edit.type = 'button'; edit.textContent = 'Edit';
                edit.addEventListener('click', () => beginEdit(row));
                const remove = document.createElement('button');
                remove.className = 'btn btn-sm btn-outline-danger';
                remove.type = 'button'; remove.textContent = 'Delete';
                remove.addEventListener('click', () => removeSection(row));
                actions.append(edit, remove);
            }
            tr.appendChild(actions); body.appendChild(tr);
        }
    }
    async function load() {
        const request = ++state.sequence;
        const batchId = el('sectionBatch').value, search = text('sectionFilter');
        const query = new URLSearchParams({ page: String(state.page), pageSize: '20', search });
        if (batchId) query.set('academicBatchId', batchId);
        const data = await api('/api/academic-sections?' + query);
        if (!sameSession() || request !== state.sequence) return;
        state.hasPrevious = data.hasPreviousPage;
        state.hasNext = data.hasNextPage;
        el('sectionPrevious').disabled = !state.hasPrevious;
        el('sectionNext').disabled = !state.hasNext;
        el('sectionPageStatus').textContent = 'Page ' + data.page + ' · ' + data.totalCount + ' sections';
        render(data.items || []);
    }
    async function beginEdit(row) {
        if (state.busy || !sameSession() || !clearEditor()) return;
        if (!state.batches.some(x => x.id === row.academicBatchId)) {
            el('sectionBatchSearch').value = row.academicBatchName || '';
            await loadBatches();
        }
        if (!state.batches.some(x => x.id === row.academicBatchId)) {
            notice('Find the parent batch first, then edit the section.', true); return;
        }
        el('sectionBatch').value = String(row.academicBatchId);
        el('sectionBatch').disabled = true;
        el('sectionId').value = String(row.id);
        el('sectionVersion').value = row.rowVersion;
        el('sectionName').value = row.name;
        el('sectionCode').value = row.code;
        el('sectionCode').readOnly = true;
        el('sectionCapacity').value = String(row.capacity);
        el('sectionOrder').value = String(row.displayOrder);
        el('sectionActive').value = String(row.isActive);
        el('sectionSave').textContent = 'Save Section';
        guard?.markClean();
        form.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
    async function removeSection(row) {
        if (!sameSession() || state.busy || !confirm('Delete unused section ' + row.name + '?')) return;
        setBusy(true);
        try {
            await api('/api/academic-sections/' + encodeURIComponent(String(row.id)), {
                method: 'DELETE', body: JSON.stringify({ rowVersion: row.rowVersion })
            });
            if (sameSession()) { notice('Section removed.'); await load(); }
        } catch (error) { if (sameSession()) notice(error.message, true); }
        finally { setBusy(false); }
    }
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (state.busy || !sameSession() || !form.reportValidity()) return;
        const batch = selectedBatch();
        if (!batch) { notice('Select an academic batch first.', true); return; }
        const id = text('sectionId'), capacity = Number(text('sectionCapacity'));
        const order = Number(text('sectionOrder'));
        if (!Number.isInteger(capacity) || capacity < 0 || capacity > batch.capacity ||
            !Number.isInteger(order) || order < 0) {
            notice('Capacity must be within the parent batch capacity and order must be non-negative.', true);
            return;
        }
        const payload = id
            ? { name: text('sectionName'), capacity, displayOrder: order,
                isActive: el('sectionActive').value === 'true', rowVersion: text('sectionVersion') }
            : { academicBatchId: batch.id, name: text('sectionName'), code: text('sectionCode'),
                capacity, displayOrder: order };
        if (!id) {
            const fingerprint = JSON.stringify(payload);
            if (fingerprint !== state.fingerprint) {
                state.fingerprint = fingerprint;
                state.requestId = window.crypto?.randomUUID?.() || '';
            }
            if (!state.requestId) { notice('Secure request ID unavailable. Reload over HTTPS.', true); return; }
            payload.clientRequestId = state.requestId;
        }
        setBusy(true);
        let committed = false;
        try {
            await api(id ? '/api/academic-sections/' + encodeURIComponent(id) : '/api/academic-sections', {
                method: id ? 'PUT' : 'POST', body: JSON.stringify(payload)
            });
            committed = true;
            if (!sameSession()) return;
            guard?.markClean();
            clearEditor();
            notice('Section saved successfully.');
            await load();
        } catch (error) {
            if (sameSession()) notice(committed ? 'Saved, but reload failed. Refresh before editing.' : error.message, true);
        } finally { setBusy(false); }
    });
    el('sectionCancel').addEventListener('click', () => { if (!state.busy) clearEditor(); });
    el('sectionRefresh').addEventListener('click', async () => {
        if (state.busy || !sameSession() || !clearEditor()) return;
        try { await loadBatches(); await load(); }
        catch (e) { notice(e.message, true); }
    });
    el('sectionBatchLookup').addEventListener('submit', async event => {
        event.preventDefault();
        if (state.busy || !sameSession()) return;
        if (guard?.isDirty() && !guard.confirmDiscard()) return;
        try { await loadBatches(); state.page = 1; await load(); }
        catch (e) { notice(e.message, true); }
    });
    el('sectionBatch').addEventListener('change', () => { state.page = 1; load().catch(e => notice(e.message, true)); });
    el('sectionFilterButton').addEventListener('click', () => {
        state.page = 1; load().catch(e => notice(e.message, true));
    });
    el('sectionPrevious').addEventListener('click', () => {
        if (!state.busy && state.hasPrevious) { state.page--; load().catch(e => notice(e.message, true)); }
    });
    el('sectionNext').addEventListener('click', () => {
        if (!state.busy && state.hasNext) { state.page++; load().catch(e => notice(e.message, true)); }
    });
    loadBatches().then(load).catch(e => notice(e.message, true));
})();
