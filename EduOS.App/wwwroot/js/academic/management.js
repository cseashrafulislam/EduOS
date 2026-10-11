(() => {
    'use strict';
    const root = document.getElementById('academicManagement');
    if (!root) return;
    const el = id => document.getElementById(id);
    const state = { years: [], terms: [], campuses: [], catalog: null };
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
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
        const select = el(id), selected = chosen === undefined ? select.value : chosen;
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
        if (!confirm('Archive ' + row.name + '? Linked academic records may block removal.')) return;
        control.disabled = true;
        try {
            await api('/api/institution-onboarding/' + kind + '/' + encodeURIComponent(row.id) +
                '?rowVersion=' + encodeURIComponent(row.rowVersion), { method: 'DELETE' });
            await reload();
            notice('Record archived.');
        } catch (e) { notice(e.message, true); }
        finally { control.disabled = false; }
    }
    function editYear(row) {
        el('yearForm').reset();
        for (const [id, value] of Object.entries({
            yearId: row.id, yearVersion: row.rowVersion, yearName: row.name, yearCode: row.code,
            yearStart: row.startDate, yearEnd: row.endDate, yearCycle: row.cycleType
        })) el(id).value = value ?? '';
        options('yearCampus', enabled(state.campuses), 'All campuses', row.campusId, true);
        el('yearCurrent').checked = row.isCurrent;
        el('yearForm').scrollIntoView({ behavior: 'smooth' });
    }
    function editTerm(row) {
        el('termForm').reset();
        for (const [id, value] of Object.entries({
            termId: row.id, termVersion: row.rowVersion, termName: row.name, termCode: row.code,
            termStart: row.startDate, termEnd: row.endDate
        })) el(id).value = value ?? '';
        options('termYear', enabled(state.years), 'Select year', row.academicYearId);
        el('termCurrent').checked = row.isCurrent;
        el('termForm').scrollIntoView({ behavior: 'smooth' });
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
        table('levelRows', catalog.levels, [
            x => x.name, x => x.code,
            x => find('programs', x.academicProgramId)?.name || '', x => x.levelNo
        ]);
        table('sectionRows', catalog.batches, [
            x => x.name, x => x.code, x => findCampus(x.campusId)?.name || '',
            x => findYear(x.academicYearId)?.name || '',
            x => find('levels', x.academicLevelId)?.name || '', x => x.capacity
        ]);
        table('subjectRows', catalog.subjects,
            [x => x.name, x => x.code, x => x.defaultCreditHours]);
        table('curriculumRows', catalog.curricula, [
            x => x.name + ' (' + x.code + ')',
            x => find('programs', x.academicProgramId)?.name || '',
            x => x.versionNo, x => x.effectiveFrom + (x.isCurrent ? ' • Current' : ''),
            x => catalog.curriculumSubjects.filter(y => y.academicCurriculumId === x.id).length
        ]);
    }
    async function reload() {
        const [years, terms, campuses, catalog] = await Promise.all([
            api('/api/institution-onboarding/academic-years'),
            api('/api/institution-onboarding/academic-terms'),
            api('/api/institution-onboarding/campus-list'),
            api('/api/academic-setup/catalog?take=100')
        ]);
        Object.assign(state, { years, terms, campuses, catalog });
        selectLists(); render();
    }
    window.eduosAcademicManagement = { state, el, field, idOf, enabled, find, findYear,
        notice, api, reload, options, checked };
    root.querySelectorAll('button[data-panel]').forEach(btn => btn.addEventListener('click', () => {
        root.querySelectorAll('button[data-panel]').forEach(x => x.classList.toggle('active', x === btn));
        root.querySelectorAll('section[data-section]').forEach(x =>
            x.classList.toggle('d-none', x.dataset.section !== btn.dataset.panel));
    }));
    el('sectionProgram').addEventListener('change', levelsForSection);
    el('sectionYear').addEventListener('change', termsForSection);
    el('curriculumSelection').addEventListener('change', levelsForCurriculum);
    el('academicRefresh').addEventListener('click', () => reload()
        .then(() => notice('Academic setup refreshed.')).catch(e => notice(e.message, true)));
    reload().catch(e => notice(e.message, true));
})();
