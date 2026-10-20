(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('hostelAvailabilityPanel')) return;
    const from = el('hostelAvailabilityFrom'), to = el('hostelAvailabilityTo');
    const d = new Date(), today = [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');
    from.min = today; from.value = today; to.min = today; to.value = today;
    let page = 1, pages = 1, serial = 0, loaded = false, exporting = false;
    const status = (s, bad=false) => { const n=el('hostelAvailabilityStatus'); n.textContent=s; n.className=bad?'small text-danger mb-2':'small text-muted mb-2'; };
    function params(paged=true) {
        const q = new URLSearchParams({fromDate:from.value,toDate:to.value});
        if (paged) { q.set('page',String(page)); q.set('pageSize','25'); }
        const campus = el('hostelAvailabilityCampus').value;
        if (campus) q.set('campusId',campus);
        const term = el('hostelAvailabilitySearch').value.trim();
        if (term) q.set('search',term);
        return q;
    }
    function invalidate() {
        ++serial; loaded=false; el('hostelAvailabilityRows').replaceChildren();
        el('hostelAvailabilityCsv').disabled=true;
        el('hostelAvailabilityPrev').disabled=true; el('hostelAvailabilityNext').disabled=true;
    }
    async function load() {
        invalidate();
        const n=serial, requested=page;
        if (!from.value || !to.value || from.value < today || to.value < from.value) { status('Select a valid future date range.',true); return; }
        status('Checking interval availability…');
        try {
            const response = await fetch('/api/hostel/bed-availability?'+params(),{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
            const result = await response.json().catch(()=>null);
            if (n !== serial) return;
            if (!response.ok || !result?.success) throw Error(result?.message || 'Availability request failed.');
            const data=result.data || {}, items=Array.isArray(data.items)?data.items:[];
            pages=Math.max(1,Math.ceil((Number(data.totalCount)||0)/25));
            for (const item of items) {
                const tr=document.createElement('tr');
                for (const value of [item.hostelName,item.roomNumber,item.bedNumber,item.roomCapacity,item.remainingRoomSlots]) {
                    const td=document.createElement('td'); td.textContent=String(value??''); tr.append(td);
                }
                el('hostelAvailabilityRows').append(tr);
            }
            if (!items.length) {
                const tr=document.createElement('tr'),td=document.createElement('td');
                td.colSpan=5;td.textContent='No beds available for the entire interval.';tr.append(td);el('hostelAvailabilityRows').append(tr);
            }
            loaded=items.length>0;
            el('hostelAvailabilityPage').textContent='Page '+requested+' of '+pages+' · '+(Number(data.totalCount)||0)+' available beds';
            el('hostelAvailabilityPrev').disabled=requested<=1;
            el('hostelAvailabilityNext').disabled=requested>=pages;
            el('hostelAvailabilityCsv').disabled=!loaded||exporting;
            status('Available for the full interval. Results are not a reservation.');
        } catch (error) { if (n===serial) { invalidate(); status(error.message,true); } }
    }
    function changed() { page=1;invalidate();status('Filters changed. Select Find to refresh.'); }
    for (const id of ['hostelAvailabilityFrom','hostelAvailabilityTo','hostelAvailabilityCampus','hostelAvailabilitySearch'])
        el(id).addEventListener(id==='hostelAvailabilitySearch'?'input':'change',changed);
    from.addEventListener('change',()=>{to.min=from.value||today;if(to.value<from.value)to.value=from.value;});
    el('hostelAvailabilityForm').addEventListener('submit',e=>{e.preventDefault();page=1;load();});
    el('hostelAvailabilityPrev').addEventListener('click',()=>{page=Math.max(1,page-1);load();});
    el('hostelAvailabilityNext').addEventListener('click',()=>{page=Math.min(pages,page+1);load();});
    el('hostelAvailabilityCsv').addEventListener('click',async()=>{
        if (!loaded||exporting) return;
        const n=serial, button=el('hostelAvailabilityCsv'); exporting=true;button.disabled=true;
        try {
            const response=await fetch('/api/hostel/bed-availability/export?'+params(false),{credentials:'same-origin',cache:'no-store',headers:{Accept:'text/csv'}});
            if (!response.ok) { const error=await response.json().catch(()=>null);throw Error(error?.message||'Export failed.'); }
            const blob=await response.blob();if(n!==serial)return;
            const url=URL.createObjectURL(blob),link=document.createElement('a');
            link.href=url;link.download='hostel-bed-availability.csv';document.body.append(link);link.click();link.remove();
            setTimeout(()=>URL.revokeObjectURL(url),1000);
            status('Availability CSV downloaded.');
        } catch(error) {if(n===serial)status(error.message,true);}
        finally {exporting=false;button.disabled=!loaded;}
    });
    async function campuses() {
        try {
            const response=await fetch('/api/hostel/facility-campuses',{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
            const result=await response.json();
            if (!response.ok||!result?.success||!Array.isArray(result.data))throw Error('Campus list unavailable.');
            const select=el('hostelAvailabilityCampus');
            for(const campus of result.data) {
                const id=Number(campus.id);
                if(Number.isSafeInteger(id)&&id>0)select.add(new Option(String(campus.name??'Campus'),String(id)));
            }
        } catch(error) {status(error.message,true);}
    }
    load();campuses();
})();
