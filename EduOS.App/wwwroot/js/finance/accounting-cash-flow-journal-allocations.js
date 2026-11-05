const scope = document.getElementById('accountingSetup');
if (scope && document.getElementById('cashFlowAllocationForm')) {
    const $ = id => document.getElementById(id);
    const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const admin = scope.dataset.admin === 'true';
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    let page = 1, pages = 1, rows = [], selected = null, busy = false;
    let submitKey = crypto.randomUUID(), reviewKey = crypto.randomUUID(), reviewApprove = null;
    const status = (message, error = false) => { $('cashFlowAllocationStatus').textContent = (error ? 'Error: ' : '') + message; };
    const run = async action => { try { await action(); } catch (e) { status(e.message, true); } };
    const api = async (path, method = 'GET', body = null) => {
        const options = {method, credentials:'same-origin', cache:'no-store', headers:{Accept:'application/json'}};
        if (body !== null) {
            options.headers['Content-Type'] = 'application/json';
            options.headers.RequestVerificationToken = token;
            options.body = JSON.stringify(body);
        }
        const response = await fetch('/api/finance/accounting/setup/' + path, options);
        const data = await response.json();
        if (!response.ok || !data.success) throw new Error(data.message || 'Journal classification unavailable.');
        return data.data;
    };
    const load = async () => {
        const result = await api('cash-flow-journal-allocations?' + new URLSearchParams({page,pageSize:25}));
        rows = result.items || []; pages = Math.max(1, result.totalPages || 1);
        $('cashFlowAllocationPage').textContent = 'Page ' + page + ' / ' + pages + ' (' + result.totalCount + ')';
        $('cashFlowAllocationPrev').disabled = page <= 1;
        $('cashFlowAllocationNext').disabled = page >= pages;
        $('cashFlowAllocationRows').innerHTML = rows.map(x => '<tr><td>' + esc(x.journalNumber) +
            ' (#' + esc(x.journalId) + ')</td><td>' + esc(x.direction) + '</td><td>' + esc(x.operatingAmount) +
            '</td><td>' + esc(x.investingAmount) + '</td><td>' + esc(x.financingAmount) +
            '</td><td>' + esc(x.stateCode) + '</td><td>' + esc(x.submittedByUserId) +
            '</td><td>' + esc(x.reviewedByUserId) + '</td><td>' + (admin && x.stateCode === 'Submitted' ?
                '<button type="button" data-review-allocation="' + Number(x.id) + '" class="btn btn-outline-primary btn-sm">Review</button>' :
                esc(x.reviewReason)) + '</td></tr>').join('') || '<tr><td colspan="9">No manual journal allocations.</td></tr>';
    };
    const money = id => {
        const s = $(id).value.trim();
        if (!/^(?:0|[1-9]\d{0,15})(?:\.\d{1,2})?$/.test(s)) throw new Error('Amounts must be positive BDT with at most two decimal places.');
        return Number(s);
    };
    $('cashFlowAllocationForm').addEventListener('input', () => { if (!busy) submitKey = crypto.randomUUID(); });
    $('cashFlowAllocationForm').addEventListener('submit', e => { e.preventDefault(); run(async () => {
        if (busy) return;
        const journalId = Number($('cashFlowAllocationJournal').value);
        const operatingAmount = money('cashFlowAllocationOperating');
        const investingAmount = money('cashFlowAllocationInvesting');
        const financingAmount = money('cashFlowAllocationFinancing');
        const reason = $('cashFlowAllocationReason').value.trim();
        if (!Number.isSafeInteger(journalId) || journalId <= 0 || reason.length < 20 || reason.length > 500 ||
            operatingAmount + investingAmount + financingAmount <= 0)
            throw new Error('Journal ID, exact BDT allocation and 20–500 character evidence required.');
        busy = true; $('cashFlowAllocationSubmit').disabled = true;
        try {
            await api('cash-flow-journal-allocations', 'POST',
                {journalId, operatingAmount, investingAmount, financingAmount, reason, clientRequestId:submitKey});
            $('cashFlowAllocationForm').reset(); submitKey = crypto.randomUUID(); page = 1;
            await load(); status('Journal allocation submitted. A different TenantAdmin must approve it.');
        } finally { busy = false; $('cashFlowAllocationSubmit').disabled = false; }
    }); });
    $('cashFlowAllocationPrev').addEventListener('click', () => run(async () => { if (page > 1) { page--; await load(); } }));
    $('cashFlowAllocationNext').addEventListener('click', () => run(async () => { if (page < pages) { page++; await load(); } }));
    const cancel = () => { selected = null; reviewApprove = null; $('cashFlowAllocationReviewPanel')?.classList.add('d-none'); };
    if (admin) {
        $('cashFlowAllocationRows').addEventListener('click', e => {
            selected = rows.find(x => String(x.id) === e.target.closest('[data-review-allocation]')?.dataset.reviewAllocation &&
                x.stateCode === 'Submitted') || null;
            if (!selected) return;
            reviewKey = crypto.randomUUID(); reviewApprove = null;
            $('cashFlowAllocationReviewTitle').textContent = selected.journalNumber + ' (#' + selected.journalId + '), ' +
                selected.direction + '. Operating ' + selected.operatingAmount + ', Investing ' +
                selected.investingAmount + ', Financing ' + selected.financingAmount + '. Evidence: ' + selected.reason;
            $('cashFlowAllocationReviewPanel').classList.remove('d-none');
        });
        $('cashFlowAllocationCancel').addEventListener('click', cancel);
        $('cashFlowAllocationReviewReason').addEventListener('input', () => { reviewKey = crypto.randomUUID(); reviewApprove = null; });
        const review = approve => run(async () => {
            if (!selected || busy) return;
            const reason = $('cashFlowAllocationReviewReason').value.trim();
            if (!approve && !reason || reason.length > 500) throw new Error('Rejection reason required (max 500 characters).');
            if (approve && !confirm('Approve a manual cash-flow allocation for posted journal ' +
                selected.journalNumber + '? This affects reporting only; posted GL remains unchanged.')) return;
            if (reviewApprove !== approve) { reviewKey = crypto.randomUUID(); reviewApprove = approve; }
            busy = true; $('cashFlowAllocationApprove').disabled = true; $('cashFlowAllocationReject').disabled = true;
            try {
                await api('cash-flow-journal-allocations/' + encodeURIComponent(selected.id) + '/review', 'POST',
                    {approve, reviewRequestId:reviewKey, rowVersion:selected.rowVersion, reason});
                cancel(); await load(); status(approve ? 'Journal classification approved.' : 'Journal classification rejected.');
            } finally {
                busy = false; $('cashFlowAllocationApprove').disabled = false; $('cashFlowAllocationReject').disabled = false;
            }
        });
        $('cashFlowAllocationApprove').addEventListener('click', () => review(true));
        $('cashFlowAllocationReject').addEventListener('click', () => review(false));
    }
    const direct = new URLSearchParams(window.location.search).get('cashFlowJournal');
    if (direct && /^\d{1,15}$/.test(direct)) {
        $('cashFlowAllocationJournal').value = direct;
        const tab = document.querySelector('[data-bs-target="#cashFlowPolicyPanel"]');
        if (tab && window.bootstrap?.Tab) window.bootstrap.Tab.getOrCreateInstance(tab).show();
    }
    run(load);
}
