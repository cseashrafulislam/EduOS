(() => {
    'use strict';
    const el = id => document.getElementById(id), form = el('transportVehicleSetupForm');
    if (!form) return;
    const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    let page = 1, pages = 1, sequence = 0;
    function alertUser(message, ok = false) {
        const box = el('transportVehicleSetupAlert');
        box.className = 'alert alert-' + (ok ? 'success' : 'danger');
        box.textContent = message; box.focus();
    }
    async function api(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) {
            headers['Content-Type'] = 'application/json';
            headers.RequestVerificationToken = token;
        }
        const response = await fetch(url, { method, headers, credentials: 'same-origin',
            cache: 'no-store', ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const data = await response.json().catch(() => null);
        if (!response.ok || !data?.success) throw new Error(data?.message || 'Vehicle request failed.');
        return data.data;
    }
    function option(select, value, label) {
        const item = document.createElement('option'); item.value = String(value);
        item.textContent = label; select.append(item);
    }
    function cell(value) {
        const item = document.createElement('td');
        item.textContent = value == null ? '—' : String(value); return item;
    }
    async function loadCampuses() {
        const data = await api('/api/transport/route-setup/campuses');
        const create = el('transportVehicleSetupCampus'), filter = el('transportVehicleSetupFilterCampus');
        create.replaceChildren(); filter.replaceChildren();
        option(create, '', 'Select campus'); option(filter, '', 'All authorized campuses');
        (data || []).forEach(x => { option(create, x.id, x.name); option(filter, x.id, x.name); });
    }
    function row(item) {
        const tr = document.createElement('tr');
        [item.campusName, item.vehicleNumber, item.vehicleType || '—'].forEach(x => tr.append(cell(x)));
        const capacityCell = document.createElement('td'), capacity = document.createElement('input');
        capacity.type = 'number'; capacity.min = '1'; capacity.max = '1000';
        capacity.value = String(item.capacity); capacity.className = 'form-control form-control-sm';
        capacity.setAttribute('aria-label', 'Capacity for ' + item.vehicleNumber);
        capacityCell.append(capacity); tr.append(capacityCell);
        tr.append(cell(item.occupiedToday)); tr.append(cell(item.assignedStudents)); tr.append(cell(item.peakReservedSeats)); tr.append(cell(item.currentRouteAssignments));
        const statusCell = document.createElement('td'), status = document.createElement('select');
        status.className = 'form-select form-select-sm';
        option(status, 'true', 'Active'); option(status, 'false', 'Inactive');
        status.value = String(item.isActive); statusCell.append(status); tr.append(statusCell);
        const action = document.createElement('td'), button = document.createElement('button');
        button.type = 'button'; button.className = 'btn btn-outline-primary btn-sm'; button.textContent = 'Save';
        button.addEventListener('click', async () => {
            const value = Number(capacity.value), active = status.value === 'true';
            if (!Number.isInteger(value) || value < 1 || value > 1000) {
                alertUser('Capacity must be 1–1000.'); return;
            }
            if (value < item.peakReservedSeats || (!active &&
                (item.assignedStudents > 0 || item.currentRouteAssignments > 0))) {
                alertUser('Capacity is below peak simultaneous reservations, or deactivation conflicts with existing assignments.'); return;
            }
            if (!active && item.isActive && !confirm('Deactivate this vehicle?')) return;
            button.disabled = true;
            try {
                await api('/api/transport/vehicle-setup/' + encodeURIComponent(item.reference),
                    'PUT', { capacity: value, isActive: active, rowVersion: item.rowVersion });
                alertUser('Vehicle updated.', true);
                window.dispatchEvent(new CustomEvent('transport:vehicles-changed'));
                await load();
            } catch (err) { alertUser(err.message); } finally { button.disabled = false; }
        });
        action.append(button); tr.append(action); return tr;
    }
    async function load() {
        const current = ++sequence;
        const q = new URLSearchParams({ page: String(page), pageSize: '15' });
        const campus = el('transportVehicleSetupFilterCampus').value;
        const search = el('transportVehicleSetupSearch').value.trim();
        if (campus) q.set('campusId', campus);
        if (search) q.set('search', search);
        const data = await api('/api/transport/vehicle-setup?' + q);
        if (current !== sequence) return;
        const body = el('transportVehicleSetupRows'); body.replaceChildren();
        (data.items || []).forEach(item => body.append(row(item)));
        if (!data.items?.length) {
            const tr = document.createElement('tr'), td = cell('No vehicles found');
            td.colSpan = 10; tr.append(td); body.append(tr);
        }
        pages = Math.max(1, Number(data.totalPages) || 1);
        el('transportVehicleSetupPageInfo').textContent =
            'Page ' + page + ' of ' + pages + ' · ' + data.totalCount + ' vehicles';
        el('transportVehicleSetupPrev').disabled = page <= 1;
        el('transportVehicleSetupNext').disabled = page >= pages;
    }
    form.addEventListener('submit', async event => {
        event.preventDefault(); if (!form.reportValidity()) return;
        const button = el('transportVehicleSetupCreate'); button.disabled = true;
        try {
            await api('/api/transport/vehicle-setup', 'POST', {
                campusId: Number(el('transportVehicleSetupCampus').value),
                vehicleNumber: el('transportVehicleSetupNumber').value.trim(),
                vehicleType: el('transportVehicleSetupType').value.trim() || null,
                capacity: Number(el('transportVehicleSetupCapacity').value)
            });
            form.reset(); page = 1; alertUser('Vehicle created.', true);
            window.dispatchEvent(new CustomEvent('transport:vehicles-changed'));
            await load();
        } catch (err) { alertUser(err.message); } finally { button.disabled = false; }
    });
    el('transportVehicleSetupSearchForm').addEventListener('submit', event => {
        event.preventDefault(); page = 1; load().catch(e => alertUser(e.message));
    });
    el('transportVehicleSetupPrev').addEventListener('click', () => {
        page = Math.max(1, page - 1); load().catch(e => alertUser(e.message));
    });
    el('transportVehicleSetupNext').addEventListener('click', () => {
        page = Math.min(pages, page + 1); load().catch(e => alertUser(e.message));
    });
    Promise.all([loadCampuses(), load()]).catch(e => alertUser(e.message));
})();
