(() => {
    'use strict';

    const configElement = document.getElementById('verificationStrings');
    const i18n = parseJson(configElement?.textContent);
    const apiBase = '/api/subscription-payment/admin';

    const paymentList = document.getElementById('paymentList');
    const loadingPanel = document.getElementById('loadingPanel');
    const emptyPanel = document.getElementById('emptyPanel');
    const reviewEmpty = document.getElementById('reviewEmpty');
    const reviewPanel = document.getElementById('reviewPanel');
    const searchInput = document.getElementById('searchInput');
    const pendingText = document.getElementById('pendingText');

    let payments = [];
    let selectedPayment = null;

    document.addEventListener('DOMContentLoaded', initialize, { once: true });

    function initialize() {
        document.getElementById('btnRefresh')?.addEventListener('click', loadPayments);
        document.getElementById('btnApprove')?.addEventListener('click', () => verifyPayment(true));
        document.getElementById('btnReject')?.addEventListener('click', () => verifyPayment(false));
        document.getElementById('btnViewSlip')?.addEventListener('click', viewDepositSlip);
        searchInput?.addEventListener('input', renderFilteredPayments);
        paymentList?.addEventListener('click', selectPayment);

        loadPayments();
    }

    async function loadPayments() {
        setLoading(true);
        hideAlert();

        try {
            const response = await fetch(`${apiBase}/pending-verifications`, {
                method: 'GET',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: {
                    'Accept': 'application/json'
                }
            });

            const payload = await response.json().catch(() => null);

            if (response.status === 401) {
                window.location.assign('/Account/Login');
                return;
            }

            if (!response.ok || !payload?.success) {
                console.error('Pending payment load failed.', {
                    status: response.status,
                    payload
                });

                payments = [];
                updatePendingCount();
                renderPayments([]);

                showAlert(
                    'danger',
                    payload?.message ||
                    i18n.paymentLoadFailed ||
                    'Failed to load pending payments.'
                );

                return;
            }

            payments = Array.isArray(payload.data) ? payload.data : [];

            if (selectedPayment && !payments.some(x => Number(x.id) === Number(selectedPayment.id)))
                clearSelection();

            updatePendingCount();
            renderFilteredPayments();
        } catch (error) {
            console.error('Pending payment load failed.', error);

            payments = [];
            updatePendingCount();
            renderPayments([]);

            showAlert(
                'danger',
                i18n.paymentLoadNetworkError ||
                'Unable to load pending payments. Please try again.'
            );
        } finally {
            setLoading(false);
        }
    }

    function updatePendingCount() {
        if (!pendingText) return;

        pendingText.textContent = replaceTemplate(
            i18n.waitingTemplate || '{count} payment(s) waiting for verification',
            {
                count: payments.length
            }
        );
    }

    function renderFilteredPayments() {
        const search = String(searchInput?.value || '')
            .trim()
            .toLowerCase();

        if (!search) {
            renderPayments(payments);
            return;
        }

        const filtered = payments.filter(item => {
            const values = [
                item.invoiceNumber,
                item.transactionId,
                item.payerBankName,
                item.payerAccountNumber,
                item.depositSlipNumber,
                item.amount,
                item.currency
            ];

            return values.some(value =>
                String(value ?? '')
                    .toLowerCase()
                    .includes(search)
            );
        });

        renderPayments(filtered);
    }

    function renderPayments(items) {
        if (!paymentList) return;

        paymentList.replaceChildren();

        if (!items.length) {
            paymentList.classList.add('d-none');
            emptyPanel?.classList.remove('d-none');
            return;
        }

        emptyPanel?.classList.add('d-none');
        paymentList.classList.remove('d-none');

        const fragment = document.createDocumentFragment();

        items.forEach(item => {
            const button = document.createElement('button');

            button.type = 'button';
            button.className = 'verification-card';
            button.dataset.id = String(item.id);

            if (selectedPayment && Number(selectedPayment.id) === Number(item.id))
                button.classList.add('selected');

            const top = document.createElement('div');
            top.className = 'verification-card-top';

            const titleBox = document.createElement('div');

            const title = document.createElement('div');
            title.className = 'verification-card-title';
            title.textContent = item.invoiceNumber || i18n.invoice || '-';

            const transaction = document.createElement('div');
            transaction.className = 'verification-card-subtitle';
            transaction.textContent = item.transactionId || '-';

            titleBox.append(title, transaction);

            const amount = document.createElement('div');
            amount.className = 'verification-card-amount';
            amount.textContent = formatMoney(item.amount, item.currency);

            top.append(titleBox, amount);

            const meta = document.createElement('div');
            meta.className = 'verification-card-meta';

            meta.append(
                createMetaItem(
                    i18n.bank || 'Bank',
                    item.payerBankName || '-'
                ),
                createMetaItem(
                    i18n.slipNumber || 'Slip No.',
                    item.depositSlipNumber || '-'
                ),
                createMetaItem(
                    i18n.depositDate || 'Deposit Date',
                    formatDate(item.depositDate)
                ),
                createMetaItem(
                    i18n.submitted || 'Submitted',
                    formatDateTime(item.initiatedAt)
                )
            );

            button.append(top, meta);
            fragment.append(button);
        });

        paymentList.append(fragment);
    }

    function selectPayment(event) {
        const card = event.target.closest('.verification-card');

        if (!card)
            return;

        const paymentId = parsePositiveInteger(card.dataset.id);

        if (!paymentId) {
            showAlert(
                'danger',
                i18n.paymentNotFound ||
                'Payment information was not found.'
            );

            return;
        }

        const payment = payments.find(x => Number(x.id) === paymentId);

        if (!payment) {
            showAlert(
                'danger',
                i18n.paymentNotFound ||
                'Payment information was not found.'
            );

            return;
        }

        selectedPayment = payment;

        paymentList.querySelectorAll('.verification-card').forEach(item => {
            item.classList.toggle(
                'selected',
                Number(item.dataset.id) === paymentId
            );
        });

        renderReview(payment);
        hideAlert();
    }

    function renderReview(payment) {
        setText('reviewInvoice', payment.invoiceNumber);
        setText('reviewAmount', formatMoney(payment.amount, payment.currency));
        setText('reviewTransaction', payment.transactionId);
        setText('reviewBank', payment.payerBankName);
        setText('reviewAccount', payment.payerAccountNumber);
        setText('reviewSlip', payment.depositSlipNumber);
        setText('reviewDepositDate', formatDate(payment.depositDate));
        setText('reviewSubmittedAt', formatDateTime(payment.initiatedAt));

        const selectedPaymentId = document.getElementById('selectedPaymentId');
        const verificationNote = document.getElementById('verificationNote');

        if (selectedPaymentId)
            selectedPaymentId.value = String(payment.id);

        if (verificationNote)
            verificationNote.value = payment.verificationNote || '';

        reviewEmpty?.classList.add('d-none');
        reviewPanel?.classList.remove('d-none');
    }

    async function verifyPayment(approve) {
        if (!selectedPayment?.id) {
            showAlert(
                'danger',
                i18n.selectPaymentFirst ||
                'Select a payment first.'
            );

            return;
        }

        const noteElement = document.getElementById('verificationNote');
        const note = String(noteElement?.value || '').trim();

        if (!approve && !note) {
            showAlert(
                'danger',
                i18n.rejectionReasonRequired ||
                'Verification note is required when rejecting a payment.'
            );

            noteElement?.focus();
            return;
        }

        const confirmMessage = approve
            ? i18n.confirmApprove
            : i18n.confirmReject;

        if (!window.confirm(
            confirmMessage ||
            (approve
                ? 'Are you sure you want to approve this payment?'
                : 'Are you sure you want to reject this payment?')
        ))
            return;

        const paymentId = parsePositiveInteger(selectedPayment.id);

        if (!paymentId) {
            showAlert(
                'danger',
                i18n.paymentNotFound ||
                'Payment information was not found.'
            );

            return;
        }

        setVerificationBusy(true);
        hideAlert();

        try {
            const response = await fetch(`${apiBase}/verify`, {
                method: 'POST',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: {
                    'Accept': 'application/json',
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getAntiForgeryToken()
                },
                body: JSON.stringify({
                    paymentId,
                    approve,
                    verificationNote: note || null
                })
            });

            const payload = await response.json().catch(() => null);

            if (response.status === 401) {
                window.location.assign('/Account/Login');
                return;
            }

            if (!response.ok || !payload?.success) {
                console.error('Payment verification failed.', {
                    status: response.status,
                    payload
                });

                showAlert(
                    'danger',
                    payload?.message ||
                    i18n.verificationFailed ||
                    'Payment verification failed.'
                );

                return;
            }

            const successMessage = approve
                ? i18n.approved
                : i18n.rejected;

            clearSelection();

            await loadPayments();

            showAlert(
                'success',
                successMessage ||
                (approve
                    ? 'Payment approved successfully.'
                    : 'Payment rejected successfully.')
            );
        } catch (error) {
            console.error('Payment verification failed.', error);

            showAlert(
                'danger',
                i18n.verificationNetworkError ||
                'Unable to verify payment. Please try again.'
            );
        } finally {
            setVerificationBusy(false);
        }
    }

    function viewDepositSlip() {
        if (!selectedPayment?.id) {
            showAlert(
                'danger',
                i18n.selectPaymentFirst ||
                'Select a payment first.'
            );

            return;
        }

        const paymentId = parsePositiveInteger(selectedPayment.id);

        if (!paymentId) {
            showAlert(
                'danger',
                i18n.paymentNotFound ||
                'Payment information was not found.'
            );

            return;
        }

        const url = `${apiBase}/payments/${encodeURIComponent(paymentId)}/deposit-slip`;

        window.open(
            url,
            '_blank',
            'noopener,noreferrer'
        );
    }

    function clearSelection() {
        selectedPayment = null;

        const selectedPaymentId = document.getElementById('selectedPaymentId');
        const verificationNote = document.getElementById('verificationNote');

        if (selectedPaymentId)
            selectedPaymentId.value = '';

        if (verificationNote)
            verificationNote.value = '';

        reviewPanel?.classList.add('d-none');
        reviewEmpty?.classList.remove('d-none');

        paymentList?.querySelectorAll('.verification-card').forEach(item => {
            item.classList.remove('selected');
        });
    }

    function createMetaItem(label, value) {
        const wrapper = document.createElement('div');
        const title = document.createElement('span');
        const content = document.createElement('strong');

        title.textContent = label || '';
        content.textContent = value || '-';

        wrapper.append(title, content);

        return wrapper;
    }

    function setText(id, value) {
        const element = document.getElementById(id);

        if (element)
            element.textContent = value || '-';
    }

    function setLoading(loading) {
        loadingPanel?.classList.toggle('d-none', !loading);

        const refreshButton = document.getElementById('btnRefresh');

        if (refreshButton)
            refreshButton.disabled = loading;

        if (loading) {
            paymentList?.classList.add('d-none');
            emptyPanel?.classList.add('d-none');
        }
    }

    function setVerificationBusy(busy) {
        setButtonBusy(document.getElementById('btnApprove'), busy);
        setButtonBusy(document.getElementById('btnReject'), busy);

        const viewSlipButton = document.getElementById('btnViewSlip');

        if (viewSlipButton)
            viewSlipButton.disabled = busy;
    }

    function setButtonBusy(button, busy) {
        if (!button)
            return;

        button.disabled = busy;

        if (busy) {
            button.replaceChildren();

            const spinner = document.createElement('span');
            spinner.className = 'spinner-border spinner-border-sm me-2';
            spinner.setAttribute('aria-hidden', 'true');

            button.append(
                spinner,
                document.createTextNode(
                    i18n.processing || 'Processing...'
                )
            );

            return;
        }

        button.textContent = button.dataset.idleLabel || '';
    }

    function getAntiForgeryToken() {
        return document
            .querySelector('input[name="__RequestVerificationToken"]')
            ?.value ||
            document
                .querySelector('meta[name="request-verification-token"]')
                ?.content ||
            '';
    }

    function showAlert(type, message) {
        const container = document.getElementById('alertContainer');

        if (!container)
            return;

        container.className =
            `alert alert-${type === 'success' ? 'success' : 'danger'}`;

        container.textContent = message || '';
        container.focus();
    }

    function hideAlert() {
        const container = document.getElementById('alertContainer');

        if (!container)
            return;

        container.className = 'd-none';
        container.textContent = '';
    }

    function formatMoney(value, currency) {
        const amount = Number(value || 0);
        const code = String(currency || 'BDT').toUpperCase();
        const culture = i18n.culture || 'en-BD';

        if (code === 'BDT') {
            return `৳${amount.toLocaleString(culture, {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            })}`;
        }

        try {
            return new Intl.NumberFormat(culture, {
                style: 'currency',
                currency: code,
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            }).format(amount);
        } catch {
            return `${amount.toFixed(2)} ${code}`;
        }
    }

    function formatDate(value) {
        if (!value)
            return '-';

        const date = new Date(value);

        if (Number.isNaN(date.getTime()))
            return '-';

        return date.toLocaleDateString(
            i18n.culture || 'en-BD',
            {
                year: 'numeric',
                month: 'short',
                day: '2-digit'
            }
        );
    }

    function formatDateTime(value) {
        if (!value)
            return '-';

        const date = new Date(value);

        if (Number.isNaN(date.getTime()))
            return '-';

        return date.toLocaleString(
            i18n.culture || 'en-BD',
            {
                year: 'numeric',
                month: 'short',
                day: '2-digit',
                hour: '2-digit',
                minute: '2-digit'
            }
        );
    }

    function replaceTemplate(value, replacements) {
        let result = String(value || '');

        Object.entries(replacements || {}).forEach(([key, replacement]) => {
            result = result.split(`{${key}}`).join(String(replacement));
        });

        return result;
    }

    function parsePositiveInteger(value) {
        const number = Number(value);

        return Number.isSafeInteger(number) && number > 0
            ? number
            : null;
    }

    function parseJson(value) {
        if (!value)
            return {};

        try {
            return JSON.parse(value);
        } catch (error) {
            console.error('Invalid payment verification localization configuration.', error);
            return {};
        }
    }
})();