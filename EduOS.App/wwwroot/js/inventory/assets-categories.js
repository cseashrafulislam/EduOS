import { api, el, td, canCategory, addButton, notice } from './assets-api.js';
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
                el('assetCategoryId').value = row.id; el('assetCategoryVersion').value = row.rowVersion;
                el('assetCategoryCode').value = row.code; el('assetCategoryName').value = row.name;
                el('assetCategoryActive').checked = row.isActive;
            });
            tr.append(cell);
        }
        target.append(tr);
    }
}
export function bindCategories(reload) {
    if (!canCategory) return;
    el('assetCategoryForm').addEventListener('submit', async event => {
        event.preventDefault();
        const id = el('assetCategoryId').value;
        const body = { code: el('assetCategoryCode').value.trim(), name: el('assetCategoryName').value.trim(),
            isActive: el('assetCategoryActive').checked, rowVersion: el('assetCategoryVersion').value || null };
        try {
            await api('/categories' + (id ? '/' + id : ''), id ? 'PUT' : 'POST', body);
            el('assetCategoryForm').reset(); el('assetCategoryId').value = ''; el('assetCategoryVersion').value = '';
            await reload(); notice('Category saved.', true);
        } catch (error) { notice(error.message); }
    });
}
