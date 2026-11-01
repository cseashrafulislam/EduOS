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
    const session = {
        reference: document.body?.dataset.authSessionReference || '',
        tenant: document.body?.dataset.authTenantId || ''
    };
    const shield = el('eduosSessionShield');
    let blocked = false, creating = false, transitioning = false;
    let page = 1, pages = 1, selected = null, historyPage = 1, historyPages = 1;
    let listGeneration = 0, historyGeneration = 0, locationGeneration = 0, technicianGeneration = 0;
    let locationPage = 1, locationPages = 1, chosenLocation = null;
    let locationChoices = new Map();
    let technicianPage = 1, technicianPages = 1, technicianChoices = new Map();
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
    // This API has no response-bound session identity. Reconcile with the
    // existing authenticated session shield before displaying any readback.
    function lockPage() {
        if (blocked) return;
        blocked = true;
        listGeneration++;
        historyGeneration++;
        locationGeneration++;
        technicianGeneration++;
        technicianChoices.clear();
        chosenLocation = null;
        locationChoices.clear();
        selected = null;
        pending.clear();
        for (const id of ['wfRows', 'wfDashboard', 'wfHistory']) el(id).replaceChildren();
        el('wfSelected').textContent = t('Select a request', 'কাজ নির্বাচন করুন');
        el('wfDetails').textContent = '';
        for (const id of ['wfCreate', 'wfTransition']) {
            const form = el(id);
            form.reset();
            for (const control of form.querySelectorAll('input, select, textarea, button'))
                control.disabled = true;
        }
        el('wfTransition').classList.add('d-none');
        for (const id of ['wfPrev', 'wfNext', 'facilityExport']) el(id).disabled = true;
        notice(t('Institution session changed. Reload this page.',
            'প্রতিষ্ঠানের সেশন পরিবর্তিত হয়েছে। পেজটি আবার লোড করুন।'));
    }
    function sessionCurrent() {
        return !blocked && !!session.reference && !!session.tenant &&
            !window.EduOSSessionTransitionPending?.() &&
            document.body?.dataset.authSessionReference === session.reference &&
            document.body?.dataset.authTenantId === session.tenant;
    }
    async function verifySession() {
        if (!sessionCurrent() || !window.EduOSVerifySessionContext || !shield) {
            lockPage();
            throw new Error('session-changed');
        }
        try { await window.EduOSVerifySessionContext(); }
        catch {
            lockPage();
            throw new Error('session-changed');
        }
        if (!sessionCurrent() || shield.hidden !== true) {
            lockPage();
            throw new Error('session-changed');
        }
    }
    function notice(message, ok = false) {
        const box = el('workflowAlert');
        box.className = 'alert alert-' + (ok ? 'success' : 'danger');
        box.textContent = message;
    }
    async function api(path = '', method = 'GET', body) {
        if (method !== 'GET' && !token) throw new Error(t('Security token missing. Reload.', 'নিরাপত্তা টোকেন নেই। পুনরায় লোড করুন।'));
        if (!sessionCurrent()) { lockPage(); throw new Error('session-changed'); }
        if (method !== 'GET') await verifySession();
        const headers = { Accept: 'application/json' };
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        if (method !== 'GET') headers.RequestVerificationToken = token;
        const response = await fetch(base + path, {
            method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        await verifySession();
        const data = await response.json().catch(() => null);
        // JSON parsing is asynchronous: the institution may switch after the
        // verification response but before the private payload is consumed.
        if (!sessionCurrent() || shield.hidden !== true) {
            lockPage();
            throw new Error('session-changed');
        }
        if (response.status === 401 || response.status === 403) { lockPage(); throw new Error('session-changed'); }
        if (!response.ok || !data?.success) {
            const error = new Error(data?.message || t('Request failed.', 'অনুরোধ ব্যর্থ।'));
            error.status = response.status;
            throw error;
        }
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
        if (!sessionCurrent()) { lockPage(); return; }
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
        el('wfTech').value = '';
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
        const scheduling = action === 'schedule';
        el('wfTechnicianLookup').classList.toggle('d-none', !scheduling);
        if (scheduling && selected) {
            technicianPage = 1;
            loadTechnicians();
        } else {
            technicianGeneration++;
            technicianChoices.clear();
            el('wfTech').value = '';
            el('wfTechSelect').replaceChildren();
            el('wfTechSelect').disabled = true;
        }
        el('wfNote').required = ['reject', 'pause', 'complete', 'verify', 'return', 'unschedule', 'escalate', 'cancelrequest', 'cancelapprove', 'cancelreject'].includes(action);
        el('wfNote').minLength = ['return', 'escalate', 'cancelrequest', 'cancelapprove', 'cancelreject'].includes(action) ? 10 : action === 'unschedule' ? 5 : 0;
        el('wfNote').maxLength = ['schedule', 'unschedule', 'escalate', 'return', 'cancelrequest', 'cancelapprove', 'cancelreject'].includes(action) ? 500 : 1000;
    }
    // Active, granted campus spaces are supplied by the Facilities workflow
    // endpoint, not guessed from IDs or the privileged space-management API.
    function clearLocation() {
        chosenLocation = null;
        el('wfCampus').value = '';
        el('wfSpace').value = '';
        el('wfLocationSelect').value = '';
        const submit = el('wfCreate').querySelector('button[type="submit"]');
        if (submit && !creating) submit.disabled = true;
    }
    async function loadLocations() {
        if (blocked) return;
        const generation = ++locationGeneration;
        clearLocation();
        locationChoices = new Map();
        const picker = el('wfLocationSelect');
        picker.disabled = true;
        picker.replaceChildren();
        el('wfLocationPage').textContent = t('Loading spaces…', 'স্থান লোড হচ্ছে…');
        el('wfLocationPrev').disabled = true;
        el('wfLocationNext').disabled = true;
        try {
            const q = new URLSearchParams({ page: String(locationPage), pageSize: '25' });
            const search = el('wfLocationSearch').value.trim();
            if (search) q.set('search', search);
            const data = await api('/locations?' + q);
            if (generation !== locationGeneration || blocked) return;
            locationPages = Math.max(1, data.totalPages || 1);
            const placeholder = document.createElement('option');
            placeholder.value = '';
            placeholder.textContent = t('Choose campus / space', 'ক্যাম্পাস / স্থান নির্বাচন করুন');
            picker.append(placeholder);
            for (const row of data.items || []) {
                const key = row.campusId + ':' + row.facilitySpaceId;
                const option = document.createElement('option');
                option.value = key;
                option.textContent = row.campusName + ' (' + row.campusCode + ') / ' +
                    row.spaceName + ' (' + row.spaceCode + ')';
                locationChoices.set(key, row);
                picker.append(option);
            }
            picker.value = '';
            picker.disabled = locationChoices.size === 0;
            el('wfLocationPage').textContent = locationPage + ' / ' + locationPages +
                (locationChoices.size ? '' : t(' — no active spaces', ' — সক্রিয় স্থান নেই'));
            el('wfLocationPrev').disabled = locationPage <= 1;
            el('wfLocationNext').disabled = locationPage >= locationPages;
        } catch (error) {
            if (generation !== locationGeneration || blocked || error.message === 'session-changed') return;
            picker.replaceChildren();
            picker.disabled = true;
            el('wfLocationPage').textContent = t('Unable to load spaces. Retry search.', 'স্থান লোড হয়নি। আবার খুঁজুন।');
            notice(error.message);
        }
    }
    // A manager may select only current eligible staff; stale campus/work-order
    // search results cannot restore previously authorized technician choices.
    async function loadTechnicians() {
        if (blocked || !selected || el('wfAction').value !== 'schedule') return;
        const generation = ++technicianGeneration;
        const requestId = selected.id, campusId = selected.campusId;
        technicianChoices = new Map();
        el('wfTech').value = '';
        const picker = el('wfTechSelect');
        picker.replaceChildren();
        picker.disabled = true;
        el('wfTechPrev').disabled = true;
        el('wfTechNext').disabled = true;
        el('wfTechPage').textContent = t('Loading technicians…', 'টেকনিশিয়ান লোড হচ্ছে…');
        try {
            const q = new URLSearchParams({
                campusId: String(campusId), page: String(technicianPage), pageSize: '25'
            });
            const search = el('wfTechSearch').value.trim();
            if (search) q.set('search', search);
            const data = await api('/technicians?' + q);
            if (generation !== technicianGeneration || blocked || selected?.id !== requestId ||
                el('wfAction').value !== 'schedule') return;
            technicianPages = Math.max(1, data.totalPages || 1);
            const placeholder = document.createElement('option');
            placeholder.value = '';
            placeholder.textContent = t('Choose a technician', 'টেকনিশিয়ান নির্বাচন করুন');
            picker.append(placeholder);
            for (const row of data.items || []) {
                const id = Number(row.userId);
                if (!Number.isSafeInteger(id) || id <= 0) continue;
                const option = document.createElement('option');
                option.value = String(id);
                option.textContent = (row.displayName || t('Technician', 'টেকনিশিয়ান')) + ' (#' + id + ')';
                technicianChoices.set(id, row);
                picker.append(option);
            }
            picker.disabled = technicianChoices.size === 0;
            const assigned = Number(selected.technicianUserId);
            if (technicianChoices.has(assigned)) {
                picker.value = String(assigned);
                el('wfTech').value = String(assigned);
            } else picker.value = '';
            el('wfTechPage').textContent = technicianPage + ' / ' + technicianPages +
                (technicianChoices.size ? '' : t(' — no eligible technicians', ' — যোগ্য টেকনিশিয়ান নেই'));
            el('wfTechPrev').disabled = technicianPage <= 1;
            el('wfTechNext').disabled = technicianPage >= technicianPages;
        } catch (error) {
            if (generation !== technicianGeneration || blocked || selected?.id !== requestId ||
                error.message === 'session-changed') return;
            picker.replaceChildren();
            picker.disabled = true;
            el('wfTechPage').textContent = t('Lookup failed. Retry search.', 'খোঁজা ব্যর্থ। আবার চেষ্টা করুন।');
            notice(error.message);
        }
    }
    async function dashboard(generation, campusId) {
        const q = new URLSearchParams();
        if (campusId) q.set('campusId', campusId);
        const summary = await api('/dashboard?' + q);
        if (generation !== listGeneration || blocked) return;
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
        if (blocked) return;
        const generation = ++listGeneration;
        // Clear previous-campus data before a new authorized response arrives.
        const tbody = el('wfRows'); tbody.replaceChildren();
        const loading = document.createElement('tr');
        const loadingCell = cell(t('Loading requests…', 'অনুরোধ লোড হচ্ছে…'));
        loadingCell.colSpan = 6; loading.append(loadingCell); tbody.append(loading);
        el('wfDashboard').replaceChildren();
        el('wfPrev').disabled = true; el('wfNext').disabled = true;
        try {
            const q = new URLSearchParams({ page: String(page), pageSize: '25' });
            if (el('wfCampusFilter').value) q.set('campusId', el('wfCampusFilter').value);
            if (el('wfStateFilter').value) q.set('state', el('wfStateFilter').value);
            if (el('wfOverdueOnly').checked) q.set('overdueOnly', 'true');
            const data = await api('?' + q);
            if (generation !== listGeneration || blocked) return;
            tbody.replaceChildren();
            pages = Math.max(1, data.totalPages || 1);
            // Refresh may exclude a previously selected work order after approval,
            // filtering, reassignment or a revoked campus grant.
            const currentItems = data.items || [];
            if (selected && !currentItems.some(row => row.id === selected.id))
                clearSelection();
            for (const row of currentItems) {
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
            await dashboard(generation, q.get('campusId'));
        } catch (error) {
            if (generation !== listGeneration || blocked || error.message === 'session-changed') return;
            // A failed refresh cannot validate old actions or rowversion.
            clearSelection();
            tbody.replaceChildren();
            const tr = document.createElement('tr');
            const td = cell(t('Unable to load requests. Search again.', 'অনুরোধ লোড হয়নি। আবার খুঁজুন।'));
            td.colSpan = 6; tr.append(td); tbody.append(tr);
            notice(error.message);
        }
    }
    async function history() {
        if (!selected || blocked) return;
        const generation = ++historyGeneration;
        const requestId = selected.id, requestedPage = historyPage;
        const box = el('wfHistory');
        box.textContent = t('Loading history…', 'ইতিহাস লোড হচ্ছে…');
        try {
            const data = await api('/' + requestId + '/history?page=' + requestedPage + '&pageSize=25');
            if (generation !== historyGeneration || selected?.id !== requestId || blocked) return;
            historyPages = Math.max(1, data.totalPages || 1);
            box.replaceChildren();
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
        } catch (error) {
            if (generation !== historyGeneration || selected?.id !== requestId || blocked || error.message === 'session-changed') return;
            box.textContent = error.message;
            notice(error.message);
        }
    }
    function clearSelection() {
        selected = null;
        historyGeneration++;
        technicianGeneration++;
        technicianChoices.clear();
        el('wfTech').value = '';
        el('wfTechSelect').replaceChildren();
        el('wfTechSelect').disabled = true;
        el('wfSelected').textContent = t('Select a request', 'কাজ নির্বাচন করুন');
        el('wfDetails').textContent = '';
        el('wfTransition').classList.add('d-none');
        el('wfHistory').replaceChildren();
    }
    el('wfLocationFind').addEventListener('click', () => {
        if (blocked) return;
        locationPage = 1;
        loadLocations();
    });
    el('wfLocationSearch').addEventListener('keydown', e => {
        if (e.key === 'Enter') { e.preventDefault(); locationPage = 1; loadLocations(); }
    });
    el('wfLocationPrev').addEventListener('click', () => {
        if (!blocked && locationPage > 1) { locationPage--; loadLocations(); }
    });
    el('wfLocationNext').addEventListener('click', () => {
        if (!blocked && locationPage < locationPages) { locationPage++; loadLocations(); }
    });
    el('wfLocationSelect').addEventListener('change', () => {
        if (!sessionCurrent()) { lockPage(); return; }
        chosenLocation = locationChoices.get(el('wfLocationSelect').value) || null;
        el('wfCampus').value = chosenLocation?.campusId ?? '';
        el('wfSpace').value = chosenLocation?.facilitySpaceId ?? '';
        const submit = el('wfCreate').querySelector('button[type="submit"]');
        if (submit) submit.disabled = !chosenLocation || creating;
    });
    el('wfTechFind').addEventListener('click', () => {
        if (!blocked) { technicianPage = 1; loadTechnicians(); }
    });
    el('wfTechSearch').addEventListener('keydown', e => {
        if (e.key === 'Enter') { e.preventDefault(); technicianPage = 1; loadTechnicians(); }
    });
    el('wfTechPrev').addEventListener('click', () => {
        if (!blocked && technicianPage > 1) { technicianPage--; loadTechnicians(); }
    });
    el('wfTechNext').addEventListener('click', () => {
        if (!blocked && technicianPage < technicianPages) { technicianPage++; loadTechnicians(); }
    });
    el('wfTechSelect').addEventListener('change', () => {
        if (!sessionCurrent()) { lockPage(); return; }
        const id = Number(el('wfTechSelect').value);
        el('wfTech').value = technicianChoices.has(id) ? String(id) : '';
    });
    el('workflowSearch').addEventListener('submit', e => { e.preventDefault(); page = 1; clearSelection(); load(); });
    el('wfPrev').addEventListener('click', () => { if (page > 1) { page--; clearSelection(); load(); } });
    el('wfNext').addEventListener('click', () => { if (page < pages) { page++; clearSelection(); load(); } });
    el('facilityExport').addEventListener('click', async () => {
        try { await verifySession(); }
        catch (error) { if (!blocked) notice(error.message); return; }
        if (blocked) return;
        const q = new URLSearchParams();
        if (el('wfCampusFilter').value) q.set('campusId', el('wfCampusFilter').value);
        if (el('wfStateFilter').value) q.set('state', el('wfStateFilter').value);
        if (el('wfOverdueOnly').checked) q.set('overdueOnly', 'true');
        window.location.assign(base + '/export?' + q);
    });
    el('wfAction').addEventListener('change', toggleFields);
    el('wfCreate').addEventListener('submit', async e => {
        e.preventDefault();
        if (blocked || creating) return;
        if (!chosenLocation ||
            Number(el('wfCampus').value) !== chosenLocation.campusId ||
            Number(el('wfSpace').value) !== chosenLocation.facilitySpaceId) {
            notice(t('Select an authorized active campus space first.',
                'আগে অনুমোদিত সক্রিয় ক্যাম্পাস স্থান নির্বাচন করুন।'));
            return;
        }
        creating = true;
        const submit = el('wfCreate').querySelector('button[type="submit"]');
        if (submit) submit.disabled = true;
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
            el('wfCreate').reset(); clearLocation(); notice(t('Request submitted.', 'অনুরোধ জমা হয়েছে।'), true);
            page = 1; await load();
        } catch (error) {
            // A confirmed validation rejection did not commit; corrected details
            // may use a new ID. Network/5xx/409 outcomes require safe replay.
            if (error.status === 400 || error.status === 422) pending.delete(key);
            if (!blocked) notice(error.message);
        } finally {
            creating = false;
            if (!blocked && submit) submit.disabled = !chosenLocation;
        }
    });
    el('wfTransition').addEventListener('submit', async e => {
        e.preventDefault();
        if (!selected || blocked || transitioning) return;
        if (el('wfAction').value === 'schedule' &&
            !technicianChoices.has(Number(el('wfTech').value))) {
            notice(t('Choose an eligible technician for this campus.',
                'এই ক্যাম্পাসের যোগ্য টেকনিশিয়ান নির্বাচন করুন।'));
            return;
        }
        transitioning = true;
        const submit = el('wfTransition').querySelector('button[type="submit"]');
        if (submit) submit.disabled = true;
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
            // Do not restore a work order the user deselected during this POST.
            if (!blocked && selected?.id === result.id) select(result);
            await load();
        } catch (error) {
            if (error.status === 400 || error.status === 422) pending.delete(key);
            if (!blocked) notice(error.message);
        } finally {
            transitioning = false;
            if (!blocked && submit) submit.disabled = false;
        }
    });
    load();
    loadLocations();
})();