(() => {
    'use strict';
    const root = document.getElementById('hostelHistoryPanel');
    if (!root) return;
    const el = id => document.getElementById(id);
    const pageSize = 25;
    let page = 1, pages = 1, serial = 0;
    const stateName = value => ({1:'Active',2:'Closed',3:'Cancelled'})[String(value)] || String(value ?? 'Unknown');
    const status = (message, error = false) => {
        const node = el('hostelHistoryStatus');
        node.textContent = message; node.className = error ? 'small text-danger mb-2' : 'small text-muted mb-2';
    };
    function filters() {
        const params = new URLSearchParams({page:String(page),pageSize:String(pageSize)});
        for (const [field, key] of [['hostelHistoryCampus','campusId'],['hostelHistoryState','state'],
            ['hostelHistoryFrom','fromDate'],['hostelHistoryTo','toDate'],['hostelHistorySearch','search']]) {
            const value = el(field).value.trim();
            if (value) params.set(key,value);
        }
        return params;
    }
    function invalidate() {
        ++serial;
        el('hostelHistoryPrev').disabled = true; el('hostelHistoryNext').disabled = true;
        el('hostelHistoryRows').replaceChildren();
        el('hostelHistoryPage').textContent = '';
    }
    async function load() {
        invalidate();
        const token = serial, requestedPage = page;
        const from = el('hostelHistoryFrom').value, to = el('hostelHistoryTo').value;
        if (from && to && from > to) { status('Start-from date cannot exceed start-to date.',true); return; }
        status('Loading allocation history…');
        try {
            const response = await fetch('/api/hostel/allocation-history?' + filters(),{
                credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
            const result = await response.json().catch(() => null);
            if (token !== serial) return;
            if (!response.ok || !result?.success) throw Error(result?.message || 'History could not be loaded.');
            const data = result.data || {}, records = Array.isArray(data.items) ? data.items : [];
            pages = Math.max(1,Math.ceil((Number(data.totalCount)||0)/pageSize));
            const body = el('hostelHistoryRows');
            for (const item of records) {
                const tr = document.createElement('tr');
                for (const value of [item.campusName,item.studentName,item.studentCode,item.hostelName,
                    (item.roomNumber || '') + ' / ' + (item.bedNumber || ''),item.startDate,item.endDate || '—',
                    stateName(item.state)]) {
                    const cell = document.createElement('td');
                    cell.textContent = String(value ?? '—'); tr.append(cell);
                }
                body.append(tr);
            }
            if (!records.length) {
                const tr = document.createElement('tr'), cell = document.createElement('td');
                cell.colSpan = 8; cell.textContent = 'No matching allocation history.';
                tr.append(cell); body.append(tr);
            }
            el('hostelHistoryPage').textContent = 'Page ' + requestedPage + ' of ' + pages +
                ' · ' + (Number(data.totalCount)||0) + ' records';
            el('hostelHistoryPrev').disabled = requestedPage <= 1;
            el('hostelHistoryNext').disabled = requestedPage >= pages;
            status('Showing authorized allocation history without financial or contact details.');
        } catch (error) {
            if (token === serial) { invalidate(); status(error.message,true); }
        }
    }
    el('hostelHistoryFilter').addEventListener('submit', event => {
        event.preventDefault(); page = 1; load();
    });
    for (const field of ['hostelHistoryCampus','hostelHistoryState','hostelHistoryFrom','hostelHistoryTo','hostelHistorySearch']) {
        el(field).addEventListener('change', () => { page = 1; invalidate(); status('Filters changed; select Filter to refresh.'); });
    }
    el('hostelHistoryPrev').addEventListener('click', () => { page = Math.max(1,page-1); load(); });
    el('hostelHistoryNext').addEventListener('click', () => { page = Math.min(pages,page+1); load(); });
    async function campuses() {
        try {
            const response = await fetch('/api/hostel/facility-campuses',{
                credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success || !Array.isArray(result.data))
                throw Error(result?.message || 'Campus filter options unavailable.');
            const select = el('hostelHistoryCampus');
            select.replaceChildren(new Option('All permitted campuses',''));
            for (const campus of result.data) {
                const id = Number(campus.id);
                if (Number.isSafeInteger(id) && id > 0)
                    select.add(new Option(String(campus.name || 'Campus'),String(id)));
            }
        } catch (error) { status(error.message + ' You may still view permitted campuses.',true); }
    }
    load(); campuses();
})();
