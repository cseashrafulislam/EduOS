(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('transportFinanceRoot')) return;
    const root = '/api/finance/adjustments/transport';
    const assignments = new Map(), vehicles = new Map();
    let riderPage = 1, page = 1, pages = 1, policyVersion = null;
    let selected = null, review = null, lastPayload = null, lastKey = null, busy = false;
    const today = () => { const d = new Date(); return [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'), String(d.getDate()).padStart(2, '0')].join('-'); };
    const num = id => { const s = el(id).value.trim(); return s ? Number(s) : null; };
    const signed = id => { const s = el(id).value.trim();
        if (s && !/^-?\d{1,12}(\.\d{1,2})?$/.test(s)) throw Error('Signed adjustment must have at most 2 decimal places.');
        return s || null; };
    const amount = n => Number(n || 0).toFixed(2);
    function notice(message, success = false) {
        const box = el('tfAlert'); box.className = 'alert alert-' + (success ? 'success' : 'danger');
        box.textContent = message; box.focus();
    }
    async function api(url, method = 'GET', data) {
        const headers = { Accept: 'application/json' };
        if (data !== undefined) {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw Error('Security token unavailable. Reload the page.');
            headers.RequestVerificationToken = token;
            headers['Content-Type'] = 'application/json';
        }
        const response = await fetch(url, { method, headers, credentials: 'same-origin', cache: 'no-store',
            ...(data !== undefined ? { body: JSON.stringify(data) } : {}) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw Error(result?.message || 'Transport Finance operation failed.');
        return result.data;
    }
    async function loadPolicy() {
        const campusId = num('tfCampus');
        if (!Number.isSafeInteger(campusId) || campusId <= 0) throw Error('Enter a valid campus ID.');
        const policy = await api(`${root}/billing-policy/${campusId}`);
        el('tfMode').value = String(policy.calculationMode);
        el('tfOverrideAllowed').checked = policy.allowManualOverride;
        const permitManual = Number(policy.calculationMode) === 3 || policy.allowManualOverride;
        el('tfPeriodMode').querySelector('option[value="manual"]').disabled = !permitManual;
        el('tfPeriodMode').querySelector('option[value="hybrid"]').disabled = !policy.allowManualOverride || Number(policy.calculationMode) === 3;
        el('tfPeriodMode').value = Number(policy.calculationMode) === 3 ? 'manual' : 'auto';
        togglePeriodInput('tfPeriodMode', 'tfManualNet', el('tfOperation').value === '3');
        policyVersion = policy.rowVersion;
    }
    async function savePolicy() {
        const campusId = num('tfCampus');
        if (!Number.isSafeInteger(campusId) || campusId <= 0) throw Error('Campus ID required.');
        const data = await api(root + '/billing-policy', 'PUT', {
            campusId, calculationMode: Number(el('tfMode').value),
            allowManualOverride: el('tfOverrideAllowed').checked, rowVersion: policyVersion
        });
        policyVersion = data.rowVersion;
        notice('Transport fare calculation policy saved. Historical clearances retain their original snapshot.', true);
    }
    async function loadRiders() {
        const result = await api(`/api/transport/active-assignments?page=${riderPage}&pageSize=50`);
        const target = el('tfSource');
        if (riderPage === 1) { assignments.clear(); target.replaceChildren(new Option('Select active assignment', '')); }
        for (const item of result.items || []) {
            assignments.set(item.reference, item);
            target.append(new Option(`${item.studentName} · ${item.routeName} · ${item.vehicleNumber} · ${item.startDate}`, item.reference));
        }
        const totalPages = Math.max(1, Math.ceil(Number(result.totalCount || 0) / 50));
        riderPage = riderPage < totalPages ? riderPage + 1 : 1;
        el('tfLoadRiders').textContent = riderPage === 1 ? 'Reload first 50 riders' : 'Load next 50 riders';
    }
    async function chooseRider() {
        selected = assignments.get(el('tfSource').value) || null;
        vehicles.clear(); el('tfTarget').replaceChildren(new Option('Select rider first', ''));
        if (!selected) { el('tfRequestInfo').textContent = ''; return; }
        el('tfCampus').value = selected.campusId;
        await loadPolicy();
        el('tfRequestInfo').textContent = `Campus ${selected.campusId}; assignment ${selected.reference}; current vehicle ${selected.vehicleNumber}. Fare changes always require independent Finance reconciliation.`;
        await loadVehicles();
    }
    async function loadVehicles() {
        const isTransfer = el('tfOperation').value === '1';
        const period = el('tfOperation').value === '3';
        el('tfManualFare').disabled = period;
        el('tfPeriodMode').disabled = !period;
        togglePeriodInput('tfPeriodMode', 'tfManualNet', period);
        const box = el('tfTarget'); box.required = isTransfer; box.disabled = !isTransfer;
        box.replaceChildren(new Option(isTransfer ? 'Choose eligible destination' : 'Not required', ''));
        vehicles.clear();
        if (!selected || !isTransfer) return;
        const date = el('tfEffective').value;
        if (!date) return;
        const result = await api(`/api/transport/assignments/${encodeURIComponent(selected.reference)}/transfer-vehicles?effectiveDate=${date}&page=1&pageSize=50`);
        for (const vehicle of result.items || []) {
            if (!vehicle.available || vehicle.isCurrent) continue;
            vehicles.set(vehicle.vehicleReference, vehicle);
            box.append(new Option(`${vehicle.vehicleNumber} · ${vehicle.reservedSeats}/${vehicle.capacity} booked`, vehicle.vehicleReference));
        }
        if (Number(result.totalCount || 0) > 50) el('tfRequestInfo').textContent += ' Only first 50 candidate vehicles displayed.';
    }
    function togglePeriodInput(modeId, amountId, enabled) {
        const amount = el(amountId), manual = enabled && el(modeId).value !== 'auto';
        amount.disabled = !manual;
        amount.required = manual;
        if (!manual) {
            amount.value = '';
            if (enabled) el(amountId === 'tfManualNet' ? 'tfOverrideReason' : 'tfReviewOverrideReason').value = '';
        }
    }
    async function requestClearance() {
        if (!selected) throw Error('Select an active assignment first.');
        const transfer = el('tfOperation').value === '1';
        const period = el('tfOperation').value === '3';
        const mode = el('tfPeriodMode').value;
        const destination = el('tfTarget').value;
        if (transfer && !vehicles.has(destination)) throw Error('Choose an eligible destination vehicle.');
        const request = {
            sourceAssignmentReference: selected.reference, sourceRowVersion: selected.rowVersion,
            operation: Number(el('tfOperation').value), destinationVehicleReference: transfer ? destination : null,
            effectiveDate: el('tfEffective').value, reason: el('tfReason').value.trim(),
            manualFareCredit: transfer || el('tfOperation').value === '2' ? num('tfManualFare') : null,
            manualNetAdjustment: period && mode === 'manual' ? signed('tfManualNet') : null,
            manualAdjustmentDelta: period && mode === 'hybrid' ? signed('tfManualNet') : null,
            overrideReason: el('tfOverrideReason').value.trim() || null
        };
        const fingerprint = JSON.stringify(request);
        if (lastPayload !== fingerprint) { lastPayload = fingerprint; lastKey = crypto.randomUUID(); }
        const result = await api(root + '/clearances', 'POST', { clientRequestId: lastKey, ...request });
        notice(result.operation === 3
            ? `Transport clearance #${result.id} requested. Signed period adjustment ${amount(result.approvedNetAdjustment)} BDT. Awaiting independent review.`
            : `Transport clearance #${result.id} requested. Suggested credit ${amount(result.suggestedFareCredit)} BDT, selected credit ${amount(result.approvedFareCredit)} BDT. Awaiting independent review.`, true);
        page = 1; await listClearances();
    }
    function td(value) { const cell = document.createElement('td'); cell.textContent = value == null ? '' : String(value); return cell; }
    async function listClearances() {
        const data = await api(`${root}/clearances?page=${page}&pageSize=25`);
        pages = Math.max(1, Math.ceil(Number(data.totalCount || 0) / 25));
        el('tfPageInfo').textContent = `Page ${page} of ${pages} · ${data.totalCount} records`;
        el('tfPrevious').disabled = page <= 1;
        el('tfNext').disabled = page >= pages;
        const box = el('tfRows'); box.replaceChildren();
        for (const record of data.items || []) {
            const row = document.createElement('tr');
            [`#${record.id} / ${record.sourceAssignmentReference}`, `${record.operation === 1 ? 'Transfer' : record.operation === 3 ? 'Upcoming extension' : 'Closure'} / ${record.effectiveDate}`,
                ['—', 'Daily prorated', 'Fixed monthly', 'Manual'][record.calculationMode] || 'Unknown',
                amount(record.operation === 3 ? record.suggestedNetAdjustment : record.suggestedFareCredit),
                amount(record.operation === 3 ? record.approvedNetAdjustment : record.approvedFareCredit),
                ['—', 'Pending', 'Approved', 'Rejected', 'Consumed'][record.state] || 'Unknown'
            ].forEach(v => row.append(td(v)));
            const cell = document.createElement('td');
            if (record.state === 1) {
                const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
                button.textContent = 'Review'; button.addEventListener('click', () => openReview(record)); cell.append(button);
            } else cell.textContent = record.state === 2 ? `Finance cleared · journal ${record.settlementJournalId ?? 'none'}` : (record.reconciliationNote || '—');
            row.append(cell); box.append(row);
        }
        if (!box.children.length) { const row = document.createElement('tr'), cell = td('No Transport Finance clearances.'); cell.colSpan = 7; row.append(cell); box.append(row); }
    }
    function openReview(record) {
        review = record;
        el('tfReviewTitle').textContent = `#${record.id} · ${record.operation === 1 ? 'vehicle transfer' : record.operation === 3 ? 'upcoming extension' : 'closure'}`;
        el('tfReviewFare').value = ''; el('tfReviewOverrideReason').value = '';
        el('tfReviewNet').value = '';
        el('tfReviewMode').value = 'auto';
        el('tfReviewMode').disabled = record.operation !== 3;
        el('tfReviewFare').disabled = record.operation === 3;
        togglePeriodInput('tfReviewMode', 'tfReviewNet', record.operation === 3);
        el('tfReviewJournal').value = ''; el('tfReviewNote').value = '';
        el('tfNoAdjustment').checked = Number(record.operation === 3 ? record.approvedNetAdjustment : record.approvedFareCredit) === 0;
        el('tfAutoInvoice').checked = false;
        el('tfAutoInvoice').disabled = record.operation !== 3;
        toggleAutoInvoice();
        if (record.operation === 3) run(loadAutoInvoiceOptions);
        el('tfReviewForm').classList.remove('d-none');
        el('tfReviewForm').scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }
    function toggleAutoInvoice() {
        const active = el('tfAutoInvoice').checked;
        el('tfAutoPeriod').disabled = !active;
        el('tfAutoHead').disabled = !active;
        el('tfReviewJournal').disabled = active;
        el('tfNoAdjustment').disabled = active;
        if (active) { el('tfNoAdjustment').checked = false; el('tfReviewJournal').value = ''; }
    }
    async function loadAutoInvoiceOptions() {
        const [periods, billing] = await Promise.all([
            api('/api/finance/invoice-posting?unpostedOnly=true&pageSize=1'),
            api('/api/finance/fees/options')
        ]);
        el('tfAutoPeriod').replaceChildren(new Option('Choose open period', ''));
        el('tfAutoHead').replaceChildren(new Option('Choose configured fee head', ''));
        for (const item of periods.openPeriods || []) el('tfAutoPeriod').append(new Option(item.name, String(item.id)));
        for (const item of billing.feeHeads || []) el('tfAutoHead').append(new Option(item.name, String(item.id)));
    }
    async function decide(approve) {
        if (!review || busy) return;
        const note = el('tfReviewNote').value.trim();
        if (note.length < 20 || note.length > 1000) throw Error('Supply independent reconciliation evidence (20–1000 characters).');
        const auto = approve && el('tfAutoInvoice').checked;
        const reviewMode = el('tfReviewMode').value;
        const manualInput = review.operation === 3 && reviewMode !== 'auto' ? signed('tfReviewNet') : null;
        const selectedAmount = manualInput == null ? Number(review.approvedNetAdjustment) :
            reviewMode === 'hybrid' ? Number(review.suggestedNetAdjustment) + Number(manualInput) : Number(manualInput);
        if (auto && (!num('tfAutoPeriod') || !num('tfAutoHead') ||
            selectedAmount <= 0))
            throw Error('Automatic invoice requires a positive charge, open accounting period and fee head.');
        if (!confirm(`${approve ? 'Approve' : 'Reject'} Transport clearance #${review.id}?`)) return;
        busy = true;
        try {
            await api(`${root}/clearances/${review.id}/review`, 'POST', {
                rowVersion: review.rowVersion, approve, noAdjustmentRequired: el('tfNoAdjustment').checked,
                settlementJournalId: num('tfReviewJournal'), manualFareCredit: num('tfReviewFare'),
                autoPostInvoice: auto, accountingPeriodId: auto ? num('tfAutoPeriod') : null,
                autoFeeHeadId: auto ? num('tfAutoHead') : null,
                manualNetAdjustment: review.operation === 3 && reviewMode === 'manual' ? manualInput : null,
                manualAdjustmentDelta: review.operation === 3 && reviewMode === 'hybrid' ? manualInput : null,
                overrideReason: el('tfReviewOverrideReason').value.trim() || null, reconciliationNote: note
            });
            notice(`Transport clearance #${review.id} ${approve ? auto ? 'approved; draft invoice prepared (posting occurs with successful Operations change)' : 'approved' : 'rejected'}. No cash payout was performed.`, true);
            review = null; el('tfReviewForm').classList.add('d-none'); await listClearances();
        } finally { busy = false; }
    }
    function run(work) { Promise.resolve().then(work).catch(error => notice(error.message)); }
    el('tfEffective').value = today();
    el('tfPolicyLoad').addEventListener('click', () => run(loadPolicy));
    el('tfPolicyForm').addEventListener('submit', e => { e.preventDefault(); run(savePolicy); });
    el('tfLoadRiders').addEventListener('click', () => run(loadRiders));
    el('tfSource').addEventListener('change', () => run(chooseRider));
    el('tfOperation').addEventListener('change', () => run(loadVehicles));
    el('tfPeriodMode').addEventListener('change', () => togglePeriodInput('tfPeriodMode', 'tfManualNet', el('tfOperation').value === '3'));
    el('tfReviewMode').addEventListener('change', () => {
        togglePeriodInput('tfReviewMode', 'tfReviewNet', Boolean(review && review.operation === 3));
        if (el('tfReviewMode').value !== 'auto') el('tfNoAdjustment').checked = false;
    });
    el('tfEffective').addEventListener('change', () => run(loadVehicles));
    el('tfRequestForm').addEventListener('submit', e => { e.preventDefault(); run(requestClearance); });
    el('tfRefresh').addEventListener('click', () => run(listClearances));
    el('tfPrevious').addEventListener('click', () => run(() => { if (page > 1) { page--; return listClearances(); } }));
    el('tfNext').addEventListener('click', () => run(() => { if (page < pages) { page++; return listClearances(); } }));
    el('tfReviewForm').addEventListener('submit', e => { e.preventDefault(); run(() => decide(true)); });
    el('tfAutoInvoice').addEventListener('change', toggleAutoInvoice);
    el('tfReject').addEventListener('click', () => run(() => decide(false)));
    el('tfReviewCancel').addEventListener('click', () => { review = null; el('tfReviewForm').classList.add('d-none'); });
    run(async () => { await Promise.all([loadRiders(), listClearances()]); });
})();
