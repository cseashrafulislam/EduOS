(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const state = { page: 1, totalPages: 1, rows: [], values: [], definitions: [] };
    const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value ||
        document.querySelector('meta[name="RequestVerificationToken"]')?.content || '';
    function alertMessage(message, error = false) {
        const box = el('cfNotice'); box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = message; box.scrollIntoView({ block: 'nearest' });
    }
    async function api(path, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (method !== 'GET') { headers['Content-Type'] = 'application/json'; headers.RequestVerificationToken = token(); }
        const response = await fetch('/api/custom-fields/' + path, {
            method, credentials: 'same-origin', headers, body: body === undefined ? undefined : JSON.stringify(body)
        });
        const json = await response.json().catch(() => ({}));
        if (!response.ok || !json.success) throw Error(json.message || 'Request failed (' + response.status + ')');
        return json.data;
    }
    const td = (tr, value) => { const cell = document.createElement('td'); cell.textContent = value == null ? '' : String(value); tr.append(cell); return cell; };
    function reset() {
        el('cfForm').reset(); el('cfId').value = ''; el('cfVersion').value = '';
        el('cfKey').disabled = false; el('cfType').disabled = false; toggleChoices();
    }
    function toggleChoices() { el('cfChoicesWrap').classList.toggle('d-none', el('cfType').value !== '7'); }
    function edit(id) {
        const d = state.rows.find(x => x.id === id); if (!d) return;
        el('cfId').value = d.id; el('cfVersion').value = d.rowVersion;
        el('cfKey').value = d.fieldKey; el('cfKey').disabled = true;
        el('cfLabel').value = d.label; el('cfType').value = d.dataType; el('cfType').disabled = true;
        el('cfRequired').value = String(d.isRequired); el('cfActive').value = String(d.isActive);
        el('cfOrder').value = d.displayOrder;
        el('cfChoices').value = (d.options || []).filter(o => o.isActive).map(o => o.value + ' | ' + o.label).join('\n');
        toggleChoices(); el('cfForm').scrollIntoView({ behavior: 'smooth' });
    }
    async function loadDefinitions() {
        const data = await api('definitions?page=' + state.page + '&pageSize=20');
        state.rows = data.items || []; state.totalPages = data.totalPages || 1;
        el('cfRows').replaceChildren();
        state.rows.forEach(d => {
            const tr = document.createElement('tr');
            td(tr, d.fieldKey); td(tr, d.label); td(tr, d.dataType);
            td(tr, d.isRequired ? 'Yes' : 'No'); td(tr, d.isActive ? 'Active' : 'Inactive');
            const cell = td(tr, ''); const btn = document.createElement('button');
            btn.type = 'button'; btn.className = 'btn btn-outline-primary btn-sm'; btn.textContent = 'Edit';
            btn.addEventListener('click', () => edit(d.id)); cell.append(btn); el('cfRows').append(tr);
        });
        el('cfPage').textContent = 'Page ' + state.page + ' of ' + state.totalPages;
        el('cfPrev').disabled = state.page <= 1; el('cfNext').disabled = state.page >= state.totalPages;
    }
    function options() {
        if (el('cfType').value !== '7') return [];
        return el('cfChoices').value.split(/\r?\n/).filter(x => x.trim()).map((line, i) => {
            const parts = line.split('|');
            return { value: (parts[0] || '').trim(), label: (parts[1] || parts[0] || '').trim(), displayOrder: i, isActive: true };
        });
    }
    async function saveDefinition(event) {
        event.preventDefault();
        const id = Number(el('cfId').value) || null;
        const dto = { entityType: 'Tenant', fieldKey: el('cfKey').value.trim(), label: el('cfLabel').value.trim(),
            dataType: Number(el('cfType').value), isRequired: el('cfRequired').value === 'true',
            isActive: el('cfActive').value === 'true', displayOrder: Number(el('cfOrder').value) || 0,
            isSensitive: false, validationJson: null, options: options(), rowVersion: el('cfVersion').value || null };
        try { await api('definitions' + (id ? '/' + id : ''), id ? 'PUT' : 'POST', dto);
            alertMessage('Definition saved.'); reset(); await loadDefinitions(); await loadValues();
        } catch (e) { alertMessage(e.message, true); }
    }
    async function loadValues() {
        state.values = await api('tenant-values');
        state.definitions = [];
        let page = 1, pages = 1;
        do {
            const result = await api('definitions?page=' + page + '&pageSize=100');
            state.definitions.push(...(result.items || [])); pages = Math.min(result.totalPages || 1, 3); page++;
        } while (page <= pages);
        el('cfValueInputs').replaceChildren();
        state.values.forEach(v => {
            const def = state.definitions.find(x => x.id === v.customFieldDefinitionId);
            if (!def) return;
            const wrap = document.createElement('div'); wrap.className = 'col-md-6';
            const label = document.createElement('label'); label.className = 'form-label'; label.textContent = v.label;
            const input = def.dataType === 7 ? document.createElement('select') : document.createElement('input');
            input.className = 'form-control'; input.dataset.fieldId = v.customFieldDefinitionId;
            input.dataset.version = v.rowVersion || '';
            if (def.dataType === 7) {
                const empty = document.createElement('option'); empty.value = ''; empty.textContent = '-- Select --'; input.append(empty);
                (def.options || []).filter(o => o.isActive).forEach(o => {
                    const option = document.createElement('option'); option.value = o.value; option.textContent = o.label; input.append(option);
                });
            } else if (def.dataType === 4) input.type = 'date';
            else if (def.dataType === 2 || def.dataType === 3) { input.type = 'number'; input.step = def.dataType === 2 ? '1' : 'any'; }
            else if (def.dataType === 6) {
                input.type = 'text'; input.placeholder = 'true or false';
            } else input.type = 'text';
            input.value = v.value ?? ''; input.required = def.isRequired; label.append(input); wrap.append(label);
            el('cfValueInputs').append(wrap);
        });
    }
    async function saveValues(event) {
        event.preventDefault();
        const dto = [...el('cfValueInputs').querySelectorAll('[data-field-id]')].map(input => ({
            customFieldDefinitionId: Number(input.dataset.fieldId), value: input.value || null,
            rowVersion: input.dataset.version || null
        }));
        try { await api('tenant-values', 'PUT', dto); alertMessage('Profile values saved.'); await loadValues(); }
        catch (e) { alertMessage(e.message, true); }
    }
    el('cfForm').addEventListener('submit', saveDefinition);
    el('cfValuesForm').addEventListener('submit', saveValues);
    el('cfReset').addEventListener('click', reset);
    el('cfType').addEventListener('change', toggleChoices);
    el('cfPrev').addEventListener('click', () => { if (state.page > 1) { state.page--; loadDefinitions().catch(e => alertMessage(e.message, true)); } });
    el('cfNext').addEventListener('click', () => { if (state.page < state.totalPages) { state.page++; loadDefinitions().catch(e => alertMessage(e.message, true)); } });
    Promise.all([loadDefinitions(), loadValues()]).catch(e => alertMessage(e.message, true));
})();
