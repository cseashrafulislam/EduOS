(() => {
    'use strict';
    const form = document.getElementById('identityAddressForm');
    const student = document.getElementById('identityStudentReference');
    if (!form || !student) return;
    const type = document.getElementById('identityAddressType');
    const status = document.getElementById('identityAddressStatus');
    const save = document.getElementById('identityAddressSave');
    const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const fields = ['AddressLine1', 'AddressLine2', 'City', 'District', 'DivisionOrState', 'PostalCode', 'CountryCode'];
    const records = new Map();
    let sequence = 0;
    let busy = false;
    let pending = null;
    const el = name => document.getElementById('identity' + name);
    function inform(message, success) {
        status.className = 'small ' + (success ? 'text-success' : 'text-danger');
        status.textContent = message;
    }
    function clear() {
        records.clear();
        pending = null;
        for (const name of fields) el(name).value = name === 'CountryCode' ? 'BD' : '';
        el('AddressRowVersion').value = '';
        save.disabled = !student.value;
    }
    function display() {
        const row = records.get(type.value);
        for (const name of fields)
            el(name).value = row ? (row[name.charAt(0).toLowerCase() + name.slice(1)] || '') :
                (name === 'CountryCode' ? 'BD' : '');
        el('AddressRowVersion').value = row?.rowVersion || '';
        pending = null;
    }
    async function load() {
        const ref = student.value;
        const mySequence = ++sequence;
        clear();
        if (!ref) { inform('Select a student to manage identity addresses.', true); return; }
        save.disabled = true;
        inform('Loading structured addresses...', true);
        try {
            const response = await fetch('/api/learner-identities/students/' + encodeURIComponent(ref) + '/addresses', {
                credentials: 'same-origin', cache: 'no-store', referrerPolicy: 'no-referrer',
                headers: { Accept: 'application/json' }
            });
            const data = await response.json().catch(() => null);
            if (mySequence !== sequence || ref !== student.value) return;
            if (!response.ok || !data?.success || !Array.isArray(data.data))
                throw new Error(data?.message || 'Address access is unavailable.');
            records.clear();
            for (const row of data.data) records.set(row.addressTypeCode, row);
            display();
            inform('Current and permanent addresses are editable here. Historical student snapshots remain unchanged.', true);
            save.disabled = false;
        } catch (error) {
            if (mySequence !== sequence) return;
            inform(error.message || 'Could not load addresses.', false);
            save.disabled = true;
        }
    }
    type.addEventListener('change', () => { display(); inform('', true); });
    student.addEventListener('change', load);
    form.addEventListener('input', () => { pending = null; });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (busy || !student.value || save.disabled) return;
        if (!form.reportValidity()) return;
        busy = true;
        save.disabled = true;
        const ref = student.value, selectedType = type.value;
        const payload = pending || {
            clientRequestId: crypto.randomUUID(),
            rowVersion: el('AddressRowVersion').value
        };
        if (!pending) for (const name of fields)
            payload[name.charAt(0).toLowerCase() + name.slice(1)] = el(name).value.trim();
        pending = payload;
        inform('Saving structured address...', true);
        try {
            const response = await fetch('/api/learner-identities/students/' + encodeURIComponent(ref) +
                '/addresses/' + encodeURIComponent(selectedType), {
                method: 'PUT', credentials: 'same-origin', cache: 'no-store', referrerPolicy: 'no-referrer',
                headers: { 'Content-Type': 'application/json', Accept: 'application/json', RequestVerificationToken: token },
                body: JSON.stringify(payload)
            });
            const data = await response.json().catch(() => null);
            if (!response.ok || !data?.success) throw new Error(data?.message || 'Address could not be saved.');
            pending = null;
            if (ref === student.value && selectedType === type.value) await load();
            inform('Structured address saved.', true);
        } catch (error) {
            inform(error.message || 'Save failed. Retry without changing fields to use the same request ID.', false);
        } finally {
            busy = false;
            if (student.value) save.disabled = false;
        }
    });
    clear();
    inform('Select a student to manage identity addresses.', true);
})();
