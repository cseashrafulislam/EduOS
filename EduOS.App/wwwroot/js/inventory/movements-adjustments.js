import { api, el, fill, catalogs, notice } from './movements-api.js';
import { commandKey, refreshAfterCommit } from './movements-form-safety.js';

export async function initAdjustments(refresh) {
    const form = el('inventoryAdjustmentForm');
    if (!form) return;
    const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const data = await catalogs();
    fill(el('adjustmentLocation'), data.locations, bn ? 'স্টোর বাছুন' : 'Select store');
    fill(el('adjustmentItem'), data.items, bn ? 'পণ্য বাছুন' : 'Select item');
    const keys = commandKey(), guard = window.EduOSFormGuard?.create(form);
    let timer, searchSequence = 0, saving = false;
    el('adjustmentItemSearch').addEventListener('input', () => {
        const generation = ++searchSequence;
        clearTimeout(timer);
        timer = setTimeout(async () => {
            try {
                const found = await catalogs(el('adjustmentItemSearch').value.trim(), data.locations);
                if (generation !== searchSequence) return;
                fill(el('adjustmentItem'), found.items, bn ? 'পণ্য বাছুন' : 'Select item');
            } catch (error) { if (generation === searchSequence) notice(error.message); }
        }, 350);
    });
    const mode = () => {
        const surplus = el('adjustmentDirection').value === 'Increase';
        el('adjustmentCost').disabled = !surplus;
        el('adjustmentCost').required = surplus;
        el('adjustmentCost').min = surplus ? '0.0001' : '0';
        if (!surplus) el('adjustmentCost').value = '0';
    };
    el('adjustmentDirection').addEventListener('change', mode);
    mode();
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (saving) return;
        if (!form.reportValidity()) return;
        const quantity = el('adjustmentQuantity').value.trim();
        const cost = el('adjustmentCost').value.trim();
        if (!/^\d{1,12}(\.\d{1,6})?$/.test(quantity) ||
            !/^\d{1,12}(\.\d{1,4})?$/.test(cost) ||
            Number(quantity) <= 0 || (el('adjustmentDirection').value === 'Increase' && Number(cost) <= 0)) {
            notice(bn ? 'সঠিক পরিমাণ ও মূল্য দিন।' : 'Enter valid quantity and cost.');
            return;
        }
        const body = {
            locationId: Number(el('adjustmentLocation').value),
            inventoryItemId: Number(el('adjustmentItem').value),
            direction: el('adjustmentDirection').value,
            quantity: Number(quantity), unitCost: Number(cost),
            lotOrBatchNo: el('adjustmentLot').value.trim(), reason: el('adjustmentReason').value.trim()
        };
        saving = true; form.inert = true;
        try {
            await api('movements/adjustments', 'POST', { clientRequestId: keys.for('adjustment', body), ...body });
            keys.clear(); form.reset(); mode(); guard?.markClean();
            await refreshAfterCommit(refresh, bn ? 'স্বতন্ত্র অনুমোদনের জন্য জমা হয়েছে।' :
                'Submitted for independent approval.', notice);
        } catch (error) { notice(error.message); }
        finally { saving = false; form.inert = false; }
    });
}
