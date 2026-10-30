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
if (root) {
    const refresh = initLedger();
    initReservations().catch(e => notice(e.message));
    initReservationCoverage().catch(e => notice(e.message));
    initBalance().catch(e => notice(e.message));
    initReorder().catch(e => notice(e.message));
    initStockPosition().catch(e => notice(e.message));
    initItemHistory().catch(e => notice(e.message));
    initLotAging().catch(e => notice(e.message));
    if (root.dataset.create === 'true') {
        initForm(refresh).catch(e => notice(e.message));
        initAdjustments(refresh).catch(e => notice(e.message));
    }
}