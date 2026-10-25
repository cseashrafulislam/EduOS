(() => {
    'use strict';
    const byId = id => document.getElementById(id);
    const form = byId('hostelBedNumberForm');
    if (!form) return;
    const input = byId('hostelBedNumberValue'), status = byId('hostelBedNumberStatus');
    const save = byId('hostelBedNumberSave'), cancel = byId('hostelBedNumberCancel');
    let selected = null, busy = false;
    function message(text, error = false) {
        status.textContent = text;
        status.className = error ? 'small mt-2 text-danger' : 'small mt-2 text-muted';
    }
    function close() {
        if (busy) return;
        selected = null;
        input.value = '';
        form.classList.add('d-none');
        message('');
    }
    document.addEventListener('hostel:correct-bed-number', event => {
        if (busy) return;
        const bed = event.detail;
        if (!bed || bed.hasAllocationHistory || !Number.isSafeInteger(Number(bed.id)) || Number(bed.id) <= 0 ||
            !bed.rowVersion) return;
        selected = { id: bed.id, rowVersion: bed.rowVersion };
        byId('hostelBedNumberContext').textContent =
            String(bed.hostelName || '') + ' / ' + String(bed.roomNumber || '') + ' / ' + String(bed.bedNumber || '');
        input.value = String(bed.bedNumber || '');
        form.classList.remove('d-none');
        message('');
        input.focus();
    });
    cancel.addEventListener('click', close);
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (busy || !selected) return;
        const bedNumber = input.value.trim().toUpperCase();
        if (!bedNumber || bedNumber.length > 50 || /[\x00-\x1f\x7f-\x9f]/.test(bedNumber)) {
            message('Enter a valid bed number (maximum 50 characters).', true);
            return;
        }
        const token = document.querySelector('meta[name="request-verification-token"]')?.content;
        if (!token) { message('Security token missing. Reload the page.', true); return; }
        const snapshot = selected;
        busy = true; save.disabled = true; cancel.disabled = true;
        try {
            const response = await fetch('/api/hostel/beds/' + encodeURIComponent(snapshot.id) + '/number', {
                method: 'PUT', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json', 'Content-Type': 'application/json', RequestVerificationToken: token },
                body: JSON.stringify({ bedNumber, rowVersion: snapshot.rowVersion })
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success) throw new Error(result?.message || 'Bed number correction failed.');
            selected = null;
            form.classList.add('d-none');
            message('');
            document.dispatchEvent(new CustomEvent('hostel:bed-number-corrected'));
        } catch (error) {
            message(error.message || 'Bed number correction failed.', true);
        } finally {
            busy = false; save.disabled = false; cancel.disabled = false;
        }
    });
})();
