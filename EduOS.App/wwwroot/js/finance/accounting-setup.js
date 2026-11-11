const root = document.getElementById('accountingSetup');
if (root) {
    const base = '/api/finance/accounting/setup', writable = root.dataset.write === 'true', admin = root.dataset.admin === 'true';
    const $ = id => document.getElementById(id);
    const safe = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    let page = 1, totalPages = 1, accounts = [], periods = [], banks = [], groups = [], assetLedgers = [];
    let cashPage = 1, cashPages = 1, cashProposals = [], cashSelected = null, cashGeneration = 0;
    let cashSubmitKey = crypto.randomUUID(), cashReviewKey = null, cashReviewAction = null, cashBusy = false;
    const alert = (message, error = false) => { const el = $('accountingNotice'); el.textContent = message; el.className = 'alert ' + (error ? 'alert-danger' : 'alert-success'); };
    const request = async (path, method = 'GET', body = null) => {
        if (!sameSession()) throw new Error('Institution session changed. Reload before continuing.');
        const options = {method, credentials:'same-origin', cache:'no-store', headers:{Accept:'application/json'}};
        if (body !== null) { options.headers['Content-Type'] = 'application/json'; options.body = JSON.stringify(body); }
        if (method !== 'GET' && token) options.headers.RequestVerificationToken = token;
        const res = await fetch(base + path, options);
        let payload; try { payload = await res.json(); } catch { throw new Error('Unexpected server response.'); }
        if (!sameSession()) throw new Error('Institution session changed. Reload before continuing.');
        if (!res.ok || !payload.success) throw new Error(payload.message || 'Request failed.');
        return payload.data;
    };
    const run = async fn => { try { await fn(); } catch (e) { alert(e.message, true); } };
    const pendingForms = new WeakSet();
    const runForm = async (form, action) => {
        if (pendingForms.has(form)) return;
        pendingForms.add(form);
        const controls = Array.from(form.querySelectorAll('input,select,textarea,button'));
        const disabled = controls.map(control => control.disabled);
        controls.forEach(control => { control.disabled = true; });
        try { await action(); } catch (e) { alert(e.message, true); }
        finally {
            pendingForms.delete(form);
            controls.forEach((control, i) => { control.disabled = disabled[i]; });
        }
    };
    const guards = {};
    if (writable && window.EduOSFormGuard) {
        for (const id of ['accountForm', 'periodForm', 'bankForm'])
            guards[id] = window.EduOSFormGuard.create($(id));
    }
    const confirmDiscard = id => !guards[id] || guards[id].confirmDiscard();
    const markClean = id => guards[id]?.markClean();
    const identity = () => [document.body?.dataset?.authTenantId || '', document.body?.dataset?.authSessionReference || ''].join('|');
    const originalIdentity = identity();
    const sameSession = () => identity() === originalIdentity && !originalIdentity.startsWith('|') && !originalIdentity.endsWith('|');
    const types = {1:'Asset',2:'Liability',3:'Equity',4:'Income',5:'Expense'};
    const resetAccount = () => { $('accountForm')?.reset(); if (!writable) return; $('accountId').value = ''; $('accountVersion').value = ''; $('accountActive').checked = true; markClean('accountForm'); };
    const resetPeriod = () => { $('periodForm')?.reset(); if (!writable) return; $('periodId').value = ''; $('periodVersion').value = ''; markClean('periodForm'); };
    const resetBank = () => { $('bankForm')?.reset(); if (!writable) return; $('bankId').value = ''; $('bankVersion').value = ''; $('bankActive').checked = true; markClean('bankForm'); };
    async function loadAccounts() {
        const q = $('accountSearch').value.trim(), data = await request('/accounts?' + new URLSearchParams({page, pageSize:25, search:q}));
        accounts = data.items || []; totalPages = Math.max(1, data.totalPages || 1);
        $('accountPage').textContent = 'Page ' + page + ' of ' + totalPages + ' (' + data.totalCount + ')';
        $('accountPrev').disabled = page <= 1; $('accountNext').disabled = page >= totalPages;
        $('accountRows').innerHTML = accounts.map(a => '<tr><td>' + safe(a.code) + '</td><td>' + safe(a.name) +
            '</td><td>' + safe(types[a.accountType]) + '</td><td>' + safe(a.parentAccountName) + '</td><td>' +
            (a.isCashInHand ? 'Yes' : 'No') + '</td><td>' +
            (a.isGroup ? 'Yes' : 'No') + '</td><td>' + (a.isActive ? 'Yes' : 'No') + '</td>' +
            (writable ? '<td><button class="btn btn-sm btn-outline-primary" data-edit-account="' + a.id + '">Edit</button></td>' : '') + '</tr>').join('');
    }
    async function loadAccountOptions() {
        if (!writable) return;
        let all = [], current = 1, pages = 1;
        do {
            const data = await request('/accounts?' + new URLSearchParams({page:current, pageSize:100}));
            all.push(...(data.items || [])); pages = data.totalPages || 1; current++;
            if (pages > 100) throw new Error('Too many accounts for this selector; contact administrator.');
        } while (current <= pages);
        groups = all.filter(x => x.isGroup && x.isActive);
        assetLedgers = all.filter(x => x.accountType === 1 && !x.isGroup && x.isActive && !x.isCashInHand);
        $('accountParent').innerHTML = '<option value="">Root</option>' + groups.map(x => '<option value="' + x.id + '">' + safe(x.code + ' — ' + x.name) + '</option>').join('');
        $('bankLedger').innerHTML = '<option value="">Select ledger</option>' + assetLedgers.map(x => '<option value="' + x.id + '">' + safe(x.code + ' — ' + x.name) + '</option>').join('');
        $('historicalCashLedger').innerHTML = '<option value="">Select historically posted cash ledger</option>' + assetLedgers.map(x =>
            '<option value="' + x.id + '">' + safe(x.code + ' — ' + x.name) + '</option>').join('');
    }
    async function loadPeriods() {
        periods = await request('/periods');
        $('periodRows').innerHTML = periods.map(p => '<tr><td>' + safe(p.name) + '</td><td>' + safe(p.startDate) +
            '</td><td>' + safe(p.endDate) + '</td><td>' + (p.isClosed ? 'Closed' : 'Open') + '</td>' +
            (writable ? '<td>' + (p.isClosed ? '' : '<button class="btn btn-sm btn-outline-primary me-2" data-edit-period="' + p.id + '">Edit</button><button class="btn btn-sm btn-outline-info me-2" data-preview-period="' + p.id + '">Review close</button><button class="btn btn-sm btn-outline-danger" data-close-period="' + p.id + '">Close</button>') + '</td>' : '') + '</tr>').join('');
    }
    async function showPeriodReadiness(id) {
        const data = await request('/periods/' + id + '/close-readiness');
        const panel = $('periodCloseReadiness');
        panel.className = 'alert ' + (data.canClose ? 'alert-success' : 'alert-warning');
        panel.textContent = data.periodName + ': ' + (data.canClose ? 'Ready to close.' : 'Not ready.') +
            ' Posted: ' + data.postedCount + ', reversed: ' + data.reversedCount +
            ', deleted journals: ' + data.historicalDeletedJournalCount +
            ', deleted journal lines: ' + data.historicalDeletedJournalLineCount +
            ', drafts: ' + data.draftCount + ', submitted: ' + data.submittedCount +
            ', approved: ' + data.approvedCount + ', invalid journals: ' + data.invalidJournalCount +
            ', invalid lines: ' + data.invalidLineCount + ', unbalanced journals: ' + data.unbalancedJournalCount +
            ', invalid accounts: ' + data.missingAccountCount + '. ' + (data.blockers || []).join(' ');
        return data;
    }
    async function loadBanks() {
        if (!writable) return;
        banks = await request('/banks');
        $('bankRows').innerHTML = banks.map(b => '<tr><td>' + safe(b.bankName) + '</td><td>' + safe(b.accountNumber) +
            '</td><td>' + safe(b.ledgerAccountName) + '</td><td>' + (b.isActive ? 'Active' : 'Inactive') +
            '</td><td><button class="btn btn-sm btn-outline-primary" data-edit-bank="' + b.id + '">Edit</button></td></tr>').join('');
    }
    async function loadCashProposals() {
        if (!writable) return;
        const generation = ++cashGeneration;
        const data = await request('/cash-designations?' + new URLSearchParams({page:cashPage, pageSize:25}));
        if (generation !== cashGeneration) return;
        cashProposals = data.items || []; cashPages = Math.max(1, data.totalPages || 1);
        $('historicalCashPage').textContent = 'Page ' + cashPage + ' / ' + cashPages + ' (' + data.totalCount + ')';
        $('historicalCashPrev').disabled = cashPage <= 1;
        $('historicalCashNext').disabled = cashPage >= cashPages;
        $('historicalCashRows').innerHTML = cashProposals.map(x => '<tr><td>' + safe(x.accountCode + ' — ' + x.accountName) +
            '</td><td>' + safe(x.stateCode) + '</td><td>' + safe(x.reason) + '</td><td>' + safe(x.submittedByUserId) +
            '</td><td>' + safe(x.submittedAtUtc) + '</td><td>' + safe(x.reviewedByUserId || '') + '</td><td>' +
            (admin && x.stateCode === 'Submitted' ? '<button type="button" class="btn btn-sm btn-outline-primary" data-review-cash="' + x.id + '">Review</button>' :
                safe(x.reviewReason || '')) + '</td></tr>').join('');
    }
    $('accountSearchBtn').addEventListener('click', () => run(async () => { page = 1; await loadAccounts(); }));
    $('accountPrev').addEventListener('click', () => run(async () => { if (page > 1) { page--; await loadAccounts(); } }));
    $('accountNext').addEventListener('click', () => run(async () => { if (page < totalPages) { page++; await loadAccounts(); } }));
    if (writable) {
        $('historicalCashForm').addEventListener('input', () => { if (!cashBusy) cashSubmitKey = crypto.randomUUID(); });
        $('historicalCashLedger').addEventListener('change', () => { if (!cashBusy) cashSubmitKey = crypto.randomUUID(); });
        $('accountReset').addEventListener('click', () => { if (!pendingForms.has($('accountForm')) && confirmDiscard('accountForm')) resetAccount(); });
        $('periodReset').addEventListener('click', () => { if (!pendingForms.has($('periodForm')) && confirmDiscard('periodForm')) resetPeriod(); });
        $('bankReset').addEventListener('click', () => { if (!pendingForms.has($('bankForm')) && confirmDiscard('bankForm')) resetBank(); });
        $('accountRows').addEventListener('click', e => run(async () => {
            const id = e.target.closest('[data-edit-account]')?.dataset.editAccount; if (!id) return;
            if (pendingForms.has($('accountForm')) || !confirmDiscard('accountForm')) return;
            const before = window.EduOSFormGuard?.fingerprint($('accountForm'));
            const a = await request('/accounts/' + id);
            if (pendingForms.has($('accountForm')) ||
                (before !== undefined && before !== window.EduOSFormGuard.fingerprint($('accountForm')))) return;
            $('accountId').value = a.id; $('accountVersion').value = a.rowVersion; $('accountCode').value = a.code;
            $('accountName').value = a.name; $('accountType').value = a.accountType;
            $('accountParent').value = a.parentAccountId || ''; $('accountGroup').checked = a.isGroup;
            $('accountActive').checked = a.isActive; $('accountCash').checked = a.isCashInHand;
            markClean('accountForm');
        }));
        $('accountForm').addEventListener('submit', e => { e.preventDefault(); runForm(e.currentTarget, async () => {
            const id = $('accountId').value, body = {code:$('accountCode').value.trim(), name:$('accountName').value.trim(),
                accountType:Number($('accountType').value), parentAccountId:$('accountParent').value ? Number($('accountParent').value) : null,
                isGroup:$('accountGroup').checked, isActive:$('accountActive').checked,
                isCashInHand:$('accountCash').checked, rowVersion:$('accountVersion').value || null};
            await request('/accounts' + (id ? '/' + id : ''), id ? 'PUT' : 'POST', body);
            resetAccount(); await Promise.all([loadAccounts(), loadAccountOptions()]); alert('Account saved.');
        }); });
        const pendingPeriodCloses = new Set();
        $('periodRows').addEventListener('click', e => run(async () => {
            const edit = e.target.closest('[data-edit-period]')?.dataset.editPeriod;
            const close = e.target.closest('[data-close-period]')?.dataset.closePeriod;
            const preview = e.target.closest('[data-preview-period]')?.dataset.previewPeriod;
            if (preview) await showPeriodReadiness(preview);
            if (edit) {
                const p = periods.find(x => String(x.id) === edit); if (!p) return;
                if (!confirmDiscard('periodForm') || pendingForms.has($('periodForm'))) return;
                $('periodId').value = p.id; $('periodVersion').value = p.rowVersion;
                $('periodName').value = p.name; $('periodFrom').value = p.startDate; $('periodTo').value = p.endDate;
                markClean('periodForm');
            }
            if (close) {
                if (pendingPeriodCloses.has(close)) return;
                pendingPeriodCloses.add(close);
                try {
                const p = periods.find(x => String(x.id) === close); if (!p) return;
                const readiness = await showPeriodReadiness(close);
                if (!readiness.canClose) { alert('Period is not ready to close. Resolve the displayed blockers.', true); return; }
                if (!confirm('Close period ' + p.name + '? ' + readiness.postedCount + ' posted and ' + readiness.reversedCount + ' reversed journals verified. This cannot be undone.')) return;
                await request('/periods/' + close + '/close', 'POST', {rowVersion:readiness.rowVersion});
                await loadPeriods(); alert('Period closed.');
                } finally { pendingPeriodCloses.delete(close); }
            }
        }));
        $('periodForm').addEventListener('submit', e => { e.preventDefault(); runForm(e.currentTarget, async () => {
            const id = $('periodId').value, body = {name:$('periodName').value.trim(), startDate:$('periodFrom').value,
                endDate:$('periodTo').value, rowVersion:$('periodVersion').value || null};
            await request('/periods' + (id ? '/' + id : ''), id ? 'PUT' : 'POST', body);
            resetPeriod(); await loadPeriods(); alert('Period saved.');
        }); });
        $('bankRows').addEventListener('click', e => {
            const id = e.target.closest('[data-edit-bank]')?.dataset.editBank;
            if (!id || pendingForms.has($('bankForm')) || !confirmDiscard('bankForm')) return;
            const b = banks.find(x => String(x.id) === id); if (!b) return;
            $('bankId').value = b.id; $('bankVersion').value = b.rowVersion; $('bankLedger').value = b.accountId;
            $('bankName').value = b.bankName; $('bankAccountName').value = b.accountName; $('bankNumber').value = b.accountNumber;
            $('bankBranch').value = b.branchName || ''; $('bankRouting').value = b.routingNumber || ''; $('bankActive').checked = b.isActive;
            markClean('bankForm');
        });
        $('bankForm').addEventListener('submit', e => { e.preventDefault(); runForm(e.currentTarget, async () => {
            const id = $('bankId').value, body = {accountId:Number($('bankLedger').value), bankName:$('bankName').value.trim(),
                accountName:$('bankAccountName').value.trim(), accountNumber:$('bankNumber').value.trim(),
                branchName:$('bankBranch').value.trim() || null, routingNumber:$('bankRouting').value.trim() || null,
                currencyCode:'BDT', isActive:$('bankActive').checked, rowVersion:$('bankVersion').value || null};
            await request('/banks' + (id ? '/' + id : ''), id ? 'PUT' : 'POST', body);
            resetBank(); await loadBanks(); alert('Bank account saved.');
        }); });
        $('historicalCashForm').addEventListener('submit', e => { e.preventDefault(); run(async () => {
            if (cashBusy) return;
            cashBusy = true; $('historicalCashSubmit').disabled = true;
            try {
                const id = $('historicalCashLedger').value;
                const reason = $('historicalCashReason').value.trim();
                if (!id || reason.length < 20 || reason.length > 500) throw new Error('Select a ledger and provide 20–500 characters of evidence.');
                await request('/cash-designations', 'POST', {clientRequestId:cashSubmitKey, accountId:id, reason});
                cashSubmitKey = crypto.randomUUID(); $('historicalCashForm').reset();
                cashPage = 1; await loadCashProposals();
                $('historicalCashStatus').textContent = 'Submitted for an independent TenantAdmin review.';
            } finally { cashBusy = false; $('historicalCashSubmit').disabled = false; }
        }); });
        $('historicalCashPrev').addEventListener('click', () => run(async () => {
            if (cashPage > 1) { cashPage--; await loadCashProposals(); }
        }));
        $('historicalCashNext').addEventListener('click', () => run(async () => {
            if (cashPage < cashPages) { cashPage++; await loadCashProposals(); }
        }));
        if (admin) {
            const closeCashReview = () => { cashSelected = null; cashReviewKey = null; cashReviewAction = null;
                $('historicalCashReview').classList.add('d-none'); $('historicalCashReviewReason').value = ''; };
            $('historicalCashReviewReason').addEventListener('input', () => {
                if (!cashBusy) { cashReviewKey = crypto.randomUUID(); cashReviewAction = null; }
            });
            $('historicalCashRows').addEventListener('click', e => {
                const id = e.target.closest('[data-review-cash]')?.dataset.reviewCash;
                if (!id) return;
                cashSelected = cashProposals.find(x => String(x.id) === id && x.stateCode === 'Submitted') || null;
                if (!cashSelected) return;
                cashReviewKey = crypto.randomUUID();
                cashReviewAction = null;
                $('historicalCashReviewTitle').textContent = cashSelected.accountCode + ' — ' + cashSelected.accountName +
                    '. Evidence: ' + cashSelected.reason;
                $('historicalCashReview').classList.remove('d-none');
                $('historicalCashReviewReason').focus();
            });
            $('historicalCashReviewCancel').addEventListener('click', closeCashReview);
            const decide = async approve => {
                if (!cashSelected || cashBusy) return;
                const reason = $('historicalCashReviewReason').value.trim();
                if (reason.length > 500 || (!approve && !reason)) throw new Error('Rejection requires a review reason.');
                if (approve && !confirm('Approve physical cash classification for ' + cashSelected.accountCode + '? Historical posted GL journals will remain unchanged.')) return;
                if (cashReviewAction !== approve) { cashReviewKey = crypto.randomUUID(); cashReviewAction = approve; }
                cashBusy = true; $('historicalCashApprove').disabled = true; $('historicalCashReject').disabled = true;
                try {
                    await request('/cash-designations/' + encodeURIComponent(cashSelected.id) + '/review', 'POST',
                        {reviewRequestId:cashReviewKey, approve, reason, rowVersion:cashSelected.rowVersion});
                    closeCashReview(); await Promise.all([loadCashProposals(), loadAccounts(), loadAccountOptions()]);
                    $('historicalCashStatus').textContent = approve ? 'Historical cash account approved.' : 'Request rejected.';
                } finally { cashBusy = false; $('historicalCashApprove').disabled = false; $('historicalCashReject').disabled = false; }
            };
            $('historicalCashApprove').addEventListener('click', () => run(() => decide(true)));
            $('historicalCashReject').addEventListener('click', () => run(() => decide(false)));
        }
    }
    run(async () => { await Promise.all([loadAccounts(), loadPeriods()]); if (writable) await Promise.all([loadAccountOptions(), loadBanks(), loadCashProposals()]); });
}
