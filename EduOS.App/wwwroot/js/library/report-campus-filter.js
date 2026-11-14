(() => {
    'use strict';
    // A single, server-authorized campus selector used by the three Library report workspaces.
    // Existing query code still sends the selected internal ID; users select by campus name.
    const fieldIds = ['libraryIntegrityCampus', 'libraryOverdueCampus', 'circulationCampus'];
    const controls = fieldIds.map(id => document.getElementById(id)).filter(Boolean);
    if (!controls.length) return;
    const tenant = document.body?.dataset.authTenantId || '';
    const session = document.body?.dataset.authSessionReference || '';
    async function load() {
        const response = await fetch('/api/library/report-campus-options', {
            credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json' }
        });
        const payload = await response.json().catch(() => null);
        if (!response.ok || payload?.success !== true || !Array.isArray(payload.data))
            return; // Fail closed: "all authorized" remains selected and server policy still governs reports.
        if (document.body?.dataset.authTenantId !== tenant ||
            document.body?.dataset.authSessionReference !== session ||
            window.EduOSSessionTransitionPending?.() === true) return;
        for (const control of controls) {
            const previous = control.value;
            control.replaceChildren(new Option('All authorized campuses', ''));
            for (const campus of payload.data) {
                if (!Number.isSafeInteger(Number(campus.id)) || Number(campus.id) < 1 ||
                    typeof campus.name !== 'string') continue;
                control.add(new Option(campus.name, String(campus.id)));
            }
            if ([...control.options].some(x => x.value === previous)) control.value = previous;
            control.disabled = false;
        }
    }
    load().catch(() => {}); // Default is still tenant/role-restricted report.
})();