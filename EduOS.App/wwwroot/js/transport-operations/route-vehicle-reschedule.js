(() => {
'use strict';
const u = window.transportLink;
if (!u) return;
let requestKey = null, requestSignature = null, busy = false;
u.rescheduleUpcomingLink = async item => {
    if (busy) return;
    if (!item || item.isCancelled || item.effectiveFrom <= u.today()) return;
    const start = window.prompt('New first effective date (YYYY-MM-DD)', item.effectiveFrom);
    if (start === null) return;
    if (!/^\d{4}-\d{2}-\d{2}$/.test(start) || Number.isNaN(Date.parse(start + 'T00:00:00Z')) ||
        start <= u.today() || (item.effectiveTo && start > item.effectiveTo)) {
        u.notify('Choose a future start within the existing assignment end date.'); return;
    }
    if (!window.crypto?.randomUUID) return u.notify('Secure browser context required.');
    const signature = [item.id, start, item.rowVersion].join('|');
    if (signature !== requestSignature) { requestSignature = signature; requestKey = window.crypto.randomUUID(); }
    busy = true;
    try {
        await u.api('/api/transport/route-vehicle-links/' + encodeURIComponent(item.id) +
            '/reschedule-start', 'POST', { clientRequestId: requestKey, effectiveFrom: start, rowVersion: item.rowVersion });
        requestKey = null; requestSignature = null;
        u.notify('Upcoming route/vehicle assignment rescheduled.', true);
        await u.load();
    } catch (error) { u.notify(error.message); } finally { busy = false; }
};
})();
