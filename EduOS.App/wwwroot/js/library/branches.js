(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('branchForm')) return;
    let campuses = [], branches = [], generation = 0;
    function alertMessage(text, ok = false) {
        const node = el('branchAlert');
        node.className = 'alert alert-' + (ok ? 'success' : 'danger');
        node.textContent = text; node.focus();
    }
    async function api(path, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        if (method !== 'GET') {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload the page.');
            headers.RequestVerificationToken = token;
        }
        const response = await fetch('/api/library/branches' + path, {
            method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(body !== undefined ? { body: JSON.stringify(body) } : {})
        });
        const data = await response.json().catch(() => null);
        if (!response.ok || !data?.success) throw new Error(data?.message || 'Branch request failed.');
        return data.data;
    }
    function reset() {
        el('branchForm').reset(); el('branchId').value = ''; el('branchVersion').value = '';
        el('branchActive').checked = true;
    }
    function cell(value) {
        const td = document.createElement('td'); td.textContent = value == null || value === '' ? '—' : String(value); return td;
    }
    async function load() {
        const sequence = ++generation;
        try {
            const [campusData, branchData] = await Promise.all([api('/campuses'), api('')]);
            if (sequence !== generation) return;
            campuses = Array.isArray(campusData) ? campusData : [];
            branches = Array.isArray(branchData) ? branchData : [];
            const select = el('branchCampus'), previous = select.value;
            select.replaceChildren(new Option('Select campus', ''));
            for (const campus of campuses) select.append(new Option(campus.name + ' (' + campus.code + ')', String(campus.id)));
            if (campuses.some(x => String(x.id) === previous)) select.value = previous;
            const body = el('branchRows'); body.replaceChildren();
            for (const row of branches) {
                const tr = document.createElement('tr');
                const campus = campuses.find(x => x.id === row.campusId);
                [campus?.name || row.campusId, row.name, row.code, row.location, row.isActive ? 'Active' : 'Inactive'].forEach(x => tr.append(cell(x)));
                const td = document.createElement('td'), button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary'; button.textContent = 'Edit';
                button.addEventListener('click', () => {
                    el('branchId').value = row.id; el('branchVersion').value = row.rowVersion;
                    el('branchCampus').value = row.campusId; el('branchName').value = row.name;
                    el('branchCode').value = row.code; el('branchLocation').value = row.location || '';
                    el('branchActive').checked = !!row.isActive; el('branchName').focus();
                });
                td.append(button); tr.append(td); body.append(tr);
            }
            if (!branches.length) {
                const tr = document.createElement('tr'), td = cell('No library branches configured.');
                td.colSpan = 6; tr.append(td); body.append(tr);
            }
        } catch (error) { if (sequence === generation) alertMessage(error.message); }
    }
    el('branchForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (!el('branchForm').reportValidity()) return;
        const id = el('branchId').value, button = el('branchSave');
        const request = { campusId: Number(el('branchCampus').value), name: el('branchName').value.trim(),
            code: el('branchCode').value.trim(), location: el('branchLocation').value.trim() || null,
            isActive: el('branchActive').checked, rowVersion: el('branchVersion').value || null };
        button.disabled = true;
        try {
            await api(id ? '/' + encodeURIComponent(id) : '', id ? 'PUT' : 'POST', request);
            reset(); await load(); alertMessage('Branch saved.', true);
        } catch (error) { alertMessage(error.message); }
        finally { button.disabled = false; }
    });
    el('branchReset').addEventListener('click', reset);
    el('branchRefresh').addEventListener('click', load);
    load();
})();