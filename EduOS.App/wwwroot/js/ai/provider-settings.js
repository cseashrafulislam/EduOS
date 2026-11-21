(() => {
    'use strict';
    const aiApiRoot = '/api/ai/provider-settings';
    const form = document.getElementById('aiGatewayForm');
    if (!form) return;

    const tenantSession = [document.body?.dataset.authTenantId || '',
        document.body?.dataset.authSessionReference || ''];
    const sameSession = () => tenantSession[0] === (document.body?.dataset.authTenantId || '') &&
        tenantSession[1] === (document.body?.dataset.authSessionReference || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
    let aiSaving = false, aiSettings = null, aiLoaded = false;
    window.EduOSAiProviderSaving = false;

    const valueOf = id => document.getElementById(id)?.value?.trim() || '';
    const isChecked = id => document.getElementById(id)?.checked === true;
    function setValue(id, value) {
        const node = document.getElementById(id);
        if (node) node.value = value ?? '';
    }
    function setChecked(id, checked) {
        const node = document.getElementById(id);
        if (node) node.checked = Boolean(checked);
    }
    function enteredSecret(id) {
        const value = valueOf(id);
        return value && value !== '********' ? value : null;
    }
    function showAlert(type, message) {
        const status = document.getElementById('aiGatewayStatus');
        if (!status || !sameSession()) return;
        status.className = 'small ms-2 text-' + (type === 'success' ? 'success' : 'danger');
        status.textContent = message || '';
    }
    async function api(url, method = 'GET', data) {
        const headers = { Accept: 'application/json' };
        if (data !== undefined) {
            headers['Content-Type'] = 'application/json';
            headers.RequestVerificationToken =
                document.querySelector('meta[name="request-verification-token"]')?.content || '';
        }
        const response = await fetch(url, {
            method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(data !== undefined ? { body: JSON.stringify(data) } : {})
        });
        const payload = await response.json().catch(() => null);
        if (!response.ok || payload?.success !== true)
            throw Error(payload?.message || 'AI provider configuration request failed.');
        return payload.data;
    }

    async function loadAiGateway() {
        aiLoaded = false;
        setAiFormEnabled(false);
        try {
            const config = await api(aiApiRoot);
            if (!sameSession()) return false;
            if (!config || config.providerCode !== 'OpenAI') throw Error('Invalid AI provider settings.');
            aiSettings = config;
            setValue('aiProviderModel', config.model || 'gpt-4.1-mini');
            setChecked('aiProviderEnabled', config.isEnabled);
            setChecked('aiStudentTutorEnabled', config.studentTutorEnabled);
            setChecked('aiClearApiKey', false);
            setValue('aiProviderApiKey', '');
            document.getElementById('aiProviderApiKey').placeholder = config.hasApiKey ? '********' : '';
            document.getElementById('aiGatewayStatus').textContent = config.hasApiKey
                ? 'API key saved securely' : 'No provider key saved';
            aiLoaded = true;
            return true;
        } catch {
            if (sameSession())
                document.getElementById('aiGatewayStatus').textContent = 'AI settings unavailable. Reload to retry.';
            return false;
        } finally {
            setAiFormEnabled(aiLoaded && sameSession());
        }
    }

    function setAiFormEnabled(enabled) {
        for (const id of ['aiProviderModel', 'aiProviderApiKey', 'aiProviderEnabled',
            'aiStudentTutorEnabled', 'aiClearApiKey', 'saveAiGatewayBtn']) {
            const element = document.getElementById(id);
            if (element) element.disabled = !enabled;
        }
        updateAiRequired();
    }

    function updateAiRequired() {
        const enabled = aiLoaded && isChecked('aiProviderEnabled');
        const key = document.getElementById('aiProviderApiKey');
        const tutor = document.getElementById('aiStudentTutorEnabled');
        const clear = document.getElementById('aiClearApiKey');
        if (!key || !tutor || !clear) return;
        if (!enabled) {
            tutor.checked = false;
            tutor.disabled = true;
        } else {
            tutor.disabled = aiSaving;
            clear.checked = false;
        }
        clear.disabled = !aiLoaded || aiSaving || enabled;
        key.required = enabled && !aiSettings?.hasApiKey;
        key.disabled = !aiLoaded || aiSaving || clear.checked;
        const submit = document.getElementById('saveAiGatewayBtn');
        if (submit) submit.disabled = !aiLoaded || aiSaving || !sameSession();
    }

    async function saveAiGateway(event) {
        event.preventDefault();
        const form = document.getElementById('aiGatewayForm');
        if (!aiLoaded || aiSaving || !sameSession() || !form.reportValidity()) return;
        const enabled = isChecked('aiProviderEnabled');
        const clear = !enabled && isChecked('aiClearApiKey');
        const dto = {
            providerCode: 'OpenAI',
            model: valueOf('aiProviderModel'),
            apiKey: clear ? null : enteredSecret('aiProviderApiKey'),
            isEnabled: enabled,
            studentTutorEnabled: enabled && isChecked('aiStudentTutorEnabled'),
            clearApiKey: clear,
            rowVersion: aiSettings?.rowVersion || null
        };
        aiSaving = true;
        window.EduOSAiProviderSaving = true;
        updateAiRequired();
        try {
            const settings = await api(aiApiRoot, 'PUT', dto);
            if (!sameSession()) return;
            aiSettings = settings;
            setValue('aiProviderApiKey', '');
            setChecked('aiClearApiKey', false);
            document.getElementById('aiProviderApiKey').placeholder = settings.hasApiKey ? '********' : '';
            document.getElementById('aiGatewayStatus').textContent = 'AI provider settings saved securely.';
            showAlert('success', 'Institution AI settings saved securely.');
        } catch (error) {
            if (sameSession()) {
                document.getElementById('aiGatewayStatus').textContent =
                    'AI settings not saved. Review and retry.';
                showAlert('danger', error.message || 'Could not save AI settings.');
            }
        } finally {
            aiSaving = false;
            window.EduOSAiProviderSaving = false;
            if (sameSession()) updateAiRequired();
        }
    }


    async function loadAgents() {
        const host = document.getElementById('aiAgentRows');
        const notice = document.getElementById('aiAgentNotice');
        if (!host || !notice) return;
        try {
            const rows = await api(aiApiRoot + '/agents');
            if (!sameSession() || !Array.isArray(rows)) return;
            host.replaceChildren();
            for (const policy of rows) {
                if (!policy || typeof policy.code !== 'string' ||
                    typeof policy.title !== 'string' ||
                    typeof policy.isEnabled !== 'boolean') continue;
                const row = document.createElement('div');
                row.className = 'border rounded p-2 d-flex align-items-center justify-content-between gap-2 flex-wrap';
                const text = document.createElement('div');
                const title = document.createElement('strong');
                title.className = 'small';
                title.textContent = policy.title;
                const desc = document.createElement('div');
                desc.className = 'text-muted small';
                desc.textContent = policy.description || '';
                text.append(title, desc);
                const controls = document.createElement('div');
                controls.className = 'd-flex align-items-center gap-2';
                const label = document.createElement('label');
                label.className = 'small d-flex align-items-center gap-2 mb-0';
                const checkbox = document.createElement('input');
                checkbox.type = 'checkbox';
                checkbox.checked = policy.isEnabled;
                checkbox.setAttribute('aria-label', policy.title + ' enabled');
                const name = document.createElement('span');
                name.textContent = 'Enabled';
                label.append(checkbox, name);
                const save = document.createElement('button');
                save.type = 'button';
                save.className = 'btn btn-outline-primary btn-sm';
                save.textContent = 'Save';
                save.disabled = true;
                checkbox.addEventListener('change', () => {
                    save.disabled = !sameSession() || checkbox.checked === policy.isEnabled;
                });
                save.addEventListener('click', async () => {
                    if (!sameSession() || save.disabled || window.EduOSAiProviderSaving) return;
                    save.disabled = true;
                    checkbox.disabled = true;
                    window.EduOSAiProviderSaving = true;
                    try {
                        await api(aiApiRoot + '/agents/' + encodeURIComponent(policy.code), 'PUT',
                            { isEnabled: checkbox.checked, rowVersion: policy.rowVersion });
                        if (sameSession()) {
                            notice.textContent = 'AI capability updated.';
                            await loadAgents();
                        }
                    } catch (error) {
                        if (sameSession()) {
                            notice.textContent = error.message || 'AI capability update failed.';
                            await loadAgents();
                        }
                    } finally {
                        window.EduOSAiProviderSaving = false;
                    }
                });
                controls.append(label, save);
                row.append(text, controls);
                host.append(row);
            }
            notice.textContent = rows.length ? '' : 'No configurable AI capabilities.';
        } catch {
            if (sameSession()) notice.textContent = 'AI capability settings unavailable.';
        }
    }

    async function loadUsage() {
        const node = document.getElementById('aiUsageSummary');
        const rows = document.getElementById('aiUsageRows');
        if (!node || !rows || !sameSession()) return;
        try {
            const data = await api(aiApiRoot + '/usage?days=30');
            if (!sameSession() || !data || !Array.isArray(data.days)) return;
            node.textContent = 'Last ' + data.periodDays + ' UTC days: ' +
                data.totalReservedCalls + ' reserved attempts · ' +
                data.observedInputTokens + ' observed input tokens · ' +
                data.observedOutputTokens + ' observed output tokens · daily limit ' +
                data.dailyLimit + ' · actual provider charges not available here.';
            rows.replaceChildren();
            for (const entry of data.days) {
                if (!entry || typeof entry.usageDateUtc !== 'string' ||
                    !Number.isInteger(entry.reservedCalls)) continue;
                const tr = document.createElement('tr');
                const date = document.createElement('td');
                date.textContent = entry.usageDateUtc;
                const attempts = document.createElement('td');
                attempts.textContent = String(entry.reservedCalls);
                const input = document.createElement('td');
                input.textContent = String(entry.observedInputTokens ?? 0);
                const output = document.createElement('td');
                output.textContent = String(entry.observedOutputTokens ?? 0);
                tr.append(date, attempts, input, output);
                rows.append(tr);
            }
        } catch {
            if (sameSession()) node.textContent = 'Usage report unavailable.';
        }
    }

    document.addEventListener('DOMContentLoaded', async () => {
        form.addEventListener('submit', saveAiGateway);
        document.getElementById('aiProviderEnabled')?.addEventListener('change', updateAiRequired);
        document.getElementById('aiClearApiKey')?.addEventListener('change', updateAiRequired);
        setAiFormEnabled(false);
        await Promise.all([loadAiGateway(), loadUsage(), loadAgents()]);
    }, { once: true });
})();
