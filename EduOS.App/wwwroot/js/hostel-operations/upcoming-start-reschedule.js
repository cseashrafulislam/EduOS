(() => {
    'use strict';
    const panel = document.getElementById('hostelStartReschedulePanel');
    if (!panel) return;
    const field = document.getElementById('hostelRescheduleStart');
    const clearance = document.getElementById('hostelRescheduleFinanceClearance');
    const context = document.getElementById('hostelRescheduleContext');
    const save = document.getElementById('hostelRescheduleSave');
    const cancel = document.getElementById('hostelRescheduleCancel');
    let pending = null;
    let requestKey = null, requestSignature = null;
    const dateString = d => [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'), String(d.getDate()).padStart(2, '0')].join('-');
    const tomorrow = () => { const d = new Date(); d.setDate(d.getDate() + 1); return dateString(d); };
    const limit = () => { const d = new Date(); d.setFullYear(d.getFullYear() + 2); return dateString(d); };
    const notify = (message, success = false) => {
        const el = document.getElementById('hostelAlert');
        el.className = 'alert alert-' + (success ? 'success' : 'danger');
        el.textContent = message;
        el.focus();
    };
    const dismiss = () => {
        pending = null; requestKey = null; requestSignature = null;
        panel.classList.add('d-none'); field.value = ''; clearance.value = '';
    };
    window.hostelUpcomingStartReschedule = {
        begin(item) {
            if (!item || item.startDate <= dateString(new Date())) {
                notify('Only upcoming hostel reservations can be rescheduled.'); return;
            }
            pending = item;
            clearance.value = '';
            requestKey = null; requestSignature = null;
            context.textContent = item.studentName + ' · ' + item.hostelName +
                ' · Room ' + item.roomNumber + ' / Bed ' + item.bedNumber;
            field.min = tomorrow(); field.max = limit(); field.value = item.startDate;
            panel.classList.remove('d-none');
            panel.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
    };
    cancel.addEventListener('click', dismiss);
    save.addEventListener('click', async () => {
        if (!pending) return;
        const startDate = field.value;
        if (!/^\d{4}-\d{2}-\d{2}$/.test(startDate) || startDate < tomorrow() || startDate > limit()) {
            notify('Choose a future start date within two years.'); return;
        }
        if (startDate === pending.startDate) { dismiss(); return; }
        const raw = clearance.value.trim();
        const financeClearanceId = raw ? Number(raw) : null;
        if (raw && (!Number.isSafeInteger(financeClearanceId) || financeClearanceId <= 0)) {
            notify('Use a valid approved Finance clearance ID.'); return;
        }
        if (Number(pending.monthlyRent) !== 0 && !financeClearanceId) {
            notify('Charged stays require approved Finance clearance.'); return;
        }
        if (!confirm('Reschedule this upcoming hostel reservation?')) return;
        if (!window.crypto?.randomUUID) { notify('Secure browser context required.'); return; }
        const signature = [pending.id, startDate, pending.rowVersion, financeClearanceId ?? 'none'].join('|');
        if (signature !== requestSignature) {
            requestSignature = signature; requestKey = window.crypto.randomUUID();
        }
        save.disabled = true;
        try {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload the page.');
            const response = await fetch('/api/hostel/allocations/' + encodeURIComponent(pending.id) + '/reschedule-start', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json', 'Content-Type': 'application/json', RequestVerificationToken: token },
                body: JSON.stringify({ clientRequestId: requestKey, startDate,
                    rowVersion: pending.rowVersion, financeClearanceId })
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success) throw new Error(result?.message || 'Reschedule failed.');
            dismiss();
            notify('Upcoming hostel reservation rescheduled.', true);
            window.dispatchEvent(new CustomEvent('hostel-start-rescheduled'));
        } catch (error) { notify(error.message); }
        finally { save.disabled = false; }
    });
})();
