(() => {
    'use strict';
    const root = document.getElementById('fpfPage');
    if (!root) return;
    const el = id => document.getElementById(id);
    const bn = root.dataset.bn === 'true', maker = root.dataset.maker === 'true';
    const checker = root.dataset.checker === 'true';
    const t = (en, bd) => bn ? bd : en;
    const session = { ref: document.body?.dataset.authSessionReference || '',
        tenant: document.body?.dataset.authTenantId || '' };
    const shield = el('eduosSessionShield');
    const csrf = document.querySelector('meta[name="request-verification-token"]')?.content;
    let blocked = false, busy = false, page = 1, pages = 1, sequence = 0;
    let selected = null, record = null, options = null;
    const pending = new Map();
    const safe = () => !blocked && !!session.ref && !!session.tenant &&
        !window.EduOSSessionTransitionPending?.() &&
        document.body?.dataset.authSessionReference === session.ref &&
        document.body?.dataset.authTenantId === session.tenant;
    const sameResponse = response => safe() && /^[0-9A-F]{64}$/.test(session.ref) &&
        /^[1-9][0-9]*$/.test(session.tenant) &&
        response.headers.get('X-EduOS-Session-Reference') === session.ref &&
        response.headers.get('X-EduOS-Tenant-Id') === session.tenant;
    function status(message, error = false) {
        el('fpfStatus').className = 'alert alert-' + (error ? 'danger' : 'info');
        el('fpfStatus').textContent = message;
    }
    function lock() {
        blocked = true; ++sequence; selected = null; record = null; options = null;
        el('fpfRows').replaceChildren(); el('fpfDetails').classList.add('d-none');
        for (const id of ['fpfPrev', 'fpfNext']) el(id).disabled = true;
        status(t('Institution session changed. Reload this page.', 'প্রতিষ্ঠানের সেশন বদলেছে। পৃষ্ঠা আবার লোড করুন।'), true);
    }
    async function verify() {
        if (!safe() || !window.EduOSVerifySessionContext || !shield) {
            lock(); throw new Error('session-changed');
        }
        try { await window.EduOSVerifySessionContext(); }
        catch { lock(); throw new Error('session-changed'); }
        if (!safe() || shield.hidden !== true) { lock(); throw new Error('session-changed'); }
    }
    async function api(path, method = 'GET', data) {
        if (!safe() || (method !== 'GET' && !csrf)) {
            lock(); throw new Error('session-changed');
        }
        if (method !== 'GET') await verify();
        const headers = { Accept: 'application/json' };
        if (data !== undefined) headers['Content-Type'] = 'application/json';
        if (method !== 'GET') headers.RequestVerificationToken = csrf;
        const response = await fetch('/api/inventory/facility-parts-finance' + path, {
            method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(data === undefined ? {} : { body: JSON.stringify(data) })
        });
        if (response.status === 401 || response.status === 403) { lock(); throw new Error('session-changed'); }
        await verify();
        if (!sameResponse(response)) { lock(); throw new Error('session-changed'); }
        const payload = await response.json().catch(() => null);
        await verify();
        if (!sameResponse(response)) { lock(); throw new Error('session-changed'); }
        if (!response.ok || !payload?.success) {
            const error = new Error(payload?.message || t('Finance request failed.', 'হিসাব অনুরোধ ব্যর্থ।'));
            error.status = response.status; throw error;
        }
        return payload.data;
    }
    function selectOptions(id, rows, placeholder) {
        const select = el(id), old = select.value;
        select.replaceChildren(new Option(placeholder, ''));
        for (const x of rows) select.add(new Option(x.label, String(x.id)));
        if ([...select.options].some(x => x.value === old)) select.value = old;
    }
    async function loadOptions() {
        if (!maker) return;
        const query = new URLSearchParams({ expenseSearch: el('fpfExpenseSearch').value.trim(),
            stockSearch: el('fpfStockSearch').value.trim() });
        const result = await api('/options?' + query);
        options = result;
        const accounts = result.accounts || [], periods = result.periods || [];
        selectOptions('fpfPeriod', periods, t('Select open period', 'খোলা হিসাবকাল বাছুন'));
        selectOptions('fpfReversePeriod', periods, t('Select open period', 'খোলা হিসাবকাল বাছুন'));
        selectOptions('fpfExpense', accounts.filter(x => x.type === 'Expense'), t('Select expense account', 'ব্যয় খাত বাছুন'));
        selectOptions('fpfStock', accounts.filter(x => x.type === 'Asset'), t('Select inventory asset account', 'স্টক সম্পদ খাত বাছুন'));
        if (result.moreAccountsAvailable)
            status(t('Showing first 100 matching accounts. Search by code/name to narrow.',
                'মিলে যাওয়া প্রথম ১০০টি হিসাব খাত দেখানো হয়েছে। কোড/নাম দিয়ে খুঁজুন।'));
    }
    const cell = value => { const td = document.createElement('td'); td.textContent = String(value ?? '—'); return td; };
    function button(label, fn, style = 'btn-outline-primary') {
        const b = document.createElement('button'); b.type = 'button';
        b.className = 'btn btn-sm ' + style; b.textContent = label;
        b.addEventListener('click', fn); return b;
    }
    function clearSelected() {
        ++sequence; selected = null; record = null;
        el('fpfDetails').classList.add('d-none');
    }
    async function list() {
        clearSelected();
        const gen = sequence;
        const query = new URLSearchParams({ search: el('fpfSearch').value.trim(),
            page: String(page), pageSize: '25' });
        const result = await api('/movements?' + query);
        if (gen !== sequence) return;
        pages = Math.max(1, Math.ceil(result.totalCount / 25));
        el('fpfPageNumber').textContent = t('Page ', 'পৃষ্ঠা ') + page + ' / ' + pages;
        el('fpfPrev').disabled = page <= 1;
        el('fpfNext').disabled = page >= pages;
        const body = el('fpfRows'); body.replaceChildren();
        for (const x of result.items || []) {
            const tr = document.createElement('tr');
            [x.movementNumber, x.facilityRequestId, x.stockCostBdt,
                x.stockState, x.financeState].forEach(v => tr.append(cell(v)));
            const action = document.createElement('td');
            action.append(button(t('Open', 'খুলুন'), () => choose(x)));
            tr.append(action); body.append(tr);
        }
        if (!(result.items || []).length) status(t('No permitted posted facility stock issues found.',
            'অনুমোদিত পোস্ট করা রক্ষণাবেক্ষণ স্টক ইস্যু পাওয়া যায়নি।'));
    }
    function render() {
        if (!selected || blocked) return;
        el('fpfDetails').classList.remove('d-none');
        el('fpfChosen').textContent = selected.movementNumber + ' / ' + t('Request #', 'অনুরোধ #') + selected.facilityRequestId;
        el('fpfEvidence').textContent = t('Stock cost BDT ', 'স্টকের মূল্য ৳') + selected.stockCostBdt +
            ' · ' + t('Stock ', 'স্টক ') + selected.stockState +
            ' · ' + t('Finance ', 'হিসাব ') + (record?.state || selected.financeState) +
            (record?.financeReversalReference ? ' · ' + t('Reversal ', 'প্রত্যাবর্তন ') +
                record.financeReversalReference : '');
        if (maker) {
            const showRecognition = !record || ['Rejected', 'Cancelled'].includes(record.state);
            el('fpfRecognitionPanel').classList.toggle('d-none', !showRecognition || selected.stockState !== 'Posted');
            el('fpfReversePanel').classList.toggle('d-none', record?.state !== 'Posted' ||
                record?.reversalState !== 'None' || selected.stockState !== 'Posted');
        }
        if (checker) {
            el('fpfReviewPanel').classList.toggle('d-none', record?.state !== 'Submitted');
            el('fpfReverseReviewPanel').classList.toggle('d-none', record?.reversalState !== 'Submitted');
        }
    }
    async function choose(candidate) {
        clearSelected(); selected = candidate;
        const gen = sequence;
        render();
        try {
            record = await api('/' + encodeURIComponent(candidate.movementReference));
            if (gen !== sequence) return;
        } catch (error) {
            if (gen !== sequence || blocked) return;
            if (error.status !== 404) { clearSelected(); status(error.message, true); return; }
            record = null;
        }
        if (gen === sequence) render();
    }
    function commandId(scope, payload) {
        const fingerprint = JSON.stringify(payload);
        const prior = pending.get(scope);
        if (prior && prior.fingerprint !== fingerprint)
            throw new Error(t('Previous request may have succeeded. Reload its status before editing.',
                'আগের অনুরোধ সফল হতে পারে। পরিবর্তনের আগে অবস্থা পুনরায় লোড করুন।'));
        if (prior) return prior.id;
        const id = crypto.randomUUID(); pending.set(scope, { fingerprint, id }); return id;
    }
    async function action(handler) {
        if (busy || blocked || !selected) return;
        busy = true;
        try { await handler(); status(t('Finance state updated.', 'হিসাবের অবস্থা হালনাগাদ হয়েছে।'));
            await choose(selected); }
        catch (error) { if (!blocked) status(error.message, true); }
        finally { busy = false; }
    }
    el('fpfSearchForm').addEventListener('submit', e => {
        e.preventDefault(); page = 1; list().catch(error => { if (!blocked) status(error.message, true); });
    });
    el('fpfPrev').addEventListener('click', () => {
        if (page > 1) { page--; list().catch(error => { if (!blocked) status(error.message, true); }); }
    });
    el('fpfNext').addEventListener('click', () => {
        if (page < pages) { page++; list().catch(error => { if (!blocked) status(error.message, true); }); }
    });
    if (maker) {
        el('fpfAccountsReload').addEventListener('click', () =>
            loadOptions().catch(error => { if (!blocked) status(error.message, true); }));
        el('fpfRecognitionForm').addEventListener('submit', e => {
            e.preventDefault(); action(async () => {
                const movement = selected.movementReference;
                const payload = { accountingPeriodId: Number(el('fpfPeriod').value),
                    journalDate: el('fpfDate').value, repairsExpenseAccountId: Number(el('fpfExpense').value),
                    inventoryAssetAccountId: Number(el('fpfStock').value), reason: el('fpfReason').value.trim() };
                const key = 'recognition:' + movement;
                await api('/' + encodeURIComponent(movement) + '/recognition', 'POST',
                    { ...payload, clientRequestId: commandId(key, payload) });
                pending.delete(key);
            });
        });
        el('fpfReverseForm').addEventListener('submit', e => {
            e.preventDefault(); action(async () => {
                if (!record?.journalReference) throw new Error(t('No posted Finance journal.', 'পোস্ট করা জার্নাল নেই।'));
                const payload = { accountingPeriodId: Number(el('fpfReversePeriod').value),
                    journalDate: el('fpfReverseDate').value, reason: el('fpfReverseReason').value.trim() };
                const key = 'reverse:' + record.journalReference;
                await api('/recognitions/' + encodeURIComponent(record.journalReference) + '/reversal', 'POST',
                    { ...payload, clientRequestId: commandId(key, payload) });
                pending.delete(key);
            });
        });
    }
    if (checker) {
        const review = approve => action(async () => {
            if (!record?.journalReference || !record.rowVersion) throw new Error('Missing journal version.');
            await api('/recognitions/' + encodeURIComponent(record.journalReference) + '/review', 'POST',
                { rowVersion: record.rowVersion, approve });
        });
        const reviewReverse = approve => action(async () => {
            if (!record?.financeReversalReference || !record.reversalRowVersion)
                throw new Error('Missing reversal journal version.');
            await api('/reversals/' + encodeURIComponent(record.financeReversalReference) + '/review', 'POST',
                { rowVersion: record.reversalRowVersion, approve });
        });
        el('fpfApprove').addEventListener('click', () => review(true));
        el('fpfReject').addEventListener('click', () => review(false));
        el('fpfReverseApprove').addEventListener('click', () => reviewReverse(true));
        el('fpfReverseReject').addEventListener('click', () => reviewReverse(false));
    }
    (async () => {
        if (!session.ref || !session.tenant) { lock(); return; }
        status(t('Choose a posted facility parts issue.', 'পোস্ট করা যন্ত্রাংশ ইস্যু বাছুন।'));
        try { if (maker) await loadOptions(); await list(); }
        catch (error) { if (!blocked) status(error.message, true); }
    })();
})();
