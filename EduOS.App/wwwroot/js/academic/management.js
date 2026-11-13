(() => {
    'use strict';
    const root = document.getElementById('academicManagement');
    if (!root) return;
    const el = id => document.getElementById(id);
    const state = { years: [], terms: [], campuses: [], catalog: null };
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const identity = [document.body?.dataset?.authTenantId || '',
        document.body?.dataset?.authSessionReference || ''];
    const sameSession = () => identity[0] === (document.body?.dataset?.authTenantId || '') &&
        identity[1] === (document.body?.dataset?.authSessionReference || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
    const formGuards = new Map();
    const guard = id => {
        if (!formGuards.has(id)) formGuards.set(id, window.EduOSFormGuard?.create(el(id)) || null);
        return formGuards.get(id);
    };
    const mayReplace = id => !guard(id)?.isDirty() || guard(id).confirmDiscard();
    let allocationLookup = 0, allocationSaving = false;
    const field = id => el(id).value.trim();
    const idOf = id => Number(field(id)) || null;
    const checked = id => el(id).checked;
    const node = (tag, text, className = '') => {
        const n = document.createElement(tag);
        n.textContent = String(text ?? '');
        if (className) n.className = className;
        return n;
    };
    function notice(message, danger = false) {
        const output = el('academicNotice');
        output.className = 'alert ' + (danger ? 'alert-danger' : 'alert-success');
        output.textContent = message;
        output.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
    async function api(path, options = {}) {
        const response = await fetch(path, {
            credentials: 'same-origin', cache: 'no-store', ...options,
            headers: {
                Accept: 'application/json',
                ...(options.body ? { 'Content-Type': 'application/json' } : {}),
                ...(options.method && options.method !== 'GET' ? { RequestVerificationToken: token } : {}),
                ...(options.headers || {})
            }
        });
        const body = await response.json().catch(() => null);
        if (!response.ok || body?.success !== true)
            throw new Error(body?.message || 'Request failed. Reload the page.');
        return body.data;
    }
    function options(id, values, placeholder, chosen, optional = false) {
        const select = el(id);
        if (select.multiple) {
            const selected = chosen === undefined
                ? new Set([...select.selectedOptions].map(option => option.value))
                : new Set((Array.isArray(chosen) ? chosen : [chosen]).map(String));
            select.replaceChildren();
            values.forEach(v => {
                const option = new Option(v.name + (v.code ? ' (' + v.code + ')' : ''), String(v.id));
                option.selected = selected.has(String(v.id));
                select.add(option);
            });
            return;
        }
        const selected = chosen === undefined ? select.value : chosen;
        select.replaceChildren(new Option(placeholder, ''));
        values.forEach(v => select.add(new Option(v.name + (v.code ? ' (' + v.code + ')' : ''), String(v.id))));
        if ([...select.options].some(v => v.value === String(selected))) select.value = String(selected);
        if (!optional && !select.value && values.length === 1) select.value = String(values[0].id);
    }
    const enabled = values => (values || []).filter(v => v.isActive !== false);
    const find = (key, id) => state.catalog?.[key]?.find(v => v.id === Number(id));
    const findYear = id => state.years.find(v => v.id === Number(id));
    const findCampus = id => state.campuses.find(v => v.id === Number(id));
    function levelsForSection() {
        const programId = idOf('sectionProgram');
        options('sectionLevel', enabled(state.catalog?.levels).filter(v =>
            !programId || v.academicProgramId === programId), 'Select class / grade');
    }
    function termsForSection() {
        const yearId = idOf('sectionYear');
        options('sectionTerm', enabled(state.terms).filter(v =>
            !yearId || v.academicYearId === yearId), 'No term', undefined, true);
    }
    function levelsForCurriculum() {
        const curriculum = find('curricula', idOf('curriculumSelection'));
        options('mappingLevel', enabled(state.catalog?.levels).filter(v =>
            curriculum && v.academicProgramId === curriculum.academicProgramId), 'Select class');
    }
    function selectLists() {
        options('yearCampus', enabled(state.campuses), 'All campuses', undefined, true);
        options('programCampus', enabled(state.campuses), 'No campus restriction', undefined, true);
        options('termYear', enabled(state.years), 'Select year');
        options('levelProgram', enabled(state.catalog.programs), 'Select programme');
        options('sectionCampus', enabled(state.campuses), 'Select campus');
        options('sectionYear', enabled(state.years), 'Select year');
        options('sectionProgram', enabled(state.catalog.programs), 'Select programme');
        options('curriculumProgram', enabled(state.catalog.programs), 'Select programme');
        options('curriculumSelection', enabled(state.catalog.curricula), 'Select curriculum');
        options('mappingSubject', enabled(state.catalog.subjects), 'Select subject');
        levelsForSection(); termsForSection(); levelsForCurriculum();
    }
    function table(id, items, columns, actions) {
        const target = el(id);
        target.replaceChildren();
        if (!items.length) {
            const tr = node('tr'), td = node('td', 'No records found.');
            td.colSpan = columns.length + (actions ? 1 : 0);
            tr.append(td); target.append(tr);
        }
        items.forEach(item => {
            const tr = node('tr');
            columns.forEach(fn => tr.append(node('td', fn(item))));
            if (actions) {
                const td = node('td');
                actions(item, td);
                tr.append(td);
            }
            target.append(tr);
        });
    }
    function button(text, fn, danger = false) {
        const control = node('button', text, 'btn btn-sm me-2 ' +
            (danger ? 'btn-outline-danger' : 'btn-outline-primary'));
        control.type = 'button'; control.addEventListener('click', () => fn(control));
        return control;
    }
    async function archive(kind, row, control) {
        if (!sameSession()) return;
        if (!confirm('Archive ' + row.name + '? Linked academic records may block removal.')) return;
        control.disabled = true;
        let committed = false;
        try {
            await api('/api/institution-onboarding/' + kind + '/' + encodeURIComponent(row.id) +
                '?rowVersion=' + encodeURIComponent(row.rowVersion), { method: 'DELETE' });
            committed = true;
            if (!sameSession()) return;
            try { await reload(); notice('Record archived.'); }
            catch { notice('Record archived, but the directory could not refresh. Reload the page.', true); }
        } catch (e) { if (sameSession()) notice(committed ? 'Record archived, but the display failed.' : e.message, true); }
        finally { control.disabled = false; }
    }
    function editYear(row) {
        if (!sameSession() || !mayReplace('yearForm')) return;
        el('yearForm').reset();
        for (const [id, value] of Object.entries({
            yearId: row.id, yearVersion: row.rowVersion, yearName: row.name, yearCode: row.code,
            yearStart: row.startDate, yearEnd: row.endDate, yearCycle: row.cycleType
        })) el(id).value = value ?? '';
        options('yearCampus', enabled(state.campuses), 'All campuses', row.campusId, true);
        el('yearCurrent').checked = row.isCurrent;
        guard('yearForm')?.markClean();
        el('yearForm').scrollIntoView({ behavior: 'smooth' });
    }
    function editTerm(row) {
        if (!sameSession() || !mayReplace('termForm')) return;
        el('termForm').reset();
        for (const [id, value] of Object.entries({
            termId: row.id, termVersion: row.rowVersion, termName: row.name, termCode: row.code,
            termStart: row.startDate, termEnd: row.endDate
        })) el(id).value = value ?? '';
        options('termYear', enabled(state.years), 'Select year', row.academicYearId);
        el('termCurrent').checked = row.isCurrent;
        guard('termForm')?.markClean();
        el('termForm').scrollIntoView({ behavior: 'smooth' });
    }

    function syncAllocationPrimary() {
        const unrestricted = el('allocationAllowAll').checked;
        const campusSelect = el('allocationCampuses');
        campusSelect.disabled = unrestricted;
        const selected = unrestricted ? [] : [...campusSelect.selectedOptions].map(x => Number(x.value));
        const primary = el('allocationPrimary'), previous = primary.value;
        primary.replaceChildren(new Option(unrestricted ? 'Not applicable' : 'Select primary campus', ''));
        selected.forEach(id => {
            const campus = findCampus(id);
            primary.add(new Option(campus?.name || ('Campus #' + id), String(id)));
        });
        if (selected.some(id => String(id) === previous)) primary.value = previous;
        else if (selected.length) primary.value = String(selected[0]);
        primary.disabled = unrestricted;
    }
    async function editProgramAllocation(row) {
        if (!sameSession() || !mayReplace('programAllocationForm')) return;
        const sequence = ++allocationLookup;
        const allocation = await api('/api/academic-setup/programmes/' +
            encodeURIComponent(row.id) + '/campuses');
        if (!sameSession() || sequence !== allocationLookup || guard('programAllocationForm')?.isDirty()) return;
        el('allocationProgramId').value = allocation.academicProgramId;
        el('allocationRowVersion').value = allocation.rowVersion;
        el('allocationProgramName').textContent = allocation.programName + ' (' + allocation.programCode + ')';
        const selected = new Set(allocation.campusIds || []);
        const campuses = el('allocationCampuses');
        campuses.replaceChildren();
        enabled(state.campuses).forEach(campus => {
            const option = new Option(campus.name, String(campus.id));
            option.selected = selected.has(campus.id);
            campuses.add(option);
        });
        el('allocationAllowAll').checked = allocation.allowAllCampuses;
        syncAllocationPrimary();
        if (allocation.primaryCampusId)
            el('allocationPrimary').value = String(allocation.primaryCampusId);
        el('programAllocationForm').classList.remove('d-none');
        guard('programAllocationForm')?.markClean();
        el('programAllocationForm').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
    function render() {
        table('yearRows', state.years, [
            x => x.name, x => x.code, x => findCampus(x.campusId)?.name || 'All campuses',
            x => x.startDate + ' – ' + x.endDate, x => x.isCurrent ? 'Yes' : 'No'
        ], (x, td) => td.append(button('Edit', () => editYear(x)),
            button('Archive', b => archive('academic-year', x, b), true)));
        table('termRows', state.terms, [
            x => x.name, x => findYear(x.academicYearId)?.name || '',
            x => x.code || '—', x => x.startDate + ' – ' + x.endDate,
            x => x.isCurrent ? 'Yes' : 'No'
        ], (x, td) => td.append(button('Edit', () => editTerm(x)),
            button('Archive', b => archive('academic-term', x, b), true)));
        const catalog = state.catalog;
        const programs = el('programList');
        programs.replaceChildren();
        if (!catalog.programs.length) programs.append(node('span', 'No programmes', 'text-muted'));
        catalog.programs.forEach(x => programs.append(node('span',
            x.name + ' (' + x.code + ')', 'badge text-bg-light border me-2 mb-2')));
        table('programCampusRows', catalog.programs, [
            x => x.name + ' (' + x.code + ')',
            x => x.campusIds?.length
                ? x.campusIds.map(id => findCampus(id)?.name || ('Campus #' + id)).join(', ')
                : 'All tenant campuses'
        ], (x, td) => td.append(button('Manage campuses', async control => {
            control.disabled = true;
            try { await editProgramAllocation(x); }
            catch (error) { notice(error.message, true); }
            finally { control.disabled = false; }
        })));
        table('levelRows', catalog.levels, [
            x => x.name, x => x.code,
            x => find('programs', x.academicProgramId)?.name || '', x => x.levelNo
        ]);


    }
    async function reload() {
        const [years, terms, campuses, catalog] = await Promise.all([
            api('/api/institution-onboarding/academic-years'),
            api('/api/institution-onboarding/academic-terms'),
            api('/api/institution-onboarding/campus-list'),
            api('/api/academic-setup/catalog?take=100')
        ]);
        if (!sameSession()) return;
        const previouslyClean = [...formGuards]
            .filter(([, item]) => item && !item.isDirty())
            .map(([id]) => id);
        Object.assign(state, { years, terms, campuses, catalog });
        selectLists(); render();
        // Server-provided dropdown options are not user edits. Preserve a draft
        // that became dirty while the request was in flight.
        previouslyClean.forEach(id => guard(id)?.markClean());
        window.dispatchEvent(new Event('eduos:academic-reloaded'));
    }
    window.eduosAcademicManagement = { state, el, field, idOf, enabled, find, findYear,
        notice, api, reload, options, checked, sameSession, guard, mayReplace };
    // The server selects one coherent feature surface per authorized route.
    // Do not let a client-side tab switch expose a different editor without
    // navigating to its canonical, bookmarkable feature URL.
    el('sectionProgram').addEventListener('change', levelsForSection);
    el('sectionYear').addEventListener('change', termsForSection);
    el('curriculumSelection').addEventListener('change', levelsForCurriculum);
    el('allocationCampuses').addEventListener('change', syncAllocationPrimary);
    el('allocationAllowAll').addEventListener('change', syncAllocationPrimary);
    el('allocationCancel').addEventListener('click', () => {
        if (allocationSaving || !mayReplace('programAllocationForm')) return;
        el('programAllocationForm').classList.add('d-none');
        guard('programAllocationForm')?.markClean();
    });
    el('programAllocationForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (allocationSaving || !sameSession() || !event.currentTarget.reportValidity()) return;
        const form = event.currentTarget, submit = form.querySelector('button[type="submit"]');
        const allowAllCampuses = el('allocationAllowAll').checked;
        const campusIds = allowAllCampuses ? [] :
            [...el('allocationCampuses').selectedOptions].map(x => Number(x.value));
        const primaryCampusId = allowAllCampuses ? null : Number(el('allocationPrimary').value) || null;
        if (!allowAllCampuses && (!campusIds.length || !campusIds.includes(primaryCampusId))) {
            notice('Select allowed campuses and a primary campus, or explicitly allow all campuses.', true);
            return;
        }
        allocationSaving = true; submit.disabled = true; form.inert = true;
        let committed = false;
        try {
            await api('/api/academic-setup/programmes/' +
                encodeURIComponent(field('allocationProgramId')) + '/campuses', {
                method: 'PUT', body: JSON.stringify({
                    rowVersion: field('allocationRowVersion'), campusIds,
                    primaryCampusId, allowAllCampuses
                })
            });
            committed = true;
            if (!sameSession()) return;
            form.classList.add('d-none');
            guard('programAllocationForm')?.markClean();
            try { await reload(); notice('Programme campus allocation saved.'); }
            catch { notice('Campus allocation saved, but the directory could not refresh. Reload before another change.', true); }
        } catch (error) {
            if (sameSession()) notice(committed ? 'Campus allocation saved, but the display failed.' : error.message, true);
        } finally { allocationSaving = false; submit.disabled = false; form.inert = false; }
    });
    el('academicRefresh').addEventListener('click', () => {
        if (!sameSession()) return;
        const dirty = [...formGuards].filter(([, item]) => item?.isDirty());
        if (dirty.length && !confirm('Discard unsaved academic form changes and refresh?')) return;
        for (const [id, item] of dirty) {
            const form = el(id);
            form.reset();
            if (id === 'yearForm' || id === 'termForm') {
                const kind = id.slice(0, -4);
                el(kind + 'Id').value = '';
                el(kind + 'Version').value = '';
            }
            if (id === 'programAllocationForm') form.classList.add('d-none');
            delete form.dataset.requestId;
            delete form.dataset.requestFingerprint;
            item.markClean();
        }
        reload().then(() => notice('Academic setup refreshed.')).catch(e => notice(e.message, true));
    });
    reload().catch(e => notice(e.message, true));
})();
