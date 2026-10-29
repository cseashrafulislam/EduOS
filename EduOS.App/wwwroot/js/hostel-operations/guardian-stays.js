(() => {
    'use strict';
    const panel = document.getElementById('hostelGuardianStays');
    if (!panel) return;
    const el = id => document.getElementById(id);
    let page = 1, pages = 1, request = 0, exportRequest = 0;
    const query = () => new URLSearchParams({ page: String(page), search: el('hostelGuardianSearch').value.trim() });
    const cell = value => { const td = document.createElement('td'); td.textContent = value || '—'; return td; };
    async function load() {
        const seq = ++request;
        ++exportRequest;
        el('hostelGuardianCsv').disabled = true;
        el('hostelGuardianStatus').textContent = 'Loading…';
        try {
            const q = query(); q.set('pageSize', '20');
            const response = await fetch('/api/hostel/guardian-stays?' + q, {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
            });
            const result = await response.json();
            if (!response.ok || !result?.success) throw new Error(result?.message || 'Unable to load stays.');
            if (seq !== request) return;
            const data = result.data, body = el('hostelGuardianRows');
            body.replaceChildren();
            for (const item of data.items || []) {
                const tr = document.createElement('tr');
                [item.studentName, item.studentCode, item.campusName, item.hostelName,
                    item.roomNumber + ' / ' + item.bedNumber, item.startDate, item.endDate].forEach(v => tr.append(cell(v)));
                body.append(tr);
            }
            if (!data.items?.length) {
                const tr = document.createElement('tr'), td = cell('No authorized current stays.');
                td.colSpan = 7; tr.append(td); body.append(tr);
            }
            pages = Math.max(1, Number(data.totalPages) || 1);
            el('hostelGuardianPage').textContent = 'Page ' + page + ' of ' + pages;
            el('hostelGuardianPrev').disabled = page <= 1;
            el('hostelGuardianNext').disabled = page >= pages;
            el('hostelGuardianCsv').disabled = !data.items?.length;
            el('hostelGuardianPrint').href = '/hostel/guardian-stays/print?' + query();
            el('hostelGuardianStatus').textContent = data.totalCount + ' authorized scheduled stays';
        } catch (error) {
            if (seq === request) {
                el('hostelGuardianRows').replaceChildren();
                el('hostelGuardianStatus').textContent = error.message;
            }
        }
    }
    el('hostelGuardianFilter').addEventListener('submit', e => { e.preventDefault(); page = 1; load(); });
    el('hostelGuardianPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
    el('hostelGuardianNext').addEventListener('click', () => { if (page < pages) { page++; load(); } });
    el('hostelGuardianCsv').addEventListener('click', async () => {
        const seq = ++exportRequest, button = el('hostelGuardianCsv');
        button.disabled = true;
        try {
            const response = await fetch('/api/hostel/guardian-stays/export?' + query(), {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/csv' }
            });
            if (!response.ok) throw new Error('Unable to export stays.');
            const blob = await response.blob();
            if (seq !== exportRequest) return;
            const url = URL.createObjectURL(blob), a = document.createElement('a');
            a.href = url; a.download = 'guardian-hostel-stays.csv';
            document.body.append(a); a.click(); a.remove();
            URL.revokeObjectURL(url);
        } catch (error) {
            if (seq === exportRequest) el('hostelGuardianStatus').textContent = error.message;
        } finally { if (seq === exportRequest) button.disabled = false; }
    });
    load();
})();
