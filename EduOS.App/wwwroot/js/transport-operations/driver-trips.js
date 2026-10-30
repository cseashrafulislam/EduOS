(() => {
    'use strict';
    const root = document.getElementById('driverTrips');
    if (!root) return;
    const el = id => document.getElementById(id);
    let page = 1, pages = 1, riderPage = 1, riderPages = 1, selected = null, selectedState = '';
    const pendingKeys = new Map();
    const status = (message, error = false) => {
        el('driverTripStatus').textContent = message;
        el('driverTripStatus').className = 'small ' + (error ? 'text-danger' : 'text-muted');
    };
    const cell = value => {
        const td = document.createElement('td');
        td.textContent = value == null || value === '' ? '—' : String(value);
        return td;
    };
    async function api(url, data) {
        const headers = { Accept: 'application/json' };
        if (data !== undefined) {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content ||
                root.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (!token) throw new Error('Security token unavailable; reload.');
            headers.RequestVerificationToken = token;
            headers['Content-Type'] = 'application/json';
        }
        const response = await fetch(url, {
            method: data === undefined ? 'GET' : 'POST', headers, credentials: 'same-origin',
            cache: 'no-store', ...(data === undefined ? {} : { body: JSON.stringify(data) })
        });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success)
            throw new Error(result?.message || 'Transport request failed.');
        return result.data;
    }
    async function loadTrips() {
        try {
            const data = await api('/api/transport/driver-trips?' +
                new URLSearchParams({page:String(page),pageSize:'20'}));
            pages = data.totalPages || 1;
            el('driverTripPage').textContent = page + ' / ' + pages;
            el('driverTripPrev').disabled = page <= 1;
            el('driverTripNext').disabled = page >= pages;
            const body = el('driverTripRows'); body.replaceChildren();
            for (const item of data.items || []) {
                const tr = document.createElement('tr');
                [item.routeName,item.vehicleNumber,item.direction,item.state,item.riderCount]
                    .forEach(x => tr.append(cell(x)));
                const action = document.createElement('td');
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-outline-primary btn-sm';
                button.textContent = 'Riders / যাত্রী';
                button.addEventListener('click', () => {
                    selected = item.reference; selectedState = item.state; riderPage = 1;
                    loadRiders();
                });
                action.append(button); tr.append(action); body.append(tr);
            }
            if (!data.items?.length) {
                const tr = document.createElement('tr'), td = cell('No assigned trips / নির্ধারিত ট্রিপ নেই');
                td.colSpan = 6; tr.append(td); body.append(tr);
                el('driverManifest').hidden = true; selected = null;
            }
        } catch (e) { status(e.message, true); }
    }
    async function loadRiders() {
        if (!selected) return;
        const reference = selected;
        try {
            const data = await api('/api/transport/driver-trips/' +
                encodeURIComponent(reference) + '/riders?' +
                new URLSearchParams({page:String(riderPage),pageSize:'25'}));
            if (reference !== selected) return;
            riderPages = data.totalPages || 1;
            el('driverManifest').hidden = false;
            el('driverRiderPage').textContent = riderPage + ' / ' + riderPages;
            el('driverRiderPrev').disabled = riderPage <= 1;
            el('driverRiderNext').disabled = riderPage >= riderPages;
            const body = el('driverRiderRows'); body.replaceChildren();
            for (const rider of data.items || []) {
                const tr = document.createElement('tr');
                [rider.studentCode,rider.pickupStop,rider.dropStop,rider.attendanceState]
                    .forEach(x => tr.append(cell(x)));
                const action = document.createElement('td');
                const next = selectedState !== 'Dispatched' ? [] :
                    rider.attendanceState === 'Awaiting' ? ['Boarded','NoShow'] :
                    rider.attendanceState === 'Boarded' ? ['Dropped'] : [];
                for (const eventType of next) {
                    const button = document.createElement('button');
                    button.type = 'button'; button.className = 'btn btn-outline-primary btn-sm me-1 mb-1';
                    button.textContent = ({
                        Boarded:'Board / উঠেছে', Dropped:'Drop / নেমেছে',
                        NoShow:'No-show / অনুপস্থিত'
                    })[eventType];
                    button.addEventListener('click', async () => {
                        if (!confirm('Record ' + eventType + ' for ' + rider.studentCode + '?')) return;
                        const key = reference + ':' + rider.studentCode + ':' + eventType;
                        if (!pendingKeys.has(key)) pendingKeys.set(key, crypto.randomUUID());
                        button.disabled = true;
                        try {
                            await api('/api/transport/driver-trips/' +
                                encodeURIComponent(reference) + '/attendance', {
                                clientRequestId:pendingKeys.get(key), studentCode:rider.studentCode, eventType
                            });
                            pendingKeys.delete(key);
                            status('Attendance saved / উপস্থিতি সংরক্ষিত');
                            await loadRiders();
                        } catch (e) { status(e.message, true); }
                        finally { button.disabled = false; }
                    });
                    action.append(button);
                }
                tr.append(action); body.append(tr);
            }
        } catch (e) {
            el('driverManifest').hidden = true;
            status(e.message, true);
        }
    }
    el('driverTripReload').addEventListener('click', () => { page = 1; loadTrips(); if (selected) loadRiders(); });
    el('driverTripPrint').addEventListener('click', () => {
        if (!selected) return;
        window.open('/transport/driver-trips/' + encodeURIComponent(selected) + '/print',
            '_blank', 'noopener,noreferrer');
    });
    el('driverTripExport').addEventListener('click', async () => {
        if (!selected) return;
        const button = el('driverTripExport'); button.disabled = true;
        try {
            const response = await fetch('/api/transport/driver-trips/' +
                encodeURIComponent(selected) + '/export', {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/csv' }
            });
            if (!response.ok) {
                const error = await response.json().catch(() => null);
                throw new Error(error?.message || 'Manifest export unavailable.');
            }
            const blob = await response.blob(), url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url; link.download = 'my-transport-trip-riders.csv';
            document.body.append(link); link.click(); link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch (e) { status(e.message, true); }
        finally { button.disabled = false; }
    });
    el('driverTripPrev').addEventListener('click', () => { if (page > 1) { page--; loadTrips(); } });
    el('driverTripNext').addEventListener('click', () => { if (page < pages) { page++; loadTrips(); } });
    el('driverRiderPrev').addEventListener('click', () => { if (riderPage > 1) { riderPage--; loadRiders(); } });
    el('driverRiderNext').addEventListener('click', () => { if (riderPage < riderPages) { riderPage++; loadRiders(); } });
    loadTrips();
})();
