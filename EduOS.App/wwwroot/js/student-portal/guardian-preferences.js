(() => {
    'use strict';
    const root = document.getElementById('guardianPreferences');
    if (!root) return;
    const list = document.getElementById('guardianPreferenceList');
    const notice = document.getElementById('guardianPreferenceAlert');
    const refresh = document.getElementById('guardianPreferenceRefresh');
    const endpoint = '/api/guardian-communication-preferences';
    let loading = false;
    const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    function message(value, success = false) {
        notice.className = 'alert alert-' + (success ? 'success' : 'danger');
        notice.textContent = value;
    }
    async function api(path, method = 'GET', body = null) {
        const response = await fetch(path, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers: {
                Accept: 'application/json',
                ...(body ? { 'Content-Type': 'application/json', RequestVerificationToken: token() } : {})
            },
            ...(body ? { body: JSON.stringify(body) } : {})
        });
        const payload = await response.json().catch(() => null);
        if (!response.ok || !payload?.success) throw new Error(payload?.message || 'Unable to load preferences.');
        return payload.data;
    }
    function checkbox(label, value) {
        const wrap = document.createElement('label');
        wrap.className = 'form-check form-switch me-3';
        const input = document.createElement('input');
        input.type = 'checkbox'; input.className = 'form-check-input'; input.checked = Boolean(value);
        const text = document.createElement('span');
        text.className = 'form-check-label'; text.textContent = label;
        wrap.append(input, text);
        return { wrap, input };
    }
    function render(rows) {
        list.replaceChildren();
        if (!rows?.length) {
            const p = document.createElement('p');
            p.className = 'text-muted mb-0';
            p.textContent = 'No active student relationship is linked to this guardian account.';
            list.append(p); return;
        }
        for (const item of rows) {
            const card = document.createElement('div');
            card.className = 'border rounded p-3 mb-2';
            const title = document.createElement('h4');
            title.className = 'h6';
            title.textContent = item.studentName + ' · ' + item.studentCode + ' · ' + item.relationCode;
            const controls = document.createElement('div');
            controls.className = 'd-flex flex-wrap align-items-center gap-2';
            const sms = checkbox('Receive SMS', item.canReceiveSms);
            const email = checkbox('Receive email', item.canReceiveEmail);
            const save = document.createElement('button');
            save.type = 'button'; save.className = 'btn btn-primary btn-sm'; save.textContent = 'Save preferences';
            save.addEventListener('click', async () => {
                save.disabled = true;
                try {
                    await api(endpoint + '/' + encodeURIComponent(item.linkId), 'PUT', {
                        canReceiveSms: sms.input.checked, canReceiveEmail: email.input.checked,
                        rowVersion: item.rowVersion
                    });
                    message('Communication preferences saved.', true);
                    await load();
                } catch (error) {
                    message(error.message);
                    save.disabled = false;
                }
            });
            controls.append(sms.wrap, email.wrap, save);
            card.append(title, controls); list.append(card);
        }
    }
    async function load() {
        if (loading) return;
        loading = true; refresh.disabled = true;
        try {
            const rows = await api(endpoint);
            render(rows);
        } catch (error) {
            message(error.message);
        } finally {
            loading = false; refresh.disabled = false;
        }
    }
    refresh.addEventListener('click', load);
    load();
})();
