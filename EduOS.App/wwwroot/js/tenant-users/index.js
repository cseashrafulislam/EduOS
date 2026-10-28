(() => {
    'use strict';
    const root = document.getElementById('tenantUsersApp');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const state = { page: 1, pageSize: 20, total: 0, members: [], roles: [], permissions: [], selected: null,
        historyPage: 1, historyTotal: 0 };
    const endpoint = '/api/tenant-users';
    const bn = document.documentElement.lang.toLowerCase().startsWith('bn');
    const t = (en, bangla) => bn ? bangla : en;

    function message(text, danger = false) {
        const node = $('adminMessage');
        node.className = 'alert ' + (danger ? 'alert-danger' : 'alert-success');
        node.textContent = text;
        node.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    async function request(path, init = {}) {
        const response = await fetch(endpoint + path, {
            credentials: 'same-origin', cache: 'no-store',
            headers: { 'Accept': 'application/json', ...(init.body ? { 'Content-Type': 'application/json', 'RequestVerificationToken': root.querySelector('input[name="__RequestVerificationToken"]')?.value || '' } : {}) },
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

    async function loadInviteCampuses() {
        const response = await fetch('/api/institution-onboarding/campus-list', {
            credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
        });
        const result = await response.json().catch(() => null);
        if (!response.ok || result?.success !== true)
            throw new Error(result?.message || 'Unable to load institution campuses.');
        const selector = $('inviteCampus');
        const selected = selector.value;
        selector.replaceChildren(new Option('No campus (assign later)', ''));
        (result.data || []).filter(x => x.isActive).forEach(campus =>
            selector.append(new Option(campus.name, String(campus.id))));
        if ([...selector.options].some(x => x.value === selected)) selector.value = selected;
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
            const roleCell = textNode('td', member.isOwner ? 'TenantAdmin (owner)' + (names ? ', ' + names : '') : names || 'No assigned role');
            if (member.unscopedIdentityRoleNames?.length) roleCell.append(textNode('div',
                'Global Identity role grants (not institution-scoped): ' + member.unscopedIdentityRoleNames.join(', '), 'small text-warning'));
            tr.append(roleCell);
            const actions = document.createElement('td');
            const button = textNode('button', 'Manage', 'btn btn-outline-primary btn-sm');
            button.type = 'button';
            button.addEventListener('click', () => {
                state.selected = member; state.historyPage = 1; state.historyTotal = 0;
                $('memberHistoryChangeFilter').value = 'all';
                $('memberHistoryFrom').value = '';
                $('memberHistoryTo').value = '';
                $('memberHistoryPanel').classList.add('d-none');
                renderMemberEditor(); loadMemberCampuses(member);
            });
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
        const hasUnscopedIdentityRoles = Boolean(member.unscopedIdentityRoleNames?.length);
        const notice = $('memberUnscopedIdentityRoleNotice');
        notice.classList.toggle('d-none', !hasUnscopedIdentityRoles);
        notice.textContent = !hasUnscopedIdentityRoles ? '' : member.canRemoveUnscopedIdentityRoles
            ? 'Global Identity role grants (not institution-scoped): ' + member.unscopedIdentityRoleNames.join(', ') + '. Saving institution roles will remove these grants after your explicit approval. Compare access before applying.'
            : 'Global Identity role grants (not institution-scoped): ' + member.unscopedIdentityRoleNames.join(', ') + '. This user has other institution memberships. Global grants cannot be removed here; refer to Platform security review.';
        $('memberUnscopedIdentityRoleApprovalLabel').classList.toggle('d-none', !hasUnscopedIdentityRoles || !member.canRemoveUnscopedIdentityRoles);
        $('memberUnscopedIdentityRoleApproval').checked = false;
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

    async function loadMemberCampuses(member) {
        const options = $('memberCampusOptions');
        options.textContent = 'Loading campus access...';
        try {
            const campuses = await request('/' + member.membershipId + '/campuses');
            if (state.selected?.membershipId !== member.membershipId) return;
            options.replaceChildren();
            campuses.forEach(campus => {
                const cell = document.createElement('div');
                cell.className = 'col-md-4';
                cell.append(checkbox(campus.campusId, campus.name, campus.isActive));
                options.append(cell);
            });
            const select = $('memberDefaultCampus');
            select.replaceChildren(new Option('No default campus', ''));
            campuses.forEach(campus => select.append(new Option(campus.name, String(campus.campusId))));
            select.value = String(campuses.find(x => x.isDefault)?.campusId || '');
        } catch (error) {
            options.textContent = 'Unable to load campus access.';
            message(error.message, true);
        }
    }

    function historyRange() {
        const from = $('memberHistoryFrom').value;
        const to = $('memberHistoryTo').value;
        if (!!from !== !!to) throw new Error(t('Select both UTC dates or clear both.', 'উভয় UTC তারিখ দিন অথবা দুটিই খালি রাখুন।'));
        return { from, to, suffix: from ? '&from=' + encodeURIComponent(from) + '&to=' + encodeURIComponent(to) : '' };
    }

    async function loadMemberHistory(memberId) {
        const page = state.historyPage;
        const change = $('memberHistoryChangeFilter').value;
        const range = historyRange();
        const body = $('memberHistoryRows');
        body.replaceChildren();
        const loading = document.createElement('tr');
        const loadingCell = textNode('td', t('Loading history...', 'ইতিহাস লোড হচ্ছে…'));
        loadingCell.colSpan = 5; loading.append(loadingCell); body.append(loading);
        const result = await request('/' + memberId + '/access-history?page=' + page + '&pageSize=10&change=' + encodeURIComponent(change) + range.suffix);
        if (state.selected?.membershipId !== memberId || state.historyPage !== page ||
            $('memberHistoryChangeFilter').value !== change ||
            $('memberHistoryFrom').value !== range.from || $('memberHistoryTo').value !== range.to) return;
        state.historyTotal = result.totalCount || 0;
        body.replaceChildren();
        const names = { TenantMemberRolesChanged: t('Roles', 'ভূমিকা'), TenantMemberCampusesChanged: t('Campuses', 'ক্যাম্পাস'),
            TenantMemberStatusChanged: t('Membership status', 'সদস্যপদের অবস্থা'),
            TenantMemberPermissionOverrideChanged: t('Permission exception', 'অনুমতির ব্যতিক্রম') };
        for (const item of result.items || []) {
            const tr = document.createElement('tr');
            const when = item.occurredAt ? new Date(item.occurredAt).toLocaleString() : 'Unknown';
            [when, names[item.action] || item.action, item.actor, item.previousValue || '—',
                item.newValue || '—'].forEach(value => tr.append(textNode('td', value, 'small text-break')));
            body.append(tr);
        }
        if (!body.children.length) {
            const tr = document.createElement('tr'); const td = textNode('td', t('No access changes recorded.', 'প্রবেশাধিকার পরিবর্তনের কোনো ইতিহাস নেই।'));
            td.colSpan = 5; tr.append(td); body.append(tr);
        }
        $('memberHistoryCount').textContent = t('Page ', 'পৃষ্ঠা ') + page + ' • ' + state.historyTotal + t(' changes', ' পরিবর্তন');
        $('memberHistoryPrev').disabled = page <= 1;
        $('memberHistoryNext').disabled = page * 10 >= state.historyTotal;
    }

    async function showMemberHistory() {
        if (!state.selected) return;
        const memberId = state.selected.membershipId;
        $('memberHistoryPanel').classList.remove('d-none');
        try { await loadMemberHistory(memberId); }
        catch (error) { if (state.selected?.membershipId === memberId) message(error.message, true); }
    }

    function renderPermissions() {
        const role = state.roles.find(x => String(x.id) === $('permissionRole').value);
        $('editRoleDescription').value = role?.description || '';
        $('editRoleActive').checked = role?.isActive || false;
        $('saveRoleSettings').disabled = !role;
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


    async function exportMemberHistory() {
        const memberId = state.selected?.membershipId;
        if (!memberId) return;
        const change = $('memberHistoryChangeFilter').value;
        const button = $('memberHistoryExport');
        button.disabled = true;
        try {
            const range = historyRange();
            const response = await fetch(endpoint + '/' + memberId + '/access-history/export?change=' + encodeURIComponent(change) + range.suffix, {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/csv' }
            });
            if (!response.ok) {
                const error = await response.json().catch(() => null);
                throw new Error(error?.message || t('Unable to export access history.', 'ইতিহাস ডাউনলোড করা যায়নি।'));
            }
            const blob = await response.blob();
            if (state.selected?.membershipId !== memberId || $('memberHistoryChangeFilter').value !== change ||
                $('memberHistoryFrom').value !== range.from || $('memberHistoryTo').value !== range.to) return;
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = 'institution-member-access-history.csv';
            document.body.append(link);
            link.click();
            link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch (error) {
            if (state.selected?.membershipId === memberId) message(error.message, true);
        } finally {
            button.disabled = false;
        }
    }
    $('memberHistoryExport').addEventListener('click', exportMemberHistory);

    $('memberHistoryButton').addEventListener('click', showMemberHistory);
    $('memberHistoryChangeFilter').addEventListener('change', () => {
        state.historyPage = 1;
        if (!$('memberHistoryPanel').classList.contains('d-none')) showMemberHistory();
    });
    ['memberHistoryFrom', 'memberHistoryTo'].forEach(id => $(id).addEventListener('change', () => {
        state.historyPage = 1;
        if (!$('memberHistoryPanel').classList.contains('d-none')) showMemberHistory();
    }));
    $('memberHistoryPrev').addEventListener('click', () => {
        if (state.historyPage > 1) { state.historyPage--; showMemberHistory(); }
    });
    $('memberHistoryNext').addEventListener('click', () => {
        if (state.historyPage * 10 < state.historyTotal) { state.historyPage++; showMemberHistory(); }
    });
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
                clientRequestId: crypto.randomUUID(),
                email: $('inviteEmail').value.trim(),
                roleId: $('inviteRole').value ? Number($('inviteRole').value) : null,
                campusId: $('inviteCampus').value ? Number($('inviteCampus').value) : null
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
            const confirmUnscopedIdentityRoleRemoval = Boolean(state.selected.canRemoveUnscopedIdentityRoles &&
                state.selected.unscopedIdentityRoleNames?.length && $('memberUnscopedIdentityRoleApproval').checked);
            if (state.selected.canRemoveUnscopedIdentityRoles && state.selected.unscopedIdentityRoleNames?.length &&
                !confirmUnscopedIdentityRoleRemoval) throw new Error('Review and approve removal of unscoped global Identity grants before saving.');
            await request('/' + state.selected.membershipId + '/roles', {
                method: 'PUT', body: JSON.stringify({ roleIds, rowVersion: state.selected.rowVersion,
                    confirmUnscopedIdentityRoleRemoval })
            });
            state.selected = null; $('memberEditor').classList.add('d-none');
            await loadMembers(); message('Roles saved. The user must sign in again.');
        });
    });
    $('saveMemberCampuses').addEventListener('click', async e => {
        if (!state.selected) return;
        const campuses = [...$('memberCampusOptions').querySelectorAll('input:checked')]
            .map(x => Number(x.value));
        const selectedDefault = $('memberDefaultCampus').value;
        const defaultCampusId = selectedDefault ? Number(selectedDefault) : null;
        if (defaultCampusId !== null && !campuses.includes(defaultCampusId)) {
            message('Default campus must be among the allowed campuses.', true);
            return;
        }
        await submit(e.currentTarget, async () => {
            await request('/' + state.selected.membershipId + '/campuses', {
                method: 'PUT', body: JSON.stringify({
                    campusIds: campuses,
                    defaultCampusId,
                    membershipRowVersion: state.selected.rowVersion
                })
            });
            state.selected = null; $('memberEditor').classList.add('d-none');
            await loadMembers();
            message('Campus access saved. The user must sign in again.');
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
    $('saveRoleSettings').addEventListener('click', async event => {
        const role = state.roles.find(x => String(x.id) === $('permissionRole').value);
        if (!role) return;
        if (role.isActive && !$('editRoleActive').checked &&
            !confirm('Deactivating this role removes its access from assigned users. Continue?')) return;
        await submit(event.currentTarget, async () => {
            await request('/roles/' + role.id, { method: 'PUT', body: JSON.stringify({
                concurrencyStamp: role.concurrencyStamp,
                description: $('editRoleDescription').value.trim(),
                isActive: $('editRoleActive').checked
            }) });
            await loadCatalog();
            await loadMembers();
            message('Role settings saved.');
        });
    });
    $('saveRolePermissionsForm').addEventListener('submit', async e => {
        e.preventDefault();
        const roleId = Number($('permissionRole').value);
        if (!roleId) { message('Select an institution role.', true); return; }
        await submit(e.submitter, async () => {
            const permissionIds = [...$('permissionOptions').querySelectorAll('input:checked')].map(x => Number(x.value));
            const role = state.roles.find(x => x.id === roleId);
            if (!role?.concurrencyStamp) throw new Error('Reload the role before saving permissions.');
            await request('/roles/' + roleId + '/permissions', {
                method: 'PUT', body: JSON.stringify({ permissionIds, concurrencyStamp: role.concurrencyStamp })
            });
            await loadCatalog(); message('Role permissions saved. Affected users must sign in again.');
        });
    });

    (async () => {
        try { await Promise.all([loadCatalog(), loadInviteCampuses()]); await loadMembers(); }
        catch (error) { message(error.message, true); }
    })();
})();
