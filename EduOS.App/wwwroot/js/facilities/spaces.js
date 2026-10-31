(() => {
    'use strict';
    const root = document.getElementById('facilitySpaces');
    if (!root) return;
    const bn = root.dataset.bn === 'true', manage = root.dataset.manage === 'true';
    const el = id => document.getElementById(id);
    const token = document.querySelector('meta[name="request-verification-token"]')?.content;
    const tr = (en, bd) => bn ? bd : en;
    let page = 1, pages = 1, rows = [];
    function notice(message, ok = false) {
        const target = el('facilityAlert');
        target.className = 'alert alert-' + (ok ? 'success' : 'danger');
        target.textContent = message;
    }
    async function api(path, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        if (method !== 'GET' && token) headers.RequestVerificationToken = token;
        const response = await fetch('/api/facilities/spaces' + path, {
            method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        const data = await response.json().catch(() => null);
        if (!response.ok || !data?.success) throw new Error(data?.message || tr('Request failed.', 'অনুরোধ ব্যর্থ হয়েছে।'));
        return data.data;
    }
    function cell(value) {
        const td = document.createElement('td');
        td.textContent = value == null || value === '' ? '—' : String(value);
        return td;
    }
    function render() {
        const target = el('facilityRows'); target.replaceChildren();
        for (const item of rows) {
            const line = document.createElement('tr');
            [item.campusId, item.code, item.name, item.address,
                item.isActive ? tr('Active', 'সক্রিয়') : tr('Inactive', 'নিষ্ক্রিয়')].forEach(v => line.append(cell(v)));
            if (manage) {
                const td = document.createElement('td'), button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-outline-primary btn-sm';
                button.textContent = tr('Edit', 'সম্পাদনা');
                button.addEventListener('click', () => {
                    el('facilityId').value = item.id; el('facilityVersion').value = item.rowVersion;
                    el('facilityCampus').value = item.campusId; el('facilityCode').value = item.code;
                    el('facilityName').value = item.name; el('facilityAddress').value = item.address || '';
                    el('facilityActive').checked = item.isActive;
                    el('facilityForm').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
                });
                td.append(button); line.append(td);
            }
            target.append(line);
        }
        if (!rows.length) {
            const line = document.createElement('tr'), td = cell(tr('No spaces found.', 'কোনো স্থান পাওয়া যায়নি।'));
            td.colSpan = manage ? 6 : 5; line.append(td); target.append(line);
        }
        el('facilityPage').textContent = tr('Page ', 'পৃষ্ঠা ') + page + ' / ' + pages;
        el('facilityPrev').disabled = page <= 1; el('facilityNext').disabled = page >= pages;
    }
    async function load() {
        try {
            const query = new URLSearchParams({ page: String(page), pageSize: '25', search: el('facilitySearch').value.trim() });
            if (el('facilityCampusFilter').value) query.set('campusId', el('facilityCampusFilter').value);
            const result = await api('?' + query.toString());
            rows = result.items || []; pages = Math.max(1, result.totalPages || 1); render();
        } catch (error) { notice(error.message); }
    }
    el('facilitySearchForm').addEventListener('submit', event => { event.preventDefault(); page = 1; load(); });
    el('facilityPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
    el('facilityNext').addEventListener('click', () => { if (page < pages) { page++; load(); } });
    if (manage) {
        el('facilityReset').addEventListener('click', () => {
            el('facilityForm').reset(); el('facilityId').value = ''; el('facilityVersion').value = '';
        });
        el('facilityForm').addEventListener('submit', async event => {
            event.preventDefault();
            const id = el('facilityId').value;
            const body = {
                campusId: Number(el('facilityCampus').value), code: el('facilityCode').value.trim(),
                name: el('facilityName').value.trim(), address: el('facilityAddress').value.trim() || null,
                isActive: el('facilityActive').checked, rowVersion: el('facilityVersion').value || null
            };
            try {
                await api(id ? '/' + encodeURIComponent(id) : '', id ? 'PUT' : 'POST', body);
                notice(tr('Space saved.', 'স্থান সংরক্ষিত হয়েছে।'), true);
                el('facilityReset').click(); page = 1; await load();
            } catch (error) { notice(error.message); }
        });
    }
    load();
})();
