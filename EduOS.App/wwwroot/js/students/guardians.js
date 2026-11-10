import { setupGuardianSearch } from './guardian-search.js';
import { setupGuardianSave } from './guardian-save.js';
import { setupGuardianReuse } from './guardian-reuse.js';
import { renderGuardianLinks } from './guardian-link-edit.js';
import { renderRevokedGuardianLinks } from './guardian-reactivation.js';
import { setupRevokedGuardianPaging } from './guardian-revoked-paging.js';
import { setupGuardianHistory } from './guardian-history.js';
import { setupGuardianContact } from './guardian-contact-profile.js';

const $ = id => document.getElementById(id);
const root = $('guardianWorkspace');
if (root) {
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const identity = [document.body?.dataset?.authTenantId || '',
        document.body?.dataset?.authSessionReference || ''];
    const sameSession = () => identity[0] === (document.body?.dataset?.authTenantId || '') &&
        identity[1] === (document.body?.dataset?.authSessionReference || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
    const state = { student: null, requestId: null, requestFingerprint: null,
        busy: false, selecting: false, requiresRefresh: false, searchVersion: 0,
        selectionVersion: 0, guards: [], sameSession };
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
    const guardianHistory = setupGuardianHistory({ $, api, state });
    const revokedPaging = setupRevokedGuardianPaging({ $, api, state, renderRevokedGuardianLinks, notice, render: student => render(student) });
    function refreshActions() {
        const locked = state.busy || state.selecting || !sameSession();
        $('guardianCreate').disabled = locked || !state.student || state.requiresRefresh;
        $('guardianReuse').disabled = locked || !state.student || state.requiresRefresh;
        $('guardianRefresh').disabled = locked || !state.student;
        state.updateContactAction?.();
    }
    state.refreshActions = refreshActions;
    state.confirmStudentSwitch = () => {
        if (state.busy || state.selecting || !sameSession()) return false;
        const dirty = state.guards.find(guard => guard?.isDirty());
        if (dirty) return dirty.confirmDiscard();
        const openInlineAction = ['guardianLinks', 'guardianRevokedLinks'].some(id =>
            Array.from($(id).querySelectorAll('form')).some(form => !form.classList.contains('d-none')));
        return !openInlineAction || window.confirm('Switch student and discard the open guardian action?');
    };
    state.setSelecting = value => {
        state.selecting = value;
        for (const id of ['guardianCreateForm', 'guardianReuseForm', 'guardianContactForm',
            'guardianLinks', 'guardianRevokedLinks'])
            $(id).inert = value;
        refreshActions();
    };
    function render(student) {
        if (!sameSession() || !student?.studentReference) return;
        const previousReference = state.student?.studentReference;
        state.student = student;
        if (previousReference !== student.studentReference) {
            for (const id of ['guardianCreateForm', 'guardianReuseForm']) {
                $(id).reset();
                $(id).__guardianGuard?.markClean();
            }
            state.requestId = null; state.requestFingerprint = null;
            document.dispatchEvent(new CustomEvent('guardian:student-selected', { detail: { studentReference: student.studentReference } }));
        }
        $('guardianSelected').textContent = student.studentName + ' (' + student.studentCode + ')';
        refreshActions();
        renderGuardianLinks({ $, api, state, render, notice });
        revokedPaging.refresh(student.studentReference);
        guardianHistory.refresh();
    }
    $('guardianRefresh').addEventListener('click', async () => {
        if (!state.student || state.busy || state.selecting || !sameSession()) return;
        const reference = state.student.studentReference;
        const request = ++state.selectionVersion;
        try {
            const updated = await api('/api/student-guardians/students/' + encodeURIComponent(reference));
            if (request !== state.selectionVersion || state.student?.studentReference !== reference ||
                state.busy || state.selecting || !sameSession()) return;
            if (updated?.studentReference !== reference) throw Error('Selected student did not match the response.');
            state.requiresRefresh = false;
            render(updated);
            notice('Guardian relationships refreshed.');
        } catch (error) { if (request === state.selectionVersion && sameSession()) notice(error.message, true); }
    });
    setupGuardianSearch({ $, api, state, render, notice });
    setupGuardianSave({ $, api, state, render, notice });
    setupGuardianReuse({ $, api, state, render, notice });
    setupGuardianContact({ $, api, state, render, notice });
    const reference = new URLSearchParams(location.search).get('studentReference');
    if (reference && /^[0-9a-f-]{36}$/i.test(reference)) {
        const request = ++state.selectionVersion;
        api('/api/student-guardians/students/' + encodeURIComponent(reference))
            .then(student => {
                if (request !== state.selectionVersion || !sameSession()) return;
                if (student?.studentReference !== reference) throw Error('Selected student did not match the response.');
                state.requiresRefresh = false;
                render(student);
            })
            .catch(error => { if (request === state.selectionVersion && sameSession()) notice(error.message, true); });
    }
}
