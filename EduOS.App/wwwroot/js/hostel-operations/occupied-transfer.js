(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('hostelOccupiedTransferPanel')) return;
    let pending = null, page = 1, totalPages = 1, generation = 0;
    let busy = false, requestId = null, options = new Map();
    const today = () => {
        const now = new Date();
        return [now.getFullYear(), String(now.getMonth() + 1).padStart(2, '0'),
            String(now.getDate()).padStart(2, '0')].join('-');
    };
    function notify(message, success = false) {
        const node = el('hostelAlert');
        node.className = 'alert alert-' + (success ? 'success' : 'danger');
        node.textContent = message; node.focus();
    }
    async function api(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw Error('Security token missing. Reload the page.');
            headers['Content-Type'] = 'application/json'; headers.RequestVerificationToken = token;
        }
        const response = await fetch(url, { method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw Error(result?.message || 'Occupied transfer failed.');
        return result.data;
    }
    function dismiss() {
        pending = null; generation++; options.clear();
        el('hostelOccupiedTransferPanel').classList.add('d-none');
        el('hostelOccupiedTransferBed').replaceChildren();
        el('hostelOccupiedTransferReason').value = '';
        el('hostelOccupiedTransferClearance').value = '';
    }
    async function load() {
        if (!pending) return;
        const id = pending.id, current = ++generation;
        el('hostelOccupiedTransferSave').disabled = true;
        options.clear();
        const select = el('hostelOccupiedTransferBed');
        select.replaceChildren(new Option('Loading available beds…', ''));
        try {
            const params = new URLSearchParams({ page: String(page), pageSize: '25' });
            const result = await api('/api/hostel/allocations/' + encodeURIComponent(id) + '/occupied-transfer-beds?' + params);
            if (current !== generation || pending?.id !== id) return;
            select.replaceChildren(new Option('Choose destination bed', ''));
            for (const item of result.items || []) {
                options.set(String(item.id), item.rowVersion);
                select.append(new Option(item.hostelName + ' · Room ' + item.roomNumber +
                    ' / Bed ' + item.bedNumber, String(item.id)));
            }
            totalPages = Math.max(1, Number(result.totalPages) || 1);
            el('hostelOccupiedTransferPage').textContent = 'Page ' + page + ' of ' + totalPages +
                ' · ' + result.totalCount + ' potentially available beds (rechecked on save)';
            el('hostelOccupiedTransferPrev').disabled = page <= 1;
            el('hostelOccupiedTransferNext').disabled = page >= totalPages;
            el('hostelOccupiedTransferSave').disabled = !options.size;
        } catch (error) { if (current === generation) notify(error.message); }
    }
    function begin(item) {
        if (!item || item.startDate >= today()) {
            notify('Only a currently occupied stay that began before today can transfer.'); return;
        }
        pending = item; page = 1; totalPages = 1; requestId = crypto.randomUUID();
        el('hostelOccupiedTransferContext').textContent = item.studentName + ' · ' + item.hostelName +
            ' · Room ' + item.roomNumber + ' / Bed ' + item.bedNumber;
        el('hostelOccupiedTransferReason').value = '';
        el('hostelOccupiedTransferPanel').classList.remove('d-none');
        load(); el('hostelOccupiedTransferPanel').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
    async function save() {
        if (!pending || busy) return;
        const destinationBedId = Number(el('hostelOccupiedTransferBed').value);
        const destinationBedRowVersion = options.get(String(destinationBedId));
        const reason = el('hostelOccupiedTransferReason').value.trim();
        const clearanceText = el('hostelOccupiedTransferClearance').value.trim();
        const financeClearanceId = clearanceText ? Number(clearanceText) : null;
        if ((!pending.canTransferNoCharge && !financeClearanceId) ||
            (financeClearanceId !== null && (!Number.isSafeInteger(financeClearanceId) || financeClearanceId <= 0))) {
            notify('This stay needs a valid approved Finance clearance ID.'); return;
        }
        if (!Number.isSafeInteger(destinationBedId) || destinationBedId <= 0 || !destinationBedRowVersion) {
            notify('Select an available destination bed.'); return;
        }
        if (reason.length < 5 || reason.length > 500) {
            notify('Enter an audit reason of 5–500 characters.'); return;
        }
        if (!confirm('Transfer the resident today? The previous stay will close yesterday and a new stay will start today.')) return;
        busy = true; el('hostelOccupiedTransferSave').disabled = true;
        try {
            await api('/api/hostel/allocations/' + encodeURIComponent(pending.id) + '/occupied-transfer',
                'POST', { clientRequestId: requestId, destinationBedId, effectiveFrom: today(),
                    sourceRowVersion: pending.rowVersion, destinationBedRowVersion, reason, financeClearanceId });
            dismiss(); notify('Resident transferred. Both stays are available in allocation history.', true);
            window.dispatchEvent(new Event('hostel-occupied-transfer-completed'));
        } catch (error) { notify(error.message); }
        finally { busy = false; if (pending) el('hostelOccupiedTransferSave').disabled = false; }
    }
    el('hostelOccupiedTransferPrev').addEventListener('click', () => { if (page > 1 && !busy) { page--; load(); } });
    el('hostelOccupiedTransferNext').addEventListener('click', () => { if (page < totalPages && !busy) { page++; load(); } });
    el('hostelOccupiedTransferSave').addEventListener('click', save);
    el('hostelOccupiedTransferCancel').addEventListener('click', dismiss);
    window.hostelOccupiedTransfer = { begin };
})();
