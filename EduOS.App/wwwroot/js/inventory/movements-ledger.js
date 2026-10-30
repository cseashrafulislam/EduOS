import { el, api, notice, td } from './movements-api.js';

export function initLedger() {
    let page = 1, last = 1, releaseTarget = null;
    const pendingReversals = new Map(), pendingReceipts = new Map();
    const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const releaseDialog = el('inventoryReleaseDialog');
    const releaseForm = el('inventoryReleaseForm');
    const releaseSubmit = el('inventoryReleaseSubmit');

    el('inventoryReleaseCancel')?.addEventListener('click', () => releaseDialog.close());
    releaseDialog?.addEventListener('close', () => {
        releaseTarget = null;
        if (releaseForm) releaseForm.reset();
    });
    releaseForm?.addEventListener('submit', async event => {
        event.preventDefault();
        if (!releaseTarget || !releaseForm.reportValidity()) return;
        const target = releaseTarget;
        releaseSubmit.disabled = true;
        try {
            await api('movements/' + target.reference + '/release', 'POST', {
                rowVersion: target.rowVersion,
                reason: el('inventoryReleaseReason').value.trim()
            });
            releaseDialog.close();
            notice(bn ? 'রিজার্ভেশন বাতিল হয়েছে।' : 'Reservation released.', true);
            await load();
        } catch (error) { notice(error.message); }
        finally { releaseSubmit.disabled = false; }
    });

    async function receive(reference) {
        if (/^IM-[0-9a-f]{32}$/i.test(reference)) {
            const hex = reference.slice(3);
            reference = [hex.slice(0, 8), hex.slice(8, 12), hex.slice(12, 16),
                hex.slice(16, 20), hex.slice(20)].join('-');
        }
        if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(reference))
            return notice(bn ? 'সঠিক ডিসপ্যাচ রেফারেন্স দিন।' : 'Enter a valid dispatch reference.');
        if (!pendingReceipts.has(reference)) pendingReceipts.set(reference, crypto.randomUUID());
        try {
            await api('movements/transfers/' + reference + '/receive', 'POST',
                { clientRequestId: pendingReceipts.get(reference) });
            pendingReceipts.delete(reference);
            notice(bn ? 'স্টক গ্রহণ সম্পন্ন।' : 'Transfer stock received.', true);
            await load();
        } catch (e) { notice(e.message); }
    }

    el('transferReceiveForm')?.addEventListener('submit', event => {
        event.preventDefault();
        receive(el('transferDispatchReference').value.trim());
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
                if (row.stateCode === 'Reserved' && el('inventoryMovements').dataset.create === 'true')
                    button(bn ? 'ছাড়ুন' : 'Release', () => {
                        releaseTarget = { reference: row.reference, rowVersion: row.rowVersion };
                        releaseDialog.showModal();
                        el('inventoryReleaseReason').focus();
                    });
                if (['Draft', 'Reserved'].includes(row.stateCode) &&
                    el('inventoryMovements').dataset.post === 'true')
                    button(bn ? 'পোস্ট' : 'Post', async () => {
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
                        if (!pendingReversals.has(row.reference)) pendingReversals.set(row.reference, crypto.randomUUID());
                        try {
                            await api('movements/' + row.reference + '/reverse', 'POST', {
                                clientRequestId: pendingReversals.get(row.reference),
                                rowVersion: row.rowVersion, reason
                            });
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
