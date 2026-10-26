(() => {
    'use strict';
    const form = document.getElementById('studentContactForm');
    if (!form) return;
    const status = document.getElementById('studentContactStatus');
    const save = document.getElementById('studentContactSave');
    const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { reference: '', version: '', busy: false, requestId: '' };
    const fields = {
        phone: document.getElementById('studentContactPhone'),
        email: document.getElementById('studentContactEmail'),
        address: document.getElementById('studentContactAddress'),
        preferredLanguage: document.getElementById('studentContactLanguage'),
        reason: document.getElementById('studentContactReason')
    };
    function inform(message, good) {
        status.textContent = message;
        status.className = 'small ' + (good ? 'text-success' : 'text-danger');
    }
    function requestId() {
        return crypto.randomUUID ? crypto.randomUUID() :
            'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
                const r = crypto.getRandomValues(new Uint8Array(1))[0] & 15;
                return (c === 'x' ? r : (r & 3 | 8)).toString(16);
            });
    }
    async function load(reference) {
        state.reference = reference;
        state.version = '';
        state.requestId = '';
        save.disabled = true;
        inform('Loading contact details...', true);
        try {
            const response = await fetch('/api/students/' + encodeURIComponent(reference) + '/contact', {
                credentials: 'same-origin', cache: 'no-store', referrerPolicy: 'no-referrer',
                headers: { Accept: 'application/json' }
            });
            const body = await response.json().catch(() => null);
            if (!response.ok || !body?.success || !body.data || state.reference !== reference)
                throw new Error(body?.message || 'Unable to load contact profile.');
            const item = body.data;
            for (const key of ['phone', 'email', 'address']) fields[key].value = item[key] || '';
            fields.preferredLanguage.value = item.preferredLanguage || 'bn-BD';
            fields.reason.value = '';
            state.version = item.rowVersion || '';
            save.disabled = false;
            inform('', true);
        } catch (error) {
            if (state.reference === reference) inform(error.message || 'Unable to load contact profile.', false);
        }
    }
    document.addEventListener('student:opened', event => {
        if (event.detail?.reference) load(event.detail.reference);
    });
    form.addEventListener('input', () => { if (!state.busy) state.requestId = ''; });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (state.busy || !state.reference || !form.reportValidity()) return;
        state.busy = true;
        save.disabled = true;
        state.requestId ||= requestId();
        const reference = state.reference;
        const payload = {
            clientRequestId: state.requestId, rowVersion: state.version,
            phone: fields.phone.value.trim() || null,
            email: fields.email.value.trim() || null,
            address: fields.address.value.trim() || null,
            preferredLanguage: fields.preferredLanguage.value,
            reason: fields.reason.value.trim()
        };
        inform('Saving contact details...', true);
        try {
            const response = await fetch('/api/students/' + encodeURIComponent(reference) + '/contact', {
                method: 'PUT', credentials: 'same-origin', cache: 'no-store', referrerPolicy: 'no-referrer',
                headers: { 'Content-Type': 'application/json', Accept: 'application/json', RequestVerificationToken: token },
                body: JSON.stringify(payload)
            });
            const body = await response.json().catch(() => null);
            if (!response.ok || !body?.success || !body.data)
                throw new Error(body?.message || 'Could not save contact details.');
            state.version = body.data.rowVersion || '';
            state.requestId = '';
            fields.reason.value = '';
            inform('Contact details saved.', true);
            document.dispatchEvent(new CustomEvent('student:contact-updated', { detail: { reference } }));
        } catch (error) {
            inform(error.message || 'Save failed. Check the record before retrying.', false);
        } finally {
            state.busy = false;
            save.disabled = !state.version && !state.reference;
        }
    });
})();
