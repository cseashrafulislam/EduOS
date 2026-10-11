(() => {
    'use strict';
    const root = document.getElementById('tenantUsersApp');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const state = { page: 1, pageSize: 20, total: 0, members: [], roles: [], permissions: [], selected: null };
    const endpoint = '/api/tenant-users';

    function message(text, danger = false) {
        const node = $('adminMessage');
        node.className = 'alert ' + (danger ? 'alert-danger' : 'alert-success');
        node.textContent = text;
        node.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    async function request(path, init = {}) {
        const response = await fetch(endpoint + path, {
            credentials: 'same-origin', cache: 'no-store',
            headers: { 'Accept': 'application/json', ...(init.body ? { 'Content-Type': 'application/json' } : {}) },
            ...init
        });
        const result = await response.json().catch(() => null);
        if (!response.ok || result?.success !== true)
            throw new Error(result?.message || 'Unable to complete request.');
        return result.data;
    }

    function textNode(tag, text, className) {
        const node = document.createElement(tag);
        node.textContent = String(text ?? '');
        if (className) node.className = className;
        return node;
    }

    function checkbox(id, labelText, checked = false) {
        const holder = textNode('label', '', 'form-check');
        const control = document.createElement('input');
        control.type = 'checkbox'; control.value = String(id); control.checked = checked;
        control.className = 'form-check-input me-2';
        const title = textNode('span', labelText, 'form-check-label');
        holder.append(control, title);
        return holder;
    }

    async function loadCatalog() {
        const [roles, permissions] = await Promise.all([request('/roles'), request('/permissions')]);
        state.roles = roles;
        state.permissions = permissions;
        const invitationRoles = $('inviteRole');
        const invitationSelected = invitationRoles.value;
        invitationRoles.replaceChildren(new Option('No initial role', ''));
        roles.filter(x => x.isActive).forEach(role => invitationRoles.append(new Option(role.name, String(role.id))));
        if (roles.some(x => String(x.id) === invitationSelected)) invitationRoles.value = invitationSelected;
        const selector = $('permissionRole');
        const selected = selector.value;
        selector.replaceChildren();
        roles.forEach(role => {
            const option = textNode('option', role.name);
            option.value = String(role.id);
            selector.append(option);
        });
        if (roles.some(x => String(x.id) === selected)) selector.value = selected;
        renderPermissions();
        if (state.selected) renderMemberEditor();
    }

    async function loadMembers() {
        const query = new URLSearchParams({
            page: state.page, pageSize: state.pageSize, search: $('memberSearch').value.trim()
        });
        const result = await request('?' + query);
        state.members = result.items || [];
        state.total = result.totalCount || 0;
        const tbody = $('tenantUsersBody');
        tbody.replaceChildren();
        if (!state.members.length) {
            const tr = document.createElement('tr'), td = textNode('td', 'No matching users.');
            td.colSpan = 4; tr.append(td); tbody.append(tr);
        }
        state.members.forEach(member => {
            const tr = document.createElement('tr');
            const identity = document.createElement('td');
            identity.append(textNode('strong', member.fullName), textNode('div', member.email, 'text-muted small'));
            tr.append(identity);
            tr.append(textNode('td', (member.isOwner ? 'Owner • ' : '') +
                ({ 2: 'Active', 3: 'Suspended', 4: 'Left' }[member.status] || member.status)));
            const names = member.roleIds.map(id => state.roles.find(role => role.id === id)?.name)
                .filter(Boolean).join(', ');
            tr.append(textNode('td', member.isOwner ? 'TenantAdmin (owner)' + (names ? ', ' + names : '') : names || 'No assigned role'));
            const actions = document.createElement('td');
            const button = textNode('button', 'Manage', 'btn btn-outline-primary btn-sm');
            button.type = 'button';
            button.addEventListener('click', () => { state.selected = member; renderMemberEditor(); });
            actions.append(button); tr.append(actions); tbody.append(tr);
        });
        $('memberCount').textContent = 'Page ' + state.page + ' • ' + state.total + ' members';
        $('prevMembers').disabled = state.page <= 1;
        $('nextMembers').disabled = state.page * state.pageSize >= state.total;
    }

    function renderMemberEditor() {
        const member = state.selected;
        if (!member) { $('memberEditor').classList.add('d-none'); return; }
        $('memberEditor').classList.remove('d-none');
        $('memberEditorTitle').textContent = member.fullName + ' — institution roles';
        const options = $('memberRoleOptions');
        options.replaceChildren();
        state.roles.filter(x => x.isActive).forEach(role => {
            const cell = document.createElement('div');
            cell.append(checkbox(role.id, role.name, member.roleIds.includes(role.id)));
            options.append(cell);
        });
        $('memberStatusSelect').value = String(member.status);
        $('memberStatusSelect').disabled = member.isOwner;
        $('saveMemberStatus').disabled = member.isOwner;
        $('memberEditor').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    function renderPermissions() {
        const role = state.roles.find(x => String(x.id) === $('permissionRole').value);
        const container = $('permissionOptions');
        container.replaceChildren();
        for (const perm of state.permissions) {
            const cell = document.createElement('div');
            cell.className = 'col-md-4';
            cell.append(checkbox(perm.id, perm.moduleCode + ' • ' + perm.name,
                role?.permissionIds?.includes(perm.id)));
            container.append(cell);
        }
    }

    async function submit(button, operation) {
        button.disabled = true;
        try { await operation(); }
        catch (error) { message(error.message, true); }
        finally { button.disabled = false; }
    }

    $('tabUsers').addEventListener('click', () => switchTab(false));
    $('tabRoles').addEventListener('click', () => switchTab(true));
    function switchTab(roles) {
        $('usersSection').classList.toggle('d-none', roles);
        $('rolesSection').classList.toggle('d-none', !roles);
        $('tabUsers').classList.toggle('active', !roles);
        $('tabRoles').classList.toggle('active', roles);
    }

    $('memberSearchForm').addEventListener('submit', async e => {
        e.preventDefault(); state.page = 1;
        try { await loadMembers(); } catch (error) { message(error.message, true); }
    });
    $('refreshUsers').addEventListener('click', async () => {
        try { await loadMembers(); } catch (error) { message(error.message, true); }
    });
    $('prevMembers').addEventListener('click', async () => {
        if (state.page <= 1) return; state.page--;
        try { await loadMembers(); } catch (error) { message(error.message, true); }
    });
    $('nextMembers').addEventListener('click', async () => {
        if (state.page * state.pageSize >= state.total) return; state.page++;
        try { await loadMembers(); } catch (error) { message(error.message, true); }
    });
    $('inviteUserForm').addEventListener('submit', async e => {
        e.preventDefault();
        await submit(e.submitter, async () => {
            await request('/invitations', { method: 'POST', body: JSON.stringify({
                email: $('inviteEmail').value.trim(),
                roleId: $('inviteRole').value ? Number($('inviteRole').value) : null
            }) });
            e.target.reset();
            message('An invitation email has been sent. The user must accept it before appearing here.');
        });
    });

    $('createTenantRoleForm').addEventListener('submit', async e => {
        e.preventDefault();
        await submit(e.submitter, async () => {
            await request('/roles', { method: 'POST', body: JSON.stringify({
                name: $('newRoleName').value.trim(),
                description: $('newRoleDescription').value.trim()
            }) });
            e.target.reset(); await loadCatalog(); await loadMembers();
            message('Institution role created.');
        });
    });
    $('saveMemberRolesForm').addEventListener('submit', async e => {
        e.preventDefault();
        if (!state.selected) return;
        await submit(e.submitter, async () => {
            const roleIds = [...$('memberRoleOptions').querySelectorAll('input:checked')].map(x => Number(x.value));
            await request('/' + state.selected.membershipId + '/roles', {
                method: 'PUT', body: JSON.stringify({ roleIds, rowVersion: state.selected.rowVersion })
            });
            state.selected = null; $('memberEditor').classList.add('d-none');
            await loadMembers(); message('Roles saved. The user must sign in again.');
        });
    });
    $('saveMemberStatus').addEventListener('click', async e => {
        if (!state.selected || state.selected.isOwner) return;
        const status = Number($('memberStatusSelect').value);
        if (!confirm('Change this institution membership status?')) return;
        await submit(e.currentTarget, async () => {
            await request('/' + state.selected.membershipId + '/status', {
                method: 'PUT', body: JSON.stringify({ status, rowVersion: state.selected.rowVersion })
            });
            state.selected = null; $('memberEditor').classList.add('d-none');
            await loadMembers(); message('Membership status saved.');
        });
    });
    $('permissionRole').addEventListener('change', renderPermissions);
    $('saveRolePermissionsForm').addEventListener('submit', async e => {
        e.preventDefault();
        const roleId = Number($('permissionRole').value);
        if (!roleId) { message('Select an institution role.', true); return; }
        await submit(e.submitter, async () => {
            const permissionIds = [...$('permissionOptions').querySelectorAll('input:checked')].map(x => Number(x.value));
            await request('/roles/' + roleId + '/permissions', {
                method: 'PUT', body: JSON.stringify({ permissionIds })
            });
            await loadCatalog(); message('Role permissions saved. Affected users must sign in again.');
        });
    });

    (async () => {
        try { await loadCatalog(); await loadMembers(); }
        catch (error) { message(error.message, true); }
    })();
})();
