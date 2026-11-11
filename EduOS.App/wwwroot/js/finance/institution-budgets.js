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
    let submissionKey = crypto.randomUUID(), submittedFingerprint = null;
    let reviewKey = null, reviewedFingerprint = null, submitting = false, reviewing = false;
    let listGeneration = 0, detailGeneration = 0;
    const identity = () => [document.body?.dataset?.authTenantId || '', document.body?.dataset?.authSessionReference || ''];
    const initialIdentity = identity().join('|');
    const sameSession = () => identity().join('|') === initialIdentity && !!identity()[0] && !!identity()[1];
    const language = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
    const t = (en, bn) => language ? bn : en;
    const formState = () => JSON.stringify([$('budgetName').value, $('budgetFrom').value,
        $('budgetTo').value, $('budgetAccount').value, $('budgetAmount').value, draft.map(x => [x.id, x.amount])]);
    let savedFormState = null;
    const dirty = () => (savedFormState !== null && formState() !== savedFormState) ||
        (selected?.stateCode === 'Submitted' && !!$('budgetReviewReason').value.trim());
    const markClean = () => { savedFormState = formState(); };
    const checkedSession = () => {
        if (!sameSession()) throw new Error(t('Institution session changed. Reload before continuing.', 'প্রতিষ্ঠানের সেশন বদলেছে। চালিয়ে যাওয়ার আগে রিলোড করুন।'));
    };
    const notice = (message, error = false) => { const el = $('budgetNotice'); el.textContent = message; el.className = 'alert ' + (error ? 'alert-danger' : 'alert-success'); };
    async function request(path, method = 'GET', body = null) {
        checkedSession();
        const headers = {Accept:'application/json'};
        if (body !== null) headers['Content-Type'] = 'application/json';
        if (method !== 'GET' && csrf) headers.RequestVerificationToken = csrf;
        const response = await fetch(uri(path), {method,headers,credentials:'same-origin',body:body === null ? undefined : JSON.stringify(body),cache:'no-store'});
        const payload = await response.json().catch(() => null);
        checkedSession();
        if (!response.ok || !payload?.success) throw new Error(payload?.message || 'Request failed.');
        return payload.data;
    }
    const run = async fn => { try { await fn(); } catch (e) { notice(e.message, true); } };
    async function accountOptions() {
        let all = [], next = 1, pages = 1;
        do {
            const data = await fetch('/api/finance/accounting/setup/accounts?' + new URLSearchParams({page:next,pageSize:100}), {credentials:'same-origin'}).then(x => x.json());
            checkedSession();
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
        const generation = ++listGeneration;
        const requestedPage = page;
        const data = await request('?' + new URLSearchParams({page,pageSize:25}));
        if (generation !== listGeneration || requestedPage !== page) return;
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
        const generation = ++detailGeneration;
        const budget = await request('/' + id);
        if (generation !== detailGeneration) return;
        selected = budget;
        reviewKey = null; reviewedFingerprint = null;
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
        if (submitting) return;
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
        if (submitting) return;
        const id = e.target.closest('[data-remove]')?.dataset.remove;
        if (!id) return;
        draft = draft.filter(x => String(x.id) !== id); draftRows();
    });
    $('budgetForm').addEventListener('submit', e => { e.preventDefault(); run(async () => {
        if (submitting) return;
        checkedSession();
        if (draft.length === 0) throw new Error('Add at least one income or expense budget line.');
        if ($('budgetTo').value < $('budgetFrom').value) throw new Error('Budget end date precedes start date.');
        const command = {name:$('budgetName').value.trim(),startDate:$('budgetFrom').value,endDate:$('budgetTo').value,
            lines:draft.map(x => ({accountId:x.id,plannedAmountBdt:x.amount}))};
        const fingerprint = JSON.stringify(command);
        if (submittedFingerprint !== fingerprint) { submissionKey = crypto.randomUUID(); submittedFingerprint = fingerprint; }
        submitting = true;
        const controls = Array.from($('budgetForm').querySelectorAll('input,select,button'));
        const disabled = controls.map(x => x.disabled);
        controls.forEach(x => { x.disabled = true; });
        try {
            const result = await request('', 'POST', {clientRequestId:submissionKey,...command});
            submissionKey = crypto.randomUUID(); submittedFingerprint = null;
            draft = []; draftRows(); $('budgetForm').reset(); markClean();
            notice(t('Budget submitted. Refreshing the register…', 'বাজেট জমা হয়েছে। তালিকা হালনাগাদ হচ্ছে…'));
            try {
                page = 1; await list(); await openBudget(result.id);
                notice(t('Budget submitted for independent approval.', 'স্বতন্ত্র অনুমোদনের জন্য বাজেট জমা হয়েছে।'));
            } catch (_) {
                notice(t('Budget submitted; the register could not refresh. Reload to view it.', 'বাজেট জমা হয়েছে; তালিকা হালনাগাদ হয়নি। দেখতে রিলোড করুন।'));
            }
        } finally {
            submitting = false;
            controls.forEach((x, i) => { x.disabled = disabled[i]; });
        }
    }); });
    $('budgetRows').addEventListener('click', e => run(async () => {
        const id = e.target.closest('[data-open]')?.dataset.open;
        if (!id || reviewing) return;
        if (selected?.stateCode === 'Submitted' && $('budgetReviewReason').value.trim() &&
            !confirm(t('Discard the unsaved review note?', 'সংরক্ষণ না করা রিভিউ মন্তব্য বাদ দেবেন?'))) return;
        await openBudget(id);
    }));
    $('budgetDetailsClose').addEventListener('click', () => {
        if (reviewing || (selected?.stateCode === 'Submitted' && $('budgetReviewReason').value.trim() &&
            !confirm(t('Discard the unsaved review note?', 'সংরক্ষণ না করা রিভিউ মন্তব্য বাদ দেবেন?')))) return;
        ++detailGeneration;
        $('budgetReviewReason').value = '';
        $('budgetDetails').classList.add('d-none');
    });
    $('budgetPrevious').addEventListener('click', () => run(async () => { if (page > 1) { page--; await list(); } }));
    $('budgetNext').addEventListener('click', () => run(async () => { if (page < totalPages) { page++; await list(); } }));
    if (reviewer) {
        async function review(approve) {
            if (reviewing) return;
            checkedSession();
            if (!selected || selected.stateCode !== 'Submitted') throw new Error('Select an unreviewed budget.');
            const reason = $('budgetReviewReason').value.trim();
            if (!approve && !reason) throw new Error('Rejection reason is required.');
            if (!confirm((approve ? 'Approve' : 'Reject') + ' budget ' + selected.name + '?')) return;
            const fingerprint = JSON.stringify([selected.id, selected.rowVersion, approve, reason]);
            if (reviewedFingerprint !== fingerprint) { reviewKey = crypto.randomUUID(); reviewedFingerprint = fingerprint; }
            reviewing = true;
            $('budgetApprove').disabled = true; $('budgetReject').disabled = true;
            try {
                const reviewedId = selected.id;
                await request('/' + reviewedId + '/review', 'POST', {reviewRequestId:reviewKey,
                    approve,reason,rowVersion:selected.rowVersion});
                reviewKey = null; reviewedFingerprint = null;
                $('budgetReviewReason').value = '';
                notice(t('Budget reviewed. Refreshing the register…', 'বাজেট পর্যালোচনা হয়েছে। তালিকা হালনাগাদ হচ্ছে…'));
                try {
                    await Promise.all([list(),openBudget(reviewedId)]);
                    notice(t('Budget reviewed.', 'বাজেট পর্যালোচনা সম্পন্ন।'));
                } catch (_) {
                    notice(t('Review saved; the register could not refresh. Reload to view it.', 'রিভিউ সংরক্ষিত হয়েছে; তালিকা হালনাগাদ হয়নি। রিলোড করুন।'));
                }
                markClean();
            } finally { reviewing = false; $('budgetApprove').disabled = false; $('budgetReject').disabled = false; }
        }
        $('budgetApprove').addEventListener('click', () => run(() => review(true)));
        $('budgetReject').addEventListener('click', () => run(() => review(false)));
        $('budgetActualRun').addEventListener('click', () => run(async () => {
            if (!selected || selected.stateCode !== 'Approved') return;
            const budgetId = selected.id, generation = detailGeneration;
            const actual = await request('/' + budgetId + '/actual');
            if (generation !== detailGeneration || selected?.id !== budgetId) return;
            lineRows(actual.lines);
            $('budgetVarianceSummary').textContent = 'Income BDT ' + money(actual.actualIncomeBdt) + ' / budget ' +
                money(actual.plannedIncomeBdt) + ' · Expenses BDT ' + money(actual.actualExpenseBdt) +
                ' / budget ' + money(actual.plannedExpenseBdt) + '. Positive variance is favorable.';
        }));
    }
    window.addEventListener('beforeunload', event => {
        if (!dirty() && !submitting && !reviewing) return;
        event.preventDefault(); event.returnValue = '';
    });
    window.addEventListener('pageshow', () => { if (!sameSession()) notice(t('Institution session changed. Reload before continuing.', 'প্রতিষ্ঠানের সেশন বদলেছে। রিলোড করুন।'), true); });
    markClean();
    run(async () => { await Promise.all([accountOptions(),list()]); });
}
