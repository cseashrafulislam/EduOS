(() => {
    'use strict';
    const root = document.getElementById('paymentReconciliationApp');
    if (!root) return;
    const get = id => document.getElementById(id);
    const body = get('reconciliationRows');
    const status = get('reconciliationStatus');
    const pageLabel = get('reconciliationPage');
    const previous = get('reconciliationPrevious');
    const next = get('reconciliationNext');
    let page = 1;
    let total = 0;
    let requestId = 0;
    const pageSize = 25;

    const cell = (row, value, detail) => {
        const td = document.createElement('td');
        const primary = document.createElement('div');
        primary.textContent = String(value ?? '—');
        td.append(primary);
        if (detail != null && detail !== '') {
            const small = document.createElement('small');
            small.className = 'text-muted';
            small.textContent = String(detail);
            td.append(small);
        }
        row.append(td);
    };
    const money = (amount, currency) => Number(amount || 0).toFixed(2) + ' ' + (currency || 'BDT');
    const date = value => {
        if (!value) return '—';
        const parsed = new Date(value);
        return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString();
    };
    function render(items) {
        body.replaceChildren();
        for (const item of items) {
            const tr = document.createElement('tr');
            const approved = Number(item.paymentState) === 3;
            cell(tr, item.institutionName, item.institutionCode + ' · ' + item.invoiceNumber);
            cell(tr, approved ? 'Approved' : 'Rejected', date(item.verifiedAt));
            cell(tr, money(item.amount, item.currencyCode), item.transactionId);
            cell(tr, 'Paid: ' + money(item.invoicePaidAmount, item.currencyCode),
                'Due: ' + money(item.invoiceDueAmount, item.currencyCode) +
                ' · Invoice state: ' + item.invoiceState);
            cell(tr, date(item.periodStartsAt), 'Ends: ' + date(item.periodEndsAt) +
                ' · Subscription state: ' + item.subscriptionState);
            cell(tr, item.verificationNote || '—', 'Reviewer ID: ' + (item.verifiedByUserId ?? '—'));
            body.append(tr);
        }
        if (!items.length) {
            const tr = document.createElement('tr');
            const td = document.createElement('td');
            td.colSpan = 6;
            td.className = 'text-muted py-4 text-center';
            td.textContent = 'No verified bank-transfer decisions match these filters.';
            tr.append(td);
            body.append(tr);
        }
        const pages = Math.max(1, Math.ceil(total / pageSize));
        pageLabel.textContent = 'Page ' + page + ' of ' + pages + ' · ' + total + ' decisions';
        previous.disabled = page <= 1;
        next.disabled = page >= pages;
    }
    async function load() {
        const current = ++requestId;
        status.textContent = 'Loading verification history…';
        previous.disabled = true;
        next.disabled = true;
        const query = new URLSearchParams({
            page: String(page), pageSize: String(pageSize),
            search: get('reconciliationSearch').value.trim(),
            outcome: get('reconciliationOutcome').value
        });
        try {
            const response = await fetch('/api/subscription-payment/admin/reconciliation-history?' + query, {
                credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (current !== requestId) return;
            if (!response.ok || payload?.success !== true)
                throw new Error(payload?.message || 'Unable to load payment history.');
            total = Number(payload.data?.totalCount || 0);
            const pages = Math.max(1, Math.ceil(total / pageSize));
            if (page > pages) { page = pages; await load(); return; }
            render(Array.isArray(payload.data?.items) ? payload.data.items : []);
            status.textContent = 'Verified bank transfers only; no payment or subscription state is changed here.';
        } catch (error) {
            if (current !== requestId) return;
            total = 0;
            render([]);
            status.textContent = error.message || 'Unable to load verification history.';
            status.className = 'small text-danger my-3';
        }
    }
    get('reconciliationFilters').addEventListener('submit', event => {
        event.preventDefault();
        page = 1;
        status.className = 'small text-muted my-3';
        load();
    });
    previous.addEventListener('click', () => { if (page > 1) { page--; load(); } });
    next.addEventListener('click', () => {
        if (page < Math.ceil(total / pageSize)) { page++; load(); }
    });
    load();

    // Recovery is intentionally a separate, SuperAdmin-only panel. Never trust
    // client-provided payment outcomes; the server verifies AamarPay itself.
    const gatewayRows = get('gatewayRecoveryRows');
    const gatewayStatus = get('gatewayRecoveryStatus');
    const gatewayPageLabel = get('gatewayRecoveryPage');
    const gatewayPrevious = get('gatewayRecoveryPrevious');
    const gatewayNext = get('gatewayRecoveryNext');
    let gatewayPage = 1;
    let gatewayTotal = 0;
    let gatewayRequestId = 0;
    let gatewayBusy = false;

    function gatewayRender(items) {
        gatewayRows.replaceChildren();
        for (const item of items) {
            const tr = document.createElement('tr');
            cell(tr, item.institutionName, item.institutionCode + ' · ' + item.invoiceNumber);
            cell(tr, item.transactionId, 'Payment ID: ' + item.paymentId);
            cell(tr, money(item.amount, item.currencyCode), 'Invoice due: ' + money(item.invoiceDueAmount, item.currencyCode));
            cell(tr, date(item.initiatedAt), 'Gateway: AamarPay');
            cell(tr, item.exceptionCategory || 'Awaiting', String(item.ageHours ?? 0) +
                ' hours · ' + (item.exceptionReason || 'Provider status is not yet verified.'));
            const td = document.createElement('td');
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'btn btn-outline-primary btn-sm';
            button.textContent = 'Verify with gateway';
            button.dataset.blocked = String(item.canReconcile === false);
            button.disabled = gatewayBusy || item.canReconcile === false;
            if (item.canReconcile === false)
                button.title = 'Invoice cannot accept settlement; investigate the blocked exception.';
            button.addEventListener('click', () => gatewayReconcile(item.paymentId, item.rowVersion));
            td.append(button);
            tr.append(td);
            gatewayRows.append(tr);
        }
        if (!items.length) {
            const tr = document.createElement('tr');
            const td = document.createElement('td');
            td.colSpan = 6;
            td.className = 'text-center text-muted py-3';
            td.textContent = 'No eligible unreconciled gateway payments.';
            tr.append(td);
            gatewayRows.append(tr);
        }
        const pages = Math.max(1, Math.ceil(gatewayTotal / pageSize));
        gatewayPageLabel.textContent = 'Page ' + gatewayPage + ' of ' + pages + ' · ' + gatewayTotal + ' pending';
        gatewayPrevious.disabled = gatewayBusy || gatewayPage <= 1;
        gatewayNext.disabled = gatewayBusy || gatewayPage >= pages;
    }
    async function gatewayLoad() {
        const current = ++gatewayRequestId;
        gatewayStatus.textContent = 'Loading pending gateway transactions…';
        gatewayStatus.className = 'small text-muted my-3';
        gatewayPrevious.disabled = true;
        gatewayNext.disabled = true;
        const category = get('gatewayRecoveryCategory').value;
        const query = new URLSearchParams({
            page: String(gatewayPage), pageSize: String(pageSize),
            search: get('gatewayRecoverySearch').value.trim()
        });
        const endpoint = category === 'All'
            ? '/api/subscription-payment/admin/gateway-pending?'
            : '/api/subscription-payment/admin/gateway-exceptions?';
        if (category !== 'All') query.set('category', category);
        try {
            const response = await fetch(endpoint + query, {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (current !== gatewayRequestId) return;
            if (!response.ok || payload?.success !== true)
                throw new Error(payload?.message || 'Unable to load gateway payments.');
            gatewayTotal = Number(payload.data?.totalCount || 0);
            const pages = Math.max(1, Math.ceil(gatewayTotal / pageSize));
            if (gatewayPage > pages) { gatewayPage = pages; await gatewayLoad(); return; }
            gatewayRender(Array.isArray(payload.data?.items) ? payload.data.items : []);
            gatewayStatus.textContent = 'Exception labels are diagnostic only; only AamarPay can confirm settlement.';
        } catch (error) {
            if (current !== gatewayRequestId) return;
            gatewayTotal = 0;
            gatewayRender([]);
            gatewayStatus.textContent = error.message || 'Unable to load gateway payments.';
            gatewayStatus.className = 'small text-danger my-3';
        }
    }
    async function gatewayReconcile(paymentId, rowVersion) {
        if (gatewayBusy || !rowVersion) return;
        gatewayBusy = true;
        gatewayRows.querySelectorAll('button').forEach(button => { button.disabled = true; });
        gatewayPrevious.disabled = true;
        gatewayNext.disabled = true;
        gatewayStatus.className = 'small text-muted my-3';
        gatewayStatus.textContent = 'Checking transaction with AamarPay…';
        try {
            const token = get('gatewayRecoveryAntiforgery')
                .querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (!token) throw new Error('Security token unavailable. Reload the page.');
            const response = await fetch('/api/subscription-payment/admin/gateway-payments/' +
                encodeURIComponent(String(paymentId)) + '/reconcile', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify({ rowVersion })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || payload?.success !== true)
                throw new Error(payload?.message || 'Gateway verification did not confirm payment.');
            gatewayStatus.textContent = 'Provider-confirmed payment reconciled. Reloading…';
            gatewayBusy = false;
            await gatewayLoad();
            await load();
        } catch (error) {
            gatewayStatus.textContent = error.message || 'Gateway reconciliation failed.';
            gatewayStatus.className = 'small text-danger my-3';
        } finally {
            gatewayBusy = false;
            gatewayRows.querySelectorAll('button').forEach(button => {
                button.disabled = button.dataset.blocked === 'true';
            });
        }
    }
    get('gatewayRecoveryFilters').addEventListener('submit', event => {
        event.preventDefault(); gatewayPage = 1; gatewayLoad();
    });
    gatewayPrevious.addEventListener('click', () => {
        if (!gatewayBusy && gatewayPage > 1) { gatewayPage--; gatewayLoad(); }
    });
    gatewayNext.addEventListener('click', () => {
        if (!gatewayBusy && gatewayPage < Math.ceil(gatewayTotal / pageSize)) {
            gatewayPage++; gatewayLoad();
        }
    });
    gatewayLoad();
})();
