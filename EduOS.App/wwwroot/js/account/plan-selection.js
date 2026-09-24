(() => {
    'use strict';

    const configElement = document.getElementById('planSelectionStrings');
    const i18n = configElement ? JSON.parse(configElement.textContent || '{}') : {};
    const plansGrid = document.getElementById('plansGrid');
    const billingToggle = document.getElementById('billingToggle');
    const continueButton = document.getElementById('continueBtn');
    let plans = [];
    let selectedPlanId = null;
    let selectedCycle = 1;

    document.addEventListener('DOMContentLoaded', initialize, { once: true });

    async function initialize() {
        if (!plansGrid || !continueButton) return;
        const recovered = await recoverExistingSubscription();
        if (!recovered) await loadPlans();
        billingToggle?.addEventListener('click', changeBillingCycle);
        continueButton.addEventListener('click', createSubscription);
    }

    async function recoverExistingSubscription() {
        try {
            const response = await fetch('/api/subscription/current', {
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            if (response.status === 404) return false;

            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data) {
                renderLoadError();
                return true;
            }

            showAlert('info', i18n.recovering);
            if (Number(payload.data.status) === 1) {
                const invoiceId = await findPendingInvoice();
                if (invoiceId) {
                    window.location.assign(`/Account/Payment?invoiceId=${encodeURIComponent(invoiceId)}`);
                    return true;
                }
            }

            await redirectToCurrentSetup('/Account/CampusSetup');
            return true;
        } catch {
            renderLoadError();
            return true;
        }
    }

    async function findPendingInvoice() {
        const response = await fetch('/api/subscription/invoices/unpaid', {
            cache: 'no-store',
            credentials: 'same-origin',
            headers: { 'Accept': 'application/json' }
        });
        const payload = await response.json().catch(() => null);
        if (!response.ok || !payload?.success || !Array.isArray(payload.data)) return null;
        const invoice = payload.data.find(item => positiveInteger(item.id) && Number(item.dueAmount) > 0);
        return positiveInteger(invoice?.id);
    }

    async function redirectToCurrentSetup(fallback) {
        try {
            const response = await fetch('/api/onboarding/status', {
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            const nextUrl = localUrl(payload?.data?.nextStepUrl);
            window.location.assign(nextUrl || fallback);
        } catch {
            window.location.assign(fallback);
        }
    }

    async function loadPlans() {
        setGridBusy(true);
        try {
            const response = await fetch('/api/subscription-plans', {
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !Array.isArray(payload.data)) {
                throw new Error('Invalid plan response');
            }
            plans = payload.data.filter(plan => positiveInteger(plan.id));
            renderPlans();
        } catch (error) {
            console.warn('EduOS plans were unavailable.', error);
            renderLoadError();
        } finally {
            setGridBusy(false);
        }
    }

    function renderPlans() {
        if (!plansGrid) return;
        if (!plans.length) {
            plansGrid.replaceChildren(createEmptyState(i18n.noPlans));
            return;
        }

        const cards = plans.map(plan => {
            const article = document.createElement('article');
            article.className = 'plan-selection-card';
            if (plan.isRecommended) article.classList.add('recommended');
            if (Number(plan.id) === selectedPlanId) article.classList.add('selected');

            const localizedName = localized(plan.name, plan.nameBangla);
            const content = document.createElement('div');
            content.className = 'plan-card-content';

            const choose = document.createElement('button');
            choose.type = 'button';
            choose.className = 'btn btn-outline-primary plan-select-button';
            choose.dataset.planId = String(plan.id);
            choose.dataset.planName = localizedName;
            choose.setAttribute('aria-pressed', String(Number(plan.id) === selectedPlanId));
            choose.setAttribute('aria-label', template(i18n.selectPlanTemplate, { name: localizedName }));
            choose.textContent = Number(plan.id) === selectedPlanId
                ? i18n.selected || ''
                : template(i18n.selectPlanTemplate, { name: localizedName });

            if (plan.isRecommended) {
                const badge = document.createElement('span');
                badge.className = 'plan-recommended-badge';
                badge.textContent = i18n.recommended || '';
                content.append(badge);
            }

            const heading = document.createElement('span');
            heading.className = 'plan-card-heading';
            const name = document.createElement('strong');
            name.textContent = localizedName;
            heading.append(name);

            const description = document.createElement('span');
            description.className = 'plan-card-description';
            description.textContent = localized(plan.shortDescription, plan.shortDescriptionBangla);

            const price = document.createElement('span');
            price.className = 'plan-card-price';
            const priceValue = document.createElement('strong');
            const period = document.createElement('small');
            if (plan.isFreeTrial) {
                priceValue.textContent = i18n.free || '';
                period.textContent = template(i18n.daysTrialTemplate, {
                    count: formatNumber(plan.trialDays || 0)
                });
            } else {
                priceValue.textContent = formatMoney(priceForCycle(plan, selectedCycle), plan.currency);
                period.textContent = template(i18n.perPeriodTemplate, {
                    period: i18n.periods?.[String(selectedCycle)] || ''
                });
            }
            price.append(priceValue, period);

            const limits = document.createElement('ul');
            limits.className = 'plan-card-limits';
            limits.append(
                listItem(template(i18n.studentsTemplate, { count: formatNumber(plan.maxStudents) })),
                listItem(template(i18n.teachersTemplate, { count: formatNumber(plan.maxTeachers) })),
                listItem(template(i18n.campusesTemplate, { count: formatNumber(plan.maxCampuses) }))
            );

            const features = document.createElement('ul');
            features.className = 'plan-card-features';
            const enabledFeatures = Array.isArray(plan.features) ? plan.features.slice(0, 6) : [];
            enabledFeatures.forEach(feature => {
                features.append(listItem(`✓ ${localized(feature.featureName, feature.featureNameBangla)}`));
            });
            const remaining = (Array.isArray(plan.features) ? plan.features.length : 0) - enabledFeatures.length;
            if (remaining > 0) {
                const more = listItem(template(i18n.moreFeaturesTemplate, { count: formatNumber(remaining) }));
                more.className = 'text-muted';
                features.append(more);
            }

            content.append(heading, description, price, limits, features);
            if (!plan.isFreeTrial && Number(plan.setupFee) > 0) {
                const fee = document.createElement('span');
                fee.className = 'plan-setup-fee';
                fee.textContent = template(i18n.setupFeeTemplate, {
                    amount: formatMoney(plan.setupFee, plan.currency)
                });
                content.append(fee);
            }
            choose.addEventListener('click', () => selectPlan(Number(plan.id)));
            article.append(content, choose);
            return article;
        });
        plansGrid.replaceChildren(...cards);
    }

    function selectPlan(planId) {
        if (!positiveInteger(planId)) return;
        selectedPlanId = planId;
        continueButton.disabled = false;
        plansGrid.querySelectorAll('.plan-selection-card').forEach(card => {
            const button = card.querySelector('button[data-plan-id]');
            const selected = Number(button?.dataset.planId) === planId;
            card.classList.toggle('selected', selected);
            button?.setAttribute('aria-pressed', String(selected));
            if (button) {
                button.textContent = selected
                    ? i18n.selected || ''
                    : template(i18n.selectPlanTemplate, { name: button.dataset.planName || '' });
            }
        });
    }

    function changeBillingCycle(event) {
        const button = event.target.closest('button[data-cycle]');
        const cycle = positiveInteger(button?.dataset.cycle);
        if (!button || !cycle || cycle > 4) return;
        selectedCycle = cycle;
        billingToggle.querySelectorAll('button[data-cycle]').forEach(item => {
            const active = item === button;
            item.classList.toggle('active', active);
            item.setAttribute('aria-pressed', String(active));
        });
        renderPlans();
    }

    async function createSubscription() {
        if (!selectedPlanId) return;
        setLoading(continueButton, true);
        try {
            const response = await fetch('/api/subscription', {
                method: 'POST',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                body: JSON.stringify({
                    subscriptionPlanId: selectedPlanId,
                    billingCycle: selectedCycle,
                    paymentMethod: 1,
                    autoRenew: true
                })
            });
            const payload = await response.json().catch(() => null);
            if (response.status === 409) {
                const recovered = await recoverExistingSubscription();
                if (!recovered) showAlert('danger', i18n.createFailed);
                return;
            }
            if (!response.ok || !payload?.success || !payload.data) {
                showAlert('danger', i18n.createFailed);
                return;
            }

            if (payload.data.isTrialActivated) {
                window.location.assign('/Account/CampusSetup');
                return;
            }
            const invoiceId = positiveInteger(payload.data.invoiceId);
            if (!invoiceId) {
                showAlert('danger', i18n.createFailed);
                return;
            }
            window.location.assign(`/Account/Payment?invoiceId=${encodeURIComponent(invoiceId)}`);
        } catch {
            showAlert('danger', i18n.networkError);
        } finally {
            setLoading(continueButton, false);
        }
    }

    function renderLoadError() {
        if (!plansGrid) return;
        const state = createEmptyState(i18n.loadFailed);
        const retry = document.createElement('button');
        retry.type = 'button';
        retry.className = 'btn btn-sm btn-outline-primary';
        retry.textContent = i18n.retry || '';
        retry.addEventListener('click', initialize, { once: true });
        state.append(retry);
        plansGrid.replaceChildren(state);
        setGridBusy(false);
    }

    function createEmptyState(message) {
        const state = document.createElement('div');
        state.className = 'setup-empty-state plan-grid-message';
        const text = document.createElement('p');
        text.textContent = message || '';
        state.append(text);
        return state;
    }

    function listItem(text) {
        const item = document.createElement('li');
        item.textContent = text || '';
        return item;
    }

    function localized(english, bangla) {
        return i18n.isBangla && String(bangla || '').trim()
            ? String(bangla).trim()
            : String(english || '').trim();
    }

    function priceForCycle(plan, cycle) {
        return Number({
            1: plan.monthlyPrice,
            2: plan.quarterlyPrice,
            3: plan.halfYearlyPrice,
            4: plan.yearlyPrice
        }[cycle] || 0);
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

    function formatNumber(value) {
        return Number(value || 0).toLocaleString(i18n.culture || 'en-BD');
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

    function localUrl(value) {
        if (!value) return null;
        try {
            const url = new URL(String(value), window.location.origin);
            return url.origin === window.location.origin ? `${url.pathname}${url.search}${url.hash}` : null;
        } catch {
            return null;
        }
    }

    function setGridBusy(busy) {
        plansGrid?.setAttribute('aria-busy', String(Boolean(busy)));
    }

    function showAlert(type, message) {
        const container = document.getElementById('alertContainer');
        if (!container) return;
        container.className = `alert alert-${type === 'info' ? 'info' : 'danger'}`;
        container.textContent = message || '';
        container.focus();
    }

    function setLoading(button, loading) {
        if (!button) return;
        button.disabled = loading || !selectedPlanId;
        button.replaceChildren();
        if (loading) {
            const spinner = document.createElement('span');
            spinner.className = 'spinner-border spinner-border-sm me-2';
            spinner.setAttribute('aria-hidden', 'true');
            button.append(spinner, button.dataset.loadingLabel || i18n.processing || '');
        } else {
            button.textContent = button.dataset.idleLabel || i18n.continueLabel || '';
        }
    }
})();
