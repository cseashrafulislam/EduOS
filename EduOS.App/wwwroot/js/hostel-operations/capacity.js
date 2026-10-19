(() => {
    'use strict';
    const root = document.getElementById('hostelCapacityPanel');
    if (!root) return;
    const el = id => document.getElementById(id);
    let page = 1, pages = 1, serial = 0, currentPage = 0, hasRows = false, exporting = false;
    const cols = ['Hostel','Room','Capacity','Active beds','Occupied','Available','Inactive beds','Risk'];
    const values = row => [row.hostelName,row.roomNumber,row.capacity,row.activeBeds,
        row.occupiedBeds,row.availableBeds,row.inactiveBeds,row.hasRisk ? 'Review' : 'OK'];
    const status = (message,error=false) => {
        el('hostelCapacityStatus').textContent = message;
        el('hostelCapacityStatus').className = error ? 'small text-danger' : 'small text-muted';
    };
    function query(includePage=true) {
        const params = new URLSearchParams();
        if (includePage) { params.set('page',String(page)); params.set('pageSize','25'); }
        const campus = el('hostelCapacityCampus').value;
        if (campus) params.set('campusId',campus);
        const search = el('hostelCapacitySearch').value.trim();
        if (search) params.set('search',search);
        if (el('hostelCapacityAttentionOnly').checked) params.set('attentionOnly','true');
        return params;
    }
    function invalidate() {
        ++serial; currentPage = 0; hasRows = false;
        el('hostelCapacityCsv').disabled = true;
        el('hostelCapacityPrev').disabled = true;
        el('hostelCapacityNext').disabled = true;
        el('hostelCapacityRows').replaceChildren();
    }
    async function load() {
        invalidate();
        const requestSerial = serial, requestedPage = page;
        status('Loading room capacity…');
        try {
            const response = await fetch('/api/hostel/capacity?' + query(),{
                credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
            const result = await response.json().catch(()=>null);
            if (requestSerial !== serial) return;
            if (!response.ok || !result?.success) throw Error(result?.message || 'Unable to load capacity.');
            const data = result.data || {}, rows = Array.isArray(data.items) ? data.items : [];
            currentPage = requestedPage; hasRows = rows.length > 0;
            pages = Math.max(1,Math.ceil((Number(data.totalCount)||0)/25));
            const tbody = el('hostelCapacityRows');
            for (const row of rows) {
                const tr = document.createElement('tr');
                for (const value of values(row)) {
                    const td = document.createElement('td'); td.textContent = String(value??''); tr.append(td);
                }
                if (row.hasRisk) tr.classList.add('table-warning');
                tbody.append(tr);
            }
            if (!rows.length) {
                const tr = document.createElement('tr'), td = document.createElement('td');
                td.colSpan = cols.length; td.textContent = 'No rooms match the filter.'; tr.append(td); tbody.append(tr);
            }
            el('hostelCapacityPage').textContent = 'Page ' + requestedPage + ' of ' + pages + ' · ' + (Number(data.totalCount)||0) + ' rooms';
            el('hostelCapacityPrev').disabled = requestedPage <= 1;
            el('hostelCapacityNext').disabled = requestedPage >= pages;
            el('hostelCapacityCsv').disabled = !hasRows || exporting;
            status(el('hostelCapacityAttentionOnly').checked ?
                'Showing rooms needing review. Risk-only mode scans at most 1,000 candidate rooms.' :
                'Capacity uses authorized, non-deleted beds and active allocations.');
        } catch (error) {
            if (requestSerial === serial) { invalidate(); status(error.message,true); }
        }
    }
    const filterChanged = () => {
        page = 1; invalidate(); el('hostelCapacityPage').textContent = 'Apply filter to load capacity.';
        status('Filter changed; apply to refresh the report.');
    };
    el('hostelCapacitySearch').addEventListener('input',filterChanged);
    el('hostelCapacityCampus').addEventListener('change',filterChanged);
    el('hostelCapacityAttentionOnly').addEventListener('change',filterChanged);
    el('hostelCapacityFilter').addEventListener('submit',e=>{e.preventDefault();page=1;load();});
    el('hostelCapacityPrev').addEventListener('click',()=>{page=Math.max(1,page-1);load();});
    el('hostelCapacityNext').addEventListener('click',()=>{page=Math.min(pages,page+1);load();});
    el('hostelCapacityCsv').addEventListener('click',async()=>{
        if (!hasRows || currentPage !== page || exporting) return;
        const requestSerial = serial, button = el('hostelCapacityCsv');
        exporting = true; button.disabled = true;
        try {
            const response = await fetch('/api/hostel/capacity/export?' + query(false),{
                credentials:'same-origin',cache:'no-store',headers:{Accept:'text/csv'}});
            if (!response.ok) {
                const error = await response.json().catch(()=>null);
                throw Error(error?.message || 'Capacity CSV export failed.');
            }
            const blob = await response.blob();
            if (requestSerial !== serial) return;
            const url = URL.createObjectURL(blob), link = document.createElement('a');
            link.href = url; link.download = el('hostelCapacityAttentionOnly').checked ?
                'hostel-capacity-risks.csv' : 'hostel-capacity.csv';
            document.body.append(link); link.click(); link.remove();
            setTimeout(()=>URL.revokeObjectURL(url),1000);
            status('Filtered capacity CSV downloaded.');
        } catch (error) { if (requestSerial === serial) status(error.message,true); }
        finally {
            exporting = false;
            button.disabled = !hasRows || currentPage !== page;
        }
    });
    el('hostelCapacityCsv').textContent = 'Export all filtered rooms (CSV, max 1,000)';
    async function loadCampuses() {
        const campus = el('hostelCapacityCampus');
        campus.disabled = true;
        try {
            const response = await fetch('/api/hostel/facility-campuses', {
                credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
            const result = await response.json().catch(()=>null);
            if (!response.ok || !result?.success || !Array.isArray(result.data))
                throw Error(result?.message || 'Campus options unavailable.');
            campus.replaceChildren(new Option('All permitted campuses',''));
            for (const item of result.data) {
                const id = Number(item.id);
                if (Number.isSafeInteger(id) && id > 0)
                    campus.add(new Option(String(item.name ?? 'Campus'),String(id)));
            }
        } catch (error) {
            status(error.message + ' All permitted campuses remain available.',true);
        } finally { campus.disabled = false; }
    }
    load();
    loadCampuses();
})();