(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const panel = el('transportUpcomingRiderExtensionPanel');
    if (!panel) return;
    let pending = null, busy = false, requestKey = null, requestSignature = null;
    function localDate() {
        const d = new Date();
        return [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'),
            String(d.getDate()).padStart(2, '0')].join('-');
    }
    function validDate(value) {
        if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
        const date = new Date(value + 'T00:00:00Z');
        return !Number.isNaN(date.getTime()) && date.toISOString().slice(0, 10) === value;
    }
    function notify(message, success = false) {
        const node = el('transportAlert');
        node.className = 'alert alert-' + (success ? 'success' : 'danger');
        node.textContent = message;
        node.focus();
    }
    function dismiss() { pending = null; panel.hidden = true; requestKey = null; requestSignature = null; }
    function begin(item) {
        if (!item || !item.endDate || item.startDate <= localDate() ||
            item.endDate < item.startDate) {
            notify('Only future reservations with a finite end can be extended.');
            return;
        }
        pending = item; requestKey = null; requestSignature = null;
        el('transportUpcomingRiderExtensionDetails').textContent =
            item.studentName + ' · ' + item.routeName + ' · ' + item.vehicleNumber +
            ' · current end ' + item.endDate;
        const input = el('transportUpcomingRiderExtensionDate');
        input.min = item.endDate;
        input.value = '';
        panel.hidden = false;
        input.focus();
    }
    async function save(event) {
        event.preventDefault();
        if (!pending || busy) return;
        const endDate = el('transportUpcomingRiderExtensionDate').value;
        if (!validDate(endDate) || endDate <= pending.endDate ||
            pending.startDate <= localDate()) {
            notify('Choose a valid date after the scheduled reservation end.');
            return;
        }
        if (!window.confirm('Extend the scheduled reservation for ' +
            pending.studentName + ' through ' + endDate + '?')) return;
        if (!window.crypto?.randomUUID) { notify('Secure browser context required.'); return; }
        const signature = [pending.reference, endDate, pending.rowVersion].join('|');
        if (signature !== requestSignature) { requestSignature = signature; requestKey = window.crypto.randomUUID(); }
        const button = el('transportUpcomingRiderExtensionSave');
        busy = true; button.disabled = true;
        try {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload the page.');
            const response = await fetch('/api/transport/assignments/' +
                encodeURIComponent(pending.reference) + '/extend-upcoming', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json', 'Content-Type': 'application/json',
                    RequestVerificationToken: token },
                body: JSON.stringify({ clientRequestId: requestKey, endDate, rowVersion: pending.rowVersion })
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success)
                throw new Error(result?.message || 'Unable to extend this reservation.');
            dismiss();
            notify('Scheduled rider reservation extended.', true);
            window.dispatchEvent(new Event('transport:refresh-assignments'));
        } catch (error) { notify(error.message); }
        finally { busy = false; button.disabled = false; }
    }
    window.addEventListener('transport:extend-upcoming-rider', event => begin(event.detail));
    el('transportUpcomingRiderExtensionForm').addEventListener('submit', save);
    el('transportUpcomingRiderExtensionCancel').addEventListener('click', dismiss);
})();
