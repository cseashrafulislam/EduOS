import { root, notice } from './movements-api.js';
import { initForm } from './movements-form.js';
import { initAdjustments } from './movements-adjustments.js';
import { initLedger } from './movements-ledger.js';
import { initReservations } from './movements-reservations.js';
import { initReservationCoverage } from './movements-reservation-coverage.js';
import { initBalance } from './movements-balance.js';
import { initReorder } from './movements-reorder.js';
import { initStockPosition } from './movements-stock-position.js';
import { initItemHistory } from './movements-item-history.js';
import { initLotAging } from './movements-lot-aging.js';
import { initPhysicalCounts } from './movements-physical-counts.js';
if (root) {
    // Task pages server-render only their own controls. Do not bind hidden or
    // absent forms, or run unrelated tenant-scoped queries in the background.
    // The legacy Index keeps every original initialization for old deep links.
    const scope = root.dataset.taskPage || 'all';
    const all = scope === 'all';
    const transactions = all || scope === 'transactions';
    const adjustments = all || scope === 'adjustments';
    const counts = all || scope === 'counts';
    const reports = all || scope === 'reports';
    const refresh = transactions ? initLedger() : async () => {};
    if (transactions) initBalance().catch(e => notice(e.message));
    if (reports) {
        initReservations().catch(e => notice(e.message));
        initReservationCoverage().catch(e => notice(e.message));
        initReorder().catch(e => notice(e.message));
        initStockPosition().catch(e => notice(e.message));
        initItemHistory().catch(e => notice(e.message));
        initLotAging().catch(e => notice(e.message));
    }
    if (counts) initPhysicalCounts(refresh).catch(e => notice(e.message));
    if (root.dataset.create === 'true') {
        if (transactions) initForm(refresh).catch(e => notice(e.message));
        if (adjustments) initAdjustments(refresh).catch(e => notice(e.message));
    }
}
