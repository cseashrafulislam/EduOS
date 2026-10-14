(() => {
    'use strict';
    const root = document.getElementById('hostelCapacityPanel');
    if (!root) return;
    const el = id => document.getElementById(id);
    let page = 1, pages = 1, serial = 0, currentRows = [], currentPage = 0;
    const cols = ['Hostel','Room','Capacity','Active beds','Occupied','Available','Inactive beds','Risk'];
    const csv = value => {
        const valueText = String(value ?? '');
        const safe = /^\s*[=+\-@\t\r]/.test(valueText) ? "'" + valueText : valueText;
        return '"' + safe.replace(/"/g, '""') + '"';
    };
    const values = row => [row.hostelName,row.roomNumber,row.capacity,row.activeBeds,
        row.occupiedBeds,row.availableBeds,row.inactiveBeds,row.hasRisk ? 'Review' : 'OK'];
    const status = (message,error=false) => {
        el('hostelCapacityStatus').textContent=message;
        el('hostelCapacityStatus').className=error ? 'small text-danger' : 'small text-muted';
    };
    function invalidate() {
        ++serial; currentRows=[]; currentPage=0;
        el('hostelCapacityCsv').disabled=true;
        el('hostelCapacityPrev').disabled=true;
        el('hostelCapacityNext').disabled=true;
        el('hostelCapacityRows').replaceChildren();
    }
    async function load() {
        invalidate();
        const requestSerial=serial, requestedPage=page;
        const params=new URLSearchParams({page:String(page),pageSize:'25'});
        const search=el('hostelCapacitySearch').value.trim();
        if (search) params.set('search',search);
        status('Loading room capacity…');
        try {
            const response=await fetch('/api/hostel/capacity?'+params,{
                credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
            const result=await response.json().catch(()=>null);
            if (requestSerial!==serial) return;
            if (!response.ok || !result?.success) throw new Error(result?.message||'Unable to load capacity.');
            const data=result.data||{}, rows=Array.isArray(data.items)?data.items:[];
            currentRows=rows; currentPage=requestedPage;
            pages=Math.max(1,Math.ceil((Number(data.totalCount)||0)/25));
            const tbody=el('hostelCapacityRows');
            for (const row of rows) {
                const tr=document.createElement('tr');
                for (const value of values(row)) {
                    const td=document.createElement('td');td.textContent=String(value??'');tr.append(td);
                }
                if (row.hasRisk) tr.classList.add('table-warning');
                tbody.append(tr);
            }
            if (!rows.length) {
                const tr=document.createElement('tr'),td=document.createElement('td');
                td.colSpan=cols.length;td.textContent='No rooms match the filter.';tr.append(td);tbody.append(tr);
            }
            el('hostelCapacityPage').textContent='Page '+requestedPage+' of '+pages+' · '+(Number(data.totalCount)||0)+' rooms';
            el('hostelCapacityPrev').disabled=requestedPage<=1;
            el('hostelCapacityNext').disabled=requestedPage>=pages;
            el('hostelCapacityCsv').disabled=!rows.length;
            status('Capacity is calculated from tenant-scoped beds and active allocations.');
        } catch (error) {
            if (requestSerial===serial) { invalidate(); status(error.message,true); }
        }
    }
    el('hostelCapacitySearch').addEventListener('input',()=>{
        page=1;invalidate();el('hostelCapacityPage').textContent='Apply filter to load capacity.';
        status('Filter changed; apply to refresh the report.');
    });
    el('hostelCapacityFilter').addEventListener('submit',e=>{e.preventDefault();page=1;load();});
    el('hostelCapacityPrev').addEventListener('click',()=>{page=Math.max(1,page-1);load();});
    el('hostelCapacityNext').addEventListener('click',()=>{page=Math.min(pages,page+1);load();});
    el('hostelCapacityCsv').addEventListener('click',()=>{
        if (!currentRows.length||currentPage!==page) return;
        const lines=[cols.map(csv).join(',')];
        for (const row of currentRows) lines.push(values(row).map(csv).join(','));
        const blob=new Blob(['\uFEFF'+lines.join('\r\n')],{type:'text/csv;charset=utf-8'});
        const url=URL.createObjectURL(blob),a=document.createElement('a');
        a.href=url;a.download='hostel-capacity-page-'+page+'.csv';
        document.body.append(a);a.click();a.remove();
        setTimeout(()=>URL.revokeObjectURL(url),1000);
    });
    load();
})();
