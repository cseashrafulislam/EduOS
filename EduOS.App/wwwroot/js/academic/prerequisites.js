(() => {
    'use strict';
    const root = document.getElementById('prerequisiteWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { catalog: null, page: 1, pageSize: 20, total: 0, rows: [] };
    const id = key => Number(el(key).value) || 0;
    const notice = (message, danger = false) => {
        const n = el('prerequisiteNotice'); n.className = 'alert ' + (danger ? 'alert-danger' : 'alert-success');
        n.textContent = message;
    };
    async function api(path, method = 'GET', data) {
        const response = await fetch(path, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json',
                ...(data ? { 'Content-Type': 'application/json' } : {}),
                ...(method !== 'GET' ? { RequestVerificationToken: token } : {}) },
            ...(data ? { body: JSON.stringify(data) } : {})
        });
        const result = await response.json().catch(() => null);
        if (!response.ok || result?.success !== true) throw new Error(result?.message || 'Request failed.');
        return result.data;
    }
    function fill(select, rows, placeholder) {
        const old = select.value;
        select.replaceChildren(new Option(placeholder, ''));
        rows.forEach(row => select.add(new Option(row.name + (row.code ? ' (' + row.code + ')' : ''), String(row.id))));
        if ([...select.options].some(o => o.value === old)) select.value = old;
    }
    function mappedSubjects() {
        const curriculumId = id('prerequisiteCurriculum');
        const ids = new Set((state.catalog?.curriculumSubjects || [])
            .filter(x => x.academicCurriculumId === curriculumId && x.isActive).map(x => x.subjectId));
        return (state.catalog?.subjects || []).filter(x => ids.has(x.id) && x.isActive);
    }
    function subjectOptions() {
        const subjects = mappedSubjects();
        fill(el('prerequisiteSubject'), subjects, 'Select subject');
        fill(el('prerequisiteRequired'), subjects, 'Select prerequisite');
    }
    async function catalog() {
        const search = el('prerequisiteCatalogSearch').value.trim();
        state.catalog = await api('/api/academic-setup/catalog?take=100&search=' + encodeURIComponent(search));
        fill(el('prerequisiteCurriculum'), (state.catalog.curricula || []).filter(x => x.isActive), 'Select curriculum');
        subjectOptions();
        await load();
    }
    function button(label, callback, style = 'btn-outline-primary') {
        const b = document.createElement('button'); b.type = 'button';
        b.className = 'btn btn-sm ' + style + ' me-1'; b.textContent = label;
        b.addEventListener('click', callback); return b;
    }
    function render() {
        const body = el('prerequisiteRows'); body.replaceChildren();
        for (const row of state.rows) {
            const tr = document.createElement('tr');
            [row.subjectName, row.prerequisiteSubjectName, row.isMandatory ? 'Yes' : 'No'].forEach(value => {
                const td = document.createElement('td'); td.textContent = value; tr.append(td);
            });
            const td = document.createElement('td');
            td.append(button('Toggle mandatory', () => change(row)),
                button('Archive', () => archive(row), 'btn-outline-danger'));
            tr.append(td); body.append(tr);
        }
        if (!state.rows.length) {
            const tr = document.createElement('tr'), td = document.createElement('td');
            td.colSpan = 4; td.textContent = 'No prerequisites found.'; tr.append(td); body.append(tr);
        }
        el('prerequisiteCount').textContent = state.total + ' records · Page ' + state.page;
        el('prerequisitePrev').disabled = state.page <= 1;
        el('prerequisiteNext').disabled = state.page * state.pageSize >= state.total;
    }
    async function load() {
        const curriculumId = id('prerequisiteCurriculum');
        if (!curriculumId) { state.rows = []; state.total = 0; render(); return; }
        const data = await api('/api/academic-setup/prerequisites?curriculumId=' + curriculumId +
            '&page=' + state.page + '&pageSize=' + state.pageSize +
            '&search=' + encodeURIComponent(el('prerequisiteSearch').value.trim()));
        state.rows = data.items || []; state.total = data.totalCount || 0; render();
    }
    async function change(row) {
        if (!confirm('Change mandatory flag for this prerequisite?')) return;
        try {
            await api('/api/academic-setup/prerequisites/' + row.id, 'PUT', {
                academicCurriculumId: row.academicCurriculumId, subjectId: row.subjectId,
                prerequisiteSubjectId: row.prerequisiteSubjectId, isMandatory: !row.isMandatory, rowVersion: row.rowVersion
            });
            await load(); notice('Prerequisite updated.');
        } catch (e) { notice(e.message, true); }
    }
    async function archive(row) {
        if (!confirm('Archive this prerequisite?')) return;
        try {
            await api('/api/academic-setup/prerequisites/' + row.id +
                '?rowVersion=' + encodeURIComponent(row.rowVersion), 'DELETE');
            await load(); notice('Prerequisite archived.');
        } catch (e) { notice(e.message, true); }
    }
    function exportCsv() {
        const cells = [['Subject', 'Prerequisite', 'Mandatory'], ...state.rows.map(x =>
            [x.subjectName, x.prerequisiteSubjectName, x.isMandatory ? 'Yes' : 'No'])];
        const csv = cells.map(row => row.map(x => '"' + String(x).replaceAll('"', '""') + '"').join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\ufeff' + csv], { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a'); a.href = url; a.download = 'curriculum-prerequisites-page.csv';
        a.click(); URL.revokeObjectURL(url);
    }
    el('prerequisiteLoadCatalog').addEventListener('click', () => catalog().catch(e => notice(e.message, true)));
    el('prerequisiteCurriculum').addEventListener('change', () => {
        state.page = 1; subjectOptions(); load().catch(e => notice(e.message, true));
    });
    el('prerequisiteForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (id('prerequisiteSubject') === id('prerequisiteRequired')) {
            notice('Choose two different subjects.', true); return;
        }
        try {
            await api('/api/academic-setup/prerequisites', 'POST', {
                academicCurriculumId: id('prerequisiteCurriculum'), subjectId: id('prerequisiteSubject'),
                prerequisiteSubjectId: id('prerequisiteRequired'), isMandatory: el('prerequisiteMandatory').checked
            });
            state.page = 1; await load(); notice('Prerequisite saved.');
        } catch (e) { notice(e.message, true); }
    });
    el('prerequisiteRefresh').addEventListener('click', () => {
        state.page = 1; load().catch(e => notice(e.message, true));
    });
    el('prerequisitePrev').addEventListener('click', () => {
        if (state.page > 1) { state.page--; load().catch(e => notice(e.message, true)); }
    });
    el('prerequisiteNext').addEventListener('click', () => {
        if (state.page * state.pageSize < state.total) {
            state.page++; load().catch(e => notice(e.message, true));
        }
    });
    el('prerequisiteCsv').addEventListener('click', exportCsv);
    el('prerequisitePrint').addEventListener('click', () => window.print());
    catalog().catch(e => notice(e.message, true));
})();
