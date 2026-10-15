(() => {
    'use strict';
    if (!document.getElementById('operationsInsightsPage')) return;
    const el = id => document.getElementById(id);
    const modules = [
        { key: 'Transport', campuses: '/api/transport/route-setup/campuses',
          endpoint: '/api/transport/fleet-insights',
          metrics: [['totalVehicles','Vehicles'],['activeVehicles','Active vehicles'],
            ['totalSeats','Seats'],['activeRiders','Riders'],['availableSeats','Available seats'],
            ['fleetUtilizationPercent','Utilization','%'],['overCapacityVehicles','Over capacity'],
            ['withoutCurrentRouteVehicles','No current route'],
            ['inactiveWithRidersVehicles','Inactive with riders'],
            ['highUtilizationVehicles','At least 90% full'],['riskVehicles','Vehicles needing review']] },
        { key: 'Hostel', campuses: '/api/hostel/facility-campuses',
          endpoint: '/api/hostel/occupancy-insights',
          metrics: [['totalRooms','Rooms'],['activeRooms','Active rooms'],
            ['totalCapacity','Configured capacity'],['activeBeds','Active beds'],
            ['occupiedBeds','Occupied beds'],['availableBeds','Available beds'],
            ['occupancyPercent','Occupancy','%'],['fullRooms','Full rooms'],
            ['overCapacityRooms','Over capacity'],['inactiveRoomsWithAllocations','Inactive with allocations'],
            ['roomsWithBedCapacityMismatch','Bed/capacity mismatch'],
            ['unprovisionedRooms','Rooms without active beds'],
            ['highUtilizationRooms','At least 90% full'],['riskRooms','Rooms needing review']] }
    ];
    async function api(url, signal) {
        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json' }, signal });
        const body = await response.json().catch(() => null);
        if (response.status === 401 || response.status === 403)
            throw new Error('This module is not available for your role or subscription.');
        if (!response.ok || !body?.success || body.data == null)
            throw new Error('Metrics are unavailable. Refresh or narrow the filter.');
        return body.data;
    }
    function number(value, suffix) {
        if (value === null || value === undefined || value === '') return '—';
        const n = Number(value);
        if (!Number.isFinite(n) || n < 0 || n > 1000000000000) return '—';
        return (suffix ? n.toLocaleString(undefined, { maximumFractionDigits: 1 }) :
            n.toLocaleString(undefined, { maximumFractionDigits: 0 })) + (suffix || '');
    }
    function setup(module) {
        const prefix = 'ops' + module.key, campus = el(prefix + 'Campus'),
            search = el(prefix + 'Search'), button = el(prefix + 'Refresh'),
            status = el(prefix + 'Status'), grid = el(prefix + 'Metrics'),
            form = el(prefix + 'Filters');
        let serial = 0, controller = null, available = false;
        function invalidate() {
            serial++;
            controller?.abort();
            controller = null;
            grid.replaceChildren();
            // A filter change aborts an in-flight load; its stale finally block cannot re-enable Refresh.
            if (available) button.disabled = false;
            status.textContent = 'Filters changed; refresh to load current metrics.';
        }
        function query() {
            const params = new URLSearchParams(), campusId = campus.value,
                term = search.value.trim();
            if (campusId) params.set('campusId', campusId);
            if (term) params.set('search', term);
            return params.toString();
        }
        async function load() {
            if (!available) return;
            invalidate();
            const token = serial, snapshot = query();
            controller = new AbortController();
            const signal = controller.signal;
            button.disabled = true;
            status.textContent = 'Loading authorized aggregate metrics…';
            try {
                const data = await api(module.endpoint + '?' + snapshot, signal);
                if (token !== serial || snapshot !== query()) return;
                for (const [key, title, suffix = ''] of module.metrics) {
                    const col = document.createElement('div'), card = document.createElement('div'),
                        label = document.createElement('div'), value = document.createElement('strong');
                    col.className = 'col'; card.className = 'border rounded p-2 h-100';
                    label.className = 'small text-muted'; label.textContent = title;
                    value.className = 'fs-5'; value.textContent = number(data[key], suffix);
                    card.append(label, document.createElement('br'), value);
                    col.append(card); grid.append(col);
                }
                const timestamp = new Date(data.generatedAtUtc);
                status.textContent = 'Updated ' + (Number.isNaN(timestamp.getTime()) ?
                    'just now' : timestamp.toLocaleString()) + ' · Live estimates; refresh for changes.';
            } catch (error) {
                if (token === serial && error.name !== 'AbortError') {
                    grid.replaceChildren();
                    status.textContent = error.message;
                }
            } finally {
                if (token === serial) { controller = null; button.disabled = false; }
            }
        }
        async function initialize() {
            try {
                const options = await api(module.campuses);
                if (!Array.isArray(options)) throw new Error('Campus access is unavailable.');
                campus.replaceChildren(new Option('All authorized campuses', ''));
                for (const item of options) {
                    const id = Number(item.id);
                    if (Number.isSafeInteger(id) && id > 0)
                        campus.add(new Option(String(item.name ?? 'Campus'), String(id)));
                }
                available = true; campus.disabled = false; button.disabled = false;
                await load();
            } catch (error) {
                available = false; campus.disabled = true; button.disabled = true;
                grid.replaceChildren(); status.textContent = error.message;
            }
        }
        search.addEventListener('input', invalidate);
        campus.addEventListener('change', invalidate);
        form.addEventListener('submit', event => { event.preventDefault(); load(); });
        initialize();
    }
    modules.forEach(setup);
})();
