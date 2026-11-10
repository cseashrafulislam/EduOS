import { api, el, canManage, notice, money } from './assets-api.js';
import { locations } from './assets-register.js';
import { registerGuard } from './assets-form-guards.js';
export function bindAssetForm(reload) {
    if (!canManage) return;
    const form = el('assetForm');
    let saving = false;
    const clear = () => {
        form.reset(); el('assetReference').value = ''; el('assetVersion').value = '';
        registerGuard.markClean();
    };
    el('assetNew').addEventListener('click', () => {
        if (saving || !registerGuard.confirmDiscard()) return;
        clear();
    });
    el('assetLocation').addEventListener('change', () => {
        const loc = locations.find(x => String(x.id) === el('assetLocation').value);
        if (loc?.campusId) el('assetCampus').value = loc.campusId;
    });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (saving) return;
        const reference = el('assetReference').value;
        saving = true; form.inert = true;
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
            clear(); notice('Asset saved.', true);
            try { await reload(); } catch (_) { notice('Asset saved; list refresh failed. Reload the register.'); }
        } catch (error) { notice(error.message); }
        finally { saving = false; form.inert = false; }
    });
}
