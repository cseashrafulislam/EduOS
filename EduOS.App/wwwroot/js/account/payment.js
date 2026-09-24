(() => {
    'use strict';

    const configElement = document.getElementById('paymentStrings');
    const i18n = configElement ? JSON.parse(configElement.textContent || '{}') : {};
    const invoiceId = positiveInteger(new URLSearchParams(window.location.search).get('invoiceId'));
    const invoiceDetails = document.getElementById('invoiceDetails');
    const paymentMethods = document.getElementById('paymentMethods');
    const manualSection = document.getElementById('manualSection');
    const onlineSection = document.getElementById('aamarpaySection');
    let invoice = null;
    let manualInstructionsLoaded = false;

    document.addEventListener('DOMContentLoaded', initialize, { once: true });

    async function initialize() {
        paymentMethods?.addEventListener('click', selectPaymentMethod);
        document.getElementById('payOnlineBtn')?.addEventListener('click', startOnlinePayment);
        document.getElementById('manualForm')?.addEventListener('submit', submitManualPayment);
        document.getElementById('depositSlipFile')?.addEventListener('change', updateReceiptLabel);

        if (!invoiceId) {
            renderInvoiceError(i18n.noInvoice, false);
            disablePaymentMethods();
            return;
        }
        await loadInvoice();
    }

    async function loadInvoice() {
        invoiceDetails?.setAttribute('aria-busy', 'true');
        try {
            const response = await fetch(`/api/subscription/invoices/${encodeURIComponent(invoiceId)}`, {
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data) {
                throw new Error('Invalid invoice response');
            }
            invoice = payload.data;
            renderInvoice();
            await applyPaymentState();
        } catch (error) {
            console.warn('EduOS invoice was unavailable.', error);
            renderInvoiceError(i18n.invoiceLoadFailed, true);
            disablePaymentMethods();
        } finally {
            invoiceDetails?.removeAttribute('aria-busy');
        }
    }

    function renderInvoice() {
        if (!invoiceDetails || !invoice) return;
        const fragment = document.createDocumentFragment();

        const number = document.createElement('p');
        number.className = 'invoice-number';
        number.textContent = template(i18n.invoiceTemplate, { number: invoice.invoiceNumber || '' });
        fragment.append(number);

        fragment.append(invoiceRow(
            localized(invoice.planName, invoice.planNameBangla) || invoice.description || i18n.subscription,
            formatMoney(invoice.subtotal, invoice.currency)
        ));
        if (Number(invoice.discountAmount) > 0) {
            fragment.append(invoiceRow(
                i18n.discount,
                `− ${formatMoney(invoice.discountAmount, invoice.currency)}`,
                'text-success'
            ));
        }
        if (Number(invoice.taxAmount) > 0) {
            fragment.append(invoiceRow(i18n.tax, formatMoney(invoice.taxAmount, invoice.currency)));
        }
        fragment.append(invoiceRow(
            i18n.totalDue,
            formatMoney(invoice.dueAmount, invoice.currency),
            'invoice-total'
        ));

        const period = document.createElement('p');
        period.className = 'invoice-period';
        period.textContent = template(i18n.periodTemplate, {
            start: formatDate(invoice.periodStart),
            end: formatDate(invoice.periodEnd)
        });
        fragment.append(period);
        invoiceDetails.replaceChildren(fragment);

        const onlineAmount = document.getElementById('onlineAmountLabel');
        if (onlineAmount) onlineAmount.textContent = formatMoney(invoice.dueAmount, invoice.currency);
        const manualAmount = document.getElementById('manualAmount');
        if (manualAmount) manualAmount.value = Number(invoice.dueAmount || 0).toFixed(2);
    }

    async function applyPaymentState() {
        const status = Number(invoice?.paymentStatus);
        if (status === 3 || Number(invoice?.dueAmount) <= 0) {
            showStatus(i18n.alreadyPaid, true);
            return;
        }
        if (status === 7) {
            showStatus(i18n.awaiting, false);
            return;
        }

        try {
            const response = await fetch(`/api/subscription-payment/invoice/${encodeURIComponent(invoiceId)}`, {
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            const payments = Array.isArray(payload?.data) ? payload.data : [];
            if (response.ok && payload?.success && payments.some(item => Number(item.status) === 2)) {
                showStatus(i18n.processing, false);
            }
        } catch {
            // The invoice remains usable when optional payment-history lookup fails.
        }
    }

    function showStatus(message, canContinue) {
        disablePaymentMethods();
        const panel = document.getElementById('paymentStatus');
        if (!panel) return;
        panel.className = `payment-status-panel ${canContinue ? 'success' : 'pending'}`;
        const text = document.createElement('p');
        text.textContent = message || '';
        const action = document.createElement('button');
        action.type = 'button';
        action.className = `btn ${canContinue ? 'btn-success' : 'btn-outline-primary'}`;
        action.textContent = canContinue ? i18n.continueSetup : i18n.reload;
        action.addEventListener('click', () => {
            if (canContinue) window.location.assign('/Account/CampusSetup');
            else window.location.reload();
        });
        panel.replaceChildren(text, action);
    }

    function selectPaymentMethod(event) {
        const card = event.target.closest('button[data-method]');
        if (!card || card.disabled || !invoice) return;
        paymentMethods.querySelectorAll('button[data-method]').forEach(item => {
            const selected = item === card;
            item.classList.toggle('selected', selected);
            item.setAttribute('aria-pressed', String(selected));
        });
        const method = card.dataset.method;
        if (onlineSection) onlineSection.hidden = method !== 'aamarpay';
        if (manualSection) manualSection.hidden = method !== 'manual';
        if (method === 'manual' && !manualInstructionsLoaded) loadManualInstructions(card);
    }

    async function loadManualInstructions(card) {
        const bankDetails = document.getElementById('bankDetails');
        if (!bankDetails) return;
        bankDetails.replaceChildren(paragraph(i18n.loading, 'text-muted mb-0'));
        try {
            const response = await fetch(
                `/api/subscription-payment/manual-instructions/${encodeURIComponent(invoiceId)}`,
                { cache: 'no-store', credentials: 'same-origin', headers: { 'Accept': 'application/json' } }
            );
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data) throw new Error('Manual payment unavailable');
            renderBankDetails(payload.data);
            manualInstructionsLoaded = true;
        } catch {
            card.disabled = true;
            card.setAttribute('aria-disabled', 'true');
            bankDetails.replaceChildren(paragraph(i18n.bankUnavailable, 'text-danger mb-0'));
            document.getElementById('submitManualBtn')?.setAttribute('disabled', 'disabled');
            showAlert('danger', i18n.bankUnavailable);
        }
    }

    function renderBankDetails(details) {
        const bankDetails = document.getElementById('bankDetails');
        if (!bankDetails) return;
        const list = document.createElement('dl');
        list.className = 'bank-detail-list';
        appendDefinition(list, i18n.bank, details.bankName);
        appendDefinition(list, i18n.accountName, details.accountName);
        appendDefinition(list, i18n.accountNumber, details.accountNumber);
        appendDefinition(list, i18n.routingNumber, details.routingNumber);
        appendDefinition(list, i18n.branch, details.branchName);
        appendDefinition(list, i18n.reference, details.reference);
        const children = [list];
        if (String(details.instructions || '').trim()) {
            children.push(paragraph(details.instructions, 'bank-instructions'));
        }
        bankDetails.replaceChildren(...children);
    }

    async function startOnlinePayment() {
        if (!invoice) return;
        const button = document.getElementById('payOnlineBtn');
        setLoading(button, true);
        try {
            const response = await fetch('/api/subscription-payment/initiate', {
                method: 'POST',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                body: JSON.stringify({ invoiceId, paymentMethod: 2 })
            });
            const payload = await response.json().catch(() => null);
            if (response.status === 409) {
                showStatus(i18n.processing, false);
                return;
            }
            if (!response.ok || !payload?.success || !payload.data?.paymentUrl) {
                showAlert('danger', i18n.gatewayFailed);
                return;
            }
            const target = trustedGatewayUrl(payload.data.paymentUrl);
            if (!target) {
                showAlert('danger', i18n.invalidGatewayUrl);
                return;
            }
            window.location.assign(target);
        } catch {
            showAlert('danger', i18n.networkError);
        } finally {
            setLoading(button, false);
        }
    }

    async function submitManualPayment(event) {
        event.preventDefault();
        const form = event.currentTarget;
        const button = document.getElementById('submitManualBtn');
        if (!invoice || !manualInstructionsLoaded || !form.reportValidity()) {
            showAlert('danger', i18n.requiredFields);
            return;
        }

        const receipt = document.getElementById('depositSlipFile')?.files?.[0];
        if (!receipt) {
            showAlert('danger', i18n.receiptRequired);
            return;
        }
        const allowedTypes = new Set(['application/pdf', 'image/jpeg', 'image/png']);
        if (receipt.size <= 0 || receipt.size > 5 * 1024 * 1024 || !allowedTypes.has(receipt.type)) {
            showAlert('danger', i18n.receiptInvalid);
            return;
        }

        const data = new FormData(form);
        data.set('invoiceId', String(invoiceId));
        data.set('amount', Number(invoice.dueAmount || 0).toFixed(2));
        setLoading(button, true);
        try {
            const response = await fetch('/api/subscription-payment/manual', {
                method: 'POST',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' },
                body: data
            });
            const payload = await response.json().catch(() => null);
            if (response.status === 409) {
                showStatus(i18n.awaiting, false);
                return;
            }
            if (!response.ok || !payload?.success) {
                showAlert('danger', i18n.submitFailed);
                return;
            }
            showStatus(i18n.submitted, false);
        } catch {
            showAlert('danger', i18n.networkError);
        } finally {
            setLoading(button, false);
        }
    }

    function updateReceiptLabel(event) {
        const receipt = event.currentTarget.files?.[0];
        const label = document.getElementById('uploadText');
        const zone = document.getElementById('uploadZone');
        if (!receipt || !label || !zone) return;
        label.textContent = receipt.name;
        zone.classList.add('has-file');
    }

    function renderInvoiceError(message, allowRetry) {
        if (!invoiceDetails) return;
        const state = document.createElement('div');
        state.className = 'setup-empty-state';
        state.append(paragraph(message));
        if (allowRetry) {
            const retry = document.createElement('button');
            retry.type = 'button';
            retry.className = 'btn btn-sm btn-outline-primary';
            retry.textContent = i18n.retry || '';
            retry.addEventListener('click', loadInvoice, { once: true });
            state.append(retry);
        }
        invoiceDetails.replaceChildren(state);
    }

    function invoiceRow(label, value, className) {
        const row = document.createElement('div');
        row.className = `invoice-row ${className || ''}`.trim();
        const name = document.createElement('span');
        name.textContent = label || '';
        const amount = document.createElement('span');
        amount.textContent = value || '';
        row.append(name, amount);
        return row;
    }

    function appendDefinition(list, term, value) {
        if (!String(value || '').trim()) return;
        const dt = document.createElement('dt');
        dt.textContent = term || '';
        const dd = document.createElement('dd');
        dd.textContent = String(value);
        list.append(dt, dd);
    }

    function paragraph(text, className) {
        const element = document.createElement('p');
        if (className) element.className = className;
        element.textContent = text || '';
        return element;
    }

    function disablePaymentMethods() {
        paymentMethods?.querySelectorAll('button[data-method]').forEach(button => {
            button.disabled = true;
            button.setAttribute('aria-disabled', 'true');
        });
        if (onlineSection) onlineSection.hidden = true;
        if (manualSection) manualSection.hidden = true;
    }

    function setLoading(button, loading) {
        if (!button) return;
        button.disabled = loading;
        button.replaceChildren();
        if (loading) {
            const spinner = document.createElement('span');
            spinner.className = 'spinner-border spinner-border-sm me-2';
            spinner.setAttribute('aria-hidden', 'true');
            button.append(spinner, button.dataset.loadingLabel || i18n.submitting || '');
            return;
        }
        if (button.id === 'payOnlineBtn') {
            button.textContent = template(i18n.payTemplate, {
                amount: formatMoney(invoice?.dueAmount, invoice?.currency)
            });
        } else {
            button.textContent = button.dataset.idleLabel || i18n.submitLabel || '';
        }
    }

    function showAlert(type, message) {
        const container = document.getElementById('alertContainer');
        if (!container) return;
        container.className = `alert alert-${type === 'success' ? 'success' : 'danger'}`;
        container.textContent = message || '';
        container.focus();
    }

    function trustedGatewayUrl(value) {
        try {
            const url = new URL(String(value));
            return url.protocol === 'https:' || (url.protocol === 'http:' && url.hostname === 'localhost')
                ? url.href
                : null;
        } catch {
            return null;
        }
    }

    function localized(english, bangla) {
        return i18n.isBangla && String(bangla || '').trim()
            ? String(bangla).trim()
            : String(english || '').trim();
    }

    function formatMoney(value, currency) {
        const amount = Number(value || 0);
        if (String(currency || 'BDT').toUpperCase() === 'BDT') {
            return `৳${amount.toLocaleString(i18n.culture || 'en-BD', { maximumFractionDigits: 2 })}`;
        }
        try {
            return new Intl.NumberFormat(i18n.culture || 'en', {
                style: 'currency', currency: String(currency), maximumFractionDigits: 2
            }).format(amount);
        } catch {
            return `${amount.toLocaleString(i18n.culture || 'en')} ${String(currency || '')}`.trim();
        }
    }

    function formatDate(value) {
        if (!value) return '';
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? '' : date.toLocaleDateString(i18n.culture || 'en-BD');
    }

    function template(value, replacements) {
        let result = String(value || '');
        Object.entries(replacements).forEach(([key, replacement]) => {
            result = result.replaceAll(`{${key}}`, String(replacement));
        });
        return result;
    }

    function positiveInteger(value) {
        const number = Number(value);
        return Number.isSafeInteger(number) && number > 0 ? number : null;
    }
})();
