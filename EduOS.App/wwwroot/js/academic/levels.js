(() => {
    'use strict';
    const vm = window.eduosAcademicManagement;
    if (!vm) return;
    const { state, el, api, notice, reload } = vm;
    const pageSize = 20;
    let page = 1, total = 0, rows = [], loaded = false, generation = 0;
    const value = id => el(id).value.trim();
    const node = (tag, value) => {
        const x = document.createElement(tag);
        x.textContent = String(value ?? '');
        return x;
    };
    function programOptions() {
        const select = el('levelProgramFilter'), previous = select.value;
        select.replaceChildren(new Option('All programmes', ''));
        (state.catalog?.programs || []).forEach(x =>
            select.add(new Option(x.name + ' (' + x.code + ')', String(x.id))));
        if ([...select.options].some(x => x.value === previous)) select.value = previous;
    }
    function render() {
        const body = el('levelRows');
        body.replaceChildren();
        if (!rows.length) {
            const tr = node('tr', ''), td = node('td', 'No levels found.');
            td.colSpan = 7; tr.append(td); body.append(tr);
        }
        rows.forEach(level => {
            const tr = document.createElement('tr');
            [level.name, level.code, level.academicProgramName || level.academicProgramId,
                level.levelNo, level.isTerminalLevel ? 'Terminal' : level.isPromotable ? 'Promotable' : 'Not promotable',
                level.isActive ? 'Active' : 'Inactive'].forEach(x => tr.append(node('td', x)));
            const action = document.createElement('td'), button = node('button', 'Edit');
            button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
            button.addEventListener('click', async () => {
                button.disabled = true;
                try {
                    const row = await api('/api/academic-setup/levels/' + encodeURIComponent(level.id));
                    el('levelEditId').value = row.id;
                    el('levelEditVersion').value = row.rowVersion;
                    el('levelEditContext').textContent = row.code + ' · ' +
                        (row.academicProgramName || row.academicProgramId);
                    el('levelEditName').value = row.name;
                    el('levelEditNumber').value = row.levelNo;
                    el('levelEditOrder').value = row.displayOrder;
                    el('levelEditPromotable').checked = row.isPromotable;
                    el('levelEditTerminal').checked = row.isTerminalLevel;
                    el('levelEditActive').checked = row.isActive;
                    el('levelEditForm').classList.remove('d-none');
                } catch (error) { notice(error.message, true); }
                finally { button.disabled = false; }
            });
            action.append(button); tr.append(action); body.append(tr);
        });
        el('levelPage').textContent = total + ' levels · page ' + page + ' of ' +
            Math.max(1, Math.ceil(total / pageSize));
        el('levelPrev').disabled = page <= 1;
        el('levelNext').disabled = page * pageSize >= total;
        el('levelCsv').disabled = !rows.length;
    }
    async function load() {
        const current = ++generation;
        const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
        if (value('levelSearch')) params.set('search', value('levelSearch'));
        if (value('levelProgramFilter')) params.set('academicProgramId', value('levelProgramFilter'));
        const result = await api('/api/academic-setup/levels?' + params);
        if (current !== generation) return;
        rows = result.items || []; total = result.totalCount || 0;
        loaded = true; render();
    }
    function csv(value) {
        let s = String(value ?? '');
        if (/^\s*[=+@-]/.test(s)) s = "'" + s;
        return '"' + s.replace(/"/g, '""') + '"';
    }
    el('levelCsv').addEventListener('click', () => {
        if (!rows.length) return;
        const lines = [['Level', 'Code', 'Programme', 'Number', 'Promotable', 'Terminal', 'Active', 'Order']
            .map(csv).join(',')];
        rows.forEach(x => lines.push([x.name, x.code, x.academicProgramName,
            x.levelNo, x.isPromotable, x.isTerminalLevel, x.isActive, x.displayOrder].map(csv).join(',')));
        const url = URL.createObjectURL(new Blob(['\uFEFF' + lines.join('\r\n')],
            { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a');
        a.href = url; a.download = 'academic-levels-page-' + page + '.csv'; a.click();
        URL.revokeObjectURL(url);
    });
    el('levelSearchForm').addEventListener('submit', e => {
        e.preventDefault(); page = 1; load().catch(x => notice(x.message, true));
    });
    el('levelPrev').addEventListener('click', () => {
        if (page > 1) { page--; load().catch(x => notice(x.message, true)); }
    });
    el('levelNext').addEventListener('click', () => {
        if (page * pageSize < total) { page++; load().catch(x => notice(x.message, true)); }
    });
    el('levelEditCancel').addEventListener('click', () => el('levelEditForm').classList.add('d-none'));
    el('levelEditForm').addEventListener('submit', async e => {
        e.preventDefault();
        const form = e.currentTarget, button = form.querySelector('button[type="submit"]');
        const levelNo = Number(value('levelEditNumber')), order = Number(value('levelEditOrder'));
        const isPromotable = el('levelEditPromotable').checked;
        const isTerminalLevel = el('levelEditTerminal').checked;
        if (!Number.isInteger(levelNo) || levelNo < 1 || levelNo > 1000 ||
            !Number.isInteger(order) || order < 0 || (isPromotable && isTerminalLevel)) {
            notice('Check level number, display order and progression flags.', true); return;
        }
        button.disabled = true;
        try {
            await api('/api/academic-setup/levels/' + encodeURIComponent(value('levelEditId')), {
                method: 'PUT', body: JSON.stringify({
                    name: value('levelEditName'), levelNo, displayOrder: order,
                    isPromotable, isTerminalLevel, isActive: el('levelEditActive').checked,
                    rowVersion: value('levelEditVersion')
                })
            });
            form.classList.add('d-none');
            await reload(); await load();
            notice('Academic level saved.');
        } catch (error) { notice(error.message, true); }
        finally { button.disabled = false; }
    });
    window.addEventListener('eduos:academic-reloaded', () => {
        programOptions();
        if (loaded || document.querySelector('button[data-panel="classes"].active'))
            load().catch(x => notice(x.message, true));
    });
    document.querySelector('button[data-panel="classes"]').addEventListener('click', () => {
        if (!loaded) load().catch(x => notice(x.message, true));
    });
})();
