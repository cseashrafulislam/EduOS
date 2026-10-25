(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('hostelOccupancyInsightsPanel')) return;
    let sequence = 0;
    const labels = [
        ['totalRooms', 'Rooms'], ['activeRooms', 'Active rooms'],
        ['totalCapacity', 'Configured capacity'], ['activeBeds', 'Active beds'],
        ['occupiedBeds', 'Occupied beds'], ['availableBeds', 'Available beds'],
        ['occupancyPercent', 'Occupancy', '%'], ['fullRooms', 'Full rooms'],
        ['overCapacityRooms', 'Over capacity'], ['inactiveRoomsWithAllocations', 'Inactive with allocations'],
        ['roomsWithBedCapacityMismatch', 'Bed/capacity mismatch'],
        ['unprovisionedRooms', 'Rooms without active beds'],
        ['highUtilizationRooms', 'At least 90% occupied'], ['riskRooms', 'Rooms needing review']
    ];
    function filters() {
        const q = new URLSearchParams(), campus = el('hostelCapacityCampus')?.value, search = el('hostelCapacitySearch')?.value.trim();
        if (campus) q.set('campusId', campus);
        if (search) q.set('search', search);
        return q.toString();
    }
    function stale() {
        ++sequence;
        el('hostelOccupancyInsightsMetrics').replaceChildren();
        el('hostelOccupancyInsightsExport').disabled = true;
        el('hostelOccupancyInsightsRefresh').disabled = false;
        el('hostelOccupancyInsightsStatus').textContent = 'Filter changed; apply to refresh metrics.';
    }
    async function load() {
        const token = ++sequence, snapshot = filters();
        const status = el('hostelOccupancyInsightsStatus'), button = el('hostelOccupancyInsightsRefresh');
        status.textContent = 'Loading occupancy metrics…'; button.disabled = true;
        el('hostelOccupancyInsightsExport').disabled = true;
        try {
            const response = await fetch('/api/hostel/occupancy-insights?' + snapshot,
                {credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
            const result = await response.json().catch(()=>null);
            if (token !== sequence || snapshot !== filters()) return;
            if (!response.ok || !result?.success || !result.data)
                throw Error(result?.message || 'Hostel occupancy metrics unavailable.');
            const grid = el('hostelOccupancyInsightsMetrics'); grid.replaceChildren();
            for (const [key, title, suffix = ''] of labels) {
                const col = document.createElement('div'), card = document.createElement('div'),
                    name = document.createElement('div'), value = document.createElement('strong');
                col.className = 'col'; card.className = 'border rounded p-2 h-100';
                name.className = 'small text-muted'; name.textContent = title;
                value.className = 'fs-5'; value.textContent = String(result.data[key] ?? '—') + suffix;
                card.append(name, document.createElement('br'), value); col.append(card); grid.append(col);
            }
            el('hostelOccupancyInsightsExport').disabled = false;
            status.textContent = 'Updated ' + new Date(result.data.generatedAtUtc).toLocaleString() +
                ' · Live occupancy may change during retrieval.';
        } catch (error) {
            if (token === sequence) {
                el('hostelOccupancyInsightsMetrics').replaceChildren();
                status.textContent = error.message;
            }
        } finally { if (token === sequence) button.disabled = false; }
    }
    el('hostelOccupancyInsightsRefresh').addEventListener('click', load);
    el('hostelCapacitySearch')?.addEventListener('input', stale);
    el('hostelCapacityCampus')?.addEventListener('change', stale);
    el('hostelCapacityFilter')?.addEventListener('submit', () => setTimeout(load, 0));
    window.addEventListener('hostel:allocations-changed', load);
    el('hostelOccupancyInsightsExport').addEventListener('click', async () => {
        const button = el('hostelOccupancyInsightsExport'), snapshot = filters(), token = sequence;
        if (button.disabled) return;
        button.disabled = true;
        try {
            const response = await fetch('/api/hostel/occupancy-insights/export?' + snapshot,
                { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/csv' } });
            if (!response.ok) {
                const error = await response.json().catch(() => null);
                throw Error(error?.message || 'Hostel occupancy export failed.');
            }
            const blob = await response.blob();
            if (token !== sequence || snapshot !== filters())
                throw Error('Occupancy filters changed during export. Refresh and retry.');
            const url = URL.createObjectURL(blob), link = document.createElement('a');
            link.href = url; link.download = 'hostel-occupancy-insights.csv';
            document.body.append(link); link.click(); link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch (error) {
            if (token === sequence) el('hostelOccupancyInsightsStatus').textContent = error.message;
        } finally {
            if (token === sequence && snapshot === filters()) button.disabled = false;
        }
    });
    load();
})();
