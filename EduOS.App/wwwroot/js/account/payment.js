// ============================================================
// PAYMENT.JS - Onboarding Step 3
// ============================================================

document.addEventListener('DOMContentLoaded', function () {

    const invoiceId = new URLSearchParams(window.location.search).get('invoiceId');
    let invoice = null;
    let selectedMethod = null; // 'aamarpay' | 'manual'

    // ── Load invoice summary ──────────────────────────────────
    async function loadInvoice() {
        if (!invoiceId) {
            setInvoiceHtml('<div class="text-danger small">No invoice found. Please go back and choose a plan.</div>');
            return;
        }
        try {
            const res = await fetch(`/api/subscription/invoices/${invoiceId}`, { credentials: 'include' });
            const json = await res.json();
            if (json.success) {
                invoice = json.data;
                renderInvoice();
            }
        } catch { /* ignore */ }
    }

    function renderInvoice() {
        if (!invoice) return;
        const html = `
            <div class="small text-muted mb-2">Invoice ${escHtml(invoice.invoiceNumber)}</div>
            <div class="invoice-row"><span>${escHtml(invoice.planName || invoice.description || 'Subscription')}</span><span>৳${fmt(invoice.subtotal)}</span></div>
            ${invoice.discountAmount > 0 ? `<div class="invoice-row text-success"><span>Discount</span><span>− ৳${fmt(invoice.discountAmount)}</span></div>` : ''}
            ${invoice.taxAmount > 0     ? `<div class="invoice-row"><span>Tax</span><span>৳${fmt(invoice.taxAmount)}</span></div>` : ''}
            <div class="invoice-row invoice-total"><span>Total due</span><span class="text-primary fw-bold">৳${fmt(invoice.dueAmount)}</span></div>
            <div class="small text-muted mt-2">
                ${fmtDate(invoice.periodStart)} → ${fmtDate(invoice.periodEnd)}
            </div>`;
        setInvoiceHtml(html);
        document.getElementById('onlineAmountLabel').textContent = '৳' + fmt(invoice.dueAmount);
        const amtInput = document.getElementById('manualAmount');
        if (amtInput) amtInput.value = invoice.dueAmount;
    }

    function setInvoiceHtml(html) {
        const el = document.getElementById('invoiceDetails');
        if (el) el.innerHTML = html;
    }

    // ── Method selection ──────────────────────────────────────
    document.querySelectorAll('.payment-method-card').forEach(card => {
        card.addEventListener('click', function () {
            document.querySelectorAll('.payment-method-card').forEach(c => c.classList.remove('selected'));
            this.classList.add('selected');
            selectedMethod = this.dataset.method;
            document.getElementById('aamarpaySection').style.display = selectedMethod === 'aamarpay' ? 'block' : 'none';
            document.getElementById('manualSection').style.display   = selectedMethod === 'manual'   ? 'block' : 'none';
            if (selectedMethod === 'manual') renderBankInfo();
        });
    });

    function renderBankInfo() {
        const el = document.getElementById('bankDetails');
        if (!el) return;
        // Bank info is shown via server-side config loaded from settings
        // If you want to fetch dynamically:
        el.innerHTML = `
            <div class="bank-info-row"><span class="bank-info-label">Bank:</span> Dutch Bangla Bank Ltd</div>
            <div class="bank-info-row"><span class="bank-info-label">Account name:</span> EduOS Technology Ltd.</div>
            <div class="bank-info-row"><span class="bank-info-label">Account no:</span> <code>1234567890123</code></div>
            <div class="bank-info-row"><span class="bank-info-label">Branch:</span> Dhanmondi</div>
            <div class="bank-info-row"><span class="bank-info-label">Reference:</span> <code>${escHtml(invoice?.invoiceNumber ?? '')}</code></div>
            <div class="mt-2 small text-muted">Write the invoice number on the deposit slip.</div>`;
    }

    // ── AamarPay ──────────────────────────────────────────────
    document.getElementById('payOnlineBtn')?.addEventListener('click', async function () {
        if (!invoice) { showAlert('danger', 'Invoice not loaded.'); return; }
        const btn = this;
        setLoading(btn, true, 'Initiating...');

        try {
            const res = await fetch('/api/subscription-payment/initiate', {
                method: 'POST',
                credentials: 'include',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ invoiceId: parseInt(invoiceId), paymentMethod: 2 }) // 2 = AamarPay
            });
            const json = await res.json();
            if (json.success && json.data?.paymentUrl) {
                window.location.href = json.data.paymentUrl;
            } else {
                showAlert('danger', json.message || 'Payment gateway error. Please try manual transfer.');
                setLoading(btn, false, `<i class="bi bi-credit-card me-2"></i>Pay ৳${fmt(invoice.dueAmount)} securely`);
            }
        } catch {
            showAlert('danger', 'Network error.');
            setLoading(btn, false, `<i class="bi bi-credit-card me-2"></i>Pay ৳${fmt(invoice.dueAmount)} securely`);
        }
    });

    // ── Upload zone ───────────────────────────────────────────
    const uploadZone = document.getElementById('uploadZone');
    const fileInput = document.getElementById('depositSlipFile');
    if (uploadZone && fileInput) {
        uploadZone.addEventListener('click', () => fileInput.click());
        uploadZone.addEventListener('dragover', e => { e.preventDefault(); uploadZone.classList.add('dragging'); });
        uploadZone.addEventListener('dragleave', () => uploadZone.classList.remove('dragging'));
        uploadZone.addEventListener('drop', e => {
            e.preventDefault();
            uploadZone.classList.remove('dragging');
            if (e.dataTransfer.files.length) fileInput.files = e.dataTransfer.files;
            updateUploadLabel();
        });
        fileInput.addEventListener('change', updateUploadLabel);
    }

    function updateUploadLabel() {
        const label = document.getElementById('uploadText');
        if (fileInput?.files?.length) {
            if (label) label.textContent = fileInput.files[0].name;
            uploadZone?.classList.add('has-file');
        }
    }

    // ── Manual payment submit ─────────────────────────────────
    document.getElementById('manualForm')?.addEventListener('submit', async function (e) {
        e.preventDefault();

        if (!invoice) { showAlert('danger', 'Invoice not loaded.'); return; }

        const btn = document.getElementById('submitManualBtn');
        const fd = new FormData(this);
        fd.set('invoiceId', invoiceId);

        // Validation
        if (!fd.get('payerBankName')?.trim()) { showAlert('danger', 'Bank name is required.'); return; }
        if (!fd.get('depositSlipNumber')?.trim()) { showAlert('danger', 'Deposit slip number is required.'); return; }
        if (!fd.get('depositDate')?.trim()) { showAlert('danger', 'Deposit date is required.'); return; }
        const amt = parseFloat(fd.get('amount'));
        if (!amt || amt <= 0) { showAlert('danger', 'Valid amount is required.'); return; }

        setLoading(btn, true, 'Submitting...');

        try {
            const res = await fetch('/api/subscription-payment/manual', {
                method: 'POST',
                credentials: 'include',
                body: fd
            });
            const json = await res.json();

            if (json.success) {
                // Advance onboarding step 3 (Payment)
                await fetch('/api/onboarding/complete-step', {
                    method: 'POST',
                    credentials: 'include',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ step: 3, skipped: false })
                });
                showSuccess('Payment submitted! Our team will verify within 24 hours. Proceeding to next step...');
                setTimeout(() => { window.location.href = '/Account/CampusSetup'; }, 2000);
            } else {
                showAlert('danger', json.message || 'Submission failed.');
                setLoading(btn, false, '<i class="bi bi-send me-2"></i>Submit for verification');
            }
        } catch {
            showAlert('danger', 'Network error.');
            setLoading(btn, false, '<i class="bi bi-send me-2"></i>Submit for verification');
        }
    });

    // ── Helpers ───────────────────────────────────────────────
    function fmt(p) { return Number(p || 0).toLocaleString('en-IN', { minimumFractionDigits: 0 }); }
    function fmtDate(d) { return d ? new Date(d).toLocaleDateString('en-GB') : ''; }
    function escHtml(s) {
        return (s || '').replace(/[&<>"']/g, c =>
            ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
    }

    function showAlert(type, msg) {
        const c = document.getElementById('alertContainer');
        if (c) c.innerHTML = `<div class="alert alert-${type} alert-dismissible">
            ${msg}<button type="button" class="btn-close" data-bs-dismiss="alert"></button></div>`;
    }

    function showSuccess(msg) {
        const c = document.getElementById('alertContainer');
        if (c) c.innerHTML = `<div class="alert alert-success"><i class="bi bi-check-circle me-2"></i>${msg}</div>`;
    }

    function setLoading(btn, loading, label) {
        if (!btn) return;
        btn.disabled = loading;
        btn.innerHTML = loading
            ? `<span class="spinner-border spinner-border-sm me-2"></span>${label}`
            : label;
    }

    // ── Init ──────────────────────────────────────────────────
    loadInvoice();
    loadOnboardingStatus();
});
