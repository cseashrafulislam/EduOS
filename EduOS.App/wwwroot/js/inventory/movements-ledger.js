import { el, api, notice, td } from './movements-api.js';
import { commandKey, refreshAfterCommit } from './movements-form-safety.js';

export function initLedger() {
    let page = 1, last = 1, releaseTarget = null, fulfilTarget = null, releasing = false, fulfilling = false;
    const pendingReversals = new Map(), pendingReceipts = new Map(), receiving = new Set();
    const fulfilKeys = commandKey();
    const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const releaseDialog = el('inventoryReleaseDialog');
    const releaseForm = el('inventoryReleaseForm');
    const releaseSubmit = el('inventoryReleaseSubmit');
    const fulfilDialog = el('inventoryFulfilDialog');
    const fulfilForm = el('inventoryFulfilForm');
    const fulfilRows = el('inventoryFulfilLines');
    const fulfilSubmit = el('inventoryFulfilSubmit');
    const releaseGuard = releaseForm && window.EduOSFormGuard?.create(releaseForm);
    const fulfilGuard = fulfilForm && window.EduOSFormGuard?.create(fulfilForm);
    const receiveForm = el('transferReceiveForm');
    const receiveGuard = receiveForm && window.EduOSFormGuard?.create(receiveForm);

    function allowDialogClose(guard, busy) {
        if (busy) return false;
        return !guard || guard.confirmDiscard();
    }

    el('inventoryFulfilCancel')?.addEventListener('click', () => {
        if (allowDialogClose(fulfilGuard, fulfilling)) fulfilDialog.close();
    });
    fulfilDialog?.addEventListener('cancel', event => {
        if (!allowDialogClose(fulfilGuard, fulfilling)) event.preventDefault();
    });
    fulfilDialog?.addEventListener('close', () => {
        fulfilTarget = null;
        fulfilRows.replaceChildren();
        fulfilGuard?.markClean();
    });
    fulfilForm?.addEventListener('submit', async event => {
        event.preventDefault();
        if (fulfilling || !fulfilTarget || !fulfilForm.reportValidity()) return;
        const lines = [];
        for (const input of fulfilRows.querySelectorAll('input[data-line-no]')) {
            const quantity = Number(input.value);
            if (!Number.isFinite(quantity) || quantity < 0 ||
                quantity > Number(input.max) || !Number.isSafeInteger(Math.round(quantity * 1000000))) {
                return notice(bn ? 'সঠিক ছয় দশমিক পর্যন্ত পরিমাণ দিন।' : 'Enter valid quantities with at most six decimals.');
            }
            if (quantity > 0) lines.push({ lineNo: Number(input.dataset.lineNo), quantity });
        }
        if (!lines.length) return notice(bn ? 'অন্তত একটি পরিমাণ দিন।' : 'Enter at least one quantity.');
        const target = fulfilTarget, action = target.transfer ? 'fulfill-transfer' : 'fulfill-issue';
        const body = { rowVersion: target.rowVersion, lines };
        fulfilling = true; fulfilForm.inert = true;
        try {
            await api('movements/' + target.reference + '/' + action, 'POST',
                { clientRequestId: fulfilKeys.for(target.reference + '/' + action, body), ...body });
            fulfilKeys.clear(); fulfilGuard?.markClean();
            fulfilDialog.close();
            await refreshAfterCommit(load, target.transfer
                ? (bn ? 'আংশিক ট্রান্সফার ডিসপ্যাচ হয়েছে।' : 'Partial transfer dispatched; destination receipt required.')
                : (bn ? 'আংশিক ইস্যু পোস্ট হয়েছে।' : 'Reserved issue fulfilment posted.'), notice);
        } catch (error) { notice(error.message); }
        finally { fulfilling = false; fulfilForm.inert = false; }
    });
    function openFulfil(row) {
        const remaining = (row.lines || []).filter(line => line.quantity > (line.fulfilledQuantity || 0));
        if (!remaining.length) return;
        fulfilTarget = { reference: row.reference, rowVersion: row.rowVersion,
            transfer: row.movementTypeCode === 'Transfer' };
        el('inventoryFulfilTitle').textContent = fulfilTarget.transfer
            ? (bn ? 'রিজার্ভ থেকে আংশিক ট্রান্সফার ডিসপ্যাচ' : 'Dispatch reserved transfer')
            : (bn ? 'রিজার্ভ থেকে আংশিক ইস্যু' : 'Fulfil reserved issue');
        fulfilSubmit.textContent = fulfilTarget.transfer
            ? (bn ? 'ডিসপ্যাচ পোস্ট' : 'Post dispatch')
            : (bn ? 'ইস্যু পোস্ট' : 'Post issue');
        fulfilRows.replaceChildren();
        for (const line of remaining) {
            const wrapper = document.createElement('div');
            wrapper.className = 'mb-2';
            const label = document.createElement('label');
            const input = document.createElement('input');
            input.id = 'fulfilLine' + line.lineNo;
            input.type = 'number';
            input.className = 'form-control';
            input.min = '0';
            input.step = '0.000001';
            input.max = String(line.quantity - (line.fulfilledQuantity || 0));
            input.value = '0';
            input.required = true;
            input.dataset.lineNo = String(line.lineNo);
            label.className = 'form-label';
            label.htmlFor = input.id;
            label.textContent = (bn ? 'লাইন ' : 'Line ') + line.lineNo +
                ' · ' + line.inventoryItemId + (line.lotOrBatchNo ? ' · ' + line.lotOrBatchNo : '') +
                ' · ' + (bn ? 'অবশিষ্ট ' : 'Remaining ') + input.max;
            wrapper.append(label, input);
            fulfilRows.append(wrapper);
        }
        fulfilGuard?.markClean();
        fulfilDialog.showModal();
        fulfilRows.querySelector('input')?.focus();
    }

    el('inventoryReleaseCancel')?.addEventListener('click', () => {
        if (allowDialogClose(releaseGuard, releasing)) releaseDialog.close();
    });
    releaseDialog?.addEventListener('cancel', event => {
        if (!allowDialogClose(releaseGuard, releasing)) event.preventDefault();
    });
    releaseDialog?.addEventListener('close', () => {
        releaseTarget = null;
        if (releaseForm) releaseForm.reset();
        releaseGuard?.markClean();
    });
    releaseForm?.addEventListener('submit', async event => {
        event.preventDefault();
        if (releasing || !releaseTarget || !releaseForm.reportValidity()) return;
        const target = releaseTarget;
        releasing = true; releaseForm.inert = true;
        try {
            await api('movements/' + target.reference + '/release', 'POST', {
                rowVersion: target.rowVersion,
                reason: el('inventoryReleaseReason').value.trim()
            });
            releaseGuard?.markClean(); releaseDialog.close();
            await refreshAfterCommit(load, bn ? 'রিজার্ভেশন বাতিল হয়েছে।' : 'Reservation released.', notice);
        } catch (error) { notice(error.message); }
        finally { releasing = false; releaseForm.inert = false; }
    });

    async function receive(reference, fromManual = false) {
        if (/^IM-[0-9a-f]{32}$/i.test(reference)) {
            const hex = reference.slice(3);
            reference = [hex.slice(0, 8), hex.slice(8, 12), hex.slice(12, 16),
                hex.slice(16, 20), hex.slice(20)].join('-');
        }
        if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(reference))
            return notice(bn ? 'সঠিক ডিসপ্যাচ রেফারেন্স দিন।' : 'Enter a valid dispatch reference.');
        reference = reference.toLowerCase();
        if (receiving.has(reference)) return;
        if (!pendingReceipts.has(reference)) pendingReceipts.set(reference, crypto.randomUUID());
        receiving.add(reference);
        if (receiveForm) receiveForm.inert = true;
        try {
            await api('movements/transfers/' + reference + '/receive', 'POST',
                { clientRequestId: pendingReceipts.get(reference) });
            pendingReceipts.delete(reference);
            if (fromManual && receiveForm) {
                receiveForm.reset(); receiveGuard?.markClean();
            }
            await refreshAfterCommit(load, bn ? 'স্টক গ্রহণ সম্পন্ন।' : 'Transfer stock received.', notice);
        } catch (e) { notice(e.message); }
        finally { receiving.delete(reference); if (receiveForm) receiveForm.inert = false; }
    }

    el('transferReceiveForm')?.addEventListener('submit', event => {
        event.preventDefault();
        receive(el('transferDispatchReference').value.trim(), true);
    });

    async function load() {
        const query = new URLSearchParams({ page: String(page), pageSize: '25' });
        if (el('movementFromDate').value) query.set('fromUtc', el('movementFromDate').value + 'T00:00:00Z');
        if (el('movementToDate').value) query.set('toUtc', el('movementToDate').value + 'T23:59:59Z');
        try {
            const data = await api('movements?' + query);
            last = Math.max(1, data.totalPages || 1);
            el('movementPage').textContent = (bn ? 'পৃষ্ঠা ' : 'Page ') + page +
                (bn ? ' / ' : ' of ') + last + ' · ' + data.totalCount;
            el('movementPrev').disabled = page <= 1; el('movementNext').disabled = page >= last;
            const tbody = el('movementRows'); tbody.replaceChildren();
            for (const row of data.items || []) {
                const tr = document.createElement('tr');
                [row.movementNumber, row.movementTypeCode, new Date(row.movementAt).toLocaleString(),
                    row.fromLocationId, row.toLocationId,
                    row.transferState || row.stateCode, row.lines?.length || 0].forEach(x => tr.append(td(x)));
                const action = td('');
                function button(label, callback) {
                    const b = document.createElement('button'); b.type = 'button';
                    b.className = 'btn btn-outline-primary btn-sm me-1'; b.textContent = label;
                    b.addEventListener('click', async () => {
                        b.disabled = true;
                        try { await callback(); } finally { b.disabled = false; }
                    });
                    action.append(b);
                }
                if (row.sourceType === 'TransferDispatch' && row.transferState === 'InTransit' &&
                    el('inventoryMovements').dataset.create === 'true')
                    button(bn ? 'গ্রহণ' : 'Receive', () => receive(row.reference));
                if (row.stateCode === 'Draft' && ['Issue', 'Transfer'].includes(row.movementTypeCode) &&
                    el('inventoryMovements').dataset.create === 'true')
                    button(bn ? 'রিজার্ভ' : 'Reserve', async () => {
                        try {
                            await api('movements/' + row.reference + '/reserve', 'POST',
                                { rowVersion: row.rowVersion });
                            notice(bn ? 'স্টক রিজার্ভ হয়েছে।' : 'Stock reserved.', true);
                            await load();
                        } catch (e) { notice(e.message); }
                    });
                if (row.stateCode === 'Reserved' &&
                    ((row.movementTypeCode === 'Issue' && row.sourceType === 'ManualIssue') ||
                     (row.movementTypeCode === 'Transfer' && row.sourceType === 'ManualTransfer')) &&
                    el('inventoryMovements').dataset.post === 'true')
                    button(row.movementTypeCode === 'Transfer'
                        ? (bn ? 'আংশিক ডিসপ্যাচ' : 'Dispatch part')
                        : (bn ? 'আংশিক ইস্যু' : 'Fulfil part'), () => openFulfil(row));
                if (row.stateCode === 'Reserved' && el('inventoryMovements').dataset.create === 'true')
                    button(bn ? 'ছাড়ুন' : 'Release', () => {
                        releaseTarget = { reference: row.reference, rowVersion: row.rowVersion };
                        releaseDialog.showModal();
                        el('inventoryReleaseReason').focus();
                    });
                if (['Draft', 'Reserved'].includes(row.stateCode) &&
                    !(row.stateCode === 'Reserved' && row.lines?.some(line => line.fulfilledQuantity > 0)) &&
                    el('inventoryMovements').dataset.post === 'true' &&
                    !(row.sourceType === 'ManualAdjustment' &&
                        String(row.createdByUserId) === el('inventoryMovements').dataset.userId))
                    button(row.sourceType === 'ManualAdjustment'
                        ? (bn ? 'সমন্বয় অনুমোদন' : 'Approve adjustment') : (bn ? 'পোস্ট' : 'Post'), async () => {
                        if (!confirm(bn ? 'স্টক মুভমেন্ট স্থায়ীভাবে পোস্ট করবেন?' :
                            'Post this stock movement permanently?')) return;
                        try {
                            await api('movements/' + row.reference + '/post', 'POST',
                                { rowVersion: row.rowVersion });
                            notice(bn ? 'স্টক পোস্ট হয়েছে।' : 'Movement posted.', true);
                            await load();
                        } catch (e) { notice(e.message); }
                    });
                if (row.stateCode === 'Posted' && el('inventoryMovements').dataset.reverse === 'true')
                    button(bn ? 'রিভার্স' : 'Reverse', async () => {
                        const reason = prompt(bn ? 'রিভার্সের কারণ (কমপক্ষে ১০ অক্ষর):' :
                            'Reason for compensating reversal (at least 10 characters):');
                        if (!reason) return;
                        if (!pendingReversals.has(row.reference)) pendingReversals.set(row.reference, commandKey());
                        try {
                            const body = { rowVersion: row.rowVersion, reason };
                            await api('movements/' + row.reference + '/reverse', 'POST',
                                { clientRequestId: pendingReversals.get(row.reference).for(row.reference + '/reverse', body), ...body });
                            pendingReversals.delete(row.reference);
                            notice(bn ? 'রিভার্স সম্পন্ন।' : 'Compensating reversal posted.', true);
                            await load();
                        } catch (e) { notice(e.message); }
                    });
                tr.append(action); tbody.append(tr);
            }
        } catch (e) { notice(e.message); }
    }
    el('movementFilter').addEventListener('submit', e => { e.preventDefault(); page = 1; load(); });
    el('movementPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
    el('movementNext').addEventListener('click', () => { if (page < last) { page++; load(); } });
    load();
    return load;
}
