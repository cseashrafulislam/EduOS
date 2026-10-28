(() => {
    'use strict';
    // Only allowlisted institution-admin and audit tables opt in. Preferences never alter API/export permissions.
    const definitions = {
        'institution-users': { ids: ['user', 'membership', 'roles', 'action'], required: ['user', 'action'] },
        'institution-invitations': { ids: ['email', 'role', 'campus', 'expires', 'status', 'action'], required: ['email', 'status', 'action'] },
        'institution-feature-audit': { ids: ['feature', 'code', 'plan', 'override', 'restriction', 'reason'], required: ['feature', 'code', 'restriction'] },
        'institution-activity-audit': { ids: ['time', 'user', 'action', 'entity', 'record', 'ip', 'outcome'], required: ['time', 'user', 'action', 'outcome'] },
        'institution-campuses': { ids: ['campus', 'code', 'contact', 'type', 'action'], required: ['campus', 'code', 'action'] },
        'institution-change-history': { ids: ['when', 'action', 'fields', 'actor'], required: ['when', 'action', 'actor'] }
    };
    const tables = [...document.querySelectorAll('table[data-workspace-table]')];

    function valid(table, definition) {
        return table && Array.isArray(table.columns) && table.columns.length === definition.ids.length &&
            new Set(table.columns.map(column => column?.id)).size === definition.ids.length &&
            table.columns.every(column => column && definition.ids.includes(column.id) && typeof column.visible === 'boolean') &&
            definition.required.every(id => table.columns.find(column => column.id === id).visible);
    }

    tables.forEach(table => {
        const id = table.dataset.workspaceTable;
        const definition = definitions[id];
        const header = table.tHead?.rows[0];
        const body = table.tBodies[0];
        if (!definition || !header || !body ||
            header.cells.length !== definition.ids.length ||
            !definition.ids.every(column => [...header.cells].some(cell => cell.dataset.tableColumn === column))) return;

        let selected = definition.ids.map(column => ({ id: column, visible: true }));
        function arrange(row, columns) {
            if (row.cells.length !== definition.ids.length) return; // Preserve loading/empty colspan rows.
            const cells = [...row.cells];
            cells.forEach((cell, index) => {
                if (!cell.dataset.tableColumn) cell.dataset.tableColumn = definition.ids[index];
            });
            const byId = new Map(cells.map(cell => [cell.dataset.tableColumn, cell]));
            if (byId.size !== definition.ids.length || !definition.ids.every(column => byId.has(column))) return;
            columns.forEach(column => {
                const cell = byId.get(column.id);
                cell.hidden = !column.visible;
                row.appendChild(cell); // Move the original node; preserve actions, text and event handlers.
            });
        }
        function apply() {
            arrange(header, selected);
            [...body.rows].forEach(row => arrange(row, selected));
        }
        function update(tablesPreference) {
            const preference = Array.isArray(tablesPreference)
                ? tablesPreference.find(entry => entry?.id === id) : null;
            selected = valid(preference, definition) ? preference.columns
                : definition.ids.map(column => ({ id: column, visible: true }));
            apply();
        }
        // Member/invitation tables are re-rendered after search, pagination and revocation.
        // Observe direct row replacement only; rearranging cells does not retrigger this observer.
        const observer = new MutationObserver(apply);
        observer.observe(body, { childList: true });
        window.addEventListener('eduos:workspace-preferences', event => update(event.detail?.tablePreferences));
        update(window.EduOSWorkspacePreferences?.tablePreferences);
    });
})();
