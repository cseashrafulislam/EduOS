(() => {
    'use strict';
    const form = document.getElementById('workspacePreferencesForm');
    if (!form) return;
    const fields = ['theme', 'palette', 'navigation', 'density', 'fontScale'];
    const defaults = { theme: 'system', palette: 'blue', navigation: 'left', density: 'comfortable', fontScale: 'normal' };
    const status = document.getElementById('workspaceStatus');
    const save = document.getElementById('workspaceSave');
    const reset = document.getElementById('workspaceReset');
    const bangla = document.documentElement.lang === 'bn-BD';
    let rowVersion = '';
    let persisted = { ...defaults };
    let busy = false;
    function message(english, bengali, error = false) {
        status.textContent = bangla ? bengali : english;
        status.classList.toggle('text-danger', error);
        status.classList.toggle('text-success', !error);
    }
    function fill(preferences) {
        fields.forEach(name => {
            const select = form.elements.namedItem(name);
            const candidate = preferences?.[name] ?? defaults[name];
            select.value = [...select.options].some(option => option.value === candidate) ? candidate : defaults[name];
        });
    }
    function values() {
        return Object.fromEntries(fields.map(name => [name, form.elements.namedItem(name).value]));
    }
    function preview() {
        window.EduOSApplyWorkspacePreferences?.(values());
    }
    function setBusy(value) {
        busy = value;
        save.disabled = value;
        reset.disabled = value;
        fields.forEach(name => { form.elements.namedItem(name).disabled = value; });
    }
    async function request(url, method, payload) {
        const response = await fetch(url, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        const body = await response.json().catch(() => null);
        if (!response.ok || !body?.success) {
            const error = new Error(body?.message || 'Request failed.');
            error.conflict = response.status === 409;
            throw error;
        }
        return body.data;
    }
    async function load() {
        setBusy(true);
        try {
            const data = await request('/api/workspace-preferences', 'GET');
            persisted = { ...defaults, ...data };
            rowVersion = data.rowVersion || '';
            fill(persisted);
            preview();
            message('Appearance loaded.', 'ডিজাইন লোড হয়েছে।');
        } catch {
            fill(defaults);
            preview();
            message('Could not load saved appearance. Try refreshing.', 'সংরক্ষিত ডিজাইন লোড হয়নি। আবার চেষ্টা করুন।', true);
        } finally { setBusy(false); }
    }
    async function commit(url, payload) {
        if (busy) return;
        setBusy(true);
        try {
            const data = await request(url, url.endsWith('/reset') ? 'POST' : 'PUT', payload);
            persisted = { ...defaults, ...data };
            rowVersion = data.rowVersion || '';
            fill(persisted);
            preview();
            message('Appearance saved for this institution.', 'এই প্রতিষ্ঠানের জন্য ডিজাইন সংরক্ষিত হয়েছে।');
        } catch (error) {
            if (error.conflict) {
                message('Another device changed your design. Reloading current preferences.', 'অন্য ডিভাইসে ডিজাইন বদলেছে। নতুন তথ্য লোড হচ্ছে।', true);
                await load();
            } else {
                message('Could not save appearance. Please retry.', 'ডিজাইন সংরক্ষণ হয়নি। আবার চেষ্টা করুন।', true);
            }
        } finally { setBusy(false); }
    }
    form.addEventListener('change', preview);
    form.addEventListener('submit', event => {
        event.preventDefault();
        commit('/api/workspace-preferences', { schemaVersion: 1, ...values(), rowVersion });
    });
    reset.addEventListener('click', () => commit('/api/workspace-preferences/reset', { rowVersion }));
    load();
})();
