import { api, el, td, canCategory, addButton, notice } from './assets-api.js';
import { categoryGuard } from './assets-form-guards.js';
export function fillCategories(rows) {
    const select = el('assetCategory'); if (!select) return;
    select.replaceChildren(new Option('Select category', ''));
    rows.filter(x => x.isActive).forEach(x => select.add(new Option(x.code + ' — ' + x.name, String(x.id))));
}
export function renderCategories(rows) {
    const target = el('assetCategoryRows'); target.replaceChildren();
    for (const row of rows) {
        const tr = document.createElement('tr');
        [row.code, row.name, row.isActive ? 'Active' : 'Inactive'].forEach(x => tr.append(td(x)));
        if (canCategory) {
            const cell = document.createElement('td');
            addButton(cell, 'Edit', () => {
                if (el('assetCategoryForm').inert) { notice('Finish the category save before switching records.'); return; }
                if (!categoryGuard.confirmDiscard()) return;
                el('assetCategoryId').value = row.id; el('assetCategoryVersion').value = row.rowVersion;
                el('assetCategoryCode').value = row.code; el('assetCategoryName').value = row.name;
                el('assetCategoryActive').checked = row.isActive;
                categoryGuard.markClean();
            });
            tr.append(cell);
        }
        target.append(tr);
    }
}
export function bindCategories(reload) {
    if (!canCategory) return;
    const form = el('assetCategoryForm');
    let saving = false;
    el('assetCategoryNew').addEventListener('click', () => {
        if (saving || !categoryGuard.confirmDiscard()) return;
        form.reset(); el('assetCategoryId').value = ''; el('assetCategoryVersion').value = '';
        categoryGuard.markClean();
    });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (saving) return;
        const id = el('assetCategoryId').value;
        const body = { code: el('assetCategoryCode').value.trim(), name: el('assetCategoryName').value.trim(),
            isActive: el('assetCategoryActive').checked, rowVersion: el('assetCategoryVersion').value || null };
        saving = true; form.inert = true;
        try {
            await api('/categories' + (id ? '/' + id : ''), id ? 'PUT' : 'POST', body);
            form.reset(); el('assetCategoryId').value = ''; el('assetCategoryVersion').value = '';
            categoryGuard.markClean(); notice('Category saved.', true);
            try { await reload(); } catch (_) { notice('Category saved; catalog refresh failed. Reload before editing.'); }
        } catch (error) { notice(error.message); }
        finally { saving = false; form.inert = false; }
    });
}
