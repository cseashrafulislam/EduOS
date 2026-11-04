const root = document.getElementById('institutionBudgets');
if (root) {
    const $ = id => document.getElementById(id);
    const base = '/api/finance/accounting/budgets';
    const reviewer = root.dataset.reviewer === 'true';
    const csrf = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    const safe = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const money = n => Number(n || 0).toLocaleString('en-BD', {minimumFractionDigits:2,maximumFractionDigits:2});
    const uri = path => base + path;
    let page = 1, totalPages = 1, accounts = [], draft = [], selected = null;
    let submissionKey = crypto.randomUUID();
    const notice = (message, error = false) => { const el = $('budgetNotice'); el.textContent = message; el.className = 'alert ' + (error ? 'alert-danger' : 'alert-success'); };
    async function request(path, method = 'GET', body = null) {
        const headers = {Accept:'application/json'};
        if (body !== null) headers['Content-Type'] = 'application/json';
        if (method !== 'GET' && csrf) headers.RequestVerificationToken = csrf;
        const response = await fetch(uri(path), {method,headers,credentials:'same-origin',body:body === null ? undefined : JSON.stringify(body),cache:'no-store'});
        const payload = await response.json().catch(() => null);
        if (!response.ok || !payload?.success) throw new Error(payload?.message || 'Request failed.');
        return payload.data;
    }
    const run = async fn => { try { await fn(); } catch (e) { notice(e.message, true); } };
    async function accountOptions() {
        let all = [], next = 1, pages = 1;
        do {
            const data = await fetch('/api/finance/accounting/setup/accounts?' + new URLSearchParams({page:next,pageSize:100}), {credentials:'same-origin'}).then(x => x.json());
            if (!data.success) throw new Error(data.message || 'Unable to load posting accounts.');
            all.push(...data.data.items); pages = data.data.totalPages || 1; next++;
            if (pages > 100) throw new Error('More than 10,000 accounts; contact finance administrator.');
        } while (next <= pages);
        accounts = all.filter(x => x.isActive && !x.isGroup && (x.accountType === 4 || x.accountType === 5));
        $('budgetAccount').innerHTML = '<option value="">Select account</option>' + accounts.map(x =>
            '<option value="' + x.id + '">' + safe(x.code + ' — ' + x.name) + '</option>').join('');
    }
    function draftRows() {
        $('budgetDraftRows').innerHTML = draft.map(x => '<tr><td>' + safe(x.code + ' — ' + x.name) +
            '</td><td class="text-end">' + money(x.amount) + '</td><td class="text-end"><button class="btn btn-sm btn-outline-danger" type="button" data-remove="' + x.id + '">Remove</button></td></tr>').join('');
    }
    async function list() {
        const data = await request('?' + new URLSearchParams({page,pageSize:25}));
        totalPages = Math.max(1, data.totalPages || 1);
        $('budgetPage').textContent = 'Page ' + page + '/' + totalPages + ' (' + data.totalCount + ')';
        $('budgetPrevious').disabled = page <= 1; $('budgetNext').disabled = page >= totalPages;
        $('budgetRows').innerHTML = data.items.map(x => '<tr><td>' + safe(x.name) + '</td><td>' + safe(x.startDate + ' — ' + x.endDate) +
            '</td><td>' + safe(x.stateCode) + '</td><td><button type="button" class="btn btn-outline-primary btn-sm" data-open="' + x.id + '">Open</button></td></tr>').join('');
    }
    function lineRows(lines) {
        $('budgetDetailRows').innerHTML = lines.map(x => '<tr><td>' + safe(x.accountCode) + '</td><td>' + safe(x.accountName) +
            (x.isUnbudgeted ? ' <span class="badge text-bg-warning">Unbudgeted</span>' : '') + '</td><td>' + safe(x.category) +
            '</td><td class="text-end">' + money(x.plannedAmountBdt) + '</td><td class="text-end">' +
            money(x.actualAmountBdt) + '</td><td class="text-end">' + money(x.varianceBdt) + '</td></tr>').join('');
    }
    async function openBudget(id) {
        selected = await request('/' + id);
        $('budgetDetails').classList.remove('d-none');
        $('budgetDetailsTitle').textContent = selected.name + ' (' + selected.startDate + ' — ' + selected.endDate + ')';
        $('budgetDetailsState').textContent = selected.stateCode + ' · submitted by user ' + selected.submittedByUserId +
            (selected.reviewedByUserId ? ' · reviewed by user ' + selected.reviewedByUserId : '') +
            (selected.reviewReason ? ' · ' + selected.reviewReason : '');
        $('budgetVarianceSummary').textContent = '';
        lineRows(selected.lines);
        if (reviewer) {
            const mayReview = selected.stateCode === 'Submitted';
            $('budgetReviewForm').classList.toggle('d-none', !mayReview);
            $('budgetActualRun').classList.toggle('d-none', selected.stateCode !== 'Approved');
            $('budgetReviewReason').value = '';
        }
    }
    $('budgetAddLine').addEventListener('click', () => run(async () => {
        const id = Number($('budgetAccount').value), rawAmount = $('budgetAmount').value.trim(), amount = Number(rawAmount);
        const account = accounts.find(x => x.id === id);
        if (!account || !/^(?:\d{1,12})(?:\.\d{1,2})?$/.test(rawAmount) ||
            !(amount > 0 && amount <= 999999999999.99))
            throw new Error('Select an income/expense ledger and a valid two-decimal BDT amount.');
        if (draft.some(x => x.id === id)) throw new Error('Account is already in this budget.');
        if (draft.length >= 100) throw new Error('Maximum 100 budget lines.');
        draft.push({id,code:account.code,name:account.name,amount}); draftRows();
        $('budgetAmount').value = ''; $('budgetAccount').value = '';
    }));
    $('budgetDraftRows').addEventListener('click', e => {
        const id = e.target.closest('[data-remove]')?.dataset.remove;
        if (!id) return;
        draft = draft.filter(x => String(x.id) !== id); draftRows();
    });
    $('budgetForm').addEventListener('submit', e => { e.preventDefault(); run(async () => {
        if (draft.length === 0) throw new Error('Add at least one income or expense budget line.');
        const submit = $('budgetForm').querySelector('button[type="submit"]'); submit.disabled = true;
        try {
            const result = await request('', 'POST', {clientRequestId:submissionKey,
                name:$('budgetName').value.trim(),startDate:$('budgetFrom').value,endDate:$('budgetTo').value,
                lines:draft.map(x => ({accountId:x.id,plannedAmountBdt:x.amount}))});
            submissionKey = crypto.randomUUID(); draft = []; draftRows(); $('budgetForm').reset();
            page = 1; await list(); await openBudget(result.id); notice('Budget submitted for independent approval.');
        } finally { submit.disabled = false; }
    }); });
    $('budgetRows').addEventListener('click', e => run(async () => {
        const id = e.target.closest('[data-open]')?.dataset.open; if (id) await openBudget(id);
    }));
    $('budgetDetailsClose').addEventListener('click', () => $('budgetDetails').classList.add('d-none'));
    $('budgetPrevious').addEventListener('click', () => run(async () => { if (page > 1) { page--; await list(); } }));
    $('budgetNext').addEventListener('click', () => run(async () => { if (page < totalPages) { page++; await list(); } }));
    if (reviewer) {
        async function review(approve) {
            if (!selected || selected.stateCode !== 'Submitted') throw new Error('Select an unreviewed budget.');
            const reason = $('budgetReviewReason').value.trim();
            if (!approve && !reason) throw new Error('Rejection reason is required.');
            if (!confirm((approve ? 'Approve' : 'Reject') + ' budget ' + selected.name + '?')) return;
            await request('/' + selected.id + '/review', 'POST', {reviewRequestId:crypto.randomUUID(),
                approve,reason,rowVersion:selected.rowVersion});
            await Promise.all([list(),openBudget(selected.id)]); notice('Budget reviewed.');
        }
        $('budgetApprove').addEventListener('click', () => run(() => review(true)));
        $('budgetReject').addEventListener('click', () => run(() => review(false)));
        $('budgetActualRun').addEventListener('click', () => run(async () => {
            if (!selected || selected.stateCode !== 'Approved') return;
            const actual = await request('/' + selected.id + '/actual'); lineRows(actual.lines);
            $('budgetVarianceSummary').textContent = 'Income BDT ' + money(actual.actualIncomeBdt) + ' / budget ' +
                money(actual.plannedIncomeBdt) + ' · Expenses BDT ' + money(actual.actualExpenseBdt) +
                ' / budget ' + money(actual.plannedExpenseBdt) + '. Positive variance is favorable.';
        }));
    }
    run(async () => { await Promise.all([accountOptions(),list()]); });
}
