(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('hostelMyHistoryPanel')) return;
    let page = 1, pages = 1, requestSequence = 0, exportSequence = 0;
    const state = () => el('hostelMyHistoryState').value;
    function params() {
        const q = new URLSearchParams({ page: String(page) });
        if (state()) q.set('state', state());
        return q;
    }
    function cell(value) {
        const td = document.createElement('td');
        td.textContent = value === null || value === undefined || value === '' ? '—' : String(value);
        return td;
    }
    async function load() {
        const sequence = ++requestSequence;
        ++exportSequence;
        el('hostelMyHistoryCsv').disabled = true;
        el('hostelMyHistoryStatus').textContent = 'Loading stays…';
        try {
            const q = params(); q.set('pageSize', '20');
            const response = await fetch('/api/hostel/my-history?' + q, {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
            });
            const result = await response.json();
            if (!response.ok || !result?.success) throw new Error(result?.message || 'Unable to load stay history.');
            if (sequence !== requestSequence) return;
            const data = result.data, body = el('hostelMyHistoryRows');
            body.replaceChildren();
            for (const row of data.items || []) {
                const tr = document.createElement('tr');
                [row.campusName, row.hostelName, row.roomNumber + ' / ' + row.bedNumber,
                    row.startDate, row.endDate, row.state].forEach(v => tr.append(cell(v)));
                body.append(tr);
            }
            if (!data.items?.length) {
                const tr = document.createElement('tr'), td = cell('No hostel stays found.');
                td.colSpan = 6; tr.append(td); body.append(tr);
            }
            pages = Math.max(1, Number(data.totalPages) || 1);
            el('hostelMyHistoryPage').textContent = 'Page ' + page + ' of ' + pages;
            el('hostelMyHistoryPrev').disabled = page <= 1;
            el('hostelMyHistoryNext').disabled = page >= pages;
            el('hostelMyHistoryCsv').disabled = !data.items?.length;
            el('hostelMyHistoryPrint').href = '/hostel/my-history/print?' + params();
            el('hostelMyHistoryStatus').textContent = data.totalCount + ' authorized stays';
        } catch (error) {
            if (sequence === requestSequence) {
                el('hostelMyHistoryStatus').textContent = error.message;
                el('hostelMyHistoryRows').replaceChildren();
                el('hostelMyHistoryCsv').disabled = true;
            }
        }
    }
    el('hostelMyHistoryState').addEventListener('change', () => { page = 1; load(); });
    el('hostelMyHistoryPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
    el('hostelMyHistoryNext').addEventListener('click', () => { if (page < pages) { page++; load(); } });
    el('hostelMyHistoryCsv').addEventListener('click', async () => {
        const sequence = ++exportSequence, q = params(), button = el('hostelMyHistoryCsv');
        button.disabled = true;
        try {
            const response = await fetch('/api/hostel/my-history/export?' + q, {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/csv' }
            });
            if (!response.ok) throw new Error('Unable to export stay history.');
            const blob = await response.blob();
            if (sequence !== exportSequence) return;
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url; a.download = 'my-hostel-stays.csv';
            document.body.append(a); a.click(); a.remove();
            URL.revokeObjectURL(url);
        } catch (error) {
            if (sequence === exportSequence) el('hostelMyHistoryStatus').textContent = error.message;
        } finally {
            if (sequence === exportSequence) button.disabled = false;
        }
    });
    load();
})();
