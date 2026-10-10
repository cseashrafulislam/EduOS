(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const base = '/api/communication-admin';
    const channel = { 1: 'In-app', 2: 'Email', 3: 'SMS', 4: 'Push' };
    const state = { category: [], template: [], gateway: [], page: 1, pages: 1 };
    const config = {
        category: { route: 'notice-categories', fields: ['code', 'name', 'isActive'], columns: ['code', 'name', 'isActive'] },
        template: { route: 'templates', fields: ['code', 'name', 'channel', 'subjectTemplate', 'bodyTemplate', 'isActive'], columns: ['code', 'name', 'channel', 'isActive'] },
        gateway: { route: 'gateways', fields: ['providerCode', 'channel', 'endpoint', 'credential', 'isDefault', 'isActive', 'clearCredential'], columns: ['providerCode', 'channel', 'isDefault', 'hasCredential', 'isActive'] }
    };
    const ids = {
        category: { code: 'categoryCode', name: 'categoryName', isActive: 'categoryActive' },
        template: { code: 'templateCode', name: 'templateName', channel: 'templateChannel', subjectTemplate: 'templateSubject', bodyTemplate: 'templateBody', isActive: 'templateActive' },
        gateway: { providerCode: 'gatewayCode', channel: 'gatewayChannel', endpoint: 'gatewayEndpoint', credential: 'gatewayCredential', isDefault: 'gatewayDefault', isActive: 'gatewayActive', clearCredential: 'gatewayClear' }
    };
    function alert(message, good = false) {
        const node = el('communicationAlert');
        node.className = 'alert alert-' + (good ? 'success' : 'danger');
        node.textContent = message;
        node.focus();
    }
    async function api(path, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        const csrf = document.querySelector('meta[name="request-verification-token"]')?.content;
        if (csrf) headers.RequestVerificationToken = csrf;
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        const response = await fetch(base + path, { method, cache: 'no-store', credentials: 'same-origin',
            headers, ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Request failed.');
        return result.data;
    }
    function value(kind, field) {
        const input = el(ids[kind][field]);
        if (input.type === 'checkbox') return input.checked;
        if (field === 'channel') return Number(input.value);
        if (field === 'isActive') return input.value === 'true';
        return input.value.trim();
    }
    function clear(kind) {
        el(kind + 'Form').reset();
        el(kind + 'Id').value = '';
        el(kind + 'Version').value = '';
        if (kind === 'gateway') el('gatewayCredential').value = '';
        const code = el(ids[kind].code || ids[kind].providerCode);
        code.readOnly = false;
    }
    function edit(kind, row) {
        clear(kind);
        el(kind + 'Id').value = row.id;
        el(kind + 'Version').value = row.rowVersion || '';
        for (const field of config[kind].fields) {
            if (field === 'credential' || field === 'clearCredential') continue;
            const input = el(ids[kind][field]);
            if (input.type === 'checkbox') input.checked = !!row[field];
            else input.value = row[field] ?? '';
        }
        el(ids[kind].code || ids[kind].providerCode).readOnly = true;
        el(kind + 'Form').scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
    function cell(text) {
        const td = document.createElement('td');
        td.textContent = text == null ? '—' : String(text);
        return td;
    }
    function render(kind) {
        const tbody = el(kind + 'Rows');
        tbody.replaceChildren();
        for (const row of state[kind]) {
            const tr = document.createElement('tr');
            for (const field of config[kind].columns) {
                const raw = row[field];
                const display = field === 'channel' ? channel[raw] || raw :
                    ['isActive', 'isDefault', 'hasCredential'].includes(field) ? (raw ? 'Yes' : 'No') : raw;
                tr.append(cell(display));
            }
            const action = cell('');
            const button = document.createElement('button');
            button.type = 'button'; button.className = 'btn btn-outline-primary btn-sm';
            button.textContent = 'Edit'; button.addEventListener('click', () => edit(kind, row));
            action.append(button); tr.append(action); tbody.append(tr);
        }
        if (!state[kind].length) {
            const tr = document.createElement('tr'), td = cell('No records found.');
            td.colSpan = config[kind].columns.length + 1; tr.append(td); tbody.append(tr);
        }
    }
    async function load(kind) {
        const suffix = kind === 'template' ? '?page=' + state.page + '&pageSize=20' : '';
        const data = await api('/' + config[kind].route + suffix);
        state[kind] = kind === 'template' ? (data?.items || []) : (data || []);
        if (kind === 'template') {
            state.pages = Math.max(1, data?.totalPages || 1);
            el('templatePage').textContent = 'Page ' + state.page + ' of ' + state.pages;
            el('templatePrev').disabled = state.page <= 1;
            el('templateNext').disabled = state.page >= state.pages;
        }
        render(kind);
    }
    async function save(kind, event) {
        event.preventDefault();
        const form = el(kind + 'Form'), button = form.querySelector('button[type="submit"]');
        const id = el(kind + 'Id').value;
        const data = {};
        for (const field of config[kind].fields) data[field] = value(kind, field);
        if (kind === 'gateway' && !data.credential) data.credential = null;
        if (id) data.rowVersion = el(kind + 'Version').value;
        button.disabled = true;
        try {
            await api('/' + config[kind].route + (id ? '/' + encodeURIComponent(id) : ''),
                id ? 'PUT' : 'POST', data);
            clear(kind);
            await load(kind);
            alert('Saved successfully.', true);
        } catch (error) { alert(error.message); }
        finally { button.disabled = false; }
    }
    document.addEventListener('DOMContentLoaded', async () => {
        for (const kind of Object.keys(config)) {
            el(kind + 'Form').addEventListener('submit', event => save(kind, event));
            document.querySelector('[data-clear="' + kind + '"]').addEventListener('click', () => clear(kind));
        }
        el('templatePrev').addEventListener('click', async () => { if (state.page > 1) { state.page--; await load('template').catch(e => alert(e.message)); } });
        el('templateNext').addEventListener('click', async () => { if (state.page < state.pages) { state.page++; await load('template').catch(e => alert(e.message)); } });
        await Promise.all(Object.keys(config).map(kind => load(kind).catch(error => alert(error.message))));
    }, { once: true });
})();
