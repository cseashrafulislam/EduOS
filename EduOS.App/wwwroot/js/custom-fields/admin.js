(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const state = { page: 1, totalPages: 1, rows: [], values: [], definitions: [] };
    let saving = false, definitionSeq = 0, valueSeq = 0;
    const context = [document.body?.dataset.authTenantId || '', document.body?.dataset.authSessionReference || ''];
    const live = () => context[0] === (document.body?.dataset.authTenantId || '') &&
        context[1] === (document.body?.dataset.authSessionReference || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
    const definitionGuard = window.EduOSFormGuard?.create(el('cfForm'));
    const valuesGuard = window.EduOSFormGuard?.create(el('cfValuesForm'));
    const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value ||
        document.querySelector('meta[name="RequestVerificationToken"]')?.content || '';
    function alertMessage(message, error = false) {
        const box = el('cfNotice'); box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = message; box.scrollIntoView({ block: 'nearest' });
    }
    async function api(path, method = 'GET', body) {
        if (!live()) throw Error('Institution session changed. Reload the page.');
        const headers = { Accept: 'application/json' };
        if (method !== 'GET') {
            const csrf = token();
            if (!csrf) throw Error('Missing antiforgery token. Reload the page.');
            headers['Content-Type'] = 'application/json'; headers.RequestVerificationToken = csrf;
        }
        const response = await fetch('/api/custom-fields/' + path, {
            method, credentials: 'same-origin', cache: 'no-store', headers,
            body: body === undefined ? undefined : JSON.stringify(body)
        });
        const json = await response.json().catch(() => ({}));
        if (!live()) throw Error('Institution session changed. Reload the page.');
        if (!response.ok || !json.success) throw Error(json.message || 'Request failed (' + response.status + ')');
        return json.data;
    }
    function lock(form) {
        const fields = [...form.querySelectorAll('input, select, textarea, button')];
        const previous = fields.map(x => x.disabled);
        fields.forEach(x => { x.disabled = true; });
        return () => fields.forEach((x, index) => { x.disabled = previous[index]; });
    }
    const td = (tr, value) => { const cell = document.createElement('td'); cell.textContent = value == null ? '' : String(value); tr.append(cell); return cell; };
    function reset() {
        el('cfForm').reset(); el('cfId').value = ''; el('cfVersion').value = '';
        el('cfKey').disabled = false; el('cfType').disabled = false; toggleChoices();
        definitionGuard?.markClean();
    }
    function toggleChoices() { el('cfChoicesWrap').classList.toggle('d-none', el('cfType').value !== '7'); }
    function edit(id) {
        if (saving || !live() || definitionGuard?.confirmDiscard() === false) return;
        const d = state.rows.find(x => x.id === id); if (!d) return;
        el('cfId').value = d.id; el('cfVersion').value = d.rowVersion;
        el('cfKey').value = d.fieldKey; el('cfKey').disabled = true;
        el('cfLabel').value = d.label; el('cfType').value = d.dataType; el('cfType').disabled = true;
        el('cfRequired').value = String(d.isRequired); el('cfActive').value = String(d.isActive);
        el('cfOrder').value = d.displayOrder;
        el('cfChoices').value = (d.options || []).filter(o => o.isActive).map(o => o.value + ' | ' + o.label).join('\n');
        toggleChoices(); el('cfForm').scrollIntoView({ behavior: 'smooth' });
        definitionGuard?.markClean();
    }
    async function loadDefinitions() {
        const seq = ++definitionSeq;
        const data = await api('definitions?page=' + state.page + '&pageSize=20');
        if (seq !== definitionSeq || !live()) return;
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
        if (saving || !live()) return;
        const id = Number(el('cfId').value) || null;
        const dto = { entityType: 'Tenant', fieldKey: el('cfKey').value.trim(), label: el('cfLabel').value.trim(),
            dataType: Number(el('cfType').value), isRequired: el('cfRequired').value === 'true',
            isActive: el('cfActive').value === 'true', displayOrder: Number(el('cfOrder').value) || 0,
            isSensitive: false, validationJson: null, options: options(), rowVersion: el('cfVersion').value || null };
        saving = true; const unlock = lock(el('cfForm'));
        try {
            await api('definitions' + (id ? '/' + id : ''), id ? 'PUT' : 'POST', dto);
            reset(); alertMessage('Definition saved.');
            try {
                await loadDefinitions();
                if (!valuesGuard?.isDirty()) await loadValues();
            } catch (error) { alertMessage('Definition saved; refresh the register. ' + error.message, true); }
        } catch (e) { alertMessage(e.message, true); }
        finally { saving = false; unlock(); }
    }
    async function loadValues(force = false) {
        const seq = ++valueSeq;
        const values = await api('tenant-values'), definitions = [];
        let page = 1, pages = 1;
        do {
            const result = await api('definitions?page=' + page + '&pageSize=100');
            definitions.push(...(result.items || [])); pages = result.totalPages || 1;
            if (pages > 100) throw Error('Too many profile fields to render safely; contact support.');
            page++;
        } while (page <= pages);
        if (seq !== valueSeq || !live() || (!force && valuesGuard?.isDirty())) return;
        state.values = values; state.definitions = definitions;
        const definitionById = new Map(definitions.map(definition => [definition.id, definition]));
        el('cfValueInputs').replaceChildren();
        state.values.forEach(v => {
            const def = definitionById.get(v.customFieldDefinitionId);
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
        valuesGuard?.markClean();
    }
    async function saveValues(event) {
        event.preventDefault();
        if (saving || !live()) return;
        const dto = [...el('cfValueInputs').querySelectorAll('[data-field-id]')].map(input => ({
            customFieldDefinitionId: Number(input.dataset.fieldId), value: input.value || null,
            rowVersion: input.dataset.version || null
        }));
        saving = true; const unlock = lock(el('cfValuesForm'));
        try {
            await api('tenant-values', 'PUT', dto);
            valuesGuard?.markClean(); alertMessage('Profile values saved.');
            try { await loadValues(true); }
            catch (error) { alertMessage('Profile values saved; refresh the form. ' + error.message, true); }
        }
        catch (e) { alertMessage(e.message, true); }
        finally { saving = false; unlock(); }
    }
    el('cfForm').addEventListener('submit', saveDefinition);
    el('cfValuesForm').addEventListener('submit', saveValues);
    el('cfReset').addEventListener('click', () => {
        if (!saving && definitionGuard?.confirmDiscard() !== false) reset();
    });
    el('cfType').addEventListener('change', toggleChoices);
    el('cfPrev').addEventListener('click', () => { if (state.page > 1) { state.page--; loadDefinitions().catch(e => alertMessage(e.message, true)); } });
    el('cfNext').addEventListener('click', () => { if (state.page < state.totalPages) { state.page++; loadDefinitions().catch(e => alertMessage(e.message, true)); } });
    const observer = new MutationObserver(() => {
        if (live()) return;
        ++definitionSeq; ++valueSeq; state.rows = []; state.values = []; state.definitions = [];
        el('cfRows').replaceChildren(); el('cfValueInputs').replaceChildren();
        reset(); valuesGuard?.markClean();
        alertMessage('Institution session changed. Reload the page.', true); observer.disconnect();
    });
    observer.observe(document.body, { attributes: true,
        attributeFilter: ['data-auth-tenant-id', 'data-auth-session-reference'] });
    Promise.all([loadDefinitions(), loadValues()]).catch(e => alertMessage(e.message, true));
})();
