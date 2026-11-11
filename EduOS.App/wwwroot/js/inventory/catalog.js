(() => {
    'use strict';
    const root = document.getElementById('inventoryCatalog');
    if (!root) return;
    const el = id => document.getElementById(id);
    const canManage = root.dataset.canManage === 'true';
    const token = document.querySelector('meta[name="request-verification-token"]')?.content;
    const base = '/api/inventory/catalog';
    let page = 1, lastPage = 1, locationPage = 1, locationLastPage = 1, items = [], locations = [];
    let itemSaving = false, locationSaving = false, itemSequence = 0, locationSequence = 0;
    const context = [document.body?.dataset.authTenantId || '', document.body?.dataset.authSessionReference || ''];
    const live = () => context[0] === (document.body?.dataset.authTenantId || '') &&
        context[1] === (document.body?.dataset.authSessionReference || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
    const guards = canManage ? { item: window.EduOSFormGuard?.create(el('itemForm')),
        location: window.EduOSFormGuard?.create(el('locationForm')) } : {};
    const lock = (form, busy) => {
        if (busy) {
            const controls = [...form.querySelectorAll('input, select, button')];
            const prior = controls.map(input => input.disabled);
            controls.forEach(input => { input.disabled = true; });
            return () => controls.forEach((input, index) => { input.disabled = prior[index]; });
        }
        return () => {};
    };
    function notice(text, ok = false) {
        const box = el('catalogAlert');
        box.className = 'alert alert-' + (ok ? 'success' : 'danger');
        box.textContent = text;
    }
    async function api(path, method = 'GET', body) {
        if (!live()) throw new Error('Institution session changed. Reload the page.');
        const headers = { Accept: 'application/json' };
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        if (method !== 'GET') {
            if (!token) throw new Error('Security token missing. Reload the page.');
            headers['RequestVerificationToken'] = token;
        }
        const response = await fetch(base + path, { method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const payload = await response.json().catch(() => null);
        if (!live()) throw new Error('Institution session changed. Reload the page.');
        if (!response.ok || !payload?.success) throw new Error(payload?.message || 'Inventory request failed.');
        return payload.data;
    }
    function td(value) { const node = document.createElement('td'); node.textContent = value == null || value === '' ? '—' : String(value); return node; }
    function empty(target, span, message) {
        const tr = document.createElement('tr'), cell = td(message); cell.colSpan = span; tr.append(cell); target.replaceChildren(tr);
    }
    function action(tr, fn) {
        const cell = document.createElement('td'), button = document.createElement('button');
        button.type = 'button'; button.className = 'btn btn-outline-primary btn-sm'; button.textContent = 'Edit';
        button.addEventListener('click', fn); cell.append(button); tr.append(cell);
    }
    function renderItems() {
        const target = el('itemRows'); target.replaceChildren();
        if (!items.length) { empty(target, canManage ? 7 : 6, 'No items found.'); return; }
        for (const row of items) {
            const tr = document.createElement('tr');
            [row.code, row.name, row.unitCode, row.categoryCode, row.reorderLevel, row.isActive ? 'Active' : 'Inactive']
                .forEach(v => tr.append(td(v)));
            if (canManage) action(tr, () => editItem(row));
            target.append(tr);
        }
    }
    async function loadItems(propagateError = false) {
        const sequence = ++itemSequence;
        try {
            const query = new URLSearchParams({ page: String(page), pageSize: '25', search: el('itemSearch').value.trim() });
            const data = await api('/items?' + query);
            if (sequence !== itemSequence || !live()) return;
            items = data.items || []; lastPage = Math.max(1, data.totalPages || 1);
            el('itemPageLabel').textContent = 'Page ' + page + ' of ' + lastPage + ' · ' + (data.totalCount || 0) + ' items';
            el('itemPrev').disabled = page <= 1; el('itemNext').disabled = page >= lastPage;
            renderItems();
        } catch (error) { if (sequence === itemSequence) {
            notice(error.message); empty(el('itemRows'), canManage ? 7 : 6, 'Unable to load items.');
        } if (propagateError) throw error; }
    }
    function editItem(row) {
        if (itemSaving || !live() || guards.item && !guards.item.confirmDiscard()) return;
        const values = { itemId: row.id, itemVersion: row.rowVersion, itemCode: row.code,
            itemName: row.name, itemUnit: row.unitCode, itemCategory: row.categoryCode, itemReorder: row.reorderLevel };
        Object.entries(values).forEach(([key, value]) => { el(key).value = value ?? ''; });
        el('itemStock').checked = row.isStockTracked; el('itemAsset').checked = row.isAssetTracked;
        el('itemActive').checked = row.isActive; el('itemForm').scrollIntoView({ block: 'center', behavior: 'smooth' });
        guards.item?.markClean();
    }
    function resetItem() {
        el('itemForm').reset(); el('itemId').value = ''; el('itemVersion').value = '';
        guards.item?.markClean();
    }
    async function saveItem(event) {
        event.preventDefault();
        if (itemSaving || !live()) return;
        const id = el('itemId').value;
        const body = { code: el('itemCode').value.trim(), name: el('itemName').value.trim(),
            unitCode: el('itemUnit').value.trim(), categoryCode: el('itemCategory').value.trim() || null,
            reorderLevel: Number(el('itemReorder').value), isStockTracked: el('itemStock').checked,
            isAssetTracked: el('itemAsset').checked, isActive: el('itemActive').checked,
            rowVersion: el('itemVersion').value || null };
        itemSaving = true; const unlock = lock(el('itemForm'), true);
        try {
            await api('/items' + (id ? '/' + encodeURIComponent(id) : ''), id ? 'PUT' : 'POST', body);
            resetItem(); notice('Item saved.', true);
            try { await loadItems(true); } catch (error) { notice('Item saved; refresh the register. ' + error.message, true); }
        } catch (error) { notice(error.message); }
        finally { itemSaving = false; unlock(); }
    }
    function renderLocations() {
        const target = el('locationRows'); target.replaceChildren();
        if (!locations.length) { empty(target, canManage ? 6 : 5, 'No locations found.'); return; }
        for (const row of locations) {
            const tr = document.createElement('tr');
            [row.code, row.name, row.campusId, row.address, row.isActive ? 'Active' : 'Inactive'].forEach(v => tr.append(td(v)));
            if (canManage) action(tr, () => editLocation(row));
            target.append(tr);
        }
    }
    async function loadLocations(propagateError = false) {
        const sequence = ++locationSequence;
        try {
            const query = new URLSearchParams({ page: String(locationPage), pageSize: '25',
                search: el('locationSearch').value.trim() });
            const data = await api('/locations?' + query);
            if (sequence !== locationSequence || !live()) return;
            locations = data.items || [];
            locationLastPage = Math.max(1, data.totalPages || 1);
            el('locationPageLabel').textContent = 'Page ' + locationPage + ' of ' + locationLastPage +
                ' · ' + (data.totalCount || 0) + ' locations';
            el('locationPrev').disabled = locationPage <= 1;
            el('locationNext').disabled = locationPage >= locationLastPage;
            renderLocations();
        } catch (error) { if (sequence === locationSequence) {
            notice(error.message); empty(el('locationRows'), canManage ? 6 : 5, 'Unable to load locations.');
        } if (propagateError) throw error; }
    }
    function editLocation(row) {
        if (locationSaving || !live() || guards.location && !guards.location.confirmDiscard()) return;
        const values = { locationId: row.id, locationVersion: row.rowVersion, locationCode: row.code,
            locationName: row.name, locationCampus: row.campusId };
        Object.entries(values).forEach(([key, value]) => { el(key).value = value ?? ''; });
        el('locationAddress').value = row.address ?? ''; el('locationActive').checked = row.isActive;
        el('locationForm').scrollIntoView({ block: 'center', behavior: 'smooth' });
        guards.location?.markClean();
    }
    function resetLocation() {
        el('locationForm').reset(); el('locationId').value = ''; el('locationVersion').value = '';
        guards.location?.markClean();
    }
    async function saveLocation(event) {
        event.preventDefault();
        if (locationSaving || !live()) return;
        const id = el('locationId').value;
        const address = el('locationAddress').value.trim();
        const body = { code: el('locationCode').value.trim(), name: el('locationName').value.trim(),
            campusId: el('locationCampus').value ? Number(el('locationCampus').value) : null,
            address: address || null, isActive: el('locationActive').checked, rowVersion: el('locationVersion').value || null };
        locationSaving = true; const unlock = lock(el('locationForm'), true);
        try {
            await api('/locations' + (id ? '/' + encodeURIComponent(id) : ''), id ? 'PUT' : 'POST', body);
            resetLocation(); notice('Location saved.', true);
            try { await loadLocations(true); } catch (error) { notice('Location saved; refresh the register. ' + error.message, true); }
        } catch (error) { notice(error.message); }
        finally { locationSaving = false; unlock(); }
    }
    el('locationSearchForm').addEventListener('submit', e => { e.preventDefault(); locationPage = 1; loadLocations(); });
    el('locationPrev').addEventListener('click', () => { if (locationPage > 1) { locationPage--; loadLocations(); } });
    el('locationNext').addEventListener('click', () => { if (locationPage < locationLastPage) { locationPage++; loadLocations(); } });
    el('itemSearchForm').addEventListener('submit', e => { e.preventDefault(); page = 1; loadItems(); });
    el('itemPrev').addEventListener('click', () => { if (page > 1) { page--; loadItems(); } });
    el('itemNext').addEventListener('click', () => { if (page < lastPage) { page++; loadItems(); } });
    if (canManage) {
        el('itemForm').addEventListener('submit', saveItem);
        el('itemReset').addEventListener('click', () => { if (!itemSaving && guards.item?.confirmDiscard() !== false) resetItem(); });
        el('locationForm').addEventListener('submit', saveLocation);
        el('locationReset').addEventListener('click', () => {
            if (!locationSaving && guards.location?.confirmDiscard() !== false) resetLocation();
        });
    }
    const observer = new MutationObserver(() => {
        if (live()) return;
        ++itemSequence; ++locationSequence; items = []; locations = [];
        el('itemRows').replaceChildren(); el('locationRows').replaceChildren();
        if (canManage) { resetItem(); resetLocation(); }
        notice('Institution session changed. Reload the page.'); observer.disconnect();
    });
    observer.observe(document.body, { attributes: true,
        attributeFilter: ['data-auth-tenant-id', 'data-auth-session-reference'] });
    loadItems(); loadLocations();
})();
