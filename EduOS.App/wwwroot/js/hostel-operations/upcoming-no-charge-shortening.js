(() => {
    'use strict';
    const panel = document.getElementById('hostelNoChargeShorteningPanel');
    if (!panel) return;
    const field = document.getElementById('hostelNoChargeShorteningEnd');
    const context = document.getElementById('hostelNoChargeShorteningContext');
    const save = document.getElementById('hostelNoChargeShorteningSave');
    const cancel = document.getElementById('hostelNoChargeShorteningCancel');
    let pending = null;
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
        pending = null; field.value = ''; panel.classList.add('d-none');
    };
    window.hostelUpcomingNoChargeShortening = {
        begin(item) {
            if (!item?.canShortenNoCharge || !item.endDate ||
                item.startDate <= today() || item.endDate <= item.startDate) {
                notify('Only future, no-charge fixed-term reservations can be shortened.'); return;
            }
            pending = item;
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
        if (!confirm('Shorten this future no-charge stay? The released dates become available.')) return;
        save.disabled = true;
        try {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload the page.');
            const response = await fetch('/api/hostel/allocations/' +
                encodeURIComponent(pending.id) + '/shorten-upcoming-no-charge', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json', 'Content-Type': 'application/json',
                    RequestVerificationToken: token },
                body: JSON.stringify({ endDate, rowVersion: pending.rowVersion })
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success)
                throw new Error(result?.message || 'Checkout correction failed.');
            dismiss(); notify('Upcoming no-charge checkout shortened.', true);
            window.dispatchEvent(new CustomEvent('hostel-stay-shortened'));
        } catch (error) { notify(error.message); }
        finally { save.disabled = false; }
    });
})();
