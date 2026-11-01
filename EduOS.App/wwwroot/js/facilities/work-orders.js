(() => {
    'use strict';
    const root = document.getElementById('facilityWorkflow');
    if (!root) return;
    const bn = root.dataset.bn === 'true';
    const manager = root.dataset.manager === 'true';
    const approver = root.dataset.approver === 'true';
    const technician = root.dataset.technician === 'true';
    const currentUserId = Number(root.dataset.userId);
    const el = id => document.getElementById(id);
    const t = (en, bd) => bn ? bd : en;
    const actionLabels = {
        triage: ['Triage request', 'অনুরোধ যাচাই'],
        approve: ['Approve request', 'অনুরোধ অনুমোদন'],
        reject: ['Reject request', 'অনুরোধ প্রত্যাখ্যান'],
        schedule: ['Assign schedule', 'সময়সূচি নির্ধারণ'],
        unschedule: ['Release schedule', 'সময়সূচি বাতিল'],
        start: ['Start work', 'কাজ শুরু'],
        pause: ['Pause work', 'কাজ বিরতি'],
        resume: ['Resume work', 'কাজ পুনরায় শুরু'],
        complete: ['Submit completion', 'সমাপ্তির প্রমাণ জমা'],
        verify: ['Verify closure', 'সমাপ্তি যাচাই'],
        return: ['Return for correction', 'সংশোধনের জন্য ফেরত'],
        escalate: ['Escalate overdue SLA', 'বিলম্বিত কাজ এস্কেলেট করুন'],
        cancelrequest: ['Request cancellation', 'বাতিলের আবেদন'],
        cancelapprove: ['Approve cancellation', 'বাতিল অনুমোদন'],
        cancelreject: ['Reject cancellation', 'বাতিল আবেদন প্রত্যাখ্যান']
    };
    const token = document.querySelector('meta[name="request-verification-token"]')?.content;
    const base = '/api/facilities/work-orders';
    let page = 1, pages = 1, selected = null, historyPage = 1, historyPages = 1;
    const pending = new Map();
    // A retry may reuse an ID only for the exact original payload. After an
    // uncertain network outcome, edited details require a reload/status check
    // rather than silently creating a second maintenance request.
    const commandId = (key, payload) => {
        const fingerprint = JSON.stringify(payload);
        const previous = pending.get(key);
        if (previous && previous.fingerprint !== fingerprint)
            throw new Error(t('Previous submission may have succeeded. Reload and check the request before changing details.',
                'আগের জমা সফল হয়ে থাকতে পারে। তথ্য বদলানোর আগে পুনরায় লোড করে অনুরোধ যাচাই করুন।'));
        if (previous) return previous.id;
        const id = crypto.randomUUID();
        pending.set(key, { id, fingerprint });
        return id;
    };
    function notice(message, ok = false) {
        const box = el('workflowAlert');
        box.className = 'alert alert-' + (ok ? 'success' : 'danger');
        box.textContent = message;
    }
    async function api(path = '', method = 'GET', body) {
        if (method !== 'GET' && !token) throw new Error(t('Security token missing. Reload.', 'নিরাপত্তা টোকেন নেই। পুনরায় লোড করুন।'));
        const headers = { Accept: 'application/json' };
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        if (method !== 'GET') headers.RequestVerificationToken = token;
        const response = await fetch(base + path, {
            method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        const data = await response.json().catch(() => null);
        if (!response.ok || !data?.success) throw new Error(data?.message || t('Request failed.', 'অনুরোধ ব্যর্থ।'));
        return data.data;
    }
    const cell = value => {
        const td = document.createElement('td');
        td.textContent = value == null || value === '' ? '—' : String(value);
        return td;
    };
    function actions(row) {
        if (row.stateCode === 'Submitted')
            return manager && row.requesterUserId !== currentUserId ? ['triage'] : [];
        if (row.stateCode === 'Triaged')
            return approver && row.requesterUserId !== currentUserId &&
                row.triagedByUserId !== currentUserId ? ['approve', 'reject'] : [];
        if (row.stateCode !== 'Approved') return [];
        const state = row.workOrderState, options = [];
        const canManage = manager && row.requesterUserId !== currentUserId;
        const assigned = technician && row.technicianUserId === currentUserId;
        if (state === 'Approved' && canManage) options.push('schedule', 'cancelrequest');
        if (state === 'CancellationPending' && approver &&
            row.requesterUserId !== currentUserId &&
            row.triagedByUserId !== currentUserId) options.push('cancelapprove', 'cancelreject');
        if (state === 'Scheduled') {
            if (canManage) options.push('schedule', 'unschedule');
            if (assigned) options.push('start');
        }
        if (state === 'InProgress' && assigned) options.push('pause', 'complete');
        if (state === 'Paused' && assigned) options.push('resume');
        if (state === 'PendingVerification' && approver &&
            row.requesterUserId !== currentUserId &&
            row.triagedByUserId !== currentUserId &&
            row.technicianUserId !== currentUserId) options.push('verify', 'return');
        if (canManage && state && state !== 'Closed' && row.slaDueAtUtc &&
            Date.parse(row.slaDueAtUtc) < Date.now()) options.push('escalate');
        return options;
    }
    function select(row) {
        selected = row;
        el('wfSelected').textContent = '#' + row.id + ' — ' + row.summary;
        el('wfDetails').textContent = [
            t('Campus', 'ক্যাম্পাস') + ': ' + row.campusId,
            t('Request', 'অনুরোধ') + ': ' + row.stateCode,
            t('Work order', 'ওয়ার্ক অর্ডার') + ': ' + (row.workOrderState || '—'),
            'SLA: ' + (row.slaDueAtUtc || '—'),
            t('Technician', 'টেকনিশিয়ান') + ': ' + (row.technicianUserId || '—'),
            t('Breach', 'বিলম্ব') + ': ' + (row.slaBreached == null ? '—' : String(row.slaBreached))
        ].join(' | ');
        const options = actions(row), form = el('wfTransition'), selectAction = el('wfAction');
        selectAction.replaceChildren();
        for (const action of options) {
            const option = document.createElement('option');
            option.value = action;
            option.textContent = actionLabels[action] ? t(...actionLabels[action]) : action;
            selectAction.append(option);
        }
        form.classList.toggle('d-none', options.length === 0);
        el('wfNote').value = '';
        el('wfDue').value = '';
        el('wfTech').value = row.technicianUserId || '';
        el('wfStart').value = row.scheduledStartUtc ? toLocal(row.scheduledStartUtc) : '';
        el('wfEnd').value = row.scheduledEndUtc ? toLocal(row.scheduledEndUtc) : '';
        toggleFields();
        historyPage = 1;
        history();
    }
    function toLocal(iso) {
        const d = new Date(iso);
        return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
    }
    function toggleFields() {
        const action = el('wfAction').value;
        for (const [id, enabled] of [
            ['wfDue', action === 'triage'], ['wfTech', action === 'schedule'],
            ['wfStart', action === 'schedule'], ['wfEnd', action === 'schedule']
        ]) {
            const input = el(id);
            input.disabled = !enabled; input.required = enabled;
            input.closest('.col-12, .col-sm-6')?.classList.toggle('d-none', !enabled);
        }
        el('wfNote').required = ['reject', 'pause', 'complete', 'verify', 'return', 'unschedule', 'escalate', 'cancelrequest', 'cancelapprove', 'cancelreject'].includes(action);
        el('wfNote').minLength = ['return', 'escalate', 'cancelrequest', 'cancelapprove', 'cancelreject'].includes(action) ? 10 : action === 'unschedule' ? 5 : 0;
        el('wfNote').maxLength = ['schedule', 'unschedule', 'escalate', 'return', 'cancelrequest', 'cancelapprove', 'cancelreject'].includes(action) ? 500 : 1000;
    }
    async function dashboard() {
        const q = new URLSearchParams();
        if (el('wfCampusFilter').value) q.set('campusId', el('wfCampusFilter').value);
        const summary = await api('/dashboard?' + q);
        const cards = [
            ['totalRequests', t('Total requests', 'মোট অনুরোধ')],
            ['submitted', t('Awaiting triage', 'ট্রায়াজ অপেক্ষায়')],
            ['awaitingApproval', t('Awaiting approval', 'অনুমোদন অপেক্ষায়')],
            ['approvedUnscheduled', t('Not scheduled', 'সময়সূচি হয়নি')],
            ['cancellationPending', t('Cancellation review', 'বাতিল অনুমোদন অপেক্ষায়')],
            ['cancelled', t('Cancelled', 'বাতিল হয়েছে')],
            ['scheduled', t('Scheduled', 'নির্ধারিত')],
            ['inProgress', t('Active work', 'চলমান কাজ')],
            ['pendingVerification', t('Verify closure', 'সমাপ্তি যাচাই')],
            ['overdueOpen', t('Overdue open', 'সময় পেরিয়েছে')],
            ['closed', t('Closed', 'সমাপ্ত')],
            ['closedLate', t('Closed late', 'বিলম্বে সমাপ্ত')]
        ];
        const box = el('wfDashboard'); box.replaceChildren();
        for (const [field, label] of cards) {
            const column = document.createElement('div');
            column.className = 'col-6 col-md-4 col-xl';
            const card = document.createElement('div');
            card.className = 'border rounded p-2 h-100 bg-body';
            const value = document.createElement('strong');
            value.className = 'd-block fs-5'; value.textContent = String(summary[field] ?? 0);
            const caption = document.createElement('span');
            caption.className = 'small text-muted'; caption.textContent = label;
            card.append(value, caption); column.append(card); box.append(column);
        }
    }
    async function load() {
        try {
            const q = new URLSearchParams({ page: String(page), pageSize: '25' });
            if (el('wfCampusFilter').value) q.set('campusId', el('wfCampusFilter').value);
            if (el('wfStateFilter').value) q.set('state', el('wfStateFilter').value);
            if (el('wfOverdueOnly').checked) q.set('overdueOnly', 'true');
            const data = await api('?' + q);
            const tbody = el('wfRows'); tbody.replaceChildren();
            pages = Math.max(1, data.totalPages || 1);
            for (const row of data.items || []) {
                const tr = document.createElement('tr');
                [row.id, row.facilitySpaceId, row.summary, row.priorityCode,
                    row.workOrderState || row.stateCode].forEach(v => tr.append(cell(v)));
                const td = document.createElement('td'), button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-outline-primary btn-sm';
                button.textContent = t('Open', 'দেখুন');
                button.addEventListener('click', () => select(row));
                td.append(button); tr.append(td); tbody.append(tr);
                if (selected?.id === row.id) select(row);
            }
            if (!data.items?.length) {
                const tr = document.createElement('tr'), td = cell(t('No requests found.', 'অনুরোধ পাওয়া যায়নি।'));
                td.colSpan = 6; tr.append(td); tbody.append(tr);
            }
            el('wfPage').textContent = page + ' / ' + pages;
            el('wfPrev').disabled = page <= 1; el('wfNext').disabled = page >= pages;
            await dashboard();
        } catch (error) { notice(error.message); }
    }
    async function history() {
        if (!selected) return;
        try {
            const data = await api('/' + selected.id + '/history?page=' + historyPage + '&pageSize=25');
            historyPages = Math.max(1, data.totalPages || 1);
            const box = el('wfHistory'); box.replaceChildren();
            const list = document.createElement('ol');
            list.className = 'list-group list-group-numbered';
            for (const event of data.items || []) {
                const item = document.createElement('li');
                item.className = 'list-group-item py-1';
                item.textContent = (actionLabels[event.actionCode]?.[bn ? 1 : 0] || event.actionCode) + ': ' + event.fromState + ' → ' +
                    event.toState + ' | ' + event.actorUserId + ' | ' + event.occurredAtUtc +
                    (event.note ? ' | ' + event.note : '');
                list.append(item);
            }
            box.append(list);
            if (historyPage < historyPages) {
                const more = document.createElement('button');
                more.type = 'button'; more.className = 'btn btn-outline-secondary btn-sm mt-2';
                more.textContent = t('Next history page', 'পরের ইতিহাস');
                more.addEventListener('click', () => { historyPage++; history(); });
                box.append(more);
            }
        } catch (error) { notice(error.message); }
    }
    el('workflowSearch').addEventListener('submit', e => { e.preventDefault(); page = 1; load(); });
    el('wfPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
    el('wfNext').addEventListener('click', () => { if (page < pages) { page++; load(); } });
    el('facilityExport').addEventListener('click', () => {
        const q = new URLSearchParams();
        if (el('wfCampusFilter').value) q.set('campusId', el('wfCampusFilter').value);
        if (el('wfStateFilter').value) q.set('state', el('wfStateFilter').value);
        if (el('wfOverdueOnly').checked) q.set('overdueOnly', 'true');
        window.location.assign(base + '/export?' + q);
    });
    el('wfAction').addEventListener('change', toggleFields);
    el('wfCreate').addEventListener('submit', async e => {
        e.preventDefault();
        const key = 'create:' + el('wfCampus').value + ':' + el('wfSpace').value;
        try {
            const body = {
                campusId: Number(el('wfCampus').value),
                facilitySpaceId: Number(el('wfSpace').value), categoryCode: el('wfCategory').value,
                priorityCode: el('wfPriority').value, summary: el('wfSummary').value,
                description: el('wfDescription').value || null
            };
            body.clientRequestId = commandId(key, body);
            await api('', 'POST', body);
            pending.delete(key);
            el('wfCreate').reset(); notice(t('Request submitted.', 'অনুরোধ জমা হয়েছে।'), true);
            page = 1; await load();
        } catch (error) { notice(error.message); }
    });
    el('wfTransition').addEventListener('submit', async e => {
        e.preventDefault();
        if (!selected) return;
        const action = el('wfAction').value, key = selected.id + ':' + action;
        const date = id => el(id).value ? new Date(el(id).value).toISOString() : null;
        const body = {
            rowVersion: ['triage', 'approve', 'reject'].includes(action)
                ? selected.rowVersion : selected.workOrderRowVersion,
            note: el('wfNote').value || null,
            slaDueAtUtc: action === 'triage' ? date('wfDue') : null,
            technicianUserId: action === 'schedule' ? Number(el('wfTech').value) : null,
            scheduledStartUtc: action === 'schedule' ? date('wfStart') : null,
            scheduledEndUtc: action === 'schedule' ? date('wfEnd') : null
        };
        try {
            body.clientCommandId = commandId(key, body);
            const result = await api('/' + selected.id + '/' + action, 'POST', body);
            pending.delete(key);
            notice(t('Transition recorded.', 'পরিবর্তন সংরক্ষিত হয়েছে।'), true);
            select(result); await load();
        } catch (error) { notice(error.message); }
    });
    load();
})();