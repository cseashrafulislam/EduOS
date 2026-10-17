(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('transportHistoryPanel')) return;
    const pageSize = 25;
    let page = 1, pages = 1, serial = 0;
    const stateName = value => ({1:'Active',2:'Closed',3:'Cancelled'})[String(value)] || 'Unknown';
    const status = (message, error = false) => {
        const node = el('transportHistoryStatus');
        node.textContent = message; node.className = error ? 'small text-danger mb-2' : 'small text-muted mb-2';
    };
    const fields = [
        ['transportHistoryCampus','campusId'],['transportHistoryState','state'],
        ['transportHistoryRoute','routeReference'],['transportHistoryVehicle','vehicleReference'],
        ['transportHistoryFrom','fromDate'],['transportHistoryTo','toDate'],
        ['transportHistorySearch','search']
    ];
    function filters() {
        const params = new URLSearchParams({page:String(page),pageSize:String(pageSize)});
        for (const [field,key] of fields) {
            const value = el(field).value.trim();
            if (value) params.set(key,value);
        }
        return params;
    }
    function invalidate() {
        ++serial;
        el('transportHistoryPrev').disabled = true; el('transportHistoryNext').disabled = true;
        el('transportHistoryRows').replaceChildren();
        el('transportHistoryPage').textContent = '';
    }
    async function api(url) {
        const response = await fetch(url,{credentials:'same-origin',cache:'no-store',
            headers:{Accept:'application/json'}});
        const json = await response.json().catch(() => null);
        if (!response.ok || !json?.success) throw Error(json?.message || 'Transport history request failed.');
        return json.data;
    }
    async function load() {
        invalidate();
        const request = serial, requestedPage = page;
        const from = el('transportHistoryFrom').value, to = el('transportHistoryTo').value;
        if (from && to && from > to) { status('Start-from date cannot exceed start-to date.',true); return; }
        status('Loading transport history…');
        try {
            const data = await api('/api/transport/assignment-history?' + filters());
            if (request !== serial) return;
            const records = Array.isArray(data?.items) ? data.items : [];
            pages = Math.max(1,Math.ceil((Number(data?.totalCount)||0)/pageSize));
            const body = el('transportHistoryRows');
            for (const item of records) {
                const tr = document.createElement('tr');
                for (const value of [item.campusName,item.studentName,item.studentCode,item.routeName,
                    item.vehicleNumber,item.startDate,item.endDate || '—',stateName(item.state)]) {
                    const cell = document.createElement('td');
                    cell.textContent = String(value ?? '—'); tr.append(cell);
                }
                body.append(tr);
            }
            if (!records.length) {
                const tr = document.createElement('tr'), cell = document.createElement('td');
                cell.colSpan = 8; cell.textContent = 'No matching transport history.';
                tr.append(cell); body.append(tr);
            }
            el('transportHistoryPage').textContent = 'Page ' + requestedPage + ' of ' + pages +
                ' · ' + (Number(data?.totalCount)||0) + ' records';
            el('transportHistoryPrev').disabled = requestedPage <= 1;
            el('transportHistoryNext').disabled = requestedPage >= pages;
            status('Showing authorized transport assignment history without fares or contact details.');
        } catch (error) {
            if (request === serial) { invalidate(); status(error.message,true); }
        }
    }
    async function loadOptions() {
        const requests = [
            ['/api/transport/route-setup/campuses','transportHistoryCampus','All permitted campuses',x=>x.id,x=>x.name],
            ['/api/transport/routes','transportHistoryRoute','All permitted routes',x=>x.reference,x=>x.name],
            ['/api/transport/vehicles','transportHistoryVehicle','All permitted vehicles',x=>x.reference,x=>x.vehicleNumber]
        ];
        const results = await Promise.allSettled(requests.map(async ([url,id,placeholder,key,label]) => {
            const rows = await api(url);
            const select = el(id);
            select.replaceChildren(new Option(placeholder,''));
            for (const row of rows || []) {
                const value = key(row);
                if (value !== null && value !== undefined && String(value))
                    select.add(new Option(String(label(row) || 'Unknown'),String(value)));
            }
        }));
        if (results.some(x => x.status === 'rejected'))
            status('Some filter choices could not load. History remains campus-authorized.',true);
    }
    el('transportHistoryFilter').addEventListener('submit', event => { event.preventDefault(); page = 1; load(); });
    for (const [field] of fields) el(field).addEventListener('change', () => {
        page = 1; invalidate(); status('Filters changed; select Filter to refresh.');
    });
    el('transportHistoryPrev').addEventListener('click', () => { page = Math.max(1,page-1); load(); });
    el('transportHistoryNext').addEventListener('click', () => { page = Math.min(pages,page+1); load(); });
    load(); loadOptions();
})();
