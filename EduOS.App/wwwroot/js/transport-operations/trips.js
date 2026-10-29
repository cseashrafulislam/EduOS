(() => {
    'use strict';
    const root = document.getElementById('transportTrips');
    if (!root) return;
    const el = id => document.getElementById(id);
    const today = () => { const d = new Date(); return [d.getFullYear(), String(d.getMonth()+1).padStart(2,'0'), String(d.getDate()).padStart(2,'0')].join('-'); };
    let planKey = crypto.randomUUID(), page = 1, pages = 1, trip = null, riderPage = 1, riderPages = 1;
    el('tripDate').value = today();
    const status = (message, error = false) => { el('tripStatus').textContent = message; el('tripStatus').className = 'small ' + (error ? 'text-danger' : 'text-muted'); };
    async function api(url, method = 'GET', data) {
        const headers = { Accept: 'application/json' };
        if (data !== undefined) {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content ||
                document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (!token) throw new Error('Security token unavailable; reload.');
            headers.RequestVerificationToken = token;
            headers['Content-Type'] = 'application/json';
        }
        const response = await fetch(url, { method, headers, credentials: 'same-origin', cache: 'no-store',
            ...(data !== undefined ? { body: JSON.stringify(data) } : {}) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Transport request failed.');
        return result.data;
    }
    function option(select, value, label) { const o = document.createElement('option'); o.value = value; o.textContent = label; select.append(o); }
    function cell(value) { const td = document.createElement('td'); td.textContent = value == null || value === '' ? '—' : String(value); return td; }
    async function setup() {
        try {
            const [routes, vehicles] = await Promise.all([api('/api/transport/routes'), api('/api/transport/vehicles')]);
            for (const [id, items, label] of [['tripRoute',routes,'Select route'],['tripVehicle',vehicles,'Select vehicle']]) {
                const select = el(id); select.replaceChildren(); option(select,'',label);
                (items || []).forEach(x => option(select,x.reference,id === 'tripRoute' ? x.name : x.vehicleNumber + ' (' + x.capacity + ')'));
            }
        } catch (e) { status(e.message, true); }
    }
    async function load() {
        try {
            const data = await api('/api/transport/trips?' + new URLSearchParams({date:el('tripDate').value,page:String(page),pageSize:'25'}));
            pages = data.totalPages || 1;
            el('tripPage').textContent = page + ' / ' + pages;
            el('tripPrev').disabled = page <= 1; el('tripNext').disabled = page >= pages;
            const body = el('tripRows'); body.replaceChildren();
            for (const item of data.items || []) {
                const tr = document.createElement('tr');
                [item.campusName,item.routeName,item.vehicleNumber,item.operatingDate,item.direction,item.state,item.riderCount].forEach(x => tr.append(cell(x)));
                const actions = document.createElement('td'), button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-outline-primary btn-sm';
                button.textContent = item.state === 'Planned' ? 'Dispatch / ছাড়ুন' : 'Manifest / তালিকা';
                button.addEventListener('click', async () => {
                    button.disabled = true;
                    try {
                        if (item.state === 'Planned') {
                            if (item.operatingDate !== today()) throw new Error('Dispatch is allowed only on the operating date.');
                            if (!confirm('Dispatch this trip and freeze its rider list?')) return;
                            await api('/api/transport/trips/' + encodeURIComponent(item.reference) + '/dispatch','POST',
                                {clientRequestId:crypto.randomUUID(),rowVersion:item.rowVersion});
                            status('Trip dispatched / ট্রিপ ডিসপ্যাচ হয়েছে');
                            await load();
                        } else { trip = item.reference; riderPage = 1; await loadRiders(); }
                    } catch (e) { status(e.message, true); } finally { button.disabled = false; }
                });
                actions.append(button);
                if (item.state === 'Dispatched') {
                    const finish = document.createElement('button');
                    finish.type = 'button'; finish.className = 'btn btn-outline-success btn-sm ms-1';
                    finish.textContent = 'Complete / শেষ';
                    finish.addEventListener('click', async () => {
                        if (!confirm('Complete this trip? Every rider must be Dropped or NoShow. Attendance will be locked.')) return;
                        finish.disabled = true;
                        try {
                            await api('/api/transport/trips/' + encodeURIComponent(item.reference) + '/complete', 'POST',
                                { clientRequestId: crypto.randomUUID(), rowVersion: item.rowVersion });
                            status('Trip completed / ট্রিপ সম্পন্ন');
                            await load();
                            if (trip === item.reference) await loadRiders();
                        } catch (e) { status(e.message, true); } finally { finish.disabled = false; }
                    });
                    actions.append(finish);
                }
                tr.append(actions); body.append(tr);
            }
            if (!data.items?.length) { const tr = document.createElement('tr'), td = cell('No trips / কোনো ট্রিপ নেই'); td.colSpan = 8; tr.append(td); body.append(tr); }
        } catch (e) { status(e.message, true); }
    }
    async function loadRiders() {
        if (!trip) return;
        try {
            const data = await api('/api/transport/trips/' + encodeURIComponent(trip) + '/riders?' + new URLSearchParams({page:String(riderPage),pageSize:'25'}));
            riderPages = data.totalPages || 1;
            el('tripManifestPanel').hidden = false;
            el('tripRiderPage').textContent = riderPage + ' / ' + riderPages;
            el('tripRiderPrev').disabled = riderPage <= 1; el('tripRiderNext').disabled = riderPage >= riderPages;
            const body = el('tripManifestRows'); body.replaceChildren();
            for (const row of data.items || []) {
                const tr = document.createElement('tr');
                [row.studentCode,row.pickupStop,row.dropStop,row.attendanceState].forEach(x=>tr.append(cell(x)));
                const actions = document.createElement('td');
                const next = row.attendanceState === 'Awaiting' ? ['Boarded','NoShow'] :
                    row.attendanceState === 'Boarded' ? ['Dropped'] : [];
                for (const eventType of next) {
                    const btn = document.createElement('button');
                    btn.type = 'button'; btn.className = 'btn btn-outline-primary btn-sm me-1';
                    btn.textContent = ({Boarded:'Board / উঠেছে',Dropped:'Drop / নেমেছে',NoShow:'No-show / অনুপস্থিত'})[eventType];
                    btn.addEventListener('click', async () => {
                        if (!confirm('Record '+eventType+' for '+row.studentCode+'?')) return;
                        btn.disabled = true;
                        try {
                            await api('/api/transport/trips/'+encodeURIComponent(trip)+'/attendance','POST',
                                {clientRequestId:crypto.randomUUID(),studentCode:row.studentCode,eventType});
                            status('Attendance saved / উপস্থিতি সংরক্ষিত');
                            await loadRiders();
                        } catch (e) { status(e.message,true); } finally { btn.disabled = false; }
                    });
                    actions.append(btn);
                }
                tr.append(actions); body.append(tr);
            }
        } catch (e) { status(e.message, true); }
    }
    el('tripPlanForm').addEventListener('submit', async e => {
        e.preventDefault(); if (!e.currentTarget.reportValidity()) return;
        const button = el('tripPlan'); button.disabled = true;
        try {
            await api('/api/transport/trips','POST',{clientRequestId:planKey,routeReference:el('tripRoute').value,
                vehicleReference:el('tripVehicle').value,operatingDate:el('tripDate').value,direction:el('tripDirection').value});
            planKey = crypto.randomUUID(); page = 1; status('Trip planned / ট্রিপ পরিকল্পিত'); await load();
        } catch (err) { status(err.message, true); } finally { button.disabled = false; }
    });
    el('tripReload').addEventListener('click', () => { page=1; load(); });
    el('tripDate').addEventListener('change', () => { page=1; trip=null; el('tripManifestPanel').hidden=true; load(); });
    el('tripPrev').addEventListener('click',()=>{if(page>1){page--;load();}});
    el('tripNext').addEventListener('click',()=>{if(page<pages){page++;load();}});
    el('tripRiderPrev').addEventListener('click',()=>{if(riderPage>1){riderPage--;loadRiders();}});
    el('tripRiderNext').addEventListener('click',()=>{if(riderPage<riderPages){riderPage++;loadRiders();}});
    el('tripExport').addEventListener('click',()=>{if(trip) window.location.assign('/api/transport/trips/'+encodeURIComponent(trip)+'/export');});
    el('tripPrint').addEventListener('click',()=>window.print());
    setup(); load();
})();
