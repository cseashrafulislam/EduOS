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
    const gatewayInspectionResult = get('gatewayInspectionResult');

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
            const inspect = document.createElement('button');
            inspect.type = 'button';
            inspect.className = 'btn btn-outline-warning btn-sm ms-1';
            inspect.textContent = 'Inspect provider';
            inspect.disabled = gatewayBusy;
            inspect.addEventListener('click', () => gatewayInspect(item.paymentId, item.rowVersion));
            td.append(inspect);
            const trail = document.createElement('button');
            trail.type = 'button';
            trail.className = 'btn btn-outline-secondary btn-sm ms-1';
            trail.textContent = 'Review trail';
            trail.addEventListener('click', () => gatewayFinanceLoad(item.paymentId, item.rowVersion, 1));
            td.append(trail);
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
        gatewayInspectionResult.classList.add('d-none');
        gatewayInspectionResult.replaceChildren();
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
    async function gatewayInspect(paymentId, rowVersion) {
        if (gatewayBusy || !rowVersion) return;
        gatewayBusy = true;
        gatewayRows.querySelectorAll('button').forEach(button => { button.disabled = true; });
        gatewayInspectionResult.classList.add('d-none');
        gatewayInspectionResult.replaceChildren();
        gatewayStatus.className = 'small text-muted my-3';
        gatewayStatus.textContent = 'Inspecting provider status without settling funds…';
        try {
            const token = get('gatewayRecoveryAntiforgery')
                .querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (!token) throw new Error('Security token unavailable. Reload the page.');
            const response = await fetch('/api/subscription-payment/admin/gateway-payments/' +
                encodeURIComponent(String(paymentId)) + '/inspect-provider', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify({ rowVersion })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || payload?.success !== true)
                throw new Error(payload?.message || 'Provider inspection failed.');
            const result = payload.data || {};
            const lines = [
                result.finding || 'No provider conclusion available.',
                'Provider: ' + (result.providerStatus || 'Unconfirmed') +
                    ' · Amount match: ' + (result.amountMatches === true ? 'Yes' : 'No') +
                    ' · Currency match: ' + (result.currencyMatches === true ? 'Yes' : 'No'),
                'Audit #' + (result.auditId ?? '—') +
                    ' · Evidence SHA-256: ' + (result.evidenceSha256 || '—')
            ];
            const heading = document.createElement('strong');
            heading.textContent = result.requiresManualReview === true
                ? 'Manual finance investigation required. ' : 'Provider inspection recorded. ';
            gatewayInspectionResult.append(heading);
            for (const line of lines) {
                const p = document.createElement('p');
                p.className = 'mb-1';
                p.textContent = line;
                gatewayInspectionResult.append(p);
            }
            gatewayInspectionResult.classList.remove('d-none');
            gatewayStatus.textContent = 'Inspection audited; payment and invoice unchanged.';
        } catch (error) {
            gatewayStatus.textContent = error.message || 'Provider inspection failed.';
            gatewayStatus.className = 'small text-danger my-3';
        } finally {
            gatewayBusy = false;
            gatewayRows.querySelectorAll('button').forEach(button => {
                button.disabled = button.dataset.blocked === 'true';
            });
            gatewayPrevious.disabled = gatewayPage <= 1;
            gatewayNext.disabled = gatewayPage >= Math.max(1, Math.ceil(gatewayTotal / pageSize));
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

    // Finance triage is an append-only audit workflow. It never invokes the
    // payment settlement callback, nor accepts provider status from the browser.
    const financePanel = get('gatewayFinancePanel');
    const financeRows = get('gatewayFinanceRows');
    const financeStatus = get('gatewayFinanceStatus');
    const financePrevious = get('gatewayFinancePrevious');
    const financeNext = get('gatewayFinanceNext');
    const financePageLabel = get('gatewayFinancePage');
    const financeForm = get('gatewayFinanceForm');
    const financeSave = get('gatewayFinanceSave');
    const financeDecision = get('gatewayFinanceDecision');
    const financeNote = get('gatewayFinanceNote');
    const financeExternalCaseGroup = get('gatewayFinanceExternalCaseGroup');
    const financeExternalCaseReference = get('gatewayFinanceExternalCaseReference');
    let financePaymentId = 0;
    let financeRowVersion = '';
    let financeEvidenceAuditId = 0;
    let financePage = 1;
    let financeTotal = 0;
    let financeRequestSeq = 0;
    let financeBusy = false;
    let financeRequestId = null;
    let financeCaseClosed = false;
    const financePageSize = 10;
    function financeToggleClosure() {
        const closing = financeDecision.value === 'ExternalCaseClosed';
        financeExternalCaseGroup.classList.toggle('d-none', !closing);
        financeExternalCaseReference.required = closing;
        if (!closing) financeExternalCaseReference.value = '';
    }
    financeDecision.addEventListener('change', () => { financeRequestId = null; financeToggleClosure(); });
    financeNote.addEventListener('input', () => { financeRequestId = null; });
    financeExternalCaseReference.addEventListener('input', () => { financeRequestId = null; });
    financeToggleClosure();

    async function gatewayFinanceLoad(paymentId, rowVersion, targetPage) {
        if (financeBusy) return;
        if (financePaymentId !== paymentId) {
            financePaymentId = paymentId;
            financeEvidenceAuditId = 0;
            financeCaseClosed = false;
            financeRequestId = null;
            financeNote.value = '';
            financeExternalCaseReference.value = '';
        }
        financeRowVersion = rowVersion;
        financePage = targetPage;
        const seq = ++financeRequestSeq;
        financePanel.classList.remove('d-none');
        get('gatewayFinancePayment').textContent = 'Gateway payment #' + paymentId;
        financeStatus.className = 'small text-muted mb-3';
        financeStatus.textContent = 'Loading provider evidence and finance reviews…';
        financeSave.disabled = true;
        financePrevious.disabled = true;
        financeNext.disabled = true;
        const query = new URLSearchParams({ page: String(financePage), pageSize: String(financePageSize) });
        try {
            const response = await fetch('/api/subscription-payment/admin/gateway-payments/' +
                encodeURIComponent(String(paymentId)) + '/finance-history?' + query, {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (seq !== financeRequestSeq) return;
            if (!response.ok || payload?.success !== true)
                throw new Error(payload?.message || 'Unable to load finance review history.');
            const items = Array.isArray(payload.data?.items) ? payload.data.items : [];
            financeTotal = Number(payload.data?.totalCount || 0);
            const pages = Math.max(1, Math.ceil(financeTotal / financePageSize));
            if (financePage > pages) { await gatewayFinanceLoad(paymentId, rowVersion, pages); return; }
            financeRows.replaceChildren();
            for (const item of items) {
                const tr = document.createElement('tr');
                cell(tr, date(item.occurredAtUtc), 'Audit #' + item.auditId);
                cell(tr, item.action === 'GatewayFinanceReview' ? 'Finance review' :
                    item.action === 'GatewayProviderEscalation' ? 'Provider escalation' : 'Provider inspection',
                    item.decision || '');
                cell(tr, item.reviewerUserId ?? '—');
                cell(tr, item.note || (item.requiresManualReview ? 'Manual review required' : 'Inspection recorded'),
                    item.caseClosed ? 'Administrative case closed · ' + (item.externalCaseReference || '—') : '');
                cell(tr, item.evidenceSha256 || '—', item.evidenceAuditId ? 'Linked audit #' + item.evidenceAuditId : '');
                financeRows.append(tr);
            }
            if (!items.length) {
                const tr = document.createElement('tr');
                const td = document.createElement('td');
                td.colSpan = 5;
                td.className = 'text-muted text-center';
                td.textContent = 'No investigation history. Inspect the provider first.';
                tr.append(td);
                financeRows.append(tr);
            }
            if (financePage === 1) {
                financeCaseClosed = items.some(x => x.action === 'GatewayFinanceReview' && x.caseClosed === true);
                const escalation = items.find(x => x.action === 'GatewayProviderEscalation' &&
                    x.providerConfirmed === true && x.requiresManualReview === true);
                financeEvidenceAuditId = Number(escalation?.auditId || 0);
            }
            financePageLabel.textContent = 'Page ' + financePage + ' of ' + pages +
                ' · ' + financeTotal + ' audit records';
            financePrevious.disabled = financePage <= 1;
            financeNext.disabled = financePage >= pages;
            financeSave.disabled = !financeEvidenceAuditId || financeCaseClosed;
            financeStatus.textContent = financeCaseClosed
                ? 'External finance follow-up is closed. No financial settlement or refund was performed.'
                : financeEvidenceAuditId
                ? 'Escalation evidence #' + financeEvidenceAuditId +
                    ' selected. Notes are auditable and do not change financial balances.'
                : 'No provider-confirmed escalation evidence. Inspect the provider before recording a review.';
        } catch (error) {
            if (seq !== financeRequestSeq) return;
            financeRows.replaceChildren();
            financeSave.disabled = true;
            financeStatus.textContent = error.message || 'Unable to load finance review history.';
            financeStatus.className = 'small text-danger mb-3';
        }
    }
    financeForm.addEventListener('submit', async event => {
        event.preventDefault();
        if (financeBusy || financeCaseClosed || !financePaymentId || !financeEvidenceAuditId || !financeRowVersion) return;
        const note = financeNote.value.trim();
        const closing = financeDecision.value === 'ExternalCaseClosed';
        const externalCaseReference = closing ? financeExternalCaseReference.value.trim() : null;
        if (closing && (!externalCaseReference || !/^[A-Za-z0-9._/-]{6,100}$/.test(externalCaseReference))) {
            financeStatus.textContent = 'Enter a valid external case reference (6–100 characters).';
            return;
        }
        if (note.length < 10 || note.length > 500) {
            financeStatus.textContent = 'Enter a note between 10 and 500 characters.';
            return;
        }
        const token = get('gatewayRecoveryAntiforgery')
            .querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (!token) {
            financeStatus.textContent = 'Security token unavailable. Reload the page.';
            return;
        }
        if (!financeRequestId) financeRequestId = crypto.randomUUID();
        financeBusy = true;
        financeSave.disabled = true;
        financeStatus.className = 'small text-muted mb-3';
        financeStatus.textContent = 'Recording finance follow-up…';
        try {
            const response = await fetch('/api/subscription-payment/admin/gateway-payments/' +
                encodeURIComponent(String(financePaymentId)) + '/finance-review', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify({
                    requestId: financeRequestId, rowVersion: financeRowVersion,
                    evidenceAuditId: financeEvidenceAuditId, decision: financeDecision.value, note,
                    externalCaseReference
                })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || payload?.success !== true)
                throw new Error(payload?.message || 'Finance review could not be recorded.');
            financeRequestId = null;
            financeNote.value = '';
            financeExternalCaseReference.value = '';
            financeBusy = false;
            await gatewayFinanceLoad(financePaymentId, financeRowVersion, 1);
        } catch (error) {
            financeStatus.textContent = error.message || 'Finance review could not be recorded.';
            financeStatus.className = 'small text-danger mb-3';
        } finally {
            financeBusy = false;
            financeSave.disabled = !financeEvidenceAuditId || financeCaseClosed;
        }
    });
    financePrevious.addEventListener('click', () => {
        if (!financeBusy && financePage > 1)
            gatewayFinanceLoad(financePaymentId, financeRowVersion, financePage - 1);
    });
    financeNext.addEventListener('click', () => {
        if (!financeBusy && financePage < Math.ceil(financeTotal / financePageSize))
            gatewayFinanceLoad(financePaymentId, financeRowVersion, financePage + 1);
    });

})();
