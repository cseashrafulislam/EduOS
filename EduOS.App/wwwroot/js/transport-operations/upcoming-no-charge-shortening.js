(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const panel = el('transportNoChargeShorteningPanel');
    if (!panel) return;
    let pending = null, busy = false, requestKey = null, requestSignature = null;
    function localDate() {
        const d = new Date();
        return [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'),
            String(d.getDate()).padStart(2, '0')].join('-');
    }
    function validDate(value) {
        if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
        const d = new Date(value + 'T00:00:00Z');
        return !Number.isNaN(d.getTime()) && d.toISOString().slice(0, 10) === value;
    }
    function previousDate(value) {
        const d = new Date(value + 'T00:00:00Z');
        d.setUTCDate(d.getUTCDate() - 1);
        return d.toISOString().slice(0, 10);
    }
    function notify(message, success = false) {
        const node = el('transportAlert');
        node.className = 'alert alert-' + (success ? 'success' : 'danger');
        node.textContent = message; node.focus();
    }
    function dismiss() { pending = null; panel.hidden = true; requestKey = null; requestSignature = null; }
    function begin(item) {
        if (!item || !item.canShortenNoCharge || !item.endDate ||
            item.startDate <= localDate() || item.endDate <= item.startDate) {
            notify('Only future finite no-charge reservations can be shortened.'); return;
        }
        pending = item; requestKey = null; requestSignature = null;
        el('transportNoChargeShorteningDetails').textContent =
            item.studentName + ' · ' + item.routeName + ' · current end ' + item.endDate;
        const input = el('transportNoChargeShorteningDate');
        input.min = item.startDate; input.max = previousDate(item.endDate);
        input.value = ''; panel.hidden = false; input.focus();
    }
    async function save(event) {
        event.preventDefault();
        if (!pending || busy) return;
        const endDate = el('transportNoChargeShorteningDate').value;
        if (!validDate(endDate) || endDate < pending.startDate ||
            endDate >= pending.endDate || pending.startDate <= localDate()) {
            notify('Choose a date from start through the day before the current end.'); return;
        }
        if (!window.confirm('Release seat-days after ' + endDate + ' for ' + pending.studentName + '?')) return;
        if (!window.crypto?.randomUUID) { notify('Secure browser context required.'); return; }
        const signature = [pending.reference, endDate, pending.rowVersion].join('|');
        if (signature !== requestSignature) { requestSignature = signature; requestKey = window.crypto.randomUUID(); }
        const button = el('transportNoChargeShorteningSave');
        busy = true; button.disabled = true;
        try {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload the page.');
            const response = await fetch('/api/transport/assignments/' +
                encodeURIComponent(pending.reference) + '/shorten-upcoming-no-charge', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json', 'Content-Type': 'application/json',
                    RequestVerificationToken: token },
                body: JSON.stringify({ clientRequestId: requestKey, endDate, rowVersion: pending.rowVersion })
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success)
                throw new Error(result?.message || 'Unable to shorten this reservation.');
            dismiss(); notify('Scheduled no-charge reservation shortened.', true);
            window.dispatchEvent(new Event('transport:refresh-assignments'));
        } catch (error) { notify(error.message); }
        finally { busy = false; button.disabled = false; }
    }
    window.addEventListener('transport:shorten-upcoming-no-charge', event => begin(event.detail));
    el('transportNoChargeShorteningForm').addEventListener('submit', save);
    el('transportNoChargeShorteningCancel').addEventListener('click', dismiss);
})();
