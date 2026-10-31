(() => {
    'use strict';
    const panel = document.getElementById('hostelNoChargeExtensionPanel');
    if (!panel) return;
    const field = document.getElementById('hostelNoChargeExtensionEnd');
    const clearance = document.getElementById('hostelNoChargeExtensionClearance');
    const context = document.getElementById('hostelNoChargeExtensionContext');
    const save = document.getElementById('hostelNoChargeExtensionSave');
    const cancel = document.getElementById('hostelNoChargeExtensionCancel');
    let pending = null;
    let requestKey = null, requestSignature = null;
    const addDays = (value, days) => {
        const d = new Date(value + 'T00:00:00Z');
        d.setUTCDate(d.getUTCDate() + days);
        return d.toISOString().slice(0, 10);
    };
    const today = () => {
        const d = new Date();
        return [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'),
            String(d.getDate()).padStart(2, '0')].join('-');
    };
    const notify = (message, success = false) => {
        const alert = document.getElementById('hostelAlert');
        alert.className = 'alert alert-' + (success ? 'success' : 'danger');
        alert.textContent = message; alert.focus();
    };
    const dismiss = () => {
        pending = null; requestKey = null; requestSignature = null;
        field.value = ''; clearance.value = ''; panel.classList.add('d-none');
    };
    window.hostelNoChargeStayExtension = {
        begin(item) {
            if (!item?.endDate || item.endDate < today() ||
                item.endDate >= addDays(item.startDate, 89)) {
                notify('Only unexpired fixed-term stays with unused booking days can be extended.'); return;
            }
            pending = item;
            clearance.value = '';
            context.textContent = item.studentName + ' · ' + item.hostelName +
                ' · Room ' + item.roomNumber + ' / Bed ' + item.bedNumber +
                ' · Current checkout ' + item.endDate;
            field.min = addDays(item.endDate, 1);
            field.max = addDays(item.startDate, 89);
            field.value = field.min;
            panel.classList.remove('d-none');
            panel.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
    };
    cancel.addEventListener('click', dismiss);
    save.addEventListener('click', async () => {
        if (!pending) return;
        const endDate = field.value;
        if (!/^\d{4}-\d{2}-\d{2}$/.test(endDate) || endDate < field.min || endDate > field.max) {
            notify('Choose a later checkout within the original 90-day window.'); return;
        }
        const raw = clearance.value.trim();
        const financeClearanceId = raw ? Number(raw) : null;
        if (raw && (!Number.isSafeInteger(financeClearanceId) || financeClearanceId <= 0)) {
            notify('Enter a valid Finance clearance ID.'); return;
        }
        if (!pending.canExtendNoCharge && !financeClearanceId) {
            notify('Charged stays require an approved Finance period clearance.'); return;
        }
        if (!confirm('Extend this fixed-term stay through ' + endDate + '?')) return;
        if (!window.crypto?.randomUUID) { notify('Secure browser context required.'); return; }
        const signature = [pending.id, endDate, pending.rowVersion, financeClearanceId ?? 'none'].join('|');
        if (signature !== requestSignature) {
            requestSignature = signature; requestKey = window.crypto.randomUUID();
        }
        save.disabled = true;
        try {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload the page.');
            const response = await fetch('/api/hostel/allocations/' +
                encodeURIComponent(pending.id) + '/extend-stay', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json', 'Content-Type': 'application/json',
                    RequestVerificationToken: token },
                body: JSON.stringify({ clientRequestId: requestKey, endDate,
                    rowVersion: pending.rowVersion, financeClearanceId })
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success)
                throw new Error(result?.message || 'Stay extension failed.');
            dismiss(); notify('Hostel stay extended.', true);
            window.dispatchEvent(new CustomEvent('hostel-stay-extended'));
        } catch (error) { notify(error.message); }
        finally { save.disabled = false; }
    });
})();
