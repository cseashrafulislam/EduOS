(() => {
    'use strict';
    const root = document.getElementById('facilityCosts');
    if (!root) return;
    const bn = root.dataset.bn === 'true';
    const maker = root.dataset.maker === 'true';
    const checker = root.dataset.checker === 'true';
    const me = Number(root.dataset.userId);
    const el = id => document.getElementById(id);
    const t = (en, bd) => bn ? bd : en;
    const token = document.querySelector('meta[name="request-verification-token"]')?.content;
    const shield = el('eduosSessionShield');
    const session = {
        ref: document.body?.dataset.authSessionReference || '',
        tenant: document.body?.dataset.authTenantId || ''
    };
    const vendor = '/api/facilities/vendor-service-bills';
    const labour = '/api/facilities/labour-allocations';
    let locked = false, busy = false, billPage = 1, labourPage = 1;
    let billPages = 1, labourPages = 1, selected = null, currentDecision = null;
    let billGeneration = 0, labourGeneration = 0, vendorChoicesGeneration = 0, labourChoicesGeneration = 0;
    let agingGeneration = 0, paymentGeneration = 0, agingPage = 1, agingPages = 1;
    let paymentPage = 1, activePayment = null;
    const vendorSource = new Map(), payrollSource = new Map(), attempts = new Map();

    const sessionCurrent = () => !locked && !!session.ref && !!session.tenant &&
        !window.EduOSSessionTransitionPending?.() &&
        document.body?.dataset.authSessionReference === session.ref &&
        document.body?.dataset.authTenantId === session.tenant;
    function lock() {
        locked = true;
        ++billGeneration; ++labourGeneration;
        ++vendorChoicesGeneration; ++labourChoicesGeneration;
        ++agingGeneration; ++paymentGeneration;
        attempts.clear(); vendorSource.clear(); payrollSource.clear();
        selected = null; currentDecision = null;
        for (const target of root.querySelectorAll('input, select, button, textarea')) target.disabled = true;
        for (const id of ['fcVendorRows', 'fcLabourRows']) el(id).replaceChildren();
        el('fcDecisionPanel').classList.add('d-none');
        el('fcPaymentPanel').classList.add('d-none');
        notice(t('Institution session changed. Reload this page.',
            'প্রতিষ্ঠানের সেশন বদলেছে। পেজটি আবার লোড করুন।'));
    }
    const matches = response => sessionCurrent() &&
        /^[0-9A-F]{64}$/.test(session.ref) && /^[1-9][0-9]*$/.test(session.tenant) &&
        response.headers.get('X-EduOS-Session-Reference') === session.ref &&
        response.headers.get('X-EduOS-Tenant-Id') === session.tenant;
    async function verifySession() {
        if (!sessionCurrent() || !window.EduOSVerifySessionContext || !shield) {
            lock(); throw new Error('session-changed');
        }
        try { await window.EduOSVerifySessionContext(); }
        catch { lock(); throw new Error('session-changed'); }
        if (!sessionCurrent() || shield.hidden !== true) {
            lock(); throw new Error('session-changed');
        }
    }
    function notice(text, ok = false) {
        const box = el('fcNotice');
        box.className = 'alert alert-' + (ok ? 'success' : 'danger');
        box.textContent = text;
    }
    async function api(route, method = 'GET', payload) {
        if (!sessionCurrent()) { lock(); throw new Error('session-changed'); }
        if (method !== 'GET' && !token) throw new Error(t('Missing security token; reload.', 'নিরাপত্তা টোকেন নেই; পুনরায় লোড করুন।'));
        await verifySession();
        const headers = { Accept: 'application/json' };
        if (payload !== undefined) headers['Content-Type'] = 'application/json';
        if (method !== 'GET') headers.RequestVerificationToken = token;
        const response = await fetch(route, {
            method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(payload === undefined ? {} : { body: JSON.stringify(payload) })
        });
        if (response.status === 401 || response.status === 403) {
            lock(); throw new Error('session-changed');
        }
        await verifySession();
        if (!matches(response)) { lock(); throw new Error('session-changed'); }
        const body = await response.json().catch(() => null);
        await verifySession();
        if (!matches(response)) { lock(); throw new Error('session-changed'); }
        if (!response.ok || !body?.success) {
            const err = new Error(body?.message || t('Request failed.', 'অনুরোধটি ব্যর্থ হয়েছে।'));
            err.status = response.status;
            throw err;
        }
        return body.data;
    }
    function opId(operation, payload) {
        const signature = operation + '|' + JSON.stringify(payload);
        if (!attempts.has(signature)) attempts.set(signature, crypto.randomUUID());
        return { id: attempts.get(signature), signature };
    }
    const money = n => typeof n === 'number'
        ? new Intl.NumberFormat(bn ? 'bn-BD' : 'en-BD', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(n)
        : String(n ?? '—');
    function cell(text) {
        const td = document.createElement('td');
        td.textContent = text == null || text === '' ? '—' : String(text);
        return td;
    }
    function button(label, callback, disabled = false) {
        const b = document.createElement('button');
        b.type = 'button'; b.className = 'btn btn-outline-primary btn-sm';
        b.textContent = label; b.disabled = disabled;
        b.addEventListener('click', callback);
        return b;
    }
    const campus = () => {
        const box = el('fcCampus');
        return box ? box.value.trim() : '';
    };
    const campusQuery = () => {
        const value = campus();
        if (!value) return '';
        if (!/^[1-9][0-9]*$/.test(value)) throw new Error(t('Invalid campus ID.', 'ক্যাম্পাস আইডি ভুল।'));
        return '&campusId=' + encodeURIComponent(value);
    };
    function pagination(kind, page, total, count) {
        el(kind + 'Page').textContent = t('Page ', 'পৃষ্ঠা ') + page +
            t(' / ', ' / ') + total + ' (' + count + ')';
        el(kind + 'Prev').disabled = page <= 1;
        el(kind + 'Next').disabled = page >= total;
    }
    async function listBills() {
        const generation = ++billGeneration;
        try {
            const data = await api(vendor + '?page=' + billPage + '&pageSize=15' +
                campusQuery() + '&state=' + encodeURIComponent(el('fcVendorState').value));
            if (generation !== billGeneration || !sessionCurrent()) return;
            const list = el('fcVendorRows'); list.replaceChildren();
            const rows = data.items || [];
            billPages = Math.max(1, Math.ceil((data.totalCount || 0) / 15));
            for (const row of rows) {
                const tr = document.createElement('tr');
                const label = (row.supplierName || t('Supplier', 'সরবরাহকারী')) +
                    ' / ' + row.supplierInvoiceNumber;
                const summary = document.createElement('small');
                summary.className = 'd-block text-muted';
                summary.textContent = row.requestSummary || '';
                const first = cell(label); first.appendChild(summary);
                tr.append(first, cell(money(row.amountBdt)), cell(row.stateCode));
                const action = cell('');
                action.appendChild(button(t('Manage', 'পরিচালনা'), () => choose('vendor', row)));
                tr.appendChild(action); list.appendChild(tr);
            }
            if (!rows.length) {
                const tr = document.createElement('tr'); const td = cell(t('No invoices found.', 'কোনো বিল পাওয়া যায়নি।'));
                td.colSpan = 4; tr.appendChild(td); list.appendChild(tr);
            }
            pagination('fcVendor', billPage, billPages, data.totalCount || 0);
        } catch (err) { if (!locked) notice(err.message); }
    }
    async function listLabour() {
        const generation = ++labourGeneration;
        try {
            const data = await api(labour + '?page=' + labourPage + '&pageSize=15' + campusQuery());
            if (generation !== labourGeneration || !sessionCurrent()) return;
            const list = el('fcLabourRows'); list.replaceChildren();
            const rows = data.items || [];
            labourPages = Math.max(1, Math.ceil((data.totalCount || 0) / 15));
            for (const row of rows) {
                const tr = document.createElement('tr');
                const first = cell(row.employeeName || t('Employee', 'কর্মী'));
                const description = document.createElement('small');
                description.className = 'd-block text-muted';
                description.textContent = (row.workOrderSummary || '') + ' / ' + (row.payrollRunNumber || '');
                first.appendChild(description);
                tr.append(first, cell(money(row.allocatedAmountBdt)),
                    cell(row.stateCode + (row.payrollSourceReversed ? ' / PAYROLL REVERSED' : '')));
                const action = cell('');
                action.appendChild(button(t('Manage', 'পরিচালনা'), () => choose('labour', row)));
                tr.appendChild(action); list.appendChild(tr);
            }
            if (!rows.length) {
                const tr = document.createElement('tr'); const td = cell(t('No labour allocations.', 'কোনো শ্রম বরাদ্দ নেই।'));
                td.colSpan = 4; tr.appendChild(td); list.appendChild(tr);
            }
            pagination('fcLabour', labourPage, labourPages, data.totalCount || 0);
        } catch (err) { if (!locked) notice(err.message); }
    }
    function fillSelect(id, options, valueKey, labelFn) {
        const node = el(id); node.replaceChildren();
        node.appendChild(new Option(t('Choose…', 'নির্বাচন করুন…'), ''));
        for (const item of options) node.appendChild(new Option(labelFn(item), String(item[valueKey])));
    }
    async function findVendor() {
        const generation = ++vendorChoicesGeneration;
        try {
            const query = encodeURIComponent(el('fcVendorSearch').value.trim());
            const data = await api(vendor + '/verified-work?page=1&pageSize=20&search=' + query + campusQuery());
            if (generation !== vendorChoicesGeneration || !sessionCurrent()) return;
            vendorSource.clear();
            for (const item of data.items || []) vendorSource.set(String(item.workOrderId), item);
            fillSelect('fcVendorSource', data.items || [], 'workOrderId',
                x => x.requestSummary + ' — ' + x.supplierName + ' (#' + x.workOrderId + ')');
            notice(t('Verified source list refreshed.', 'যাচাইকৃত কাজের তালিকা এসেছে।'), true);
        } catch (err) { if (!locked) notice(err.message); }
    }
    async function findLabour() {
        const generation = ++labourChoicesGeneration;
        try {
            const query = encodeURIComponent(el('fcLabourSearch').value.trim());
            const data = await api(labour + '/eligible-sources?page=1&pageSize=20&search=' + query + campusQuery());
            if (generation !== labourChoicesGeneration || !sessionCurrent()) return;
            payrollSource.clear();
            for (const item of data.items || [])
                payrollSource.set(item.workOrderId + ':' + item.payrollEmployeeId, item);
            const node = el('fcLabourSource'); node.replaceChildren();
            node.appendChild(new Option(t('Choose…', 'নির্বাচন করুন…'), ''));
            for (const item of data.items || []) {
                const key = item.workOrderId + ':' + item.payrollEmployeeId;
                const label = item.workOrderSummary + ' / ' + item.employeeName +
                    ' / ' + item.payrollRunNumber + ' — BDT ' + money(item.payrollGrossBdt);
                node.appendChild(new Option(label, key));
            }
            notice(t('Payroll source list refreshed.', 'Payroll উৎসের তালিকা এসেছে।'), true);
        } catch (err) { if (!locked) notice(err.message); }
    }
    async function mutate(action, path, requestBody, callback) {
        if (busy) return;
        busy = true;
        const pending = opId(action, requestBody);
        try {
            const result = await api(path, 'POST', { ...requestBody, clientRequestId: pending.id });
            attempts.delete(pending.signature);
            await callback?.(result);
            notice(t('Saved successfully.', 'সফলভাবে সংরক্ষিত হয়েছে।'), true);
        } catch (err) { if (!locked) notice(err.message); }
        finally { busy = false; }
    }
    function hideDecision() {
        ++billGeneration; ++labourGeneration;
        currentDecision = null;
        el('fcDecisionPanel').classList.add('d-none');
        el('fcDecisionForm').reset();
    }
    async function choose(kind, row) {
        if (busy) return;
        try {
            const result = await api((kind === 'vendor' ? vendor : labour) + '/' +
                encodeURIComponent(row.reference));
            if (!sessionCurrent()) return;
            selected = { kind, data: result };
            if (kind === 'vendor') await loadPaymentPanel(result);
            else el('fcPaymentPanel').classList.add('d-none');
            const opts = [];
            if (kind === 'vendor') {
                if (maker && result.stateCode === 'Draft' && result.preparedByUserId === me)
                    opts.push(['submit', t('Submit bill', 'বিল জমা দিন')]);
                if (checker && result.stateCode === 'Submitted' && result.preparedByUserId !== me)
                    opts.push(['post', t('Approve and post', 'অনুমোদন ও পোস্ট')],
                        ['reject', t('Reject bill', 'বিল প্রত্যাখ্যান')]);
                if (maker && result.stateCode === 'Posted' && !result.reversalReference)
                    opts.push(['request-reversal', t('Request full reversal', 'পূর্ণ রিভার্সাল অনুরোধ')]);
                if (checker && result.reversalState === 'Submitted')
                    opts.push(['approve-reversal', t('Approve contra journal', 'বিপরীত জার্নাল অনুমোদন')],
                        ['reject-reversal', t('Reject reversal', 'রিভার্সাল প্রত্যাখ্যান')]);
            } else if (checker && result.stateCode === 'Submitted' &&
                result.requestedByUserId !== me && !result.payrollSourceReversed) {
                opts.push(['approve-labour', t('Approve allocation', 'বরাদ্দ অনুমোদন')],
                    ['reject-labour', t('Reject allocation', 'বরাদ্দ প্রত্যাখ্যান')]);
            }
            if (!opts.length) { notice(t('Record is read-only for this role/state.', 'এই অবস্থায় তথ্য শুধু দেখা যাবে।'), true); return; }
            const [first] = opts;
            await showDecision(first[0], result, opts);
        } catch (err) { if (!locked) notice(err.message); }
    }
    async function showDecision(action, row, alternatives) {
        currentDecision = { action, row };
        el('fcDecisionPanel').classList.remove('d-none');
        el('fcDecisionForm').reset();
        const human = alternatives.map(x => x[1]).join(' / ');
        el('fcDecisionTitle').textContent = human;
        el('fcDecisionSummary').textContent = (row.requestSummary || row.workOrderSummary || '') +
            ' / ' + (row.supplierName || row.employeeName || '') +
            ' — ' + row.stateCode + ' — BDT ' + money(row.amountBdt ?? row.allocatedAmountBdt);
        const selectedActions = alternatives;
        let menu = document.getElementById('fcDecisionAction');
        if (!menu) {
            const wrapper = document.createElement('div');
            wrapper.className = 'col-12';
            const label = document.createElement('label');
            label.textContent = t('Decision', 'সিদ্ধান্ত'); label.htmlFor = 'fcDecisionAction';
            label.className = 'form-label';
            menu = document.createElement('select');
            menu.id = 'fcDecisionAction'; menu.className = 'form-select';
            wrapper.append(label, menu);
            el('fcDecisionForm').prepend(wrapper);
        }
        menu.replaceChildren();
        for (const entry of selectedActions) menu.appendChild(new Option(entry[1], entry[0]));
        menu.value = action;
        const refresh = async () => {
            if (!currentDecision) return;
            currentDecision.action = menu.value;
            const needsAccounts = menu.value === 'post';
            const needsPeriod = needsAccounts || menu.value === 'request-reversal';
            for (const id of ['fcPeriodBox', 'fcExpenseBox', 'fcPayableBox']) el(id).classList.toggle('d-none',
                id === 'fcPeriodBox' ? !needsPeriod : !needsAccounts);
            el('fcReversalDateBox').classList.toggle('d-none', menu.value !== 'request-reversal');
            el('fcPeriod').required = needsPeriod;
            el('fcExpense').required = needsAccounts; el('fcPayable').required = needsAccounts;
            el('fcReversalDate').required = menu.value === 'request-reversal';
            if (needsPeriod) {
                const choices = await api(vendor + '/posting-options?campusId=' + row.campusId);
                if (currentDecision?.row?.reference !== row.reference || menu.value !== currentDecision.action) return;
                fillSelect('fcPeriod', choices.periods || [], 'accountingPeriodId',
                    p => p.name + ' (' + p.startDate + ' – ' + p.endDate + ')');
                fillSelect('fcExpense', choices.expenseAccounts || [], 'accountId',
                    a => a.accountCode + ' / ' + a.accountName);
                fillSelect('fcPayable', choices.payableAccounts || [], 'accountId',
                    a => a.accountCode + ' / ' + a.accountName);
            }
        };
        menu.onchange = () => refresh().catch(e => { if (!locked) notice(e.message); });
        await refresh();
        el('fcDecisionPanel').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
    async function decide(event) {
        event.preventDefault();
        if (!currentDecision || busy || !el('fcDecisionForm').reportValidity()) return;
        const { action, row } = currentDecision;
        const reason = el('fcDecisionReason').value.trim();
        const finish = async result => {
            hideDecision();
            await Promise.all([listBills(), listLabour()]);
        el('fcAgingDate').value = new Date().toISOString().slice(0, 10);
        await listAging();
            if (result?.reference) selected = { kind: action.includes('labour') ? 'labour' : 'vendor', data: result };
        };
        let route, payload = { reason };
        if (action === 'submit') {
            route = vendor + '/' + row.reference + '/submit'; payload = { rowVersion: row.rowVersion };
        } else if (action === 'post' || action === 'reject') {
            route = vendor + '/' + row.reference + '/review';
            payload = { ...payload, rowVersion: row.rowVersion, approve: action === 'post',
                accountingPeriodId: action === 'post' ? Number(el('fcPeriod').value) : 0,
                repairsExpenseAccountId: action === 'post' ? Number(el('fcExpense').value) : 0,
                supplierPayableAccountId: action === 'post' ? Number(el('fcPayable').value) : 0 };
        } else if (action === 'request-reversal') {
            route = vendor + '/' + row.reference + '/reversal';
            payload = { ...payload, rowVersion: row.rowVersion,
                accountingPeriodId: Number(el('fcPeriod').value),
                reversalDate: el('fcReversalDate').value };
        } else if (action === 'approve-reversal' || action === 'reject-reversal') {
            route = vendor + '/' + row.reference + '/reversal/review';
            payload = { ...payload, rowVersion: row.reversalRowVersion, approve: action === 'approve-reversal' };
        } else if (action === 'approve-labour' || action === 'reject-labour') {
            route = labour + '/' + row.reference + '/review';
            payload = { ...payload, rowVersion: row.rowVersion, approve: action === 'approve-labour' };
        } else return;
        await mutate(action, route, payload, finish);
    }

    async function listAging() {
        const generation = ++agingGeneration;
        const date = el('fcAgingDate').value;
        if (!date) return;
        try {
            const data = await api(vendor + '/supplier-aging?asOf=' +
                encodeURIComponent(date) + '&page=' + agingPage + '&pageSize=15' +
                campusQuery());
            if (generation !== agingGeneration || !sessionCurrent()) return;
            const result = data.page;
            const tbody = el('fcAgingRows'); tbody.replaceChildren();
            for (const row of result.items || []) {
                const tr = document.createElement('tr');
                const first = cell(row.invoiceSource + ' / ' + row.supplierInvoiceNumber);
                const note = document.createElement('small');
                note.className = 'd-block text-muted';
                note.textContent = t('Supplier #', 'সরবরাহকারী #') + row.supplierId;
                first.appendChild(note);
                tr.append(first, cell(money(row.invoiceAmount)),
                    cell(money(row.netPostedPayments)), cell(money(row.openBalance)),
                    cell(row.daysOverdue));
                tbody.appendChild(tr);
            }
            const total = Number(result.totalCount || 0);
            agingPages = Math.max(1, Math.ceil(total / 15));
            el('fcAgingPage').textContent = t('Page ', 'পৃষ্ঠা ') +
                agingPage + '/' + agingPages + ' (' + total + ')';
            el('fcAgingPrev').disabled = agingPage <= 1;
            el('fcAgingNext').disabled = agingPage >= agingPages;
            el('fcAgingSummary').textContent =
                t('This page only — outstanding BDT ', 'শুধু এই পৃষ্ঠার বকেয়া টাকা ') +
                money(data.outstandingOnPage) + ' · ' +
                t('Current ', 'চলতি ') + money(data.currentOnPage) + ' · 1–30: ' +
                money(data.days1To30OnPage) + ' · 31–60: ' +
                money(data.days31To60OnPage) + ' · 61–90: ' +
                money(data.days61To90OnPage) + ' · >90: ' +
                money(data.over90DaysOnPage);
        } catch (error) { if (!locked) notice(error.message); }
    }
    async function loadPaymentPanel(bill) {
        ++paymentGeneration;
        activePayment = null;
        el('fcPaymentActionPanel').classList.add('d-none');
        if (!bill || bill.stateCode !== 'Posted') {
            el('fcPaymentPanel').classList.add('d-none');
            return;
        }
        const generation = paymentGeneration;
        el('fcPaymentPanel').classList.remove('d-none');
        el('fcPaymentBillInfo').textContent = bill.supplierName + ' / ' +
            bill.supplierInvoiceNumber + ' — BDT ' + money(bill.amountBdt);
        try {
            const [options, data] = await Promise.all([
                api(vendor + '/' + encodeURIComponent(bill.reference) + '/payment-options'),
                api(vendor + '/' + encodeURIComponent(bill.reference) +
                    '/payments?page=' + paymentPage + '&pageSize=25')
            ]);
            if (generation !== paymentGeneration || !sessionCurrent() ||
                selected?.data?.reference !== bill.reference) return;
            el('fcPaymentSummary').textContent =
                t('Reserved BDT ', 'সংরক্ষিত টাকা ') + money(options.reservedAmount) +
                ' · ' + t('Paid BDT ', 'পরিশোধিত টাকা ') +
                money(options.postedNetPayments) + ' · ' +
                t('Available BDT ', 'বাকি টাকা ') + money(options.availableAmount);
            if (maker) {
                fillSelect('fcPaymentBank', options.banks || [], 'bankAccountId', x => x.name);
                fillSelect('fcPaymentPeriod', options.periods || [], 'accountingPeriodId',
                    x => x.name + ' (' + x.startDate + ' – ' + x.endDate + ')');
                el('fcPaymentAmount').max = String(options.availableAmount);
                el('fcPaymentCreate').classList.toggle('d-none', options.availableAmount <= 0);
            }
            fillSelect('fcPaymentActionPeriod', options.periods || [], 'accountingPeriodId',
                x => x.name + ' (' + x.startDate + ' – ' + x.endDate + ')');
            const body = el('fcPaymentsRows'); body.replaceChildren();
            for (const row of data.items || []) {
                const tr = document.createElement('tr');
                tr.append(cell(row.paymentDate), cell(money(row.amount)),
                    cell(row.state + (row.bankStatementCleared ?
                        t(' / bank cleared', ' / ব্যাংকে মিলেছে') : '')),
                    cell(row.bankTransactionReference || '—'));
                const action = cell('');
                action.appendChild(button(t('Review / action', 'যাচাই / অ্যাকশন'),
                    () => openPaymentAction(row)));
                tr.appendChild(action); body.appendChild(tr);
            }
            if (!data.items?.length) {
                const tr = document.createElement('tr');
                const empty = cell(t('No payment instructions yet.', 'এখনো পেমেন্ট নির্দেশ নেই।'));
                empty.colSpan = 5; tr.appendChild(empty); body.appendChild(tr);
            }
        } catch (error) { if (!locked) notice(error.message); }
    }
    function openPaymentAction(row) {
        if (!selected?.data || selected.kind !== 'vendor') return;
        const choices = [];
        if (checker && row.state === 'Submitted' &&
            row.makerId !== me && selected.data.preparedByUserId !== me)
            choices.push(['approve', t('Approve unposted instruction', 'অপোস্টেড নির্দেশ অনুমোদন')],
                ['reject', t('Reject instruction', 'নির্দেশ বাতিল')]);
        if (maker && row.state === 'Approved' &&
            row.makerId === me && row.reviewerId !== me)
            choices.push(['post', t('Confirm actual bank payment', 'ব্যাংক লেনদেন নিশ্চিত করুন')]);
        if (maker && row.state === 'Posted' && !row.reversalReference &&
            !row.bankStatementCleared)
            choices.push(['reverse', t('Request recorded bank refund', 'ব্যাংক ফেরতের অনুরোধ')]);
        if (checker && row.reversalState === 'Submitted' &&
            row.makerId !== me && selected.data.preparedByUserId !== me)
            choices.push(['approve-contra', t('Approve bank refund contra', 'ব্যাংক ফেরতের বিপরীত জার্নাল অনুমোদন')],
                ['reject-contra', t('Reject bank refund contra', 'ব্যাংক ফেরত প্রত্যাখ্যান')]);
        if (!choices.length) {
            notice(t('No action available for this payment or role.', 'এই পেমেন্টে আপনার কোনো অ্যাকশন নেই।'), true);
            return;
        }
        activePayment = row;
        const menu = el('fcPaymentActionCode'); menu.replaceChildren();
        for (const [value, label] of choices) menu.appendChild(new Option(label, value));
        el('fcPaymentActionPanel').classList.remove('d-none');
        el('fcPaymentActionTitle').textContent =
            t('Payment instruction ', 'পরিশোধ নির্দেশ ') + row.reference;
        el('fcPaymentActionForm').reset();
        menu.value = choices[0][0];
        const update = () => {
            const choice = menu.value;
            const needsDate = choice === 'post' || choice === 'reverse';
            const needsPeriod = choice === 'reverse';
            const needsReference = needsDate;
            el('fcPaymentActionDate').required = needsDate;
            el('fcPaymentActionPeriod').required = needsPeriod;
            el('fcPaymentActionReference').required = needsReference;
            el('fcPaymentActionReason').required = choice !== 'post';
        };
        menu.onchange = update; update();
    }
    async function submitPaymentAction(event) {
        event.preventDefault();
        if (!selected?.data || !activePayment || !el('fcPaymentActionForm').reportValidity()) return;
        const bill = selected.data, payment = activePayment;
        const action = el('fcPaymentActionCode').value;
        const reason = el('fcPaymentActionReason').value.trim();
        const route = vendor + '/' + bill.reference + '/payments/' + payment.reference;
        const reference = el('fcPaymentActionReference').value.trim();
        let dest, body;
        if (action === 'approve' || action === 'reject') {
            dest = route + '/review';
            body = { rowVersion: payment.rowVersion, approve: action === 'approve', reason };
        } else if (action === 'post') {
            dest = route + '/post';
            body = { rowVersion: payment.rowVersion,
                executedDate: el('fcPaymentActionDate').value, bankTransactionReference: reference };
        } else if (action === 'reverse') {
            dest = route + '/reversal';
            body = { rowVersion: payment.rowVersion, accountingPeriodId: Number(el('fcPaymentActionPeriod').value),
                reversalDate: el('fcPaymentActionDate').value,
                bankReversalReference: reference, reason };
        } else if (action === 'approve-contra' || action === 'reject-contra') {
            dest = route + '/reversal/review';
            body = { rowVersion: payment.reversalRowVersion,
                approve: action === 'approve-contra', reason };
        } else return;
        await mutate('payment-' + action, dest, body, async () => {
            el('fcPaymentActionPanel').classList.add('d-none');
            await Promise.all([loadPaymentPanel(bill), listAging(), listBills()]);
        });
    }
    async function init() {
        if (!sessionCurrent()) { lock(); return; }
        el('fcAgingForm').addEventListener('submit', e => {
            e.preventDefault(); agingPage = 1; listAging();
        });
        el('fcAgingPrev').addEventListener('click', () => {
            if (agingPage > 1) { --agingPage; listAging(); }
        });
        el('fcAgingNext').addEventListener('click', () => {
            if (agingPage < agingPages) { ++agingPage; listAging(); }
        });
        el('fcPaymentActionCancel').addEventListener('click',
            () => el('fcPaymentActionPanel').classList.add('d-none'));
        el('fcPaymentActionForm').addEventListener('submit', submitPaymentAction);
        if (maker)
            el('fcPaymentCreate').addEventListener('submit', async e => {
                e.preventDefault();
                if (!selected?.data || !e.currentTarget.reportValidity()) return;
                const bill = selected.data;
                const body = { billRowVersion: bill.rowVersion,
                    bankAccountId: Number(el('fcPaymentBank').value),
                    accountingPeriodId: Number(el('fcPaymentPeriod').value),
                    plannedDate: el('fcPaymentDate').value,
                    amount: Number(el('fcPaymentAmount').value),
                    reason: el('fcPaymentReason').value.trim() };
                await mutate('create-payment', vendor + '/' + bill.reference + '/payments',
                    body, async () => {
                        el('fcPaymentCreate').reset();
                        await Promise.all([loadPaymentPanel(bill), listAging()]);
                    });
            });
        el('fcDecisionCancel').addEventListener('click', hideDecision);
        el('fcDecisionForm').addEventListener('submit', decide);
        el('fcVendorFilter').addEventListener('submit', e => { e.preventDefault(); billPage = 1; listBills(); });
        el('fcLabourRefresh').addEventListener('click', () => { labourPage = 1; listLabour(); });
        el('fcVendorPrev').addEventListener('click', () => { if (billPage > 1) { --billPage; listBills(); } });
        el('fcVendorNext').addEventListener('click', () => { if (billPage < billPages) { ++billPage; listBills(); } });
        el('fcLabourPrev').addEventListener('click', () => { if (labourPage > 1) { --labourPage; listLabour(); } });
        el('fcLabourNext').addEventListener('click', () => { if (labourPage < labourPages) { ++labourPage; listLabour(); } });
        if (maker) {
            el('fcFindVendor').addEventListener('click', findVendor);
            el('fcFindLabour').addEventListener('click', findLabour);
            el('fcVendorCreate').addEventListener('submit', async e => {
                e.preventDefault();
                if (!e.currentTarget.reportValidity()) return;
                const source = vendorSource.get(el('fcVendorSource').value);
                if (!source) { notice(t('Select verified work.', 'যাচাইকৃত কাজ নির্বাচন করুন।')); return; }
                const request = {
                    facilityWorkOrderId: source.workOrderId,
                    supplierInvoiceNumber: el('fcVendorNumber').value.trim(),
                    amountBdt: Number(el('fcVendorAmount').value),
                    invoiceDate: el('fcVendorDate').value,
                    dueDate: el('fcVendorDue').value,
                    reason: el('fcVendorReason').value.trim()
                };
                await mutate('create-vendor', vendor, request, async () => {
                    el('fcVendorCreate').reset(); vendorSource.clear();
                    el('fcVendorSource').replaceChildren();
                    await listBills();
                });
            });
            el('fcLabourCreate').addEventListener('submit', async e => {
                e.preventDefault();
                if (!e.currentTarget.reportValidity()) return;
                const source = payrollSource.get(el('fcLabourSource').value);
                if (!source) { notice(t('Select a posted payroll source.', 'পোস্ট করা Payroll উৎস নির্বাচন করুন।')); return; }
                const request = {
                    facilityWorkOrderId: source.workOrderId,
                    payrollEmployeeId: source.payrollEmployeeId,
                    actualWorkMinutes: Number(el('fcLabourMinutes').value),
                    evidence: el('fcLabourEvidence').value.trim()
                };
                await mutate('create-labour', labour, request, async () => {
                    el('fcLabourCreate').reset(); payrollSource.clear();
                    el('fcLabourSource').replaceChildren();
                    await listLabour();
                });
            });
        }
        await Promise.all([listBills(), listLabour()]);
        notice(t('Facilities Finance data is scoped to your active institution.',
            'রক্ষণাবেক্ষণ ব্যয়ের তথ্য বর্তমান প্রতিষ্ঠানের অনুমতি অনুযায়ী দেখানো হচ্ছে।'), true);
    }
    init().catch(error => { if (!locked) notice(error.message); });
})();
