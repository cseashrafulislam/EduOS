import { el, api, notice, td, activeLocations } from './movements-api.js';

export async function initReservations() {
    const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const form = el('inventoryReservationFilter');
    if (!form) return;
    let page = 1, last = 1, generation = 0;
    const rows = el('inventoryReservationRows');
    const prev = el('inventoryReservationPrev');
    const next = el('inventoryReservationNext');
    const pageLabel = el('inventoryReservationPage');
    const exportButton = el('inventoryReservationExport');
    const printButton = el('inventoryReservationPrint');
    const locationSelect = el('inventoryReservationLocation');
    const searchInput = el('inventoryReservationSearch');
    const includeClosed = el('inventoryReservationIncludeClosed');
    const overdueOnly = el('inventoryReservationOverdueOnly');
    const scheduleDialog = el('inventoryExpiryScheduleDialog');
    const confirmDialog = el('inventoryExpiryConfirmDialog');
    let expiryTarget = null;
    el('inventoryExpiryScheduleCancel')?.addEventListener('click', () => scheduleDialog.close());
    el('inventoryExpiryConfirmCancel')?.addEventListener('click', () => confirmDialog.close());
    scheduleDialog?.addEventListener('close', () => { expiryTarget = null; el('inventoryExpiryScheduleForm').reset(); });
    confirmDialog?.addEventListener('close', () => { expiryTarget = null; el('inventoryExpiryConfirmForm').reset(); });
    el('inventoryExpiryScheduleForm')?.addEventListener('submit', async event => {
        event.preventDefault();
        if (!expiryTarget) return;
        const deadline = new Date(el('inventoryExpiryDeadline').value);
        if (!Number.isFinite(deadline.getTime())) return;
        const submit = el('inventoryExpiryScheduleSubmit');
        submit.disabled = true;
        try {
            await api('movements/' + expiryTarget.movementReference + '/schedule-expiry', 'POST',
                { rowVersion: expiryTarget.rowVersion, expiresAtUtc: deadline.toISOString() });
            scheduleDialog.close();
            notice(bn ? 'মেয়াদ সংরক্ষিত হয়েছে।' : 'Reservation deadline saved.', true);
            await load();
        } catch (error) { notice(error.message); }
        finally { submit.disabled = false; }
    });
    el('inventoryExpiryConfirmForm')?.addEventListener('submit', async event => {
        event.preventDefault();
        if (!expiryTarget) return;
        const submit = el('inventoryExpiryConfirmSubmit');
        submit.disabled = true;
        try {
            await api('movements/' + expiryTarget.movementReference + '/expire', 'POST',
                { rowVersion: expiryTarget.rowVersion, reason: el('inventoryExpiryReason').value.trim() });
            confirmDialog.close();
            notice(bn ? 'মেয়াদোত্তীর্ণ রিজার্ভেশন বন্ধ হয়েছে।' : 'Expired reservation closed.', true);
            await load();
        } catch (error) { notice(error.message); }
        finally { submit.disabled = false; }
    });

    const params = () => {
        const q = new URLSearchParams({ page: String(page), pageSize: '25' });
        if (locationSelect.value) q.set('locationId', locationSelect.value);
        if (searchInput.value.trim()) q.set('search', searchInput.value.trim());
        if (includeClosed.checked) q.set('includeClosed', 'true');
        if (overdueOnly.checked) q.set('overdueOnly', 'true');
        return q;
    };
    async function load() {
        const token = ++generation;
        try {
            const data = await api('movements/reservations?' + params());
            if (token !== generation || !form.isConnected) return;
            last = Math.max(1, data.totalPages || 1);
            prev.disabled = page <= 1;
            next.disabled = page >= last;
            pageLabel.textContent = (bn ? 'পৃষ্ঠা ' : 'Page ') + page + ' / ' + last + ' · ' + data.totalCount;
            rows.replaceChildren();
            for (const item of data.items || []) {
                const tr = document.createElement('tr');
                [item.movementNumber, item.movementTypeCode,
                    item.itemCode + ' — ' + item.itemName, item.lotOrBatchNo,
                    item.fromLocationId, item.requestedQuantity + ' / ' + item.fulfilledQuantity +
                        ' / ' + item.reservedQuantity + ' ' + item.unitCode,
                    (item.holdStartIsEstimated ? '~' : '') + new Date(item.heldSinceUtc).toLocaleString(),
                    item.holdAgeDays == null ? '—' : String(item.holdAgeDays) + (bn ? ' দিন' : ' days'),
                    (item.isOverdue ? (bn ? 'মেয়াদ পেরিয়েছে · ' : 'Overdue · ') : '') + (item.stateCode || '') + (item.reservationExpiresAtUtc ?
                        ' · ' + new Date(item.reservationExpiresAtUtc).toLocaleString() : '')].forEach(value => tr.append(td(value)));
                const actions = td('');
                const allowed = el('inventoryMovements')?.dataset.post === 'true';
                if (allowed && item.stateCode === 'Reserved') {
                    const action = (label, callback) => {
                        const button = document.createElement('button');
                        button.type = 'button'; button.className = 'btn btn-outline-primary btn-sm me-1';
                        button.textContent = label;
                        button.addEventListener('click', callback);
                        actions.append(button);
                    };
                    if (!item.reservationExpiresAtUtc)
                        action(bn ? 'মেয়াদ দিন' : 'Set deadline', () => {
                            expiryTarget = item; scheduleDialog.showModal();
                            el('inventoryExpiryDeadline').focus();
                        });
                    else if (new Date(item.reservationExpiresAtUtc).getTime() <= Date.now())
                        action(bn ? 'মেয়াদ শেষ' : 'Expire', () => {
                            expiryTarget = item; confirmDialog.showModal();
                            el('inventoryExpiryReason').focus();
                        });
                }
                tr.append(actions);
                rows.append(tr);
            }
            if (!data.items?.length) {
                const tr = document.createElement('tr'), cell = document.createElement('td');
                cell.colSpan = 10;
                cell.textContent = bn ? 'কোনো সক্রিয় রিজার্ভেশন নেই।' : 'No active reservations.';
                tr.append(cell); rows.append(tr);
            }
        } catch (error) {
            if (token === generation) notice(error.message);
        }
    }

    form.addEventListener('submit', event => { event.preventDefault(); page = 1; load(); });
    includeClosed.addEventListener('change', () => { page = 1; load(); });
    overdueOnly.addEventListener('change', () => { page = 1; load(); });
    prev.addEventListener('click', () => { if (page > 1) { page--; load(); } });
    next.addEventListener('click', () => { if (page < last) { page++; load(); } });
    printButton?.addEventListener('click', () => {
        // Server rechecks tenant, campus, role and module entitlement at print time.
        const q = params();
        q.delete('page'); q.delete('pageSize');
        window.open('/InventoryMovements/PrintReservations?' + q.toString(),
            '_blank', 'noopener,noreferrer');
    });
    exportButton.addEventListener('click', async () => {
        exportButton.disabled = true;
        try {
            const q = params();
            q.delete('page'); q.delete('pageSize');
            const response = await fetch('/api/inventory/movements/reservations/export?' + q, {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/csv' }
            });
            if (!response.ok) {
                const error = await response.json().catch(() => null);
                throw new Error(error?.message || (bn ? 'এক্সপোর্ট ব্যর্থ।' : 'Export failed.'));
            }
            const blob = await response.blob();
            const url = URL.createObjectURL(blob);
            try {
                const link = document.createElement('a');
                link.href = url; link.download = includeClosed.checked ?
                    'inventory-reservation-history.csv' : 'inventory-active-reservations.csv';
                document.body.append(link); link.click(); link.remove();
            } finally { URL.revokeObjectURL(url); }
        } catch (error) { notice(error.message); }
        finally { exportButton.disabled = false; }
    });

    window.addEventListener('pagehide', () => {
        generation++;
        rows.replaceChildren();
        pageLabel.textContent = '';
    }, { once: true });
    try {
        const locations = await activeLocations();
        if (!form.isConnected) return;
        for (const location of locations)
            locationSelect.add(new Option(location.code + ' — ' + location.name, String(location.id)));
    } catch (error) { notice(error.message); }
    await load();
}
