(() => {
    'use strict';

    const stringsNode = document.getElementById('moduleSetupStrings');
    const i18n = stringsNode ? JSON.parse(stringsNode.textContent || '{}') : {};
    const list = document.getElementById('moduleList');
    const summary = document.getElementById('moduleSummary');
    const continueButton = document.getElementById('continueBtn');
    let modules = [];
    let onboarding = null;

    document.addEventListener('DOMContentLoaded', async () => {
        continueButton?.addEventListener('click', completeStep);
        await Promise.all([loadModules(), loadOnboardingState()]);
        updateSummary();
    });

    async function loadOnboardingState() {
        try {
            const response = await fetch('/api/onboarding/status', {
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (response.ok && payload?.success && payload.data) onboarding = payload.data;
        } catch {
            onboarding = null;
        }
    }

    async function loadModules() {
        setListBusy(true);
        try {
            const response = await fetch('/api/tenant-modules', {
                cache: 'no-store',
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !Array.isArray(payload.data)) {
                throw new Error('Invalid module response');
            }

            modules = payload.data;
            renderModules();
        } catch {
            renderLoadError();
        } finally {
            setListBusy(false);
        }
    }

    function renderModules(focusCode) {
        if (!list) return;
        if (modules.length === 0) {
            const empty = document.createElement('div');
            empty.className = 'setup-empty-state';
            empty.textContent = i18n.noModules || '';
            list.replaceChildren(empty);
            updateSummary();
            return;
        }

        const groups = new Map();
        modules.forEach(module => {
            const category = module.category || 'Platform';
            if (!groups.has(category)) groups.set(category, []);
            groups.get(category).push(module);
        });

        const groupElements = Array.from(groups, ([category, items]) =>
            createModuleGroup(category, items));
        list.replaceChildren(...groupElements);
        updateSummary();

        if (focusCode) {
            document.getElementById(moduleInputId(focusCode))?.focus();
        }
    }

    function createModuleGroup(category, items) {
        const section = document.createElement('section');
        section.className = 'module-group setup-section';

        const title = document.createElement('h3');
        title.className = 'module-group-title';
        title.textContent = i18n.categories?.[category] || category;

        const grid = document.createElement('div');
        grid.className = 'module-card-grid';
        grid.append(...items.map(createModuleCard));
        section.append(title, grid);
        return section;
    }

    function createModuleCard(module) {
        const card = document.createElement('article');
        card.className = 'module-card';
        if (module.isSelected) card.classList.add('selected');
        if (!module.isIncludedInPlan) card.classList.add('not-entitled');

        const headingRow = document.createElement('div');
        headingRow.className = 'module-card-heading';

        const heading = document.createElement('h4');
        heading.textContent = localizedName(module);

        const inputId = moduleInputId(module.code);
        const toggle = document.createElement('input');
        toggle.type = 'checkbox';
        toggle.className = 'form-check-input module-toggle';
        toggle.id = inputId;
        toggle.checked = Boolean(module.isSelected);
        toggle.disabled = Boolean(
            module.isCore || module.isRequiredForInstitution ||
            (!module.isSelected && !module.canEnable));
        toggle.setAttribute('aria-label', localizedName(module));
        toggle.setAttribute('aria-describedby', `${inputId}-status`);
        toggle.addEventListener('change', () => updateModule(module, toggle));
        headingRow.append(heading, toggle);

        const badges = document.createElement('div');
        badges.className = 'module-badges';
        if (module.isCore) badges.append(createBadge(i18n.core, 'core'));
        if (module.isRequiredForInstitution) badges.append(createBadge(i18n.required, 'required'));
        badges.append(createBadge(
            module.isIncludedInPlan ? i18n.included : i18n.notIncluded,
            module.isIncludedInPlan ? 'included' : 'excluded'));

        const status = document.createElement('p');
        status.className = 'module-status';
        status.id = `${inputId}-status`;
        status.textContent = availabilityText(module);

        card.append(headingRow, badges, status);
        return card;
    }

    function createBadge(label, variant) {
        const badge = document.createElement('span');
        badge.className = `module-badge ${variant}`;
        badge.textContent = label || '';
        return badge;
    }

    async function updateModule(module, toggle) {
        const requestedState = toggle.checked;
        toggle.disabled = true;
        toggle.setAttribute('aria-busy', 'true');

        try {
            const response = await fetch(
                `/api/tenant-modules/${encodeURIComponent(module.code)}/activation`,
                {
                    method: 'PUT',
                    cache: 'no-store',
                    credentials: 'same-origin',
                    headers: {
                        'Accept': 'application/json',
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({
                        isEnabled: requestedState,
                        rowVersion: module.rowVersion || null
                    })
                });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data) {
                throw new Error(String(response.status));
            }

            modules = modules.map(item => item.code === module.code ? payload.data : item);
            renderModules(module.code);
        } catch (error) {
            toggle.checked = !requestedState;
            showAlert('danger', i18n.changeFailed);
            if (error instanceof Error && error.message === '428') await loadModules();
        } finally {
            toggle.removeAttribute('aria-busy');
            if (toggle.isConnected) toggle.disabled = false;
        }
    }

    function updateSummary() {
        const selectedCount = modules.filter(x => x.isSelected && x.isIncludedInPlan).length;
        const requiredUnavailable = modules.some(x => x.isRequiredForInstitution && !x.isAvailable);
        if (summary) {
            summary.textContent = requiredUnavailable
                ? i18n.requiredUnavailable
                : String(i18n.selectedSummary || '')
                    .replace('{selected}', selectedCount.toLocaleString(i18n.culture))
                    .replace('{total}', modules.length.toLocaleString(i18n.culture));
        }
        if (continueButton) {
            continueButton.textContent = continueActionLabel();
            continueButton.disabled = modules.length === 0 || requiredUnavailable || !onboarding;
        }
    }

    function availabilityText(module) {
        switch (module.availabilityReasonCode) {
            case 'AVAILABLE': return i18n.moduleAvailable || '';
            case 'MODULE_NOT_SELECTED': return i18n.moduleNotSelected || '';
            case 'OUTSIDE_EFFECTIVE_PERIOD': return i18n.moduleOutsidePeriod || '';
            case 'NOT_INCLUDED_IN_PLAN': return i18n.moduleUpgradeRequired || '';
            default: return module.isSelected ? i18n.selected : i18n.notSelected;
        }
    }

    function localizedName(module) {
        const isBangla = String(i18n.culture || '').toLowerCase().startsWith('bn');
        return isBangla && module.nameBangla ? module.nameBangla : module.name;
    }

    async function completeStep() {
        if (onboarding?.isComplete) {
            window.location.assign('/Dashboard/Index');
            return;
        }

        if (Number(onboarding?.currentStep) !== 9) {
            window.location.assign(safeLocalUrl(onboarding?.nextStepUrl));
            return;
        }

        setButtonLoading(continueButton, true);
        try {
            const response = await fetch('/api/onboarding/complete-step', {
                method: 'POST',
                cache: 'no-store',
                credentials: 'same-origin',
                headers: {
                    'Accept': 'application/json',
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ step: 9, skipped: false })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) {
                showAlert('danger', i18n.stepFailed);
                return;
            }
            window.location.assign('/Account/BrandingSetup');
        } catch {
            showAlert('danger', i18n.networkError);
        } finally {
            setButtonLoading(continueButton, false);
        }
    }

    function renderLoadError() {
        if (!list) return;
        const error = document.createElement('div');
        error.className = 'setup-empty-state';
        error.setAttribute('role', 'alert');

        const message = document.createElement('p');
        message.textContent = i18n.loadFailed || '';
        const retry = document.createElement('button');
        retry.type = 'button';
        retry.className = 'btn btn-outline-primary btn-sm';
        retry.textContent = i18n.retry || '';
        retry.addEventListener('click', loadModules);
        error.append(message, retry);
        list.replaceChildren(error);
        if (continueButton) continueButton.disabled = true;
    }

    function showAlert(type, message) {
        const container = document.getElementById('alertContainer');
        if (!container) return;
        container.className = `alert alert-${type}`;
        container.textContent = message || '';
        container.focus();
    }

    function setListBusy(isBusy) {
        list?.setAttribute('aria-busy', String(isBusy));
    }

    function setButtonLoading(button, loading) {
        if (!button) return;
        button.disabled = loading;
        button.textContent = loading
            ? button.dataset.loadingLabel || i18n.continuing || ''
            : continueActionLabel();
    }

    function continueActionLabel() {
        if (onboarding?.isComplete) return i18n.dashboard || '';
        if (onboarding && Number(onboarding.currentStep) !== 9) return i18n.continueSetup || '';
        return continueButton?.dataset.idleLabel || i18n.continueLabel || '';
    }

    function safeLocalUrl(value) {
        const candidate = String(value || '');
        return candidate.startsWith('/') && !candidate.startsWith('//')
            ? candidate
            : '/Dashboard/Index';
    }

    function moduleInputId(code) {
        return `module-${String(code || '').toLowerCase().replace(/[^a-z0-9_-]/g, '-')}`;
    }
})();
