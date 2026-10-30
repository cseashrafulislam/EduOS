(() => {
    'use strict';
    const root = document.getElementById('transportTrips');
    if (!root) return;
    const el = id => document.getElementById(id);
    const today = () => { const d = new Date(); return [d.getFullYear(), String(d.getMonth()+1).padStart(2,'0'), String(d.getDate()).padStart(2,'0')].join('-'); };
    let scheduleKey = crypto.randomUUID();
    let planKey = crypto.randomUUID(), page = 1, pages = 1, trip = null, tripState = null, riderPage = 1, riderPages = 1;
    let correction = null;
    const canCorrect = root.dataset.canCorrect === 'true';
    el('tripDate').value = today();
    el('tripScheduleEnd').value = today();
    el('tripScheduleEnd').min = today();
    { const max = new Date(); max.setDate(max.getDate() + 30);
      el('tripScheduleEnd').max = [max.getFullYear(), String(max.getMonth()+1).padStart(2,'0'), String(max.getDate()).padStart(2,'0')].join('-'); }
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
                        } else { trip = item.reference; tripState = item.state; riderPage = 1; await loadRiders(); }
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
                            if (trip === item.reference) { tripState = 'Completed'; await loadRiders(); }
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
                if (tripState === 'Completed') {
                    const history = document.createElement('button');
                    history.type = 'button'; history.className = 'btn btn-outline-secondary btn-sm me-1';
                    history.textContent = 'History / ইতিহাস';
                    history.addEventListener('click', async () => {
                        try {
                            const events = await api('/api/transport/trips/'+encodeURIComponent(trip)+'/attendance/history?'+
                                new URLSearchParams({studentCode:row.studentCode}));
                            status((events || []).map(x => x.eventType + ' · ' + new Date(x.occurredAt).toLocaleString() +
                                (x.isCorrection ? ' · corrected: ' + x.correctionReason : '')).join(' | ') || 'No attendance events.');
                        } catch (e) { status(e.message,true); }
                    });
                    actions.append(history);
                    if (canCorrect && ['Dropped','NoShow'].includes(row.attendanceState)) {
                        const edit = document.createElement('button');
                        edit.type = 'button'; edit.className = 'btn btn-outline-warning btn-sm';
                        edit.textContent = 'Correct / সংশোধন';
                        edit.addEventListener('click', () => {
                            correction = {studentCode:row.studentCode,expectedState:row.attendanceState,
                                correctedState:row.attendanceState === 'Dropped' ? 'NoShow' : 'Dropped'};
                            el('tripCorrectionStudent').textContent = row.studentCode;
                            el('tripCorrectionState').textContent = row.attendanceState + ' → ' + correction.correctedState;
                            el('tripCorrectionReason').value = '';
                            el('tripCorrectionDialog').showModal();
                            el('tripCorrectionReason').focus();
                        });
                        actions.append(edit);
                    }
                }
                tr.append(actions); body.append(tr);
            }
        } catch (e) { status(e.message, true); }
    }
    el('tripCorrectionCancel').addEventListener('click', () => el('tripCorrectionDialog').close());
    el('tripCorrectionForm').addEventListener('submit', async e => {
        e.preventDefault();
        if (!correction || !e.currentTarget.reportValidity()) return;
        const button = e.currentTarget.querySelector('button[type="submit"]');
        button.disabled = true;
        try {
            await api('/api/transport/trips/'+encodeURIComponent(trip)+'/attendance/corrections', 'POST', {
                ...correction, clientRequestId:crypto.randomUUID(), reason:el('tripCorrectionReason').value
            });
            el('tripCorrectionDialog').close(); correction = null;
            status('Correction recorded / সংশোধন সংরক্ষিত'); await loadRiders();
        } catch (err) { status(err.message,true); } finally { button.disabled = false; }
    });
    el('tripPlanForm').addEventListener('submit', async e => {
        e.preventDefault(); if (!e.currentTarget.reportValidity()) return;
        const button = el('tripPlan'); button.disabled = true;
        try {
            await api('/api/transport/trips','POST',{clientRequestId:planKey,routeReference:el('tripRoute').value,
                vehicleReference:el('tripVehicle').value,operatingDate:el('tripDate').value,direction:el('tripDirection').value});
            planKey = crypto.randomUUID(); page = 1; status('Trip planned / ট্রিপ পরিকল্পিত'); await load();
        } catch (err) { status(err.message, true); } finally { button.disabled = false; }
    });
    el('tripSchedule').addEventListener('click', async () => {
        if (!el('tripPlanForm').reportValidity() || !el('tripScheduleEnd').reportValidity()) return;
        const start = el('tripDate').value, end = el('tripScheduleEnd').value;
        const days = Math.round((Date.parse(end + 'T00:00:00Z') - Date.parse(start + 'T00:00:00Z')) / 86400000) + 1;
        if (!Number.isInteger(days) || days < 1 || days > 7) {
            status('Select 1–7 consecutive days / ১–৭ দিন নির্বাচন করুন', true); return;
        }
        const button = el('tripSchedule'); button.disabled = true;
        try {
            const result = await api('/api/transport/trips/schedule', 'POST', {
                clientRequestId: scheduleKey, routeReference: el('tripRoute').value,
                vehicleReference: el('tripVehicle').value, startDate: start, endDate: end,
                direction: el('tripDirection').value, skipFridaySaturday: el('tripScheduleWeekend').checked
            });
            const summary = 'New ' + result.newlyPlanned + ', existing ' + result.alreadyPlanned +
                ' / নতুন ' + result.newlyPlanned + ', পূর্বের ' + result.alreadyPlanned;
            if (result.failedDate) {
                status(summary + ' — stopped ' + result.failedDate + ': ' + result.failureMessage, true);
            } else {
                scheduleKey = crypto.randomUUID();
                status(summary); page = 1; await load();
            }
        } catch (e) { status(e.message, true); }
        finally { button.disabled = false; }
    });
    el('tripReload').addEventListener('click', () => { page=1; load(); });
    el('tripDate').addEventListener('change', () => { page=1; trip=null; el('tripManifestPanel').hidden=true; tripState=null; load(); });
    el('tripPrev').addEventListener('click',()=>{if(page>1){page--;load();}});
    el('tripNext').addEventListener('click',()=>{if(page<pages){page++;load();}});
    el('tripRiderPrev').addEventListener('click',()=>{if(riderPage>1){riderPage--;loadRiders();}});
    el('tripRiderNext').addEventListener('click',()=>{if(riderPage<riderPages){riderPage++;loadRiders();}});
    el('tripExport').addEventListener('click', async () => {
        if (!trip) return;
        const button = el('tripExport'); button.disabled = true;
        try {
            const response = await fetch('/api/transport/trips/' + encodeURIComponent(trip) + '/export', {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/csv' }
            });
            if (!response.ok) {
                const error = await response.json().catch(() => null);
                throw new Error(error?.message || 'Manifest export unavailable.');
            }
            const blob = await response.blob(), url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url; link.download = 'transport-trip-riders.csv';
            document.body.append(link); link.click(); link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch (e) { status(e.message, true); }
        finally { button.disabled = false; }
    });
    el('tripPrint').addEventListener('click', () => {
        if (!trip) return;
        window.open('/transport/trips/' + encodeURIComponent(trip) + '/print',
            '_blank', 'noopener,noreferrer');
    });
    setup(); load();
})();
