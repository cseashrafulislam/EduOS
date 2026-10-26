(() => {
    'use strict';
    const vm = window.eduosAcademicManagement;
    if (!vm) return;
    const { state, el, api, notice, reload } = vm;
    const size = 20;
    let page = 1, total = 0, rows = [], loaded = false, generation = 0;
    const value = id => el(id).value.trim();
    const cell = text => { const td = document.createElement('td'); td.textContent = String(text ?? ''); return td; };
    function populatePrograms(items) {
        for (const id of ['trackProgramFilter', 'trackCreateProgram']) {
            const select = el(id), selected = select.value;
            select.replaceChildren(new Option(id === 'trackProgramFilter' ? 'All programmes' : 'Institution-wide', ''));
            items.filter(x => id === 'trackProgramFilter' || x.isActive !== false)
                .forEach(x => select.add(new Option(x.name + ' (' + x.code + ')', String(x.id))));
            if ([...select.options].some(x => x.value === selected)) select.value = selected;
        }
    }
    function render() {
        const body = el('trackRows');
        body.replaceChildren();
        if (!rows.length) {
            const tr = document.createElement('tr'), td = cell('No tracks found.');
            td.colSpan = 6; tr.append(td); body.append(tr);
        }
        rows.forEach(track => {
            const tr = document.createElement('tr');
            [track.name, track.code, track.academicProgramName || 'Institution-wide',
                track.isDefault ? 'Yes' : 'No', track.isActive ? 'Active' : 'Inactive']
                .forEach(x => tr.append(cell(x)));
            const td = document.createElement('td'), button = document.createElement('button');
            button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary'; button.textContent = 'Edit';
            button.addEventListener('click', async () => {
                button.disabled = true;
                try {
                    const row = await api('/api/academic-setup/tracks/' + encodeURIComponent(track.id));
                    el('trackEditId').value = row.id;
                    el('trackEditVersion').value = row.rowVersion;
                    el('trackEditContext').textContent = row.code + ' · ' +
                        (row.academicProgramName || 'Institution-wide');
                    el('trackEditName').value = row.name;
                    el('trackEditDescription').value = row.description || '';
                    el('trackEditOrder').value = row.displayOrder;
                    el('trackEditDefault').checked = row.isDefault;
                    el('trackEditActive').checked = row.isActive;
                    el('trackEditForm').classList.remove('d-none');
                } catch (error) { notice(error.message, true); }
                finally { button.disabled = false; }
            });
            td.append(button); tr.append(td); body.append(tr);
        });
        el('trackPage').textContent = total + ' tracks · page ' + page + ' of ' +
            Math.max(1, Math.ceil(total / size));
        el('trackPrev').disabled = page <= 1;
        el('trackNext').disabled = page * size >= total;
        el('trackCsv').disabled = !rows.length;
    }
    async function load() {
        const current = ++generation;
        const params = new URLSearchParams({ page: String(page), pageSize: String(size) });
        if (value('trackSearch')) params.set('search', value('trackSearch'));
        if (value('trackProgramFilter')) params.set('academicProgramId', value('trackProgramFilter'));
        const result = await api('/api/academic-setup/tracks?' + params);
        if (current !== generation) return;
        rows = result.items || []; total = result.totalCount || 0;
        loaded = true; render();
    }
    const csv = x => {
        let s = String(x ?? '');
        if (/^\s*[=+@-]/.test(s)) s = "'" + s;
        return '"' + s.replace(/"/g, '""') + '"';
    };
    el('trackCsv').addEventListener('click', () => {
        if (!rows.length) return;
        const lines = [['Track', 'Code', 'Programme', 'Description', 'Default', 'Active', 'Order']
            .map(csv).join(',')];
        rows.forEach(x => lines.push([x.name, x.code, x.academicProgramName || 'Institution-wide',
            x.description, x.isDefault, x.isActive, x.displayOrder].map(csv).join(',')));
        const url = URL.createObjectURL(new Blob(['\uFEFF' + lines.join('\r\n')],
            { type: 'text/csv;charset=utf-8' }));
        const link = document.createElement('a');
        link.href = url; link.download = 'academic-tracks-page-' + page + '.csv'; link.click();
        URL.revokeObjectURL(url);
    });
    el('trackSearchForm').addEventListener('submit', e => {
        e.preventDefault(); page = 1; load().catch(x => notice(x.message, true));
    });
    el('trackPrev').addEventListener('click', () => {
        if (page > 1) { page--; load().catch(x => notice(x.message, true)); }
    });
    el('trackNext').addEventListener('click', () => {
        if (page * size < total) { page++; load().catch(x => notice(x.message, true)); }
    });
    el('trackProgrammeLookupButton').addEventListener('click', async () => {
        const button = el('trackProgrammeLookupButton');
        button.disabled = true;
        try {
            const params = new URLSearchParams({ page: '1', pageSize: '100' });
            if (value('trackProgrammeLookup')) params.set('search', value('trackProgrammeLookup'));
            const data = await api('/api/academic-setup/programmes?' + params);
            populatePrograms(data.items || []);
        } catch (error) { notice(error.message, true); }
        finally { button.disabled = false; }
    });
    el('trackCreateForm').addEventListener('submit', async e => {
        e.preventDefault();
        const form = e.currentTarget;
        const button = form.querySelector('button[type="submit"]');
        const order = Number(value('trackCreateOrder'));
        if (!Number.isInteger(order) || order < 0) { notice('Invalid display order.', true); return; }
        button.disabled = true;
        try {
            await api('/api/academic-setup/tracks', { method: 'POST', body: JSON.stringify({
                academicProgramId: value('trackCreateProgram') ? Number(value('trackCreateProgram')) : null,
                name: value('trackCreateName'), code: value('trackCreateCode').toUpperCase(),
                description: value('trackCreateDescription') || null,
                isDefault: el('trackCreateDefault').checked, isActive: true, displayOrder: order
            }) });
            form.reset();
            await reload(); page = 1; await load();
            notice('Academic track created.');
        } catch (error) { notice(error.message, true); }
        finally { button.disabled = false; }
    });
    el('trackEditCancel').addEventListener('click', () => el('trackEditForm').classList.add('d-none'));
    el('trackEditForm').addEventListener('submit', async e => {
        e.preventDefault();
        const form = e.currentTarget, button = form.querySelector('button[type="submit"]');
        const order = Number(value('trackEditOrder'));
        if (!Number.isInteger(order) || order < 0) { notice('Invalid display order.', true); return; }
        button.disabled = true;
        try {
            await api('/api/academic-setup/tracks/' + encodeURIComponent(value('trackEditId')), {
                method: 'PUT', body: JSON.stringify({
                    name: value('trackEditName'), description: value('trackEditDescription') || null,
                    isDefault: el('trackEditDefault').checked, isActive: el('trackEditActive').checked,
                    displayOrder: order, rowVersion: value('trackEditVersion')
                })
            });
            form.classList.add('d-none');
            await reload(); await load();
            notice('Academic track saved.');
        } catch (error) { notice(error.message, true); }
        finally { button.disabled = false; }
    });
    window.addEventListener('eduos:academic-reloaded', () => {
        populatePrograms(state.catalog?.programs || []);
        if (loaded || document.querySelector('button[data-panel="classes"].active'))
            load().catch(x => notice(x.message, true));
    });
    document.querySelector('button[data-panel="classes"]').addEventListener('click', () => {
        if (!loaded) load().catch(x => notice(x.message, true));
    });
})();
