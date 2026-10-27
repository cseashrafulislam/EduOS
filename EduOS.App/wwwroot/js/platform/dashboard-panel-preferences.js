(() => {
    'use strict';
    const container = document.querySelector('[data-dashboard-panels]');
    if (!container) return;

    const allowed = ['capacity', 'quick-actions', 'setup-checklist'];
    const elements = new Map(
        [...container.querySelectorAll(':scope > [data-dashboard-panel]')]
            .map(element => [element.dataset.dashboardPanel, element])
    );
    if (elements.size !== allowed.length || !allowed.every(id => elements.has(id))) return;

    function apply(panels) {
        if (!Array.isArray(panels) || panels.length !== allowed.length ||
            !panels.every(panel => panel && allowed.includes(panel.id) && typeof panel.visible === 'boolean') ||
            new Set(panels.map(panel => panel.id)).size !== allowed.length) return;

        // Move real, server-rendered panels only; never construct HTML or hide mandatory status.
        panels.forEach(panel => {
            const element = elements.get(panel.id);
            element.hidden = !panel.visible;
            container.appendChild(element);
        });
    }

    window.addEventListener('eduos:workspace-preferences', event => apply(event.detail?.dashboardPanels));
    apply(window.EduOSWorkspacePreferences?.dashboardPanels);
})();
