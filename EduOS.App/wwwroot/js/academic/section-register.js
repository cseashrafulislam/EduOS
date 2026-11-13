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
    const state = { campuses: [], levels: [], programs: [], page: 1, busy: false,
        generation: 0, requestId: null, requestBody: null, initialized: false };
    const text = id => el(id).value.trim();
    const num = id => Number(text(id)) || 0;
    const selectedCampus = () => state.campuses.find(x => x.id === num('sectionCampus'));
    const selectedLevel = () => state.levels.find(x => x.id === num('sectionLevel'));
    const notice = (message, failure = false) => {
        const box = el('sectionNotice');
        box.className = 'alert ' + (failure ? 'alert-danger' : 'alert-success');
        box.textContent = message;
    };
    async function api(url, options = {}) {
        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', ...options,
            headers: { Accept: 'application/json',
                ...(options.body ? { 'Content-Type': 'application/json' } : {}),
                ...(options.method && options.method !== 'GET' ? { RequestVerificationToken: token } : {}),
                ...(options.headers || {}) } });
        const body = await response.json().catch(() => null);
        if (!response.ok || body?.success !== true) throw new Error(body?.message || 'Request failed.');
        return body.data;
    }
    const menu = (id, values, label, name) => {
        const select = el(id), previous = select.value;
        select.replaceChildren(new Option(label, ''));
        values.forEach(x => select.add(new Option(name(x), String(x.id))));
        if (values.some(x => String(x.id) === previous)) select.value = previous;
        else if (values.length === 1) select.value = String(values[0].id);
    };
    const levelName = level => {
        const program = state.programs.find(x => x.id === level.academicProgramId);
        return (program ? program.name + ' / ' : '') + level.name;
    };
    const setBusy = busy => {
        state.busy = busy;
        for (const id of ['sectionSave', 'sectionCancel', 'sectionRefresh', 'sectionFilterButton'])
            el(id).disabled = busy;
    };
    function clearEditor(ask = true) {
        if (ask && guard?.isDirty() && !guard.confirmDiscard()) return false;
        const campusId = el('sectionCampus').value, levelId = el('sectionLevel').value;
        form.reset();
        el('sectionId').value = '';
        el('sectionVersion').value = '';
        el('sectionCampus').disabled = false;
        el('sectionLevel').disabled = false;
        el('sectionCampus').value = campusId;
        el('sectionLevel').value = levelId;
        el('sectionCode').readOnly = false;
        el('sectionSave').textContent = 'Add Section';
        el('sectionAdvanced').open = false;
        state.requestId = null;
        state.requestBody = null;
        guard?.markClean();
        return true;
    }
    async function loadOptions() {
        const [campuses, catalog] = await Promise.all([
            api('/api/institution-onboarding/campus-list'),
            api('/api/academic-setup/catalog?take=100')
        ]);
        if (!sameSession()) return;
        state.campuses = (campuses || []).filter(x => x.isActive !== false);
        state.levels = (catalog?.levels || []).filter(x => x.isActive !== false);
        state.programs = (catalog?.programs || []).filter(x => x.isActive !== false);
        menu('sectionCampus', state.campuses, 'Select campus', x => x.name);
        menu('sectionLevel', state.levels, 'Select class / grade', levelName);
        if (!state.initialized) {
            state.initialized = true;
            const fromBatch = new URLSearchParams(location.search).get('academicBatchId');
            if (fromBatch && /^\d+$/.test(fromBatch)) {
                try {
                    const batch = await api('/api/academic-setup/batches/' + encodeURIComponent(fromBatch));
                    if (!sameSession()) return;
                    if (state.campuses.some(x => x.id === batch.campusId))
                        el('sectionCampus').value = String(batch.campusId);
                    if (state.levels.some(x => x.id === batch.academicLevelId))
                        el('sectionLevel').value = String(batch.academicLevelId);
                } catch {
                    notice('Linked cohort is unavailable; choose a campus and class.', true);
                }
            }
        }
        guard?.markClean();
    }
    function render(rows) {
        const body = el('sectionRows');
        body.replaceChildren();
        if (!rows.length) {
            const tr = document.createElement('tr'), td = document.createElement('td');
            td.colSpan = 7; td.className = 'text-muted'; td.textContent = 'No sections match these filters.';
            tr.append(td); body.append(tr); return;
        }
        for (const item of rows) {
            const tr = document.createElement('tr');
            const campus = state.campuses.find(x => x.id === item.campusId);
            const level = state.levels.find(x => x.id === item.academicLevelId);
            for (const value of [item.name, campus?.name || item.campusName || '',
                level ? levelName(level) : item.academicLevelName || '', item.code,
                String(item.capacity), item.isActive ? 'Active' : 'Inactive']) {
                const td = document.createElement('td');
                td.textContent = value;
                tr.append(td);
            }
            const actions = document.createElement('td');
            const edit = document.createElement('button');
            edit.type = 'button'; edit.className = 'btn btn-sm btn-outline-primary me-2';
            edit.textContent = 'Edit';
            edit.addEventListener('click', () => beginEdit(item));
            const remove = document.createElement('button');
            remove.type = 'button'; remove.className = 'btn btn-sm btn-outline-danger';
            remove.textContent = 'Delete';
            remove.addEventListener('click', () => removeSection(item));
            actions.append(edit, remove);
            tr.append(actions); body.append(tr);
        }
    }
    async function load() {
        const sequence = ++state.generation;
        const params = new URLSearchParams({ page: String(state.page), pageSize: '20',
            search: text('sectionFilter') });
        if (num('sectionCampus')) params.set('campusId', String(num('sectionCampus')));
        if (num('sectionLevel')) params.set('academicLevelId', String(num('sectionLevel')));
        const data = await api('/api/academic-sections?' + params);
        if (!sameSession() || sequence !== state.generation) return;
        const total = data.totalCount || 0;
        el('sectionPrevious').disabled = state.page <= 1;
        el('sectionNext').disabled = state.page * 20 >= total;
        el('sectionPageStatus').textContent = 'Page ' + state.page + ' · ' + total + ' sections';
        render(data.items || []);
    }
    function beginEdit(item) {
        if (state.busy || !sameSession() || !clearEditor()) return;
        if (!state.campuses.some(x => x.id === item.campusId) ||
            !state.levels.some(x => x.id === item.academicLevelId)) {
            notice('Class or campus not in the current catalog. Refresh the setup directory.', true);
            return;
        }
        el('sectionCampus').value = String(item.campusId);
        el('sectionLevel').value = String(item.academicLevelId);
        el('sectionCampus').disabled = true;
        el('sectionLevel').disabled = true;
        el('sectionId').value = String(item.id);
        el('sectionVersion').value = item.rowVersion;
        el('sectionName').value = item.name;
        el('sectionCode').value = item.code;
        el('sectionCode').readOnly = true;
        el('sectionCapacity').value = String(item.capacity);
        el('sectionOrder').value = String(item.displayOrder);
        el('sectionActive').value = String(item.isActive);
        el('sectionSave').textContent = 'Save Section';
        el('sectionAdvanced').open = true;
        guard?.markClean();
        form.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
    async function removeSection(item) {
        if (state.busy || !sameSession() || !confirm('Delete unused section ' + item.name + '?')) return;
        setBusy(true);
        try {
            await api('/api/academic-sections/' + encodeURIComponent(item.id), {
                method: 'DELETE', body: JSON.stringify({ rowVersion: item.rowVersion })
            });
            if (sameSession()) { notice('Section removed.'); await load(); }
        } catch (error) { if (sameSession()) notice(error.message, true); }
        finally { setBusy(false); }
    }
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (state.busy || !sameSession() || !form.reportValidity()) return;
        const editing = text('sectionId');
        const capacity = Number(text('sectionCapacity')), displayOrder = Number(text('sectionOrder'));
        if (!Number.isInteger(capacity) || capacity < 0 || capacity > 100000 ||
            !Number.isInteger(displayOrder) || displayOrder < 0)
            return notice('Invalid section capacity or display order.', true);
        if (!editing && (!selectedCampus() || !selectedLevel()))
            return notice('Select a valid campus and class / grade.', true);
        const data = editing
            ? { name: text('sectionName'), capacity, displayOrder,
                isActive: text('sectionActive') === 'true', rowVersion: text('sectionVersion') }
            : { campusId: num('sectionCampus'), academicLevelId: num('sectionLevel'),
                name: text('sectionName'), code: text('sectionCode'),
                capacity, displayOrder, isActive: text('sectionActive') === 'true' };
        if (!editing) {
            const fingerprint = JSON.stringify(data);
            if (state.requestBody !== fingerprint) {
                state.requestId = window.crypto?.randomUUID?.() || '';
                state.requestBody = fingerprint;
            }
            if (!state.requestId) return notice('Secure request ID unavailable. Reload using HTTPS.', true);
            data.clientRequestId = state.requestId;
        }
        setBusy(true);
        let saved = false;
        try {
            await api(editing ? '/api/academic-sections/' + encodeURIComponent(editing) :
                '/api/academic-sections', { method: editing ? 'PUT' : 'POST', body: JSON.stringify(data) });
            saved = true;
            if (!sameSession()) return;
            clearEditor(false);
            notice('Academic Section saved.');
            await load();
        } catch (error) {
            if (sameSession()) notice(saved ? 'Saved, but register refresh failed.' : error.message, true);
        } finally { setBusy(false); }
    });
    el('sectionCancel').addEventListener('click', () => { if (!state.busy) clearEditor(); });
    for (const id of ['sectionCampus', 'sectionLevel']) el(id).addEventListener('change', () => {
        if (text('sectionId')) return;
        state.page = 1; load().catch(error => notice(error.message, true));
    });
    el('sectionFilterButton').addEventListener('click', () => {
        state.page = 1; load().catch(error => notice(error.message, true));
    });
    el('sectionRefresh').addEventListener('click', async () => {
        if (state.busy || !clearEditor()) return;
        try { await loadOptions(); await load(); }
        catch (error) { notice(error.message, true); }
    });
    el('sectionPrevious').addEventListener('click', () => {
        if (state.page > 1 && !state.busy) {
            state.page--; load().catch(error => notice(error.message, true));
        }
    });
    el('sectionNext').addEventListener('click', () => {
        if (el('sectionNext').disabled || state.busy) return;
        state.page++; load().catch(error => notice(error.message, true));
    });
    loadOptions().then(load).catch(error => notice(error.message, true));
})();
