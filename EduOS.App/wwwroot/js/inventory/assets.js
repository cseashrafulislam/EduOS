import { api, el, td, empty, root, canManage, notice, addButton } from './assets-api.js';
import { bindCategories, renderCategories, fillCategories } from './assets-categories.js';
import { loadReferenceCatalogs, editAsset } from './assets-register.js';
import { bindAssetForm } from './assets-register-form.js';
import { selectAsset } from './assets-custody.js';
import { bindAssignmentForm } from './assets-assignment-form.js';
import { bindMaintenanceForm } from './assets-maintenance-form.js';
if (root) {
    let categories = [], page = 1, pages = 1;
    async function loadCategories() {
        try {
            categories = await api('/categories'); renderCategories(categories); fillCategories(categories);
        } catch (error) { notice(error.message); empty('assetCategoryRows', 4, 'Unable to load categories.'); }
    }
    async function loadAssets() {
        try {
            const query = new URLSearchParams({ page: String(page), pageSize: '25', search: el('assetSearch').value.trim() });
            const data = await api('?' + query);
            pages = Math.max(1, data.totalPages || 1);
            el('assetPage').textContent = 'Page ' + page + ' of ' + pages + ' · ' + (data.totalCount || 0) + ' assets';
            el('assetPrev').disabled = page <= 1; el('assetNext').disabled = page >= pages;
            const target = el('assetRows'); target.replaceChildren();
            for (const row of data.items || []) {
                const tr = document.createElement('tr');
                const category = categories.find(x => x.id === row.assetCategoryId)?.name || row.assetCategoryId;
                [row.assetTag, row.name, category, row.purchaseCost, row.stateCode,
                    row.isActive ? 'Active' : 'Inactive'].forEach(x => tr.append(td(x)));
                const cell = document.createElement('td');
                addButton(cell, 'Custody', () => selectAsset(row).catch(error => notice(error.message)));
                if (canManage) addButton(cell, 'Edit', () => editAsset(row));
                tr.append(cell); target.append(tr);
            }
            if (!data.items?.length) empty('assetRows', 7, 'No assets found.');
        } catch (error) { notice(error.message); empty('assetRows', 7, 'Unable to load assets.'); }
    }
    el('assetSearchForm').addEventListener('submit', event => {
        event.preventDefault(); page = 1; loadAssets();
    });
    el('assetPrev').addEventListener('click', () => { if (page > 1) { page--; loadAssets(); } });
    el('assetNext').addEventListener('click', () => { if (page < pages) { page++; loadAssets(); } });
    bindCategories(loadCategories); bindAssetForm(loadAssets);
    bindAssignmentForm(loadAssets); bindMaintenanceForm();
    Promise.all([loadCategories(), loadReferenceCatalogs()]).then(loadAssets).catch(error => notice(error.message));
}
