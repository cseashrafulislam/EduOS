import { api, el, fill, catalogs, notice } from './movements-api.js';

export async function initAdjustments(refresh) {
    const form = el('inventoryAdjustmentForm');
    if (!form) return;
    const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const data = await catalogs();
    fill(el('adjustmentLocation'), data.locations, bn ? 'স্টোর বাছুন' : 'Select store');
    fill(el('adjustmentItem'), data.items, bn ? 'পণ্য বাছুন' : 'Select item');
    let requestId = crypto.randomUUID(), timer;
    el('adjustmentItemSearch').addEventListener('input', () => {
        clearTimeout(timer);
        timer = setTimeout(async () => {
            try {
                const found = await catalogs(el('adjustmentItemSearch').value.trim(), data.locations);
                fill(el('adjustmentItem'), found.items, bn ? 'পণ্য বাছুন' : 'Select item');
            } catch (error) { notice(error.message); }
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
        if (!form.reportValidity()) return;
        const quantity = el('adjustmentQuantity').value.trim();
        const cost = el('adjustmentCost').value.trim();
        if (!/^\d{1,12}(\.\d{1,6})?$/.test(quantity) ||
            !/^\d{1,12}(\.\d{1,4})?$/.test(cost) ||
            Number(quantity) <= 0 || (el('adjustmentDirection').value === 'Increase' && Number(cost) <= 0)) {
            notice(bn ? 'সঠিক পরিমাণ ও মূল্য দিন।' : 'Enter valid quantity and cost.');
            return;
        }
        const submit = el('adjustmentSubmit');
        submit.disabled = true;
        try {
            await api('movements/adjustments', 'POST', {
                clientRequestId: requestId,
                locationId: Number(el('adjustmentLocation').value),
                inventoryItemId: Number(el('adjustmentItem').value),
                direction: el('adjustmentDirection').value,
                quantity: Number(quantity),
                unitCost: Number(cost),
                lotOrBatchNo: el('adjustmentLot').value.trim(),
                reason: el('adjustmentReason').value.trim()
            });
            requestId = crypto.randomUUID();
            form.reset();
            mode();
            notice(bn ? 'স্বতন্ত্র অনুমোদনের জন্য জমা হয়েছে।' : 'Submitted for independent approval.', true);
            await refresh();
        } catch (error) { notice(error.message); }
        finally { submit.disabled = false; }
    });
}
