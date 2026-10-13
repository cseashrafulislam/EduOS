import { api, el, canManage, notice, money } from './assets-api.js';
import { locations } from './assets-register.js';
export function bindAssetForm(reload) {
    if (!canManage) return;
    el('assetNew').addEventListener('click', () => {
        el('assetForm').reset(); el('assetReference').value = ''; el('assetVersion').value = '';
    });
    el('assetLocation').addEventListener('change', () => {
        const loc = locations.find(x => String(x.id) === el('assetLocation').value);
        if (loc?.campusId) el('assetCampus').value = loc.campusId;
    });
    el('assetForm').addEventListener('submit', async event => {
        event.preventDefault();
        const reference = el('assetReference').value;
        try {
            const body = {
                assetTag: el('assetTag').value.trim(), name: el('assetName').value.trim(),
                assetCategoryId: Number(el('assetCategory').value),
                campusId: el('assetCampus').value ? Number(el('assetCampus').value) : null,
                inventoryLocationId: el('assetLocation').value ? Number(el('assetLocation').value) : null,
                inventoryItemId: el('assetItem').value ? Number(el('assetItem').value) : null,
                serialNumber: el('assetSerial').value.trim() || null,
                purchaseDate: el('assetPurchaseDate').value || null, purchaseCost: money(el('assetCost').value),
                isActive: el('assetActive').checked, rowVersion: el('assetVersion').value || null
            };
            await api(reference ? '/' + encodeURIComponent(reference) : '', reference ? 'PUT' : 'POST', body);
            el('assetNew').click(); await reload(); notice('Asset saved.', true);
        } catch (error) { notice(error.message); }
    });
}
