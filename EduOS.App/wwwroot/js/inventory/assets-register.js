import { catalog, el, canManage, notice } from './assets-api.js';
export let locations = [];
export async function loadReferenceCatalogs() {
    if (!canManage) return;
    try {
        const [loc, itemPage] = await Promise.all([catalog('/locations'), catalog('/items?page=1&pageSize=100')]);
        locations = (Array.isArray(loc) ? loc : loc.items || []).filter(x => x.isActive);
        const locSelect = el('assetLocation'); locSelect.replaceChildren(new Option('None', ''));
        locations.forEach(x => locSelect.add(new Option(x.code + ' — ' + x.name, String(x.id))));
        const itemSelect = el('assetItem'); itemSelect.replaceChildren(new Option('None', ''));
        (itemPage.items || []).filter(x => x.isActive && x.isAssetTracked)
            .forEach(x => itemSelect.add(new Option(x.code + ' — ' + x.name, String(x.id))));
    } catch (error) { notice('Catalog loading failed: ' + error.message); }
}
export function editAsset(row) {
    if (!canManage) return;
    const fields = { assetReference: row.reference, assetVersion: row.rowVersion, assetTag: row.assetTag,
        assetName: row.name, assetCategory: row.assetCategoryId, assetLocation: row.inventoryLocationId,
        assetItem: row.inventoryItemId, assetSerial: row.serialNumber, assetPurchaseDate: row.purchaseDate,
        assetCost: row.purchaseCost };
    Object.entries(fields).forEach(([id, value]) => { el(id).value = value ?? ''; });
    el('assetActive').checked = row.isActive;
    el('assetCampus').value = row.campusId ?? locations.find(x => x.id === row.inventoryLocationId)?.campusId ?? '';
    el('assetForm').scrollIntoView({ behavior: 'smooth', block: 'center' });
}
