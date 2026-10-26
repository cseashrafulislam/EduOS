(() => {
    'use strict';
    const byId = id => document.getElementById(id);
    const form = byId('transportDriverRegistryForm');
    if (!form) return;
    const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const endpoint = '/api/transport/driver-registry';
    let page = 1, totalPages = 1, editing = null, sequence = 0, impactSequence = 0;
    const impactButton = byId('transportDriverLicenseImpactCheck');
    const impactBox = byId('transportDriverLicenseImpact');
    function clearImpact() {
        ++impactSequence;
        impactBox.className = 'small text-muted mt-2';
        impactBox.textContent = 'Check the impact of the proposed license expiry/status before saving.';
        impactButton.disabled = !editing;
    }
    const message = (value, ok = false) => {
        const box = byId('transportDriverRegistryAlert');
        box.className = 'alert alert-' + (ok ? 'success' : 'danger');
        box.textContent = value;
        box.setAttribute('tabindex', '-1');
        box.focus();
    };
    async function api(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) {
            headers['Content-Type'] = 'application/json';
            headers.RequestVerificationToken = token;
        }
        const response = await fetch(url, { method, headers, credentials: 'same-origin', cache: 'no-store',
            ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Driver request failed.');
        return result.data;
    }
    const cell = value => {
        const td = document.createElement('td');
        td.textContent = value == null || value === '' ? '—' : String(value);
        return td;
    };
    function reset() {
        editing = null; form.reset();
        byId('transportDriverSave').textContent = 'Register driver';
        clearImpact();
    }
    function edit(item) {
        editing = item;
        clearImpact();
        byId('transportDriverName').value = item.fullName || '';
        byId('transportDriverPhone').value = item.phone || '';
        byId('transportDriverLicense').value = item.licenseNumber || '';
        byId('transportDriverExpiry').value = item.licenseExpiresOn || '';
        byId('transportDriverActive').value = String(item.isActive);
        byId('transportDriverSave').textContent = 'Update driver';
        byId('transportDriverName').focus();
    }
    function row(item) {
        const tr = document.createElement('tr');
        [item.fullName, item.phone, item.licenseNumber, item.licenseExpiresOn,
            item.isActive ? 'Active' : 'Inactive'].forEach(x => tr.append(cell(x)));
        const actions = document.createElement('td');
        const button = document.createElement('button');
        button.type = 'button'; button.className = 'btn btn-outline-primary btn-sm';
        button.textContent = 'Edit'; button.addEventListener('click', () => edit(item));
        actions.append(button); tr.append(actions);
        return tr;
    }
    async function load() {
        const current = ++sequence;
        const query = new URLSearchParams({ page: String(page), pageSize: '15' });
        const term = byId('transportDriverSearch').value.trim();
        if (term) query.set('search', term);
        const data = await api(endpoint + '?' + query);
        if (current !== sequence) return;
        const tbody = byId('transportDriverRegistryRows');
        tbody.replaceChildren();
        (data.items || []).forEach(item => tbody.append(row(item)));
        if (!data.items?.length) {
            const tr = document.createElement('tr'), td = cell('No drivers found');
            td.colSpan = 6; tr.append(td); tbody.append(tr);
        }
        totalPages = Math.max(1, Number(data.totalPages) || 1);
        byId('transportDriverPageInfo').textContent =
            'Page ' + page + ' of ' + totalPages + ' · ' + data.totalCount + ' drivers';
        byId('transportDriverPrev').disabled = page <= 1;
        byId('transportDriverNext').disabled = page >= totalPages;
    }
    ['transportDriverExpiry', 'transportDriverActive'].forEach(id =>
        byId(id).addEventListener('change', clearImpact));
    impactButton.addEventListener('click', async () => {
        if (!editing) return;
        const current = ++impactSequence;
        impactButton.disabled = true;
        impactBox.textContent = 'Checking current and scheduled assignments…';
        const query = new URLSearchParams({
            proposedActive: byId('transportDriverActive').value,
            rowVersion: editing.rowVersion
        });
        const expiry = byId('transportDriverExpiry').value;
        if (expiry) query.set('proposedExpiry', expiry);
        try {
            const impact = await api(endpoint + '/' + encodeURIComponent(editing.id) +
                '/license-impact?' + query.toString());
            if (current !== impactSequence) return;
            impactBox.className = 'small mt-2 ' + (impact.canApply ? 'text-success' : 'text-danger');
            impactBox.textContent = (impact.canApply ? 'Eligible. ' : 'Blocked. ') +
                impact.explanation + ' Current/scheduled: ' + impact.activeOrScheduledAssignments +
                ', future: ' + impact.scheduledAssignments +
                ', open-ended: ' + impact.openEndedAssignments +
                ', conflicting: ' + impact.blockingAssignments + '.';
        } catch (error) {
            if (current !== impactSequence) return;
            impactBox.className = 'small text-danger mt-2';
            impactBox.textContent = error.message;
        } finally {
            if (current === impactSequence) impactButton.disabled = false;
        }
    });
    form.addEventListener('submit', async event => {
        event.preventDefault(); if (!form.reportValidity()) return;
        const active = byId('transportDriverActive').value === 'true';
        if (editing?.isActive && !active && !confirm('Deactivate this driver? Active vehicle assignments must be closed first.')) return;
        const button = byId('transportDriverSave'); button.disabled = true;
        const payload = {
            employeeId: null, fullName: byId('transportDriverName').value.trim(),
            phone: byId('transportDriverPhone').value.trim() || null,
            licenseNumber: byId('transportDriverLicense').value.trim(),
            licenseExpiresOn: byId('transportDriverExpiry').value || null,
            isActive: active, rowVersion: editing?.rowVersion || null
        };
        try {
            await api(editing ? endpoint + '/' + encodeURIComponent(editing.id) : endpoint,
                editing ? 'PUT' : 'POST', payload);
            reset(); page = 1; message('Driver saved.', true); await load();
        } catch (error) { message(error.message); } finally { button.disabled = false; }
    });
    byId('transportDriverCancel').addEventListener('click', reset);
    byId('transportDriverRegistrySearchForm').addEventListener('submit', event => {
        event.preventDefault(); page = 1; load().catch(error => message(error.message));
    });
    byId('transportDriverPrev').addEventListener('click', () => {
        page = Math.max(1, page - 1); load().catch(error => message(error.message));
    });
    byId('transportDriverNext').addEventListener('click', () => {
        page = Math.min(totalPages, page + 1); load().catch(error => message(error.message));
    });
    load().catch(error => message(error.message));
})();