import { root, notice } from './movements-api.js';
import { initForm } from './movements-form.js';
import { initLedger } from './movements-ledger.js';
import { initBalance } from './movements-balance.js';
import { initReorder } from './movements-reorder.js';
import { initStockPosition } from './movements-stock-position.js';
import { initItemHistory } from './movements-item-history.js';
if (root) {
    const refresh = initLedger();
    initBalance().catch(e => notice(e.message));
    initReorder().catch(e => notice(e.message));
    initStockPosition().catch(e => notice(e.message));
    initItemHistory().catch(e => notice(e.message));
    if (root.dataset.create === 'true') initForm(refresh).catch(e => notice(e.message));
}