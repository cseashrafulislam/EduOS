(() => {
    'use strict';
    const vm = window.eduosAcademicManagement;
    if (!vm) return;
    const { el, state, api, notice } = vm;
    let page = 1, total = 0, rows = [], sequence = 0;
    const size = 20;
    const cell = (tr, value) => {
        const td = document.createElement('td');
        td.textContent = String(value ?? '');
        tr.append(td);
        return td;
    };
    const button = (label, action) => {
        const b = document.createElement('button');
        b.type = 'button'; b.className = 'btn btn-sm btn-outline-primary me-1';
        b.textContent = label;
        b.addEventListener('click', async () => {
            b.disabled = true;
            try { await action(); } catch (e) { notice(e.message, true); }
            finally { b.disabled = false; }
        });
        return b;
    };
    const programName = id => state.catalog?.programs?.find(x => x.id === id)?.name || ('Programme #' + id);
    function programOptions() {
        const select = el('curriculumProgramFilter'), current = select.value;
        select.replaceChildren(new Option('All programmes', ''));
        (state.catalog?.programs || []).forEach(x => select.add(new Option(x.name, String(x.id))));
        select.value = [...select.options].some(x => x.value === current) ? current : '';
    }
    async function load() {
        const ticket = ++sequence;
        const query = new URLSearchParams({ page: String(page), pageSize: String(size) });
        const search = el('curriculumSearch').value.trim();
        if (search) query.set('search', search);
        if (el('curriculumProgramFilter').value) query.set('academicProgramId', el('curriculumProgramFilter').value);
        if (el('curriculumEffectiveOn').value) query.set('effectiveOn', el('curriculumEffectiveOn').value);
        const data = await api('/api/academic-setup/curricula?' + query);
        if (ticket !== sequence) return;
        rows = data.items || []; total = data.totalCount || 0;
        const target = el('curriculumRows');
        target.replaceChildren();
        rows.forEach(x => {
            const tr = document.createElement('tr');
            cell(tr, x.name + ' (' + x.code + ')');
            cell(tr, programName(x.academicProgramId));
            cell(tr, x.versionNo);
            cell(tr, x.effectiveFrom + ' – ' + (x.effectiveTo || 'Open'));
            cell(tr, x.subjectCount);
            cell(tr, (x.isCurrent ? 'Current' : 'Historical') + (x.isActive ? '' : ' / Inactive'));
            const actions = cell(tr, '');
            actions.append(button('Subjects', () => details(x.id)));
            if (x.isActive && !x.isCurrent)
                actions.append(button('Activate', async () => {
                    if (!confirm('Activate ' + x.name + '? Previous versions remain in history.')) return;
                    await api('/api/academic-setup/curricula/' + x.id + '/activate?rowVersion=' +
                        encodeURIComponent(x.rowVersion), { method: 'PUT' });
                    await vm.reload();
                    notice('Curriculum activated.');
                }));
            target.append(tr);
        });
        if (!rows.length) {
            const tr = document.createElement('tr');
            const td = cell(tr, 'No curriculum versions found.'); td.colSpan = 7; target.append(tr);
        }
        el('curriculumPage').textContent = 'Page ' + page + ' • ' + total + ' versions';
        el('curriculumPrevious').disabled = page <= 1;
        el('curriculumNext').disabled = page * size >= total;
    }
    async function details(id) {
        const data = await api('/api/academic-setup/curricula/' + id);
        const host = el('curriculumDetails');
        host.replaceChildren();
        const heading = document.createElement('h3');
        heading.className = 'h6';
        heading.textContent = data.name + ' (' + data.code + ') — version ' + data.versionNo;
        host.append(heading);
        const summary = document.createElement('p');
        summary.className = 'small text-muted';
        summary.textContent = 'Effective: ' + data.effectiveFrom + ' to ' +
            (data.effectiveTo || 'open') + ' • ' + data.subjectCount + ' subject mappings';
        host.append(summary);
        const table = document.createElement('table');
        table.className = 'table table-sm';
        const thead = document.createElement('thead'), hr = document.createElement('tr');
        ['Class', 'Subject', 'Full marks', 'Pass marks', 'Credits', 'Optional', 'Status'].forEach(x => cell(hr, x));
        thead.append(hr); table.append(thead);
        const tbody = document.createElement('tbody');
        (data.subjects || []).forEach(x => {
            const tr = document.createElement('tr');
            [x.academicLevelName || x.academicLevelId, x.subjectName || x.subjectCode,
                x.fullMarks, x.passMarks, x.creditHours, x.isOptional ? 'Yes' : 'No',
                x.isActive ? 'Active' : 'Inactive'].forEach(v => cell(tr, v));
            tbody.append(tr);
        });
        table.append(tbody); host.append(table);
        host.classList.remove('d-none');
    }
    const safe = value => {
        let s = String(value ?? '');
        if (/^[=+\-@\t\r]/.test(s)) s = "'" + s;
        return '"' + s.replace(/"/g, '""') + '"';
    };
    el('curriculumCsv').addEventListener('click', () => {
        const lines = [['Code', 'Name', 'Programme', 'Version', 'Effective from', 'Effective to',
            'Current', 'Active', 'Subjects'], ...rows.map(x => [x.code, x.name,
            programName(x.academicProgramId), x.versionNo, x.effectiveFrom, x.effectiveTo,
            x.isCurrent, x.isActive, x.subjectCount])];
        const blob = new Blob(['\uFEFF' + lines.map(row => row.map(safe).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob), link = document.createElement('a');
        link.href = url; link.download = 'academic-curricula-page-' + page + '.csv';
        link.click(); URL.revokeObjectURL(url);
    });
    el('curriculumSearchForm').addEventListener('submit', event => {
        event.preventDefault(); page = 1; load().catch(e => notice(e.message, true));
    });
    el('curriculumPrevious').addEventListener('click', () => {
        if (page > 1) { page--; load().catch(e => notice(e.message, true)); }
    });
    el('curriculumNext').addEventListener('click', () => {
        if (page * size < total) { page++; load().catch(e => notice(e.message, true)); }
    });
    window.addEventListener('eduos:academic-reloaded', () => {
        programOptions(); load().catch(e => notice(e.message, true));
    });
    programOptions();
    load().catch(e => notice(e.message, true));
})();
