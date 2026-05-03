// ============================================================
// ONBOARDING-SHARED.JS
// Shared across all wizard pages via _OnboardingLayout
// ============================================================

// ── Load sidebar onboarding status ───────────────────────────
async function loadOnboardingStatus() {
    try {
        const res = await fetch('/api/onboarding/status', { credentials: 'include' });
        if (!res.ok) return;
        const json = await res.json();
        if (json.success && json.data) renderSidebar(json.data);
    } catch (e) {
        console.warn('Could not load onboarding status', e);
    }
}

function renderSidebar(status) {
    // Progress bar
    const bar = document.getElementById('progressBar');
    const text = document.getElementById('progressText');
    const doneCount = document.getElementById('completedCount');
    const totalCount = document.getElementById('totalCount');

    if (bar) bar.style.width = status.progressPercentage + '%';
    if (text) text.textContent = status.progressPercentage + '%';
    if (doneCount) doneCount.textContent = status.completedSteps;
    if (totalCount) totalCount.textContent = status.totalSteps;

    // Step list
    const list = document.getElementById('stepsList');
    if (!list) return;
    list.innerHTML = '';

    (status.steps || []).forEach(step => {
        const a = document.createElement('a');
        a.href = step.isLocked ? '#' : step.url;
        a.className = 'step-item';
        if (step.isCurrent) a.classList.add('current');
        if (step.isCompleted) a.classList.add('completed');
        if (step.isLocked) {
            a.classList.add('locked');
            a.addEventListener('click', e => e.preventDefault());
        }

        const iconHtml = step.isCompleted
            ? '<i class="bi bi-check-lg"></i>'
            : `<i class="${step.iconClass || 'bi bi-circle'}"></i>`;

        a.innerHTML = `
            <div class="step-icon">${iconHtml}</div>
            <div class="step-content">
                <div class="step-name">
                    ${escHtml(step.name)}
                    ${step.isSkippable ? '<span class="step-skip-badge">Optional</span>' : ''}
                </div>
                <div class="step-desc">${escHtml(step.description)}</div>
            </div>`;

        list.appendChild(a);
    });
}

// ── Global escape helper (used by all pages) ──────────────────
function escHtml(s) {
    return (s || '').replace(/[&<>"']/g, c =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}

// Auto-load on every wizard page
document.addEventListener('DOMContentLoaded', loadOnboardingStatus);
