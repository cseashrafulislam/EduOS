import { el, api, notice, fill, catalogs } from './movements-api.js';
export async function initBalance() {
    try {
        const data = await catalogs();
        fill(el('balanceItem'), data.items, 'Select stock item');
        fill(el('balanceLocation'), data.locations, 'Select location');
    } catch (e) { notice(e.message); }
    const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
    el('movementBalanceForm').addEventListener('submit', async event => {
        event.preventDefault();
        const itemId = Number(el('balanceItem').value), locationId = Number(el('balanceLocation').value);
        if (!itemId || !locationId) return notice('Select an item and location.');
        try {
            const query = new URLSearchParams({ itemId: String(itemId), locationId: String(locationId) });
            const result = await api('movements/balance?' + query);
            el('movementBalanceResult').textContent =
                (bn ? 'মজুত: ' : 'On hand: ') + result.quantityOnHand +
                ' | ' + (bn ? 'রিজার্ভ: ' : 'Reserved: ') + result.reservedQuantity +
                ' | ' + (bn ? 'ব্যবহারযোগ্য: ' : 'Available: ') + result.availableQuantity +
                ' | ' + (bn ? 'রেকর্ডকৃত মূল্য: ' : 'Recorded movement cost: ') + result.valuationAmount;
        } catch (e) { notice(e.message); }
    });
}