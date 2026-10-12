import { el, api, notice, fill, catalogs } from './movements-api.js';
export async function initBalance() {
    try {
        const data = await catalogs();
        fill(el('balanceItem'), data.items, 'Select stock item');
        fill(el('balanceLocation'), data.locations, 'Select location');
    } catch (e) { notice(e.message); }
    el('movementBalanceForm').addEventListener('submit', async event => {
        event.preventDefault();
        const itemId = Number(el('balanceItem').value), locationId = Number(el('balanceLocation').value);
        if (!itemId || !locationId) return notice('Select an item and location.');
        try {
            const query = new URLSearchParams({ itemId: String(itemId), locationId: String(locationId) });
            const result = await api('movements/balance?' + query);
            el('movementBalanceResult').textContent = 'Quantity on hand: ' + result.quantityOnHand +
                ' | Stock valuation: ' + result.valuationAmount;
        } catch (e) { notice(e.message); }
    });
}