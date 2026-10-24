(() => {
'use strict';
const u = window.transportLink;
if (!u) return;
u.rescheduleUpcomingLink = async item => {
    if (!item || item.isCancelled || !item.isCurrent || item.effectiveFrom <= u.today()) return;
    const start = window.prompt('New first effective date (YYYY-MM-DD)', item.effectiveFrom);
    if (start === null) return;
    if (!/^\d{4}-\d{2}-\d{2}$/.test(start) || Number.isNaN(Date.parse(start + 'T00:00:00Z')) ||
        start <= u.today() || (item.effectiveTo && start > item.effectiveTo)) {
        u.notify('Choose a future start within the existing assignment end date.'); return;
    }
    try {
        await u.api('/api/transport/route-vehicle-links/' + encodeURIComponent(item.id) +
            '/reschedule-start', 'POST', { effectiveFrom: start, rowVersion: item.rowVersion });
        u.notify('Upcoming route/vehicle assignment rescheduled.', true);
        await u.load();
    } catch (error) { u.notify(error.message); }
};
})();
