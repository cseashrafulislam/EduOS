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
})();
