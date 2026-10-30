(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('hostelAuthorityPanel')) return;
    let page = 1, totalPages = 1, requestId = crypto.randomUUID();
    const state = (message, error = false) => {
        const target = el('hostelAuthorityStatus'); target.className = 'small ' + (error ? 'text-danger' : 'text-muted');
        target.textContent = message;
    };
    async function api(url, method = 'GET', data) {
        const headers = { Accept: 'application/json' };
        if (data !== undefined) {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload page.');
            headers.RequestVerificationToken = token; headers['Content-Type'] = 'application/json';
        }
        const response = await fetch(url, { method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(data === undefined ? {} : { body: JSON.stringify(data) }) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Request failed.');
        return result.data;
    }
    function clearSelect(select, title) {
        select.replaceChildren(); const option = document.createElement('option');
        option.value = ''; option.textContent = title; select.append(option);
    }
    function option(select, value, title) {
        const item = document.createElement('option'); item.value = value; item.textContent = title; select.append(item);
    }
    async function findStudents() {
        try {
            const params = new URLSearchParams({ page: '1', pageSize: '50' });
            const search = el('hostelAuthoritySearch').value.trim(); if (search) params.set('search', search);
            const result = await api('/api/hostel/eligible-students?' + params);
            clearSelect(el('hostelAuthorityEnrollment'), 'Choose student');
            clearSelect(el('hostelAuthorityGuardian'), 'Choose guardian');
            (result.items || []).forEach(x => option(el('hostelAuthorityEnrollment'), x.enrollmentReference,
                x.studentName + ' (' + x.studentCode + ')'));
            state(result.totalPages > 1 ? 'First 50 enrolled students shown. Refine your search.' : 'Student lookup ready.');
        } catch (error) { state(error.message, true); }
    }
    async function loadGuardians() {
        clearSelect(el('hostelAuthorityGuardian'), 'Choose verified guardian');
        const reference = el('hostelAuthorityEnrollment').value; if (!reference) return;
        try {
            const result = await api('/api/hostel/applications/authority/guardians?' +
                new URLSearchParams({ enrollmentReference: reference, page: '1', pageSize: '50' }));
            (result.items || []).forEach(x => option(el('hostelAuthorityGuardian'), x.guardianReference,
                x.guardianName + ' (' + x.relationCode + ')'));
            if (!result.items?.length) state('No linked guardian with an active verified account.');
        } catch (error) { state(error.message, true); }
    }
    async function loadAuthorities() {
        try {
            const result = await api('/api/hostel/applications/authority?page=' + page + '&pageSize=25');
            totalPages = Math.max(1, result.totalPages || 1);
            const body = el('hostelAuthorityRows'); body.replaceChildren();
            for (const item of result.items || []) {
                const row = document.createElement('tr');
                const now = Date.now(), expired = Date.parse(item.expiresAtUtc) <= now;
                [item.studentName, item.guardianName, item.expiresAtUtc,
                    item.revokedAtUtc ? 'Revoked' : expired ? 'Expired' : 'Active'].forEach(value => {
                    const td = document.createElement('td'); td.textContent = value ?? '—'; row.append(td);
                });
                const cell = document.createElement('td');
                if (!item.revokedAtUtc) {
                    const revoke = document.createElement('button'); revoke.type = 'button';
                    revoke.className = 'btn btn-outline-danger btn-sm'; revoke.textContent = 'Revoke';
                    revoke.addEventListener('click', async () => {
                        const reason = el('hostelAuthorityRevokeReason').value.trim();
                        if (!reason) { state('Enter a revocation reason first.', true); return; }
                        revoke.disabled = true;
                        try {
                            await api('/api/hostel/applications/authority/' + item.id + '/revoke', 'POST',
                                { rowVersion: item.rowVersion, reason });
                            state('Authority revoked.'); await loadAuthorities();
                        } catch (error) { state(error.message, true); } finally { revoke.disabled = false; }
                    });
                    cell.append(revoke);
                }
                row.append(cell); body.append(row);
            }
            if (!(result.items || []).length) {
                const row = document.createElement('tr'), cell = document.createElement('td');
                cell.colSpan = 5; cell.textContent = 'No guardian delegations recorded.'; row.append(cell); body.append(row);
            }
            el('hostelAuthorityPage').textContent = page + ' / ' + totalPages;
            el('hostelAuthorityPrev').disabled = page <= 1;
            el('hostelAuthorityNext').disabled = page >= totalPages;
        } catch (error) { state(error.message, true); }
    }
    el('hostelAuthorityFindStudent').addEventListener('click', findStudents);
    el('hostelAuthorityEnrollment').addEventListener('change', loadGuardians);
    el('hostelAuthorityForm').addEventListener('submit', async event => {
        event.preventDefault(); if (!event.currentTarget.reportValidity()) return;
        const expiresAtUtc = new Date(el('hostelAuthorityExpiry').value + 'T23:59:59Z').toISOString();
        const submit = el('hostelAuthorityGrant'); submit.disabled = true;
        try {
            await api('/api/hostel/applications/authority', 'POST', {
                clientRequestId: requestId, studentEnrollmentReference: el('hostelAuthorityEnrollment').value,
                guardianReference: el('hostelAuthorityGuardian').value,
                evidenceReference: el('hostelAuthorityEvidence').value.trim(), expiresAtUtc
            });
            requestId = crypto.randomUUID(); el('hostelAuthorityEvidence').value = '';
            state('Verified guardian authority granted.'); page = 1; await loadAuthorities();
        } catch (error) { state(error.message, true); } finally { submit.disabled = false; }
    });
    el('hostelAuthorityPrev').addEventListener('click', () => { if (page > 1) { page--; loadAuthorities(); } });
    el('hostelAuthorityNext').addEventListener('click', () => { if (page < totalPages) { page++; loadAuthorities(); } });
    const tomorrow = new Date(Date.now() + 7 * 86400000);
    el('hostelAuthorityExpiry').value = tomorrow.toISOString().slice(0, 10);
    loadAuthorities();
})();
