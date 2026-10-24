(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const panel = el('transportUpcomingStartPanel');
    if (!panel) return;
    let pending = null, busy = false;
    function localDate(offset = 0) {
        const date = new Date(); date.setDate(date.getDate() + offset);
        return [date.getFullYear(), String(date.getMonth() + 1).padStart(2, '0'),
            String(date.getDate()).padStart(2, '0')].join('-');
    }
    function notify(message, success = false) {
        const node = el('transportAlert');
        node.className = 'alert alert-' + (success ? 'success' : 'danger');
        node.textContent = message; node.focus();
    }
    function dismiss() { pending = null; panel.hidden = true; }
    function begin(item) {
        if (!item || item.startDate <= localDate()) {
            notify('Only future reservations can change their start date.'); return;
        }
        pending = item;
        el('transportUpcomingStartDetails').textContent = item.studentName + ' · ' +
            item.routeName + ' · ' + item.vehicleNumber + ' · current start ' + item.startDate;
        const input = el('transportUpcomingStartDate');
        input.min = localDate(1);
        input.max = item.endDate && item.endDate < localDate(365) ? item.endDate : localDate(365);
        input.value = item.startDate;
        panel.hidden = false;
        input.focus();
    }
    async function save(event) {
        event.preventDefault();
        if (!pending || busy) return;
        const startDate = el('transportUpcomingStartDate').value;
        if (!/^\d{4}-\d{2}-\d{2}$/.test(startDate) || startDate <= localDate() ||
            startDate > localDate(365) || (pending.endDate && startDate > pending.endDate)) {
            notify('Select a future start within 365 days and before the reservation end.'); return;
        }
        const button = el('transportUpcomingStartSave'); busy = true; button.disabled = true;
        try {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload the page.');
            const response = await fetch('/api/transport/assignments/' +
                encodeURIComponent(pending.reference) + '/reschedule-start', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json', 'Content-Type': 'application/json',
                    RequestVerificationToken: token },
                body: JSON.stringify({ startDate, rowVersion: pending.rowVersion })
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success)
                throw new Error(result?.message || 'Unable to reschedule this reservation.');
            dismiss();
            notify('Upcoming transport start date rescheduled.', true);
            window.dispatchEvent(new Event('transport:refresh-assignments'));
        } catch (error) { notify(error.message); }
        finally { busy = false; button.disabled = false; }
    }
    window.addEventListener('transport:reschedule-upcoming', event => begin(event.detail));
    el('transportUpcomingStartForm').addEventListener('submit', save);
    el('transportUpcomingStartCancel').addEventListener('click', dismiss);
})();
