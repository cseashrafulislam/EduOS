(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('hostelOccupancyForecastPanel')) return;
    const endpoint = '/api/hostel/occupancy-forecast';
    const date = el('hostelOccupancyForecastDate');
    const campus = el('hostelOccupancyForecastCampus');
    const search = el('hostelOccupancyForecastSearch');
    const attention = el('hostelOccupancyForecastAttention');
    const rows = el('hostelOccupancyForecastRows');
    const exportButton = el('hostelOccupancyForecastExport');
    const previous = el('hostelOccupancyForecastPrev');
    const next = el('hostelOccupancyForecastNext');
    let page = 1, pages = 1, sequence = 0, loadedFilter = null, exporting = false;
    const iso = d => [d.getFullYear(), String(d.getMonth()+1).padStart(2,'0'), String(d.getDate()).padStart(2,'0')].join('-');
    const today = iso(new Date());
    const maxDate = new Date(); maxDate.setDate(maxDate.getDate() + 365);
    date.value = today; date.min = today; date.max = iso(maxDate);
    const status = (message, error = false) => {
        const box = el('hostelOccupancyForecastStatus');
        box.textContent = message;
        box.className = error ? 'small text-danger mb-2' : 'small text-muted mb-2';
    };
    const filters = () => {
        const q = new URLSearchParams({operatingDate:date.value,attentionOnly:String(attention.checked)});
        if (campus.value) q.set('campusId',campus.value);
        if (search.value.trim()) q.set('search',search.value.trim());
        return q.toString();
    };
    const cell = value => { const td = document.createElement('td'); td.textContent = String(value ?? ''); return td; };
    function invalidate() {
        ++sequence; loadedFilter = null; rows.replaceChildren();
        exportButton.disabled = true; previous.disabled = true; next.disabled = true;
    }
    async function load() {
        invalidate();
        if (!date.value || date.value < today || date.value > date.max) {
            status('Choose today or a date within 365 days.',true); return;
        }
        const request = sequence, snapshot = filters(), requestedPage = page;
        const q = new URLSearchParams(snapshot);
        q.set('page',String(page)); q.set('pageSize','25');
        status('Checking future room occupancy…');
        try {
            const response = await fetch(endpoint + '?' + q,{
                credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}
            });
            const result = await response.json().catch(()=>null);
            if (!response.ok || !result?.success || !result.data)
                throw Error(result?.message || 'Future occupancy unavailable.');
            if (request !== sequence || snapshot !== filters()) return;
            const data = result.data, items = Array.isArray(data.items) ? data.items : [];
            for (const item of items) {
                const tr = document.createElement('tr');
                [item.campusName,item.hostelName,item.roomNumber,item.capacity,item.activeBeds,
                 item.reservedBeds,item.availableBeds,item.inactiveReservedBeds,
                 item.duplicateReservations,item.status].forEach(value => tr.append(cell(value)));
                if (item.needsAttention) tr.classList.add('table-warning');
                rows.append(tr);
            }
            if (!items.length) {
                const tr = document.createElement('tr'), td = cell('No matching rooms');
                td.colSpan = 10; tr.append(td); rows.append(tr);
            }
            pages = Math.max(1,Math.ceil((Number(data.totalCount)||0)/25));
            el('hostelOccupancyForecastPage').textContent = 'Page ' + requestedPage + ' of ' + pages +
                ' · ' + (Number(data.totalCount)||0) + ' rooms';
            previous.disabled = requestedPage <= 1; next.disabled = requestedPage >= pages;
            loadedFilter = snapshot; exportButton.disabled = !items.length || exporting;
            status('Advisory snapshot for ' + date.value + '. Recheck before assigning beds.');
        } catch(error) {
            if (request === sequence) { invalidate(); status(error.message,true); }
        }
    }
    const changed = () => { page = 1; invalidate(); status('Filters changed. Apply to refresh.'); };
    for (const field of [date,campus,attention]) field.addEventListener('change',changed);
    search.addEventListener('input',changed);
    el('hostelOccupancyForecastFilters').addEventListener('submit',event => {
        event.preventDefault(); page = 1; load();
    });
    previous.addEventListener('click',()=>{page=Math.max(1,page-1);load();});
    next.addEventListener('click',()=>{page=Math.min(pages,page+1);load();});
    exportButton.addEventListener('click',async()=>{
        const snapshot = filters(), request = sequence;
        if (!loadedFilter || loadedFilter !== snapshot || exporting) return;
        exporting = true; exportButton.disabled = true;
        try {
            const response = await fetch(endpoint + '/export?' + snapshot,{
                credentials:'same-origin',cache:'no-store',headers:{Accept:'text/csv'}
            });
            if (!response.ok) {
                const error = await response.json().catch(()=>null);
                throw Error(error?.message || 'Forecast export failed.');
            }
            const blob = await response.blob();
            if (request !== sequence || loadedFilter !== snapshot || filters() !== snapshot)
                throw Error('Filters changed during export. Refresh and retry.');
            const url = URL.createObjectURL(blob), link = document.createElement('a');
            link.href = url; link.download = 'hostel-future-occupancy.csv';
            document.body.append(link); link.click(); link.remove();
            setTimeout(()=>URL.revokeObjectURL(url),1000);
            status('Forecast CSV downloaded.');
        } catch(error) { if (request === sequence) status(error.message,true); }
        finally { exporting = false; exportButton.disabled = !loadedFilter || loadedFilter !== filters(); }
    });
    (async()=>{
        try {
            const response = await fetch('/api/hostel/facility-campuses',{
                credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}
            });
            const result = await response.json().catch(()=>null);
            if (!response.ok || !result?.success || !Array.isArray(result.data))
                throw Error('Campus list unavailable.');
            for (const item of result.data) {
                const id = Number(item.id);
                if (Number.isSafeInteger(id) && id > 0)
                    campus.add(new Option(String(item.name ?? 'Campus'),String(id)));
            }
        } catch(error) { status(error.message,true); }
        await load();
    })();
})();
