// ============================================================
// PLAN-SELECTION.JS - Onboarding Step 2
// ============================================================

document.addEventListener('DOMContentLoaded', function () {

    let plans = [];
    let selectedPlanId = null;
    let selectedCycle = 1;

    const continueBtn = document.getElementById('continueBtn');
    const plansGrid = document.getElementById('plansGrid');
    const billingToggle = document.getElementById('billingToggle');

    // ── Load plans ────────────────────────────────────────────
    async function loadPlans() {
        try {
            const res = await fetch('/api/subscription-plans', { credentials: 'include' });
            const json = await res.json();
            if (json.success) {
                plans = json.data;
                renderPlans();
            } else {
                plansGrid.innerHTML = '<div class="col-12 text-center text-danger py-4">Failed to load plans.</div>';
            }
        } catch {
            plansGrid.innerHTML = '<div class="col-12 text-center text-danger py-4">Network error loading plans.</div>';
        }
    }

    // ── Render plan cards ─────────────────────────────────────
    function renderPlans() {
        plansGrid.innerHTML = '';

        plans.forEach(plan => {
            const { price, period } = getPriceForCycle(plan, selectedCycle);
            const isFree = plan.isFreeTrial;
            const features = plan.features || [];
            const visible = features.slice(0, 6);
            const more = features.length - 6;

            const col = document.createElement('div');
            col.className = 'col-md-6 col-lg-3';
            col.innerHTML = `
                <div class="plan-card ${plan.isRecommended ? 'recommended' : ''} ${selectedPlanId === plan.id ? 'selected' : ''}"
                     data-plan-id="${plan.id}" role="button" tabindex="0"
                     aria-label="Select ${escHtml(plan.name)} plan">
                    ${plan.isRecommended ? '<span class="recommended-badge">Recommended</span>' : ''}
                    <div class="plan-name">${escHtml(plan.name)}</div>
                    <div class="plan-desc">${escHtml(plan.shortDescription || '')}</div>
                    ${isFree
                        ? `<div class="plan-price">Free</div>
                           <div class="plan-price-period">${plan.trialDays}-day trial, no card needed</div>`
                        : `<div class="plan-price">৳${fmtPrice(price)}</div>
                           <div class="plan-price-period">per ${period}</div>`
                    }
                    <div class="mt-2 small text-muted">
                        <i class="bi bi-people"></i> Up to ${plan.maxStudents.toLocaleString()} students &nbsp;·&nbsp;
                        <i class="bi bi-person-badge"></i> ${plan.maxTeachers} teachers
                    </div>
                    <ul class="plan-features">
                        ${visible.map(f => `<li><i class="bi bi-check-circle-fill"></i>${escHtml(f.featureName)}</li>`).join('')}
                        ${more > 0 ? `<li class="text-muted">+ ${more} more features</li>` : ''}
                    </ul>
                </div>`;

            const card = col.querySelector('.plan-card');
            card.addEventListener('click', () => selectPlan(plan.id));
            card.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') selectPlan(plan.id); });
            plansGrid.appendChild(col);
        });
    }

    // ── Select plan ───────────────────────────────────────────
    function selectPlan(planId) {
        selectedPlanId = planId;
        document.querySelectorAll('.plan-card').forEach(c => c.classList.remove('selected'));
        document.querySelector(`[data-plan-id="${planId}"]`)?.classList.add('selected');
        if (continueBtn) continueBtn.disabled = false;
    }

    // ── Billing cycle toggle ──────────────────────────────────
    if (billingToggle) {
        billingToggle.addEventListener('click', function (e) {
            const btn = e.target.closest('button[data-cycle]');
            if (!btn) return;
            billingToggle.querySelectorAll('button').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            selectedCycle = parseInt(btn.dataset.cycle, 10);
            renderPlans();
            if (selectedPlanId) selectPlan(selectedPlanId); // re-apply selection
        });
    }

    // ── Continue button ───────────────────────────────────────
    if (continueBtn) {
        continueBtn.addEventListener('click', async function () {
            if (!selectedPlanId) return;

            setLoading(continueBtn, true, 'Processing...');

            try {
                const res = await fetch('/api/subscription', {
                    method: 'POST',
                    credentials: 'include',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        subscriptionPlanId: selectedPlanId,
                        billingCycle: selectedCycle,
                        paymentMethod: 1,  // default, user picks on next page
                        autoRenew: true
                    })
                });

                const json = await res.json();

                if (!json.success) {
                    showAlert('danger', json.message || 'Subscription failed.');
                    setLoading(continueBtn, false, 'Continue to payment <i class="bi bi-arrow-right ms-1"></i>');
                    return;
                }

                const data = json.data;

                // Advance onboarding step 2 (PlanSelection)
                await fetch('/api/onboarding/complete-step', {
                    method: 'POST',
                    credentials: 'include',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ step: 2, skipped: false })
                });

                if (data.isTrialActivated) {
                    // Trial → skip payment, go to next step
                    await fetch('/api/onboarding/complete-step', {
                        method: 'POST',
                        credentials: 'include',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ step: 3, skipped: true })
                    });
                    window.location.href = '/Account/CampusSetup';
                } else {
                    window.location.href = `/Account/Payment?invoiceId=${data.invoiceId}`;
                }
            } catch {
                showAlert('danger', 'Network error. Please try again.');
                setLoading(continueBtn, false, 'Continue to payment <i class="bi bi-arrow-right ms-1"></i>');
            }
        });
    }

    // ── Helpers ───────────────────────────────────────────────
    function getPriceForCycle(plan, cycle) {
        const map = {
            1: { price: plan.monthlyPrice,     period: 'month' },
            2: { price: plan.quarterlyPrice,    period: '3 months' },
            3: { price: plan.halfYearlyPrice,   period: '6 months' },
            4: { price: plan.yearlyPrice,       period: 'year' }
        };
        return map[cycle] || map[1];
    }

    function fmtPrice(p) { return Number(p || 0).toLocaleString('en-IN', { minimumFractionDigits: 0 }); }

    function escHtml(s) {
        return (s || '').replace(/[&<>"']/g, c =>
            ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
    }

    function showAlert(type, msg) {
        const c = document.getElementById('alertContainer');
        if (c) c.innerHTML = `<div class="alert alert-${type} alert-dismissible">
            ${msg}<button type="button" class="btn-close" data-bs-dismiss="alert"></button></div>`;
    }

    function setLoading(btn, loading, label) {
        if (!btn) return;
        btn.disabled = loading;
        btn.innerHTML = loading
            ? `<span class="spinner-border spinner-border-sm me-2"></span>${label}`
            : label;
    }

    // ── Init ──────────────────────────────────────────────────
    loadPlans();
    loadOnboardingStatus(); // sidebar (from _OnboardingLayout)
});
