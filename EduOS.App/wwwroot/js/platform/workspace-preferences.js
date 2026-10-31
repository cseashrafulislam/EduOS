(() => {
    'use strict';
    const form = document.getElementById('workspacePreferencesForm');
    if (!form) return;
    const fields = ['theme', 'palette', 'navigation', 'density', 'fontScale'];
    const panelIds = ['capacity', 'quick-actions', 'setup-checklist'];
    const colorRows = [...form.querySelectorAll('[data-custom-color]')];
    const panelList = document.getElementById('workspaceDashboardPanels');
    const tableDefinitions = {
        'institution-users': { ids: ['user', 'membership', 'roles', 'action'], required: ['user', 'action'] },
        'institution-invitations': { ids: ['email', 'role', 'campus', 'expires', 'status', 'action'], required: ['email', 'status', 'action'] },
        'institution-feature-audit': { ids: ['feature', 'code', 'plan', 'override', 'restriction', 'reason'], required: ['feature', 'code', 'restriction'] },
        'institution-activity-audit': { ids: ['time', 'user', 'action', 'entity', 'record', 'ip', 'outcome'], required: ['time', 'user', 'action', 'outcome'] },
        'institution-campuses': { ids: ['campus', 'code', 'contact', 'type', 'action'], required: ['campus', 'code', 'action'] },
        'institution-change-history': { ids: ['when', 'action', 'fields', 'actor'], required: ['when', 'action', 'actor'] },
        'institution-settings': { ids: ['key', 'category', 'value', 'protection', 'action'], required: ['key', 'protection', 'action'] },
        'institution-permission-exceptions': { ids: ['permission', 'mode', 'reason', 'expires', 'action'], required: ['permission', 'mode', 'expires', 'action'] }
    };
    const tableLists = [...document.querySelectorAll('[data-table-row]')];
    const defaultTables = () => Object.entries(tableDefinitions).map(([id, definition]) => ({
        id, columns: definition.ids.map(column => ({ id: column, visible: true }))
    }));
    function validTables(tables) {
        return Array.isArray(tables) && tables.length === Object.keys(tableDefinitions).length
            && new Set(tables.map(t => t?.id)).size === Object.keys(tableDefinitions).length
            && tables.every(table => {
                const def = tableDefinitions[table?.id];
                return def && Array.isArray(table.columns) && table.columns.length === def.ids.length
                    && new Set(table.columns.map(c => c?.id)).size === def.ids.length
                    && table.columns.every(c => c && def.ids.includes(c.id) && typeof c.visible === 'boolean')
                    && def.required.every(id => table.columns.find(c => c.id === id).visible);
            });
    }
    function tableColumnRows(list) { return [...list.querySelectorAll('[data-column-row]')]; }
    function readTables() {
        if (!tableLists.length) return validTables(persisted.tablePreferences)
            ? persisted.tablePreferences : defaultTables();
        return tableLists.map(list => ({
            id: list.dataset.tableRow,
            columns: tableColumnRows(list).map(row => ({
                id: row.dataset.columnRow, visible: row.querySelector('input[type="checkbox"]').checked
            }))
        }));
    }
    function updateTableButtons() {
        tableLists.forEach(list => tableColumnRows(list).forEach((row, index, rows) => {
            row.querySelector('[data-column-move="up"]').disabled = busy || sessionInvalidated || index === 0;
            row.querySelector('[data-column-move="down"]').disabled = busy || sessionInvalidated || index === rows.length - 1;
        }));
    }
    function fillTables(tables) {
        const safe = validTables(tables) ? tables : defaultTables();
        tableLists.forEach(list => {
            const table = safe.find(x => x.id === list.dataset.tableRow);
            const rows = new Map(tableColumnRows(list).map(row => [row.dataset.columnRow, row]));
            table.columns.forEach(column => {
                const row = rows.get(column.id);
                row.querySelector('input[type="checkbox"]').checked = column.visible;
                list.querySelector('[data-table-columns]').appendChild(row);
            });
        });
        updateTableButtons();
    }
    const defaultPanels = () => panelIds.map(id => ({ id, visible: true }));
    const defaults = { theme: 'system', palette: 'blue', navigation: 'left', density: 'comfortable', fontScale: 'normal', customColors: {}, dashboardPanels: defaultPanels(), tablePreferences: defaultTables() };
    function validPanels(panels) {
        return Array.isArray(panels) && panels.length === panelIds.length
            && panels.every(p => p && panelIds.includes(p.id) && typeof p.visible === 'boolean')
            && new Set(panels.map(p => p.id)).size === panelIds.length;
    }
    function panelRows() { return [...panelList.querySelectorAll('[data-panel-row]')]; }
    function readPanels() {
        return panelRows().map(row => ({ id: row.dataset.panelRow, visible: row.querySelector('input[type="checkbox"]').checked }));
    }
    function updateMoveButtons() {
        panelRows().forEach((row, index) => {
            row.querySelector('[data-panel-move="up"]').disabled = busy || sessionInvalidated || index === 0;
            row.querySelector('[data-panel-move="down"]').disabled = busy || sessionInvalidated || index === panelIds.length - 1;
        });
    }
    function fillPanels(panels) {
        const safe = validPanels(panels) ? panels : defaultPanels();
        const byId = new Map(panelRows().map(row => [row.dataset.panelRow, row]));
        safe.forEach(panel => {
            const row = byId.get(panel.id);
            row.querySelector('input[type="checkbox"]').checked = panel.visible;
            panelList.appendChild(row);
        });
        updateMoveButtons();
    }
    const status = document.getElementById('workspaceStatus');
    const save = document.getElementById('workspaceSave');
    const reset = document.getElementById('workspaceReset');
    const bangla = document.documentElement.lang === 'bn-BD';
    let rowVersion = '';
    let persisted = { ...defaults };
    let busy = false;
    let sessionInvalidated = false;
    function message(english, bengali, error = false) {
        status.textContent = bangla ? bengali : english;
        status.classList.toggle('text-danger', error);
        status.classList.toggle('text-success', !error);
    }
    function fillColors(colors) {
        colorRows.forEach(row => {
            const raw = colors?.[row.dataset.customColor] || '';
            row.querySelector('[data-custom-color-text]').value = raw;
            row.querySelector('[data-custom-color-picker]').value = window.EduOSNormalizeWorkspaceColor?.(raw) || '#1D4ED8';
            row.querySelector('[data-custom-color-text]').removeAttribute('aria-invalid');
        });
    }
    function readColors() {
        const colors = {};
        let valid = true;
        colorRows.forEach(row => {
            const input = row.querySelector('[data-custom-color-text]');
            const raw = input.value.trim();
            const normalized = raw ? window.EduOSNormalizeWorkspaceColor?.(raw) : null;
            input.setAttribute('aria-invalid', String(!!raw && !normalized));
            if (raw && !normalized) valid = false;
            else if (normalized) colors[row.dataset.customColor] = normalized;
        });
        return valid ? colors : null;
    }
    function fill(preferences) {
        fields.forEach(name => {
            const select = form.elements.namedItem(name);
            const candidate = preferences?.[name] ?? defaults[name];
            select.value = [...select.options].some(option => option.value === candidate) ? candidate : defaults[name];
        });
        fillColors(preferences?.customColors);
        fillPanels(preferences?.dashboardPanels);
        fillTables(preferences?.tablePreferences);
    }
    function values() {
        const customColors = readColors();
        return customColors === null ? null : { ...Object.fromEntries(fields.map(name => [name, form.elements.namedItem(name).value])), customColors, dashboardPanels: readPanels(), tablePreferences: readTables() };
    }
    function preview() {
        const next = values();
        if (next && !sessionInvalidated) window.EduOSApplyWorkspacePreferences?.(next);
        else if (!next) message('Use a valid HEX or RGB color.', 'বৈধ HEX বা RGB রঙ দিন।', true);
    }
    function setBusy(value) {
        busy = value;
        const disabled = value || sessionInvalidated;
        save.disabled = disabled;
        reset.disabled = disabled;
        fields.forEach(name => { form.elements.namedItem(name).disabled = disabled; });
        colorRows.forEach(row => row.querySelectorAll('input,button').forEach(input => { input.disabled = disabled; }));
        panelRows().forEach(row => { row.querySelector('input[type="checkbox"]').disabled = disabled; });
        tableLists.forEach(list => tableColumnRows(list).forEach(row => {
            row.querySelector('input[type="checkbox"]').disabled = disabled || row.dataset.columnRequired === 'true';
        }));
        updateMoveButtons();
        updateTableButtons();
    }
    function requireCurrentSession(response) {
        if (!sessionInvalidated && window.EduOSWorkspaceResponseMatchesSession?.(response)) return;
        sessionInvalidated = true;
        setBusy(true);
        window.EduOSVerifySessionContext?.();
        const error = new Error('Institution session changed.');
        error.sessionChanged = true;
        throw error;
    }
    async function request(url, method, payload) {
        const controller = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), 10000);
        try {
            const response = await fetch(url, {
                method, credentials: 'same-origin', cache: 'no-store', signal: controller.signal,
                headers: { 'Content-Type': 'application/json',
                    'RequestVerificationToken': form.querySelector('input[name="__RequestVerificationToken"]')?.value || '' },
                body: JSON.stringify(payload)
            });
            requireCurrentSession(response);
            const body = await response.json().catch(() => null);
            // Do not apply an old institution's appearance after delayed decoding.
            requireCurrentSession(response);
            if (!response.ok || !body?.success) {
                const error = new Error(body?.message || 'Request failed.');
                error.conflict = response.status === 409;
                throw error;
            }
            return body.data;
        } finally {
            window.clearTimeout(timeout);
        }
    }
    async function load() {
        if (sessionInvalidated) return;
        setBusy(true);
        try {
            const data = await request('/api/workspace-preferences', 'GET');
            persisted = { ...defaults, ...data };
            rowVersion = data.rowVersion || '';
            fill(persisted);
            preview();
            message('Appearance loaded.', 'ডিজাইন লোড হয়েছে।');
        } catch (error) {
            if (error.sessionChanged) {
                message('Institution session changed. Reload this page.', 'প্রতিষ্ঠানের সেশন পরিবর্তিত হয়েছে। পেজটি আবার লোড করুন।', true);
                return;
            }
            fill(defaults);
            preview();
            message('Could not load saved appearance. Try refreshing.', 'সংরক্ষিত ডিজাইন লোড হয়নি। আবার চেষ্টা করুন।', true);
        } finally { setBusy(false); }
    }
    async function commit(url, payload) {
        if (busy || sessionInvalidated) return;
        setBusy(true);
        try {
            const data = await request(url, url.endsWith('/reset') ? 'POST' : 'PUT', payload);
            persisted = { ...defaults, ...data };
            rowVersion = data.rowVersion || '';
            fill(persisted);
            preview();
            message('Appearance saved for this institution.', 'এই প্রতিষ্ঠানের জন্য ডিজাইন সংরক্ষিত হয়েছে।');
        } catch (error) {
            if (error.sessionChanged) {
                message('Institution session changed. Reload this page.', 'প্রতিষ্ঠানের সেশন পরিবর্তিত হয়েছে। পেজটি আবার লোড করুন।', true);
                return;
            }
            if (error.conflict) {
                message('Another device changed your design. Reloading current preferences.', 'অন্য ডিভাইসে ডিজাইন বদলেছে। নতুন তথ্য লোড হচ্ছে।', true);
                await load();
            } else {
                message('Could not save appearance. Please retry.', 'ডিজাইন সংরক্ষণ হয়নি। আবার চেষ্টা করুন।', true);
            }
        } finally { setBusy(false); }
    }
    // One shared header Info drawer owns the full manual.
    colorRows.forEach(row => {
        const text = row.querySelector('[data-custom-color-text]');
        row.querySelector('[data-custom-color-picker]').addEventListener('input', event => {
            text.value = event.target.value.toUpperCase();
            preview();
        });
        row.querySelector('[data-custom-color-clear]').addEventListener('click', () => {
            text.value = '';
            preview();
            text.focus();
        });
    });
    form.addEventListener('input', event => {
        if (event.target.matches('[data-custom-color-text]')) preview();
    });
    form.addEventListener('change', preview);
    panelList.addEventListener('click', event => {
        const button = event.target.closest('[data-panel-move]');
        if (!button || busy || sessionInvalidated) return;
        const row = button.closest('[data-panel-row]');
        const rows = panelRows();
        const index = rows.indexOf(row);
        if (button.dataset.panelMove === 'up' && index > 0)
            panelList.insertBefore(row, rows[index - 1]);
        else if (button.dataset.panelMove === 'down' && index < rows.length - 1)
            panelList.insertBefore(rows[index + 1], row);
        updateMoveButtons();
        button.focus();
    });
    tableLists.forEach(list => list.addEventListener('click', event => {
        const button = event.target.closest('[data-column-move]');
        if (!button || busy || sessionInvalidated) return;
        const row = button.closest('[data-column-row]');
        const rows = tableColumnRows(list);
        const index = rows.indexOf(row);
        const columns = list.querySelector('[data-table-columns]');
        if (button.dataset.columnMove === 'up' && index > 0)
            columns.insertBefore(row, rows[index - 1]);
        else if (button.dataset.columnMove === 'down' && index < rows.length - 1)
            columns.insertBefore(rows[index + 1], row);
        updateTableButtons();
        button.focus();
    }));
    form.addEventListener('submit', event => {
        event.preventDefault();
        const next = values();
        if (!next) return message('Correct invalid colors before saving.', 'সংরক্ষণের আগে ভুল রঙ ঠিক করুন।', true);
        commit('/api/workspace-preferences', { schemaVersion: 1, ...next, rowVersion });
    });
    reset.addEventListener('click', () => commit('/api/workspace-preferences/reset', { rowVersion }));
    load();
})();
