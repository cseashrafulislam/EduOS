import { root, notice } from './movements-api.js';
import { initForm } from './movements-form.js';
import { initLedger } from './movements-ledger.js';
import { initBalance } from './movements-balance.js';
import { initReorder } from './movements-reorder.js';
if (root) {
    const refresh = initLedger();
    initBalance().catch(e => notice(e.message));
    initReorder().catch(e => notice(e.message));
    if (root.dataset.create === 'true') initForm(refresh).catch(e => notice(e.message));
}