(() => {
    'use strict';
    const vm = window.eduosAcademicManagement;
    if (!vm) return;
    const { el, api, notice } = vm;
    const size = 20;
    let page = 1, total = 0, rows = [], loaded = false, generation = 0;
    const value = id => el(id).value.trim();
    const cell = value => {
        const td = document.createElement('td');
        td.textContent = String(value ?? '');
        return td;
    };
    function render() {
        const body = el('subjectRows');
        body.replaceChildren();
        if (!rows.length) {
            const tr = document.createElement('tr'), td = cell('No subjects found.');
            td.colSpan = 5;
            tr.append(td); body.append(tr);
        }
        rows.forEach(subject => {
            const tr = document.createElement('tr');
            [subject.name, subject.code, subject.defaultCreditHours,
                subject.isActive ? 'Active' : 'Inactive'].forEach(x => tr.append(cell(x)));
            const action = document.createElement('td'), edit = document.createElement('button');
            edit.type = 'button';
            edit.className = 'btn btn-sm btn-outline-primary';
            edit.textContent = 'Edit';
            edit.addEventListener('click', async () => {
                edit.disabled = true;
                try {
                    const row = await api('/api/academic-setup/subjects/' + encodeURIComponent(subject.id));
                    el('subjectEditId').value = row.id;
                    el('subjectEditVersion').value = row.rowVersion;
                    el('subjectEditCode').textContent = row.code;
                    el('subjectEditName').value = row.name;
                    el('subjectEditShortName').value = row.shortName || '';
                    el('subjectEditCredits').value = row.defaultCreditHours;
                    el('subjectEditActive').checked = row.isActive;
                    el('subjectEditForm').classList.remove('d-none');
                    el('subjectEditForm').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
                } catch (error) { notice(error.message, true); }
                finally { edit.disabled = false; }
            });
            action.append(edit); tr.append(action); body.append(tr);
        });
        el('subjectPage').textContent = total + ' subjects · page ' + page +
            ' of ' + Math.max(1, Math.ceil(total / size));
        el('subjectPrevious').disabled = page <= 1;
        el('subjectNext').disabled = page * size >= total;
        el('subjectCsv').disabled = !rows.length;
    }
    async function load() {
        const current = ++generation;
        const params = new URLSearchParams({ page: String(page), pageSize: String(size) });
        if (value('subjectSearch')) params.set('search', value('subjectSearch'));
        const result = await api('/api/academic-setup/subjects?' + params);
        if (current !== generation) return;
        rows = result.items || [];
        total = result.totalCount || 0;
        loaded = true;
        render();
    }
    const csv = x => {
        let s = String(x ?? '');
        if (/^\s*[=+@-]/.test(s)) s = "'" + s;
        return '"' + s.replace(/"/g, '""') + '"';
    };
    el('subjectCsv').addEventListener('click', () => {
        if (!rows.length) return;
        const lines = [['Subject', 'Code', 'Short name', 'Default credit hours', 'Active']
            .map(csv).join(',')];
        rows.forEach(x => lines.push([x.name, x.code, x.shortName, x.defaultCreditHours,
            x.isActive].map(csv).join(',')));
        const url = URL.createObjectURL(new Blob(['\uFEFF' + lines.join('\r\n')],
            { type: 'text/csv;charset=utf-8' }));
        const link = document.createElement('a');
        link.href = url;
        link.download = 'academic-subjects-page-' + page + '.csv';
        link.click();
        URL.revokeObjectURL(url);
    });
    el('subjectSearchForm').addEventListener('submit', event => {
        event.preventDefault();
        page = 1;
        load().catch(x => notice(x.message, true));
    });
    el('subjectPrevious').addEventListener('click', () => {
        if (page > 1) { page--; load().catch(x => notice(x.message, true)); }
    });
    el('subjectNext').addEventListener('click', () => {
        if (page * size < total) { page++; load().catch(x => notice(x.message, true)); }
    });
    el('subjectEditCancel').addEventListener('click', () => el('subjectEditForm').classList.add('d-none'));
    el('subjectEditForm').addEventListener('submit', async event => {
        event.preventDefault();
        const form = event.currentTarget, button = form.querySelector('button[type="submit"]');
        const credits = Number(value('subjectEditCredits'));
        if (!Number.isFinite(credits) || credits < 0 || credits > 1000) {
            notice('Credit hours must be between 0 and 1000.', true);
            return;
        }
        button.disabled = true;
        try {
            await api('/api/academic-setup/subjects/' + encodeURIComponent(value('subjectEditId')), {
                method: 'PUT',
                body: JSON.stringify({
                    name: value('subjectEditName'),
                    shortName: value('subjectEditShortName') || null,
                    defaultCreditHours: credits,
                    isActive: el('subjectEditActive').checked,
                    rowVersion: value('subjectEditVersion')
                })
            });
            form.classList.add('d-none');
            await vm.reload();
            await load();
            notice('Academic subject saved.');
        } catch (error) { notice(error.message, true); }
        finally { button.disabled = false; }
    });
    window.addEventListener('eduos:academic-reloaded', () => {
        if (loaded || document.querySelector('button[data-panel="subjects"].active'))
            load().catch(x => notice(x.message, true));
    });
    document.querySelector('button[data-panel="subjects"]').addEventListener('click', () => {
        if (!loaded) load().catch(x => notice(x.message, true));
    });
})();
