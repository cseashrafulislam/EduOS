(() => {
    'use strict';
    const panel = document.getElementById('hostelNoChargeShorteningPanel');
    if (!panel) return;
    const field = document.getElementById('hostelNoChargeShorteningEnd');
    const clearance = document.getElementById('hostelNoChargeShorteningClearance');
    const context = document.getElementById('hostelNoChargeShorteningContext');
    const save = document.getElementById('hostelNoChargeShorteningSave');
    const cancel = document.getElementById('hostelNoChargeShorteningCancel');
    let pending = null;
    let requestKey = null, requestSignature = null;
    const today = () => {
        const d = new Date();
        return [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'),
            String(d.getDate()).padStart(2, '0')].join('-');
    };
    const addDays = (value, days) => {
        const d = new Date(value + 'T00:00:00Z');
        d.setUTCDate(d.getUTCDate() + days);
        return d.toISOString().slice(0, 10);
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
    window.hostelUpcomingNoChargeShortening = {
        begin(item) {
            if (!item?.endDate ||
                item.startDate <= today() || item.endDate <= item.startDate) {
                notify('Only future fixed-term reservations can be shortened.'); return;
            }
            pending = item;
            clearance.value = '';
            context.textContent = item.studentName + ' · ' + item.hostelName +
                ' · Room ' + item.roomNumber + ' / Bed ' + item.bedNumber +
                ' · Current checkout ' + item.endDate;
            field.min = item.startDate;
            field.max = addDays(item.endDate, -1);
            field.value = field.max;
            panel.classList.remove('d-none');
            panel.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
    };
    cancel.addEventListener('click', dismiss);
    save.addEventListener('click', async () => {
        if (!pending) return;
        const endDate = field.value;
        if (!/^\d{4}-\d{2}-\d{2}$/.test(endDate) ||
            endDate < field.min || endDate > field.max) {
            notify('Choose checkout on or after arrival and before the existing checkout.'); return;
        }
        const raw = clearance.value.trim();
        const financeClearanceId = raw ? Number(raw) : null;
        if (raw && (!Number.isSafeInteger(financeClearanceId) || financeClearanceId <= 0)) {
            notify('Enter a valid Finance clearance ID.'); return;
        }
        if (!pending.canShortenNoCharge && !financeClearanceId) {
            notify('Charged stays require an approved Finance period clearance.'); return;
        }
        if (!confirm('Shorten this future stay? The released dates become available.')) return;
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
                encodeURIComponent(pending.id) + '/shorten-upcoming', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json', 'Content-Type': 'application/json',
                    RequestVerificationToken: token },
                body: JSON.stringify({ clientRequestId: requestKey, endDate,
                    rowVersion: pending.rowVersion, financeClearanceId })
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success)
                throw new Error(result?.message || 'Checkout correction failed.');
            dismiss(); notify('Upcoming checkout shortened.', true);
            window.dispatchEvent(new CustomEvent('hostel-stay-shortened'));
        } catch (error) { notify(error.message); }
        finally { save.disabled = false; }
    });
})();
