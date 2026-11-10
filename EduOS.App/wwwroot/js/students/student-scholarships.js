(() => {
    'use strict';
    const root = document.getElementById('scholarshipsApp');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
    const T = (en, bd) => bn ? bd : en;
    const review = root.dataset.reviewer === 'true';
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
        document.querySelector('meta[name="request-verification-token"]')?.content;
    const session = [document.body.dataset.authTenantId, document.body.dataset.authSessionReference];
    const safeSession = () => session[0] && session[1] &&
        session[0] === document.body.dataset.authTenantId &&
        session[1] === document.body.dataset.authSessionReference;
    const state = { busy: false, locked: false, page: 1, pageSize: 20, total: 0, enrollment: '', selected: null, evidenceUploadId: null,
        reconcilePage: 1, reconcileTotal: 0, creditPage: 1, creditTotal: 0,
        cash: null, cashLoading: false,
        cashFinanceLoaded: false };
    const guid = () => crypto.randomUUID();
    const val = id => $(id)?.value?.trim() || '';
    const date = value => value ? String(value).slice(0, 10) : '';
    const todayLocal = () => {
        const now = new Date();
        return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
    };
    const retries = new Map();
    const intakeGuard = window.EduOSFormGuard.create($('aidSubmitForm'));
    const financeGuard = review ? window.EduOSFormGuard.create($('aidCashPrepareForm')) : null;
    const auxiliaryFields = ['aidReason', 'aidCashReason', 'aidCashReverseDate', 'aidCashReverseRef', 'aidCashReversePeriod'];
    const auxiliaryBaseline = {};
    function markAuxiliaryClean(fields = auxiliaryFields) {
        for (const field of fields) auxiliaryBaseline[field] = val(field);
    }
    const auxiliaryDirty = () => auxiliaryFields.some(field => val(field) !== (auxiliaryBaseline[field] || ''));
    let financeOptionsPromise = null;
    let cashLoadGeneration = 0;
    function retryId(slot, payload) {
        const fingerprint = JSON.stringify(payload);
        const previous = retries.get(slot);
        if (previous?.fingerprint === fingerprint) return previous.id;
        const id = guid(); retries.set(slot, { fingerprint, id });
        return id;
    }
    function notice(message, danger = false) {
        const box = $('aidNotice');
        box.className = 'alert ' + (danger ? 'alert-danger' : 'alert-info');
        box.textContent = message;
    }
    function checkSession() {
        if (state.locked) return false;
        if (safeSession()) return true;
        state.locked = true; state.busy = true; state.enrollment = '';
        state.selected = null; retries.clear(); state.evidenceUploadId = null; state.total = 0;
        cashLoadGeneration++;
        for (const field of root.querySelectorAll('input,select,textarea'))
            if (field.type !== 'hidden') field.value = '';
        $('aidStudents').replaceChildren(); $('aidRows').replaceChildren();
        $('aidEvidence').value = '';
        if (review) {
            $('aidSelected').textContent = '';
            $('aidReconciliationRows').replaceChildren();
            $('aidCreditRows').replaceChildren();
            $('aidCreditReport').classList.add('d-none');
        }
        for (const control of root.querySelectorAll('button,input,select,textarea')) control.disabled = true;
        notice(T('Institution session changed. Reload this page.', 'প্রতিষ্ঠানের সেশন বদলেছে। পৃষ্ঠাটি রিলোড করুন।'), true);
        return false;
    }
    function refreshControls() {
        if (state.locked) return;
        const cashMode = val('aidMode') === 'CashGrant';
        $('aidRuleGroup').classList.toggle('d-none', cashMode);
        $('aidCashGroup').classList.toggle('d-none', !cashMode);
        $('aidRule').required = !cashMode;
        $('aidCashAmount').required = cashMode;
        $('aidSubmit').disabled = state.busy || !state.enrollment ||
            (cashMode ? !Number.isFinite(Number(val('aidCashAmount'))) ||
                Number(val('aidCashAmount')) <= 0 || Number(val('aidCashAmount')) > 99999999 : !val('aidRule')) ||
            !val('aidFrom') || !val('aidStatement') ||
            ($('aidEvidenceFile').files?.length > 0 && !val('aidEvidence'));
        $('aidEvidenceUpload').disabled = state.busy || !state.enrollment ||
            $('aidEvidenceFile').files?.length !== 1;
        for (const id of ['aidRefresh', 'aidPrev', 'aidNext']) $(id).disabled = state.busy ||
            (id === 'aidPrev' && state.page <= 1) ||
            (id === 'aidNext' && state.page * state.pageSize >= state.total);
        if (!review) return;
        $('aidApprove').disabled = state.busy || state.selected?.state !== 'Submitted';
        $('aidReject').disabled = $('aidApprove').disabled;
        $('aidAward').disabled = state.busy || state.selected?.state !== 'Approved' ||
            state.selected?.awardMode !== 'FeeCredit';
        $('aidCancel').disabled = state.busy ||
            !['Submitted', 'Approved', 'Awarded'].includes(state.selected?.state);
        $('aidEvidenceDownload').disabled = state.busy || !state.selected?.hasEvidence;
        $('aidInvoiceCredits').disabled = state.busy || !state.selected;
        $('aidCreditsPrev').disabled = state.busy || state.creditPage <= 1;
        $('aidCreditsNext').disabled = state.busy || state.creditPage * 30 >= state.creditTotal;
        $('aidReconcile').disabled = state.busy;
        $('aidReconcilePrev').disabled = state.busy || state.reconcilePage <= 1;
        $('aidReconcileNext').disabled = state.busy || state.reconcilePage * 30 >= state.reconcileTotal;
        const selectedCash = state.selected?.awardMode === 'CashGrant';
        $('aidCashPanel').classList.toggle('d-none', !selectedCash);
        $('aidCashBank').disabled = state.busy || val('aidCashMethod') !== 'Bank';
        $('aidCashLedger').disabled = state.busy || val('aidCashMethod') !== 'Cash';
        $('aidCashPrepare').disabled = state.busy || state.cashLoading || !selectedCash || state.selected?.state !== 'Approved' ||
            (state.cash && state.cash.state !== 'Rejected');
        $('aidCashConfirm').disabled = $('aidCashReject').disabled = state.busy || state.cashLoading || !state.cash ||
            state.cash.state !== 'Submitted';
        $('aidCashReverseRequest').disabled = state.busy || state.cashLoading || !state.cash || state.cash.state !== 'Posted';
        $('aidCashReverseConfirm').disabled = $('aidCashReverseReject').disabled = state.busy || state.cashLoading ||
            !state.cash || state.cash.state !== 'ReversalRequested';
    }
    async function api(path, method = 'GET', payload) {
        if (!checkSession()) throw Error(T('Institution session changed. Reload.', 'প্রতিষ্ঠানের সেশন বদলেছে। পৃষ্ঠা রিলোড করুন।'));
        const headers = { Accept: 'application/json' };
        if (method !== 'GET') {
            if (!token) throw Error(T('Security token missing. Reload.', 'নিরাপত্তা টোকেন নেই। রিলোড করুন।'));
            headers.RequestVerificationToken = token;
        }
        if (payload !== undefined) headers['Content-Type'] = 'application/json';
        const response = await fetch(path, {
            method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(payload === undefined ? {} : { body: JSON.stringify(payload) })
        });
        const json = await response.json().catch(() => null);
        if (!checkSession()) throw Error(T('Institution session changed. Reload.', 'প্রতিষ্ঠানের সেশন বদলেছে। রিলোড করুন।'));
        if (!response.ok || json?.success !== true)
            throw Error(json?.message || T('Request failed. Reload and retry.', 'অনুরোধ ব্যর্থ। রিলোড করে চেষ্টা করুন।'));
        return json.data;
    }
    async function run(action, form = null) {
        if (state.busy || !checkSession()) return false;
        state.busy = true; refreshControls();
        if (form) form.inert = true;
        try { await action(); return true; }
        catch (error) { notice(error.message, true); return false; }
        finally { if (form) form.inert = false; state.busy = false; refreshControls(); }
    }
    const make = (tag, text, css = '') => {
        const node = document.createElement(tag); node.textContent = String(text ?? '');
        if (css) node.className = css;
        return node;
    };
    async function searchStudent(event) {
        event?.preventDefault();
        await run(async () => {
            const results = await api('/api/student-support-directory/enrollments?' +
                new URLSearchParams({ search: val('aidStudentSearch'), financialOnly: 'true' }));
            const box = $('aidStudents'); box.replaceChildren();
            if (!results?.length) box.append(make('div', T('No accessible active enrollments.', 'অনুমোদিত সক্রিয় ভর্তি নেই।'), 'text-muted small'));
            for (const row of results || []) {
                const button = make('button', row.studentName + ' · ' + row.studentCode +
                    ' · ' + row.rollNo, 'list-group-item list-group-item-action');
                button.type = 'button';
                button.addEventListener('click', () => {
                    if (state.busy || !checkSession() || state.enrollment === row.enrollmentReference) return;
                    if (!intakeGuard.confirmDiscard()) return;
                    state.enrollment = row.enrollmentReference;
                    $('aidSubmitForm').reset();
                    $('aidFrom').value = todayLocal();
                    $('aidEnrollment').value = row.studentName + ' / ' + row.rollNo;
                    retries.delete('aid-apply'); state.evidenceUploadId = null;
                    $('aidEvidenceFile').value = ''; $('aidEvidence').value = '';
                    $('aidEvidenceStatus').textContent = T('No evidence uploaded for this enrollment.',
                        'এই ভর্তির জন্য কোনো প্রমাণ আপলোড হয়নি।');
                    intakeGuard.markClean();
                    box.replaceChildren(); refreshControls();
                });
                box.append(button);
            }
        });
    }
    async function rules() {
        await run(async () => {
            const draft = () => JSON.stringify(['aidEnrollment', 'aidMode', 'aidCashAmount', 'aidFrom',
                'aidTo', 'aidStatement', 'aidEvidence', 'aidRenewal'].map(val));
            const before = draft();
            const rows = await api('/api/student-support-directory/aid-rules');
            const select = $('aidRule'); select.replaceChildren(new Option(T('Select approved fee policy', 'ফি নীতি নির্বাচন করুন'), ''));
            for (const rule of rows || []) select.add(new Option(rule.name + ' · ' +
                rule.value + (rule.isPercentage ? '%' : ' BDT'), String(rule.id)));
            if (draft() === before && !$('aidEvidenceFile').files?.length) intakeGuard.markClean();
            if (!rows?.length) notice(T('No active fee-assistance policies. Contact Finance.', 'সক্রিয় ফি নীতি নেই। ফাইন্যান্সে যোগাযোগ করুন।'), true);
        });
    }
    function selectApplication(row) {
        if (state.busy || !checkSession()) return;
        if (state.selected?.reference === row.reference) return;
        if (state.selected?.reference !== row.reference && review) {
            const financeDirty = financeGuard.isDirty();
            if (financeDirty && !financeGuard.confirmDiscard()) return;
            if (!financeDirty && auxiliaryDirty() &&
                !window.confirm(T('Discard unsaved review and repayment details for the previous application?',
                    'পূর্বের আবেদনের অসংরক্ষিত যাচাই ও ফেরতের তথ্য বাদ দেবেন?'))) return;
            $('aidCashPrepareForm').reset();
            $('aidCashDate').value = todayLocal();
            for (const id of ['aidReason', 'aidCashReason', 'aidCashReverseRef']) $(id).value = '';
            $('aidCashReverseDate').value = todayLocal();
            $('aidCashReversePeriod').value = '';
            financeGuard.markClean();
            markAuxiliaryClean();
            for (const slot of ['aid-review', 'aid-cash-prepare', 'aid-cash-review',
                'aid-cash-reverse', 'aid-cash-reverse-review']) retries.delete(slot);
        }
        ++cashLoadGeneration;
        state.selected = row; state.cash = null; state.cashLoading = false;
        if (review) $('aidCashPrepareForm').inert = false;
        renderSelected();
        notice(row.reference + ' · ' + row.state + ' · ' + row.eligibilityStatement);
    }
    function renderSelected() {
        if (!review) return;
        $('aidCreditReport').classList.add('d-none');
        state.creditPage = 1; state.creditTotal = 0;
        $('aidSelected').textContent = state.selected ? state.selected.reference + ' · ' +
            state.selected.state + ' · ' + state.selected.awardMode + ' · ' +
            (state.selected.cashAmount ? Number(state.selected.cashAmount).toFixed(2) + ' BDT · ' : '') +
            state.selected.eligibilityStatement :
            T('Choose an application above.', 'উপরের তালিকা থেকে আবেদন বাছুন।');
        refreshControls();
        if (state.selected?.awardMode === 'CashGrant') loadCash();
    }
    async function list() {
        await run(async () => {
            const data = await api('/api/student-scholarships?' +
                new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize) }));
            state.total = data?.totalCount || 0;
            const tbody = $('aidRows'); tbody.replaceChildren();
            for (const row of data?.items || []) {
                const tr = document.createElement('tr');
                for (const value of [row.reference.slice(0, 8), row.state,
                    date(row.effectiveFrom) + ' – ' + (date(row.effectiveTo) || '∞')])
                    tr.append(make('td', value));
                const td = document.createElement('td');
                const button = make('button', T('View', 'দেখুন'), 'btn btn-outline-primary btn-sm');
                button.type = 'button';
                button.addEventListener('click', () => selectApplication(row));
                td.append(button); tr.append(td); tbody.append(tr);
            }
            if (!data?.items?.length) {
                const tr = document.createElement('tr');
                const td = make('td', T('No applications.', 'কোনো আবেদন নেই।'));
                td.colSpan = 4; tr.append(td); tbody.append(tr);
            }
            $('aidPage').textContent = `${state.page} · ${state.total} ${T('records', 'রেকর্ড')}`;
        });
    }
    async function submit(event) {
        event.preventDefault();
        if (!state.enrollment) return notice(T('Select current enrollment.', 'বর্তমান ভর্তি নির্বাচন করুন।'), true);
        if (val('aidTo') && val('aidTo') < val('aidFrom'))
            return notice(T('End date must follow start date.', 'শেষের তারিখ শুরুর আগে হবে না।'), true);
        if ($('aidEvidenceFile').files?.length && !val('aidEvidence'))
            return notice(T('Upload the selected evidence first.', 'নির্বাচিত প্রমাণ আগে আপলোড করুন।'), true);
        await run(async () => {
            const payload = {
                enrollmentReference: state.enrollment, awardMode: val('aidMode'),
                discountRuleId: val('aidMode') === 'CashGrant' ? 0 : Number(val('aidRule')),
                cashAmount: val('aidMode') === 'CashGrant' ? Number(val('aidCashAmount')) : null,
                effectiveFrom: val('aidFrom'), effectiveTo: val('aidTo') || null,
                eligibilityStatement: val('aidStatement'),
                evidenceFileReference: val('aidEvidence') || null,
                renewalOfReference: val('aidRenewal') || null
            };
            const result = await api('/api/student-scholarships', 'POST', {
                ...payload, requestId: retryId('aid-apply', payload)
            });
            retries.delete('aid-apply'); $('aidStatement').value = ''; $('aidEvidence').value = '';
            $('aidCashAmount').value = '';
            $('aidRenewal').value = ''; $('aidEvidenceFile').value = '';
            intakeGuard.markClean();
            notice(T('Submitted for review: ', 'পর্যালোচনার জন্য জমা: ') + result.reference);
            state.page = 1;
            try { await listWithoutBusy(); } catch (_) {
                notice(T('Submitted successfully. Register refresh failed; refresh before another application.',
                    'আবেদন সংরক্ষিত হয়েছে। তালিকা রিফ্রেশ ব্যর্থ; পরের আবেদনের আগে রিফ্রেশ করুন।'), true);
            }
        }, $('aidSubmitForm'));
    }
    async function uploadEvidence() {
        if (!state.enrollment) return notice(T('Choose an enrollment first.', 'আগে ভর্তি নির্বাচন করুন।'), true);
        const file = $('aidEvidenceFile').files?.[0];
        if (!file || file.size < 1 || file.size > 5242880)
            return notice(T('Choose a supported private file up to 5 MB.',
                'সর্বোচ্চ ৫ MB-এর সমর্থিত ব্যক্তিগত ফাইল নির্বাচন করুন।'), true);
        await run(async () => {
            if (!checkSession() || !token) throw Error(T('Session or security token unavailable.', 'সেশন বা নিরাপত্তা টোকেন নেই।'));
            const form = new FormData();
            form.append('enrollmentReference', state.enrollment);
            form.append('requestId', state.evidenceUploadId || (state.evidenceUploadId = guid()));
            form.append('file', file, file.name);
            const response = await fetch('/api/student-scholarships/evidence-upload', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { RequestVerificationToken: token, Accept: 'application/json' }, body: form
            });
            const result = await response.json().catch(() => null);
            if (!checkSession()) throw Error(T('Session changed; reload.', 'সেশন বদলেছে; রিলোড করুন।'));
            if (!response.ok || result?.success !== true)
                throw Error(result?.message || T('Secure upload failed.', 'নিরাপদ আপলোড ব্যর্থ হয়েছে।'));
            $('aidEvidence').value = result.data.reference;
            $('aidEvidenceFile').value = ''; state.evidenceUploadId = null;
            $('aidEvidenceStatus').textContent = T('Verified private evidence ready: ',
                'যাচাইকৃত ব্যক্তিগত প্রমাণ প্রস্তুত: ') + file.name;
            notice(T('Private evidence uploaded and scanned.', 'ব্যক্তিগত প্রমাণ আপলোড ও পরীক্ষা সম্পন্ন।'));
        }, $('aidSubmitForm'));
    }
    async function listWithoutBusy() {
        const data = await api('/api/student-scholarships?' +
            new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize) }));
        state.total = data?.totalCount || 0;
        const tbody = $('aidRows'); tbody.replaceChildren();
        for (const row of data?.items || []) {
            const tr = document.createElement('tr');
            for (const value of [row.reference.slice(0, 8), row.state,
                date(row.effectiveFrom) + ' – ' + (date(row.effectiveTo) || '∞')]) tr.append(make('td', value));
            const td = document.createElement('td');
            const btn = make('button', T('View', 'দেখুন'), 'btn btn-sm btn-outline-primary');
            btn.type = 'button'; btn.addEventListener('click', () => selectApplication(row));
            td.append(btn); tr.append(td); tbody.append(tr);
        }
        if (!data?.items?.length) {
            const tr = document.createElement('tr'); const td = make('td', T('No applications.', 'কোনো আবেদন নেই।'));
            td.colSpan = 4; tr.append(td); tbody.append(tr);
        }
        $('aidPage').textContent = `${state.page} · ${state.total} ${T('records', 'রেকর্ড')}`;
    }
    async function act(kind) {
        if (!state.selected) return;
        const target = state.selected;
        const reason = val('aidReason');
        if (kind !== 'award' && !reason) return notice(T('Document a reason first.', 'আগে কারণ লিখুন।'), true);
        await run(async () => {
            const path = '/api/student-scholarships/' + encodeURIComponent(target.reference);
            const payload = { rowVersion: target.rowVersion, reason, approve: kind === 'approve' };
            const data = kind === 'award' ? await api(path + '/award', 'POST') :
                await api(path + (kind === 'cancel' ? '/cancel' : '/decision'), 'POST',
                    kind === 'cancel' ? { requestId: retryId('aid-review', { reference: target.reference, kind, rowVersion: target.rowVersion, reason }),
                        rowVersion: target.rowVersion, reason } :
                        { ...payload, requestId: retryId('aid-review', { reference: target.reference, kind, ...payload }) });
            retries.delete('aid-review');
            state.selected = data; $('aidReason').value = ''; markAuxiliaryClean(['aidReason']); renderSelected();
            notice(T('Saved state: ', 'সংরক্ষিত অবস্থা: ') + data.state);
            try { await listWithoutBusy(); } catch (_) {
                notice(T('Decision saved; register refresh failed.', 'সিদ্ধান্ত সংরক্ষিত; তালিকা রিফ্রেশ হয়নি।'), true);
            }
        });
    }
    const cashBase = () => '/api/student-scholarships/' + encodeURIComponent(state.selected.reference) + '/cash-disbursement';
    const option = (value, label) => new Option(label, String(value));
    async function cashFinanceOptions() {
        if (state.cashFinanceLoaded) return;
        if (financeOptionsPromise) return financeOptionsPromise;
        financeOptionsPromise = loadFinanceOptions();
        try { await financeOptionsPromise; }
        finally { financeOptionsPromise = null; }
    }
    async function loadFinanceOptions() {
        const [first, periods, banks] = await Promise.all([
            api('/api/finance/accounting/setup/accounts?pageSize=100'),
            api('/api/finance/accounting/setup/periods'),
            api('/api/finance/accounting/setup/banks')
        ]);
        const accounts = [...(first.items || [])];
        const total = Number(first.totalCount || 0);
        for (let page = 2; (page - 1) * 100 < total && page <= 20; page++) {
            const more = await api('/api/finance/accounting/setup/accounts?' +
                new URLSearchParams({ page: String(page), pageSize: '100' }));
            accounts.push(...(more.items || []));
        }
        if (accounts.length < total) throw Error(T('Account directory too large; use Finance setup to narrow the list.',
            'হিসাবের তালিকা অনেক বড়; ফাইন্যান্স সেটআপে প্রয়োজনীয় হিসাব খুঁজুন।'));
        $('aidCashExpense').replaceChildren(option('', T('Select expense GL', 'ব্যয় লেজার বাছুন')));
        $('aidCashLedger').replaceChildren(option('', T('Select cash ledger', 'ক্যাশ লেজার বাছুন')));
        for (const account of accounts.filter(a => a.isActive && !a.isGroup)) {
            const label = account.code + ' · ' + account.name;
            if (String(account.accountType) === '5' || account.accountType === 'Expense')
                $('aidCashExpense').add(option(account.id, label));
            if (account.isCashInHand && (String(account.accountType) === '1' || account.accountType === 'Asset'))
                $('aidCashLedger').add(option(account.id, label));
        }
        $('aidCashBank').replaceChildren(option('', T('Select bank', 'ব্যাংক বাছুন')));
        for (const bank of banks || []) if (bank.isActive && bank.currencyCode === 'BDT')
            $('aidCashBank').add(option(bank.id, bank.bankName + ' · ' + bank.accountName + ' · ' + bank.accountNumber));
        for (const id of ['aidCashPeriod', 'aidCashReversePeriod']) {
            $(id).replaceChildren(option('', T('Select open period', 'খোলা হিসাবকাল বাছুন')));
            for (const period of periods || []) if (!period.isClosed)
                $(id).add(option(period.id, period.name + ' · ' + date(period.startDate) + ' – ' + date(period.endDate)));
        }
        state.bankAccounts = banks || [];
        state.cashFinanceLoaded = true;
    }
    async function loadCash() {
        const selected = state.selected?.reference;
        if (!selected || state.selected?.awardMode !== 'CashGrant') return;
        const generation = ++cashLoadGeneration;
        const form = $('aidCashPrepareForm');
        const hadDraft = financeGuard.isDirty();
        state.cashLoading = true;
        $('aidCashStatus').textContent = T('Loading verified Finance status…', 'যাচাইকৃত ফাইন্যান্স অবস্থা লোড হচ্ছে…');
        refreshControls();
        form.inert = true;
        try {
            await cashFinanceOptions();
            if (generation !== cashLoadGeneration || state.selected?.reference !== selected) return;
            const payees = await api('/api/student-scholarships/' + encodeURIComponent(selected) + '/cash-disbursement/payees');
            if (generation !== cashLoadGeneration || state.selected?.reference !== selected) return;
            $('aidCashPayee').replaceChildren();
            for (const payee of payees || []) $('aidCashPayee').add(option(payee.guardianReference || '',
                payee.type + ' · ' + payee.name));
            if (!hadDraft) financeGuard.markClean();
            const response = await fetch('/api/student-scholarships/' + encodeURIComponent(selected) +
                '/cash-disbursement', { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
            const result = await response.json().catch(() => null);
            if (!checkSession() || generation !== cashLoadGeneration || state.selected?.reference !== selected) return;
            if (response.status === 404) state.cash = null;
            else if (response.ok && result?.success) state.cash = result.data;
            else throw Error(result?.message || T('Cash register unavailable.', 'নগদ অনুদানের রেজিস্টার পাওয়া যায়নি।'));
            $('aidCashStatus').textContent = state.cash ?
                state.cash.state + ' · ' + Number(state.cash.amount).toFixed(2) + ' BDT · ' +
                state.cash.method + ' · ' + state.cash.beneficiaryName + ' · ' +
                (state.cash.journalReference || T('Not posted', 'পোস্ট হয়নি')) :
                T('No cash instruction yet. Only an approved grant may be prepared.',
                    'এখনো কোনো পেমেন্ট নির্দেশ নেই। শুধু অনুমোদিত অনুদানের নির্দেশ তৈরি করা যাবে।');
            refreshControls();
        } catch (error) { if (generation === cashLoadGeneration) notice(error.message, true); }
        finally {
            if (generation === cashLoadGeneration) {
                form.inert = false; state.cashLoading = false; refreshControls();
            }
        }
    }
    async function prepareCash(event) {
        event.preventDefault();
        if (!state.selected || !val('aidCashExpense') || !val('aidCashPeriod') || !val('aidCashDate') ||
            !val('aidCashRef') || (val('aidCashMethod') === 'Bank' ? !val('aidCashBank') : !val('aidCashLedger')))
            return notice(T('Complete the Finance payment details.', 'ফাইন্যান্স পেমেন্টের তথ্য পূরণ করুন।'), true);
        await run(async () => {
            const bankId = Number(val('aidCashBank'));
            const bank = state.bankAccounts?.find(x => x.id === bankId);
            const payload = {
                rowVersion: state.selected.rowVersion, method: val('aidCashMethod'),
                guardianReference: val('aidCashPayee') || null,
                expenseAccountId: Number(val('aidCashExpense')),
                paymentAccountId: val('aidCashMethod') === 'Bank' ? (bank?.accountId || 0) : Number(val('aidCashLedger')),
                bankAccountId: val('aidCashMethod') === 'Bank' ? bankId : null,
                accountingPeriodId: Number(val('aidCashPeriod')),
                paymentDate: val('aidCashDate'), externalPaymentReference: val('aidCashRef')
            };
            state.cash = await api(cashBase(), 'POST', {
                ...payload, requestId: retryId('aid-cash-prepare', { reference: state.selected.reference, ...payload })
            });
            retries.delete('aid-cash-prepare'); financeGuard.markClean();
            notice(T('Instruction saved. A different Finance administrator must verify payment.',
                'নির্দেশ সংরক্ষিত। অন্য ফাইন্যান্স প্রশাসককে পেমেন্ট যাচাই করতে হবে।'));
            await loadCash();
        }, $('aidCashPrepareForm'));
    }
    async function cashReview(confirmPaid) {
        if (!state.cash || !val('aidCashReason'))
            return notice(T('Review reason required.', 'পর্যালোচনার কারণ প্রয়োজন।'), true);
        await run(async () => {
            const payload = { rowVersion: state.cash.rowVersion, confirmPaid, reason: val('aidCashReason') };
            state.cash = await api(cashBase() + '/review', 'POST', {
                ...payload, requestId: retryId('aid-cash-review', { reference: state.selected.reference, ...payload })
            });
            retries.delete('aid-cash-review');
            $('aidCashReason').value = ''; markAuxiliaryClean(['aidCashReason']);
            state.selected = await api('/api/student-scholarships/' + encodeURIComponent(state.selected.reference));
            notice(T('Cash review saved: ', 'নগদ অনুদান যাচাই সংরক্ষিত: ') + state.cash.state);
            await loadCash(); await listWithoutBusy();
        });
    }
    async function cashReversalRequest() {
        if (!state.cash || !val('aidCashReason') || !val('aidCashReverseDate') ||
            !val('aidCashReverseRef') || !val('aidCashReversePeriod'))
            return notice(T('Repayment evidence, date, period and reason required.',
                'ফেরতের প্রমাণ, তারিখ, হিসাবকাল ও কারণ প্রয়োজন।'), true);
        await run(async () => {
            const payload = {
                rowVersion: state.cash.rowVersion, accountingPeriodId: Number(val('aidCashReversePeriod')),
                reversalDate: val('aidCashReverseDate'),
                externalReversalReference: val('aidCashReverseRef'), reason: val('aidCashReason')
            };
            state.cash = await api(cashBase() + '/reversal', 'POST', {
                ...payload, requestId: retryId('aid-cash-reverse', { reference: state.selected.reference, ...payload })
            });
            retries.delete('aid-cash-reverse');
            markAuxiliaryClean(['aidCashReason', 'aidCashReverseDate', 'aidCashReverseRef', 'aidCashReversePeriod']);
            notice(T('Reversal pending independent confirmation.', 'রিভার্সাল স্বাধীন নিশ্চিতকরণের অপেক্ষায়।'));
            await loadCash();
        });
    }
    async function cashReversalReview(confirmReversed) {
        if (!state.cash || !val('aidCashReason'))
            return notice(T('Document review reason first.', 'আগে যাচাইয়ের কারণ লিখুন।'), true);
        await run(async () => {
            const payload = { rowVersion: state.cash.rowVersion, confirmReversed, reason: val('aidCashReason') };
            state.cash = await api(cashBase() + '/reversal/review', 'POST', {
                ...payload, requestId: retryId('aid-cash-reverse-review', { reference: state.selected.reference, ...payload })
            });
            retries.delete('aid-cash-reverse-review'); $('aidCashReason').value = '';
            markAuxiliaryClean(['aidCashReason']);
            state.selected = await api('/api/student-scholarships/' + encodeURIComponent(state.selected.reference));
            notice(T('Reversal review saved: ', 'রিভার্সাল যাচাই সংরক্ষিত: ') + state.cash.state);
            await loadCash(); await listWithoutBusy();
        });
    }
    async function evidence() {
        if (!state.selected?.hasEvidence) return;
        await run(async () => {
            const reference = state.selected.reference;
            if (!checkSession() || !token) throw Error(T('Session or security token unavailable.', 'সেশন বা নিরাপত্তা টোকেন নেই।'));
            const response = await fetch('/api/student-scholarships/' +
                encodeURIComponent(reference) + '/evidence-download', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { RequestVerificationToken: token, Accept: 'application/octet-stream,application/json' }
            });
            if (!checkSession()) throw Error(T('Session changed. Reload.', 'সেশন বদলেছে। রিলোড করুন।'));
            if (!response.ok) {
                const error = await response.json().catch(() => null);
                throw Error(error?.message || T('Private evidence unavailable.', 'ব্যক্তিগত প্রমাণ পাওয়া যায়নি।'));
            }
            const type = (response.headers.get('content-type') || '').split(';')[0].toLowerCase();
            const extension = ({ 'application/pdf': 'pdf', 'image/png': 'png',
                'image/jpeg': 'jpg', 'image/webp': 'webp' })[type] || 'bin';
            const blob = await response.blob();
            if (!checkSession()) throw Error(T('Session changed. Reload.', 'সেশন বদলেছে। রিলোড করুন।'));
            const url = URL.createObjectURL(blob);
            try {
                const link = document.createElement('a');
                link.href = url; link.download = 'scholarship-evidence-' + reference.slice(0, 8) + '.' + extension;
                link.style.display = 'none'; document.body.append(link); link.click(); link.remove();
            } finally { setTimeout(() => URL.revokeObjectURL(url), 2000); }
            notice(T('Private evidence access recorded.', 'ব্যক্তিগত প্রমাণ দেখার তথ্য অডিটে সংরক্ষিত।'));
        });
    }
    async function reconcile() {
        await run(async () => {
            const data = await api('/api/student-scholarships/reconciliation?' + new URLSearchParams({
                page: String(state.reconcilePage), pageSize: '30'
            }));
            state.reconcileTotal = data?.totalCount || 0;
            const list = $('aidReconciliationRows'); list.replaceChildren();
            for (const row of data?.items || []) {
                const entry = make('div', row.applicationReference.slice(0, 8) + ' · ' +
                    row.applicationState + ' · ' + row.lineageStatus,
                    'list-group-item text-break');
                list.append(entry);
            }
            if (!data?.items?.length) list.append(make('div',
                T('No financial awards.', 'আর্থিক বরাদ্দ নেই।'), 'list-group-item text-muted'));
            $('aidReconcilePage').textContent = state.reconcilePage + ' · ' + state.reconcileTotal;
        });
    }
    async function invoiceCredits() {
        if (!state.selected) return;
        await run(async () => {
            const selected = state.selected.reference;
            const result = await api('/api/student-scholarships/' +
                encodeURIComponent(selected) + '/invoice-credits?' +
                new URLSearchParams({ page: String(state.creditPage), pageSize: '30' }));
            if (state.selected?.reference !== selected) return;
            state.creditTotal = result?.totalCount || 0;
            $('aidCreditTotals').textContent = T('Applied to active invoices: ', 'সক্রিয় বিলে ছাড়: ') +
                Number(result?.activeInvoiceCreditTotal || 0).toFixed(2) + ' BDT · ' +
                T('Cancelled invoice history: ', 'বাতিল বিলের ইতিহাস: ') +
                Number(result?.cancelledInvoiceCreditTotal || 0).toFixed(2) + ' BDT';
            const list = $('aidCreditRows'); list.replaceChildren();
            for (const row of result?.items || []) {
                const tr = document.createElement('tr');
                for (const value of [row.invoiceNumber, date(row.invoiceDate),
                    row.feeHeadName, row.invoiceState, Number(row.creditedAmount).toFixed(2)])
                    tr.append(make('td', value));
                tr.lastChild.classList.add('text-end'); list.append(tr);
            }
            if (!result?.items?.length) {
                const tr = document.createElement('tr'); const td = make('td',
                    T('No attributable invoices yet; older invoices cannot be inferred.',
                        'উৎসভিত্তিক নতুন ইনভয়েস নেই; পুরোনো বিলের ছাড় অনুমান করা যায় না।'));
                td.colSpan = 5; tr.append(td); list.append(tr);
            }
            $('aidCreditsPage').textContent = state.creditPage + ' · ' + state.creditTotal;
            $('aidCreditReport').classList.remove('d-none');
        });
    }
    $('aidStudentSearchForm').addEventListener('submit', searchStudent);
    $('aidSubmitForm').addEventListener('submit', submit);
    $('aidEvidenceUpload').addEventListener('click', uploadEvidence);
    $('aidEvidenceFile').addEventListener('change', () => {
        $('aidEvidence').value = ''; state.evidenceUploadId = null;
        $('aidEvidenceStatus').textContent = T('Selected evidence is not uploaded yet.',
            'নির্বাচিত প্রমাণ এখনো আপলোড হয়নি।');
        refreshControls();
    });
    for (const id of ['aidMode', 'aidRule', 'aidCashAmount', 'aidFrom', 'aidStatement', 'aidCashMethod'])
        $(id).addEventListener('input', refreshControls);
    $('aidRefresh').addEventListener('click', () => list());
    $('aidPrev').addEventListener('click', () => { if (state.page > 1) { state.page--; list(); } });
    $('aidNext').addEventListener('click', () => { if (state.page * state.pageSize < state.total) { state.page++; list(); } });
    if (review) {
        $('aidCashPrepareForm').addEventListener('submit', prepareCash);
        $('aidCashConfirm').addEventListener('click', () => cashReview(true));
        $('aidCashReject').addEventListener('click', () => cashReview(false));
        $('aidCashReverseRequest').addEventListener('click', cashReversalRequest);
        $('aidCashReverseConfirm').addEventListener('click', () => cashReversalReview(true));
        $('aidCashReverseReject').addEventListener('click', () => cashReversalReview(false));
        for (const [id, kind] of [['aidApprove', 'approve'], ['aidReject', 'reject'],
            ['aidAward', 'award'], ['aidCancel', 'cancel']]) $(id).addEventListener('click', () => act(kind));
        $('aidEvidenceDownload').addEventListener('click', evidence);
        $('aidInvoiceCredits').addEventListener('click', () => { state.creditPage = 1; invoiceCredits(); });
        $('aidCreditsPrev').addEventListener('click', () => { if (state.creditPage > 1) { state.creditPage--; invoiceCredits(); } });
        $('aidCreditsNext').addEventListener('click', () => { if (state.creditPage * 30 < state.creditTotal) { state.creditPage++; invoiceCredits(); } });
        $('aidReconcile').addEventListener('click', () => { state.reconcilePage = 1; reconcile(); });
        $('aidReconcilePrev').addEventListener('click', () => { if (state.reconcilePage > 1) { state.reconcilePage--; reconcile(); } });
        $('aidReconcileNext').addEventListener('click', () => { if (state.reconcilePage * 30 < state.reconcileTotal) { state.reconcilePage++; reconcile(); } });
    }
    $('aidFrom').value = todayLocal();
    if (review) {
        $('aidCashDate').value = $('aidFrom').value;
        $('aidCashReverseDate').value = $('aidFrom').value;
        financeGuard.markClean();
        markAuxiliaryClean();
    }
    intakeGuard.markClean();
    window.addEventListener('focus', checkSession);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) checkSession(); });
    (async () => { await rules(); await searchStudent(); await list(); })();
})();
