import { setupGuardianSearch } from './guardian-search.js';
import { setupGuardianSave } from './guardian-save.js';
const $ = id => document.getElementById(id);
const root = $('guardianWorkspace');
if (root) {
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { student: null, requestId: null };
    const notice = message => { $('guardianNotice').textContent = message; };
    async function api(url, method = 'GET', body = null) {
        const response = await fetch(url, { method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json', ...(body ? { 'Content-Type': 'application/json' } : {}),
                ...(method !== 'GET' ? { RequestVerificationToken: token } : {}) },
            ...(body ? { body: JSON.stringify(body) } : {}) });
        const json = await response.json().catch(() => null);
        if (!response.ok || !json?.success) throw Error(json?.message || 'Request failed');
        return json.data;
    }
    function render(student) {
        state.student = student;
        $('guardianSelected').textContent = student.studentName + ' (' + student.studentCode + ')';
        $('guardianCreate').disabled = false;
        const box = $('guardianLinks'); box.replaceChildren();
        for (const g of student.guardians || []) {
            const row = document.createElement('p');
            row.textContent = [g.fullName, g.relationCode, g.isPrimary ? 'Primary' : ''].filter(Boolean).join(' · ');
            box.append(row);
        }
    }
    setupGuardianSearch({ $, api, state, render, notice });
    setupGuardianSave({ $, api, state, render, notice });
}
