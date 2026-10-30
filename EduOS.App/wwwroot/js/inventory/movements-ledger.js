import { el, api, notice, td } from './movements-api.js';
export function initLedger() {
    let page = 1, last = 1;
    const pendingReversals = new Map(), pendingReceipts = new Map();
    const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
    async function receive(reference) {
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
            el('movementPage').textContent = 'Page ' + page + ' of ' + last + ' · ' + data.totalCount + ' records';
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
                    b.addEventListener('click', callback); action.append(b);
                }
                if (row.sourceType === 'TransferDispatch' && row.transferState === 'InTransit' &&
                    el('inventoryMovements').dataset.create === 'true')
                    button(bn ? 'গ্রহণ' : 'Receive', () => receive(row.reference));
                if (row.stateCode === 'Draft' && el('inventoryMovements').dataset.post === 'true')
                    button('Post', async () => {
                        if (!confirm('Post this stock movement permanently?')) return;
                        try { await api('movements/' + row.reference + '/post', 'POST', { rowVersion: row.rowVersion });
                            notice('Movement posted.', true); await load(); } catch (e) { notice(e.message); }
                    });
                if (row.stateCode === 'Posted' && el('inventoryMovements').dataset.reverse === 'true')
                    button('Reverse', async () => {
                        const reason = prompt('Reason for compensating reversal (at least 10 characters):');
                        if (!reason) return;
                        if (!pendingReversals.has(row.reference)) pendingReversals.set(row.reference, crypto.randomUUID());
                        try { await api('movements/' + row.reference + '/reverse', 'POST', {
                            clientRequestId: pendingReversals.get(row.reference), rowVersion: row.rowVersion, reason });
                            pendingReversals.delete(row.reference); notice('Compensating reversal posted.', true); await load();
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