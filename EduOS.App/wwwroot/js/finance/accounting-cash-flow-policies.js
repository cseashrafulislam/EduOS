const scope = document.getElementById('accountingSetup');
if (scope && document.getElementById('cashFlowRuleForm')) {
    const $ = id => document.getElementById(id);
    const esc = v => String(v ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    const admin = scope.dataset.admin === 'true';
    let page = 1, pages = 1, entries = [], selected = null, generation = 0;
    let submitKey = crypto.randomUUID(), reviewKey = null, reviewApprove = null, busy = false;
    const api = async (path, method = 'GET', body = null) => {
        const opts = {method, credentials:'same-origin', cache:'no-store', headers:{Accept:'application/json'}};
        if (body !== null) {
            opts.headers['Content-Type'] = 'application/json';
            opts.headers.RequestVerificationToken = token || '';
            opts.body = JSON.stringify(body);
        }
        const res = await fetch('/api/finance/accounting/setup/' + path, opts);
        const data = await res.json();
        if (!res.ok || !data.success) throw new Error(data.message || 'Policy request failed.');
        return data.data;
    };
    const status = (message, error = false) => {
        $('cashFlowRuleStatus').textContent = (error ? 'Error: ' : '') + message;
    };
    const run = async callback => { try { await callback(); } catch (e) { status(e.message, true); } };
    const cancel = () => {
        selected = null; reviewKey = null; reviewApprove = null;
        $('cashFlowRuleReviewPanel')?.classList.add('d-none');
        if (admin) $('cashFlowRuleReviewReason').value = '';
    };
    const load = async () => {
        const current = ++generation;
        const data = await api('cash-flow-rules?' + new URLSearchParams({page,pageSize:25}));
        if (current !== generation) return;
        entries = data.items || []; pages = Math.max(1,data.totalPages || 1);
        $('cashFlowRulePage').textContent = 'Page ' + page + ' / ' + pages + ' (' + data.totalCount + ')';
        $('cashFlowRulePrev').disabled = page <= 1;
        $('cashFlowRuleNext').disabled = page >= pages;
        $('cashFlowRuleRows').innerHTML = entries.map(x => '<tr><td>' + esc(x.accountCode + ' — ' + x.accountName) +
            '</td><td>' + esc(x.effectiveFromDate) + '</td><td>' + esc(x.activityCode) +
            '</td><td>' + esc(x.stateCode) + '</td><td>' + esc(x.reason) +
            '</td><td>' + esc(x.submittedByUserId) + '</td><td>' + esc(x.reviewedByUserId) +
            '</td><td>' + (admin && x.stateCode === 'Submitted' ?
                '<button type="button" class="btn btn-outline-primary btn-sm" data-review-rule="' + x.id + '">Review</button>' :
                esc(x.reviewReason)) + '</td></tr>').join('') ||
            '<tr><td colspan="8">No classification policies submitted yet.</td></tr>';
    };
    const loadAccounts = async () => {
        let current = 1, total = 1, options = [];
        do {
            const data = await api('accounts?' + new URLSearchParams({page:current,pageSize:100}));
            options.push(...(data.items || []).filter(x => x.isActive && !x.isGroup && !x.isCashInHand));
            total = data.totalPages || 1; current++;
            if (total > 100) throw new Error('More than 10,000 accounts; narrow the chart before classifying.');
        } while (current <= total);
        $('cashFlowRuleAccount').innerHTML = '<option value="">Select non-cash account</option>' +
            options.map(x => '<option value="' + Number(x.id) + '">' + esc(x.code + ' — ' + x.name) + '</option>').join('');
    };
    $('cashFlowRuleForm').addEventListener('input', () => { if (!busy) submitKey = crypto.randomUUID(); });
    $('cashFlowRuleForm').addEventListener('submit', e => { e.preventDefault(); run(async () => {
        if (busy) return;
        const accountId = Number($('cashFlowRuleAccount').value);
        const activityCode = $('cashFlowRuleActivity').value, effectiveFromDate = $('cashFlowRuleDate').value;
        const reason = $('cashFlowRuleReason').value.trim();
        if (!accountId || !effectiveFromDate || !['Operating','Investing','Financing'].includes(activityCode) ||
            reason.length < 20 || reason.length > 500) throw new Error('Select account, date, activity and 20–500 character evidence.');
        busy = true; $('cashFlowRuleSubmit').disabled = true;
        try {
            await api('cash-flow-rules', 'POST', {accountId,activityCode,effectiveFromDate,reason,clientRequestId:submitKey});
            submitKey = crypto.randomUUID(); $('cashFlowRuleForm').reset();
            page = 1; await load(); status('Policy submitted for independent TenantAdmin approval.');
        } finally { busy = false; $('cashFlowRuleSubmit').disabled = false; }
    }); });
    $('cashFlowRulePrev').addEventListener('click', () => run(async () => { if (page > 1) { page--; await load(); } }));
    $('cashFlowRuleNext').addEventListener('click', () => run(async () => { if (page < pages) { page++; await load(); } }));
    if (admin) {
        $('cashFlowRuleRows').addEventListener('click', e => {
            const id = e.target.closest('[data-review-rule]')?.dataset.reviewRule;
            selected = entries.find(x => String(x.id) === id && x.stateCode === 'Submitted') || null;
            if (!selected) return;
            reviewKey = crypto.randomUUID(); reviewApprove = null;
            $('cashFlowRuleReviewTitle').textContent = selected.accountCode + ' — ' + selected.accountName +
                ', effective ' + selected.effectiveFromDate + ': ' + selected.activityCode + '. Evidence: ' + selected.reason;
            $('cashFlowRuleReviewPanel').classList.remove('d-none');
            $('cashFlowRuleReviewReason').focus();
        });
        $('cashFlowRuleCancel').addEventListener('click', cancel);
        $('cashFlowRuleReviewReason').addEventListener('input', () => { if (!busy) { reviewKey = crypto.randomUUID(); reviewApprove = null; } });
        const review = async approve => {
            if (!selected || busy) return;
            const reason = $('cashFlowRuleReviewReason').value.trim();
            if (reason.length > 500 || !approve && !reason) throw new Error('Rejection requires a reason.');
            if (approve && !confirm('Approve ' + selected.activityCode + ' cash-flow classification for ' +
                selected.accountCode + ' effective ' + selected.effectiveFromDate + '? This changes historical report classification, not posted journals.')) return;
            if (reviewApprove !== approve) { reviewKey = crypto.randomUUID(); reviewApprove = approve; }
            busy = true;
            $('cashFlowRuleApprove').disabled = true; $('cashFlowRuleReject').disabled = true;
            try {
                await api('cash-flow-rules/' + encodeURIComponent(selected.id) + '/review', 'POST',
                    {approve,reviewRequestId:reviewKey,rowVersion:selected.rowVersion,reason});
                cancel(); await load(); status(approve ? 'Cash-flow classification approved.' : 'Policy rejected.');
            } finally {
                busy = false; $('cashFlowRuleApprove').disabled = false; $('cashFlowRuleReject').disabled = false;
            }
        };
        $('cashFlowRuleApprove').addEventListener('click', () => run(() => review(true)));
        $('cashFlowRuleReject').addEventListener('click', () => run(() => review(false)));
    }
    run(async () => { await Promise.all([loadAccounts(),load()]); });
}
