(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('transportFleetInsightsPanel')) return;
    let sequence = 0;
    const labels = [
        ['totalVehicles', 'Vehicles'], ['activeVehicles', 'Active vehicles'],
        ['totalSeats', 'Seats'], ['activeRiders', 'Riders'],
        ['availableSeats', 'Available seats'], ['fleetUtilizationPercent', 'Utilization', '%'],
        ['overCapacityVehicles', 'Over capacity'], ['withoutCurrentRouteVehicles', 'No current route'],
        ['inactiveWithRidersVehicles', 'Inactive with riders'], ['highUtilizationVehicles', 'At least 90% full'],
        ['riskVehicles', 'Vehicles needing review']
    ];
    function filters() {
        const q = new URLSearchParams(), campus = el('transportCapacityCampus')?.value,
            search = el('transportCapacitySearch')?.value.trim();
        if (campus) q.set('campusId', campus);
        if (search) q.set('search', search);
        return q.toString();
    }
    function invalidate() {
        ++sequence;
        el('transportFleetInsightsMetrics').replaceChildren();
        el('transportFleetInsightsStatus').textContent = 'Filters changed; apply to refresh fleet metrics.';
    }
    async function load() {
        const token = ++sequence, filterSnapshot = filters();
        const status = el('transportFleetInsightsStatus'), button = el('transportFleetInsightsRefresh');
        status.textContent = 'Loading fleet metrics…'; button.disabled = true;
        try {
            const response = await fetch('/api/transport/fleet-insights?' + filterSnapshot,
                { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
            const result = await response.json().catch(() => null);
            if (token !== sequence || filterSnapshot !== filters()) return;
            if (!response.ok || !result?.success || !result.data) throw Error(result?.message || 'Fleet metrics unavailable.');
            const grid = el('transportFleetInsightsMetrics'); grid.replaceChildren();
            for (const [key, title, suffix = ''] of labels) {
                const col = document.createElement('div'), card = document.createElement('div'),
                    name = document.createElement('div'), value = document.createElement('strong');
                col.className = 'col'; card.className = 'border rounded p-2 h-100';
                name.className = 'small text-muted'; name.textContent = title;
                value.className = 'fs-5'; value.textContent = String(result.data?.[key] ?? '—') + suffix;
                card.append(name, document.createElement('br'), value); col.append(card); grid.append(col);
            }
            status.textContent = 'Updated ' + new Date(result.data.generatedAtUtc).toLocaleString() +
                ' · Live counts may change during retrieval.';
        } catch (error) {
            if (token === sequence) { el('transportFleetInsightsMetrics').replaceChildren(); status.textContent = error.message; }
        } finally { if (token === sequence) button.disabled = false; }
    }
    el('transportFleetInsightsRefresh').addEventListener('click', () => load());
    el('transportCapacitySearch')?.addEventListener('input', invalidate);
    el('transportCapacityCampus')?.addEventListener('change', invalidate);
    el('transportCapacityFilter')?.addEventListener('submit', () => { setTimeout(load, 0); });
    window.addEventListener('transport:vehicles-changed', () => load());
    load();
})();
