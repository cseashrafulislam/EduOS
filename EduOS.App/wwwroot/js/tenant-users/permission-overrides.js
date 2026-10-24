(() => {
    'use strict';
    const root = document.getElementById('tenantPermissionOverridesApp');
    if (!root) return;
    const el = id => document.getElementById(id);
    const state = { page: 1, pageSize: 20, total: 0, members: [], permissions: [], member: null, overrides: [] };
    const token = () => root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const cell = (tag, value) => { const node = document.createElement(tag); node.textContent = String(value ?? ''); return node; };

    function message(text, failed = false) {
        const node = el('permissionOverrideMessage');
        node.className = 'alert ' + (failed ? 'alert-danger' : 'alert-success');
        node.textContent = text;
    }
    async function api(path, method = 'GET', body) {
        const response = await fetch('/api/tenant-users' + path, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json',
                ...(method !== 'GET' ? { 'Content-Type': 'application/json', RequestVerificationToken: token() } : {}) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        const result = await response.json().catch(() => null);
        if (!response.ok || result?.success !== true)
            throw new Error(result?.message || 'Request failed. Reload before retrying.');
        return result.data;
    }
    function selectedPermission() {
        return state.permissions.find(x => String(x.id) === el('permissionOverrideCode').value);
    }
    function selectedOverride() {
        const permission = selectedPermission();
        return state.overrides.find(x => x.permissionId === permission?.id);
    }
    function resetEditor() {
        const override = selectedOverride();
        el('permissionOverrideMode').value = override?.mode || 'Inherit';
        el('permissionOverrideReason').value = override?.reason || '';
        el('permissionOverrideExpiry').value = override?.expiresAt
            ? toLocalDateTime(override.expiresAt) : '';
        updateInheritFields();
    }
    function toLocalDateTime(iso) {
        const d = new Date(iso);
        if (!Number.isFinite(d.getTime())) return '';
        const pad = x => String(x).padStart(2, '0');
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) +
            'T' + pad(d.getHours()) + ':' + pad(d.getMinutes());
    }
    function updateInheritFields() {
        const inherit = el('permissionOverrideMode').value === 'Inherit';
        el('permissionOverrideReason').disabled = inherit;
        el('permissionOverrideExpiry').disabled = inherit;
        if (inherit) {
            el('permissionOverrideReason').value = '';
            el('permissionOverrideExpiry').value = '';
        }
    }
    function renderOverrides() {
        const tbody = el('permissionOverrideRows');
        tbody.replaceChildren();
        if (!state.member) {
            const tr = cell('tr', ''), td = cell('td', 'Choose an active member.'); td.colSpan = 5;
            tr.append(td); tbody.append(tr); return;
        }
        if (!state.overrides.length) {
            const tr = cell('tr', ''), td = cell('td', 'No exceptions. Role permissions apply.'); td.colSpan = 5;
            tr.append(td); tbody.append(tr);
        }
        for (const entry of state.overrides) {
            const tr = cell('tr', '');
            const permission = state.permissions.find(x => x.id === entry.permissionId);
            tr.append(cell('td', permission ? permission.moduleCode + ' · ' + permission.name : entry.permissionCode),
                cell('td', entry.mode), cell('td', entry.reason || '—'),
                cell('td', entry.expiresAt ? new Date(entry.expiresAt).toLocaleString() : 'Never'));
            const actions = cell('td', '');
            const edit = cell('button', 'Edit', 'btn btn-outline-primary btn-sm');
            edit.type = 'button';
            edit.addEventListener('click', () => {
                el('permissionOverrideCode').value = String(entry.permissionId);
                resetEditor();
                el('permissionOverrideEditor').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            });
            actions.append(edit); tr.append(actions); tbody.append(tr);
        }
    }
    async function loadOverrides() {
        if (!state.member) {
            state.overrides = []; el('permissionOverrideEditor').hidden = true;
            renderOverrides(); return;
        }
        const memberId = state.member.membershipId;
        const rows = await api('/' + memberId + '/permission-overrides');
        if (state.member?.membershipId !== memberId) return;
        state.overrides = rows || [];
        el('permissionOverrideEditor').hidden = false;
        renderOverrides(); resetEditor();
    }
    async function loadMembers() {
        const search = el('permissionMemberSearch').value.trim();
        const params = new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize), search });
        const result = await api('?' + params.toString());
        state.members = result.items || [];
        state.total = result.totalCount || 0;
        const select = el('permissionMember');
        const selectedId = state.member?.membershipId;
        select.replaceChildren(new Option('Select active member', ''));
        for (const member of state.members.filter(x => x.status === 2 && x.isActive && !x.isOwner))
            select.append(new Option(member.fullName + ' · ' + member.email, String(member.membershipId)));
        const stillPresent = state.members.find(x => x.membershipId === selectedId &&
            x.status === 2 && x.isActive && !x.isOwner);
        state.member = stillPresent || null;
        select.value = stillPresent ? String(stillPresent.membershipId) : '';
        el('permissionMembersPage').textContent = 'Page ' + state.page + ' · ' + state.total + ' members';
        el('permissionMembersPrev').disabled = state.page <= 1;
        el('permissionMembersNext').disabled = state.page * state.pageSize >= state.total;
        await loadOverrides();
    }
    async function loadPermissions() {
        state.permissions = await api('/permissions');
        const select = el('permissionOverrideCode');
        select.replaceChildren();
        for (const permission of state.permissions)
            select.append(new Option(permission.moduleCode + ' · ' + permission.name + ' (' + permission.code + ')',
                String(permission.id)));
    }
    el('permissionMemberSearchForm').addEventListener('submit', async event => {
        event.preventDefault(); state.page = 1;
        try { await loadMembers(); } catch (error) { message(error.message, true); }
    });
    el('permissionMembersPrev').addEventListener('click', async () => {
        if (state.page <= 1) return; state.page--;
        try { await loadMembers(); } catch (error) { message(error.message, true); }
    });
    el('permissionMembersNext').addEventListener('click', async () => {
        if (state.page * state.pageSize >= state.total) return; state.page++;
        try { await loadMembers(); } catch (error) { message(error.message, true); }
    });
    el('permissionMember').addEventListener('change', async event => {
        state.member = state.members.find(x => String(x.membershipId) === event.target.value) || null;
        try { await loadOverrides(); } catch (error) { message(error.message, true); }
    });
    el('permissionOverrideCode').addEventListener('change', resetEditor);
    el('permissionOverrideMode').addEventListener('change', updateInheritFields);
    el('permissionOverrideReload').addEventListener('click', async () => {
        try { await loadMembers(); message('Member permissions refreshed.'); }
        catch (error) { message(error.message, true); }
    });
    el('permissionOverrideForm').addEventListener('submit', async event => {
        event.preventDefault();
        const member = state.member, permission = selectedPermission();
        if (!member || !permission) return message('Choose a member and permission.', true);
        const mode = el('permissionOverrideMode').value;
        const expiry = el('permissionOverrideExpiry').value;
        const expiresAt = mode !== 'Inherit' && expiry ? new Date(expiry).toISOString() : null;
        if (expiresAt && Date.parse(expiresAt) <= Date.now())
            return message('Expiry must be in the future.', true);
        const existing = selectedOverride();
        const button = el('permissionOverrideSave');
        button.disabled = true;
        try {
            await api('/' + member.membershipId + '/permission-overrides/' + permission.id, 'PUT', {
                membershipRowVersion: member.rowVersion,
                overrideRowVersion: existing?.rowVersion || null,
                mode, reason: mode === 'Inherit' ? null : el('permissionOverrideReason').value.trim() || null,
                expiresAt
            });
            await loadOverrides();
            message('Permission exception saved. Access is checked against current institution data.');
        } catch (error) { message(error.message, true); }
        finally { button.disabled = false; }
    });
    (async () => {
        try { await loadPermissions(); await loadMembers(); }
        catch (error) { message(error.message, true); }
    })();
})();
