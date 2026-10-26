(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('hostelPeakOccupancyPanel')) return;
    const endpoint = '/api/hostel/peak-occupancy-forecast';
    const from = el('hostelPeakOccupancyFrom');
    const to = el('hostelPeakOccupancyTo');
    const campus = el('hostelPeakOccupancyCampus');
    const search = el('hostelPeakOccupancySearch');
    const attention = el('hostelPeakOccupancyAttention');
    const rows = el('hostelPeakOccupancyRows');
    const exportButton = el('hostelPeakOccupancyExport');
    const previous = el('hostelPeakOccupancyPrev');
    const next = el('hostelPeakOccupancyNext');
    let page = 1, pages = 1, sequence = 0, loadedFilter = null, exporting = false;
    const iso = d => [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');
    const addDays = (value, days) => {
        const parts = value.split('-').map(Number);
        const d = new Date(parts[0],parts[1]-1,parts[2]);
        d.setDate(d.getDate()+days);
        return iso(d);
    };
    const today = iso(new Date()), lastStart = addDays(today,365);
    from.min = today; from.max = lastStart; from.value = today;
    const updateEndBounds = () => {
        to.min = from.value;
        to.max = addDays(from.value,89);
        if (!to.value || to.value < to.min) to.value = to.min;
        if (to.value > to.max) to.value = to.max;
    };
    updateEndBounds();
    to.value = addDays(today,6);
    const status = (message, error = false) => {
        const box = el('hostelPeakOccupancyStatus');
        box.textContent = message;
        box.className = error ? 'small text-danger mb-2' : 'small text-muted mb-2';
    };
    const filters = () => {
        const q = new URLSearchParams({fromDate:from.value,toDate:to.value,attentionOnly:String(attention.checked)});
        if (campus.value) q.set('campusId',campus.value);
        if (search.value.trim()) q.set('search',search.value.trim());
        return q.toString();
    };
    const cell = value => {
        const td = document.createElement('td');
        td.textContent = String(value ?? '');
        return td;
    };
    function invalidate() {
        ++sequence; loadedFilter = null; rows.replaceChildren();
        exportButton.disabled = true; previous.disabled = true; next.disabled = true;
    }
    function validPeriod() {
        return from.value >= today && from.value <= lastStart &&
            to.value >= from.value && to.value <= addDays(from.value,89);
    }
    async function load() {
        invalidate();
        if (!validPeriod()) { status('Choose a future period of 1–90 days starting within 365 days.',true); return; }
        const request = sequence, snapshot = filters(), requestedPage = page;
        const q = new URLSearchParams(snapshot);
        q.set('page',String(page)); q.set('pageSize','25');
        status('Checking peak room occupancy…');
        try {
            const response = await fetch(endpoint + '?' + q,{
                credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}
            });
            const result = await response.json().catch(()=>null);
            if (!response.ok || !result?.success || !result.data)
                throw Error(result?.message || 'Peak occupancy unavailable.');
            if (request !== sequence || snapshot !== filters()) return;
            const data = result.data, items = Array.isArray(data.items) ? data.items : [];
            for (const item of items) {
                const tr = document.createElement('tr');
                [item.campusName,item.hostelName,item.roomNumber,item.capacity,item.activeBeds,
                 item.peakReservedBeds,item.peakDate,item.minimumAvailableBeds,
                 item.maximumDuplicateBookings,item.maximumInactiveReservedBeds,
                 item.attentionDays,item.status].forEach(value => tr.append(cell(value)));
                if (item.needsAttention) tr.classList.add('table-warning');
                rows.append(tr);
            }
            if (!items.length) {
                const tr = document.createElement('tr'), td = cell('No matching rooms');
                td.colSpan = 12; tr.append(td); rows.append(tr);
            }
            pages = Math.max(1,Math.ceil((Number(data.totalCount)||0)/25));
            el('hostelPeakOccupancyPage').textContent = 'Page ' + requestedPage + ' of ' + pages +
                ' · ' + (Number(data.totalCount)||0) + ' rooms';
            previous.disabled = requestedPage <= 1; next.disabled = requestedPage >= pages;
            loadedFilter = snapshot; exportButton.disabled = !items.length || exporting;
            status('Peak reserved beds are distinct physical beds on one day. Recheck before allocating.');
        } catch(error) {
            if (request === sequence) { invalidate(); status(error.message,true); }
        }
    }
    const changed = () => { page = 1; invalidate(); status('Filters changed. Run forecast to refresh.'); };
    from.addEventListener('change',()=>{ updateEndBounds(); changed(); });
    for (const field of [to,campus,attention]) field.addEventListener('change',changed);
    search.addEventListener('input',changed);
    el('hostelPeakOccupancyFilters').addEventListener('submit',event=>{
        event.preventDefault(); page = 1; load();
    });
    previous.addEventListener('click',()=>{page=Math.max(1,page-1);load();});
    next.addEventListener('click',()=>{page=Math.min(pages,page+1);load();});
    exportButton.addEventListener('click',async()=>{
        const snapshot = filters(), request = sequence;
        if (!loadedFilter || loadedFilter !== snapshot || exporting || !validPeriod()) return;
        exporting = true; exportButton.disabled = true;
        try {
            const response = await fetch(endpoint + '/export?' + snapshot,{
                credentials:'same-origin',cache:'no-store',headers:{Accept:'text/csv'}
            });
            if (!response.ok) {
                const error = await response.json().catch(()=>null);
                throw Error(error?.message || 'Peak forecast export failed.');
            }
            const blob = await response.blob();
            if (request !== sequence || loadedFilter !== snapshot || filters() !== snapshot)
                throw Error('Filters changed during export. Refresh and retry.');
            const url = URL.createObjectURL(blob), link = document.createElement('a');
            link.href = url; link.download = 'hostel-peak-occupancy.csv';
            document.body.append(link); link.click(); link.remove();
            setTimeout(()=>URL.revokeObjectURL(url),1000);
            status('Peak forecast CSV downloaded.');
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
