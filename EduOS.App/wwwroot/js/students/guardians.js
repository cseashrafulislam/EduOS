import { setupGuardianSearch } from './guardian-search.js';
import { setupGuardianSave } from './guardian-save.js';
import { setupGuardianReuse } from './guardian-reuse.js';
import { renderGuardianLinks } from './guardian-link-edit.js';

const $ = id => document.getElementById(id);
const root = $('guardianWorkspace');
if (root) {
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { student: null, requestId: null, busy: false };
    const notice = (message, error = false) => {
        const box = $('guardianNotice');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-info');
        box.textContent = message;
    };
    async function api(url, method = 'GET', body = null) {
        const response = await fetch(url, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers: {
                Accept: 'application/json',
                ...(body ? { 'Content-Type': 'application/json' } : {}),
                ...(method !== 'GET' ? { RequestVerificationToken: token } : {})
            },
            ...(body ? { body: JSON.stringify(body) } : {})
        });
        const json = await response.json().catch(() => null);
        if (!response.ok || json?.success !== true)
            throw Error(json?.message || 'Request failed. Refresh and retry.');
        return json.data;
    }
    function render(student) {
        state.student = student;
        $('guardianSelected').textContent = student.studentName + ' (' + student.studentCode + ')';
        $('guardianCreate').disabled = state.busy;
        $('guardianReuse').disabled = state.busy;
        $('guardianRefresh').disabled = state.busy;
        renderGuardianLinks({ $, api, state, render, notice });
    }
    $('guardianRefresh').addEventListener('click', async () => {
        if (!state.student || state.busy) return;
        try {
            render(await api('/api/student-guardians/students/' +
                encodeURIComponent(state.student.studentReference)));
            notice('Guardian relationships refreshed.');
        } catch (error) { notice(error.message, true); }
    });
    setupGuardianSearch({ $, api, state, render, notice });
    setupGuardianSave({ $, api, state, render, notice });
    setupGuardianReuse({ $, api, state, render, notice });
    const reference = new URLSearchParams(location.search).get('studentReference');
    if (reference && /^[0-9a-f-]{36}$/i.test(reference)) {
        api('/api/student-guardians/students/' + encodeURIComponent(reference))
            .then(render).catch(error => notice(error.message, true));
    }
}
