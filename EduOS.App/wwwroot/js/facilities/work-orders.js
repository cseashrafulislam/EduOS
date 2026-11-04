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
    const vendorEvidenceLabel = code => ({
        None: t('No evidence', 'প্রমাণ নেই'),
        Submitted: t('Awaiting review', 'যাচাই অপেক্ষায়'),
        Returned: t('Returned for correction', 'সংশোধনের জন্য ফেরত'),
        Verified: t('Independently verified', 'স্বাধীনভাবে যাচাইকৃত'),
        Stale: t('Invalidated by later correction', 'পরবর্তী সংশোধনে অকার্যকর')
    })[code] || String(code || '—');
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
        cancelreject: ['Reject cancellation', 'বাতিল আবেদন প্রত্যাখ্যান'],
        vendorpropose: ['Propose vendor booking', 'সরবরাহকারী বুকিং প্রস্তাব'],
        vendorapprove: ['Approve vendor booking', 'সরবরাহকারী বুকিং অনুমোদন'],
        vendorreject: ['Reject vendor booking', 'সরবরাহকারী বুকিং প্রত্যাখ্যান'],
        vendorrelease: ['Release vendor booking', 'সরবরাহকারী বুকিং মুক্ত করুন'],
        vendorattest: ['Submit completed vendor work', 'সরবরাহকারীর কাজ সম্পন্নের প্রমাণ দিন'],
        vendorverify: ['Verify vendor work evidence', 'সরবরাহকারীর কাজের প্রমাণ যাচাই'],
        vendorreturn: ['Return vendor evidence', 'সরবরাহকারীর প্রমাণ সংশোধনে ফেরত']
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
    let listGeneration = 0, historyGeneration = 0, partsGeneration = 0, partsPage = 1, locationGeneration = 0, technicianGeneration = 0;
    let slaGeneration = 0;
    let locationPage = 1, locationPages = 1, chosenLocation = null;
    let locationChoices = new Map();
    let technicianPage = 1, technicianPages = 1, technicianChoices = new Map();
    let vendorGeneration = 0, vendorPage = 1, vendorPages = 1;
    let vendorChoices = new Map();
    let availabilityWindowKey = null, vendorAvailabilityWindowKey = null;
    const windowKey = () => [selected?.id, selected?.workOrderId,
        el('wfStart').value, el('wfEnd').value].join('|');
    function invalidateTechnicians() {
        technicianGeneration++;
        availabilityWindowKey = null;
        technicianChoices.clear();
        el('wfTech').value = '';
        el('wfTechSelect').replaceChildren();
        el('wfTechSelect').disabled = true;
        el('wfTechPrev').disabled = true;
        el('wfTechNext').disabled = true;
    }
    function invalidateVendors() {
        vendorGeneration++;
        vendorAvailabilityWindowKey = null;
        vendorChoices.clear();
        if (!manager) return;
        el('wfVendorSelected').value = '';
        el('wfVendorRows').replaceChildren();
        el('wfVendorPrev').disabled = true;
        el('wfVendorNext').disabled = true;
        el('wfVendorPage').textContent = t(
            'Set a future booking start and end time, then find available vendors.',
            'বুকিংয়ের ভবিষ্যৎ শুরু ও শেষ সময় দিয়ে উপলব্ধ সরবরাহকারী খুঁজুন।');
    }
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
    function lockPage() {
        if (blocked) return;
        blocked = true;
        listGeneration++;
        slaGeneration++;
        historyGeneration++;
        partsGeneration++;
        locationGeneration++;
        technicianGeneration++;
        vendorGeneration++;
        vendorAvailabilityWindowKey = null;
        vendorChoices.clear();
        if (el('wfVendorSelected')) el('wfVendorSelected').value = '';
        technicianChoices.clear();
        const vendorPanel = el('wfVendorPanel');
        if (vendorPanel) {
            vendorPanel.classList.add('d-none');
            el('wfVendorRows').replaceChildren();
            el('wfVendorPage').textContent = '';
        }
        availabilityWindowKey = null;
        chosenLocation = null;
        locationChoices.clear();
        selected = null;
        pending.clear();
        for (const id of ['wfRows', 'wfDashboard', 'wfHistory', 'wfParts']) el(id).replaceChildren();
        el('wfPartsRefresh').disabled = true;
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
    // Every private JSON and CSV response must belong to the original institution
    // and public cookie session. The shield alone cannot authenticate a stale 200.
    const responseMatchesSession = response =>
        sessionCurrent() && /^[0-9A-F]{64}$/.test(session.reference) &&
        /^[1-9][0-9]*$/.test(session.tenant) &&
        response?.headers?.get('X-EduOS-Session-Reference') === session.reference &&
        response?.headers?.get('X-EduOS-Tenant-Id') === session.tenant;
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
        if (response.status === 401 || response.status === 403) {
            lockPage();
            throw new Error('session-changed');
        }
        await verifySession();
        // Reject before parsing private JSON, including a successful stale HTTP 200.
        if (!responseMatchesSession(response)) {
            lockPage();
            throw new Error('session-changed');
        }
        const data = await response.json().catch(() => null);
        // The institution can change during asynchronous JSON parsing.
        await verifySession();
        if (!responseMatchesSession(response)) {
            lockPage();
            throw new Error('session-changed');
        }
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
        if (state === 'Approved' && canManage) {
            options.push('schedule');
            if (!['Proposed', 'Approved'].includes(row.vendorStateCode)) options.push('cancelrequest');
        }
        if (canManage && ['Approved', 'Scheduled'].includes(state) &&
            !['Proposed', 'Approved'].includes(row.vendorStateCode)) options.push('vendorpropose');
        if (approver && row.requesterUserId !== currentUserId &&
            row.triagedByUserId !== currentUserId &&
            row.vendorProposedByUserId !== currentUserId &&
            ['Approved', 'Scheduled'].includes(state)) {
            if (row.vendorStateCode === 'Proposed') options.push('vendorapprove', 'vendorreject');
            if (row.vendorStateCode === 'Approved' &&
                row.vendorApprovedByUserId !== currentUserId) options.push('vendorrelease');
        }
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
        // Booking approval is not supplier billing or completed service proof.
        // The financial payable remains owned by a separate canonical invoice.
        const vendorWorkDone = row.vendorStateCode === 'Approved' &&
            ['PendingVerification', 'Closed'].includes(state);
        if (canManage && vendorWorkDone &&
            ['None', 'Returned', 'Stale'].includes(row.vendorAcceptanceStateCode))
            options.push('vendorattest');
        if (approver && vendorWorkDone &&
            row.requesterUserId !== currentUserId &&
            row.triagedByUserId !== currentUserId &&
            row.vendorProposedByUserId !== currentUserId &&
            row.vendorAcceptanceStateCode === 'Submitted')
            options.push('vendorverify', 'vendorreturn');
        if (canManage && state && state !== 'Closed' && row.slaDueAtUtc &&
            Date.parse(row.slaDueAtUtc) < Date.now()) options.push('escalate');
        return options;
    }
    function select(row) {
        if (!sessionCurrent()) { lockPage(); return; }
        const vendorEligible = manager && row.workOrderId > 0 &&
            row.stateCode === 'Approved' &&
            ['Approved', 'Scheduled'].includes(row.workOrderState) &&
            row.requesterUserId !== currentUserId &&
            !['Proposed', 'Approved'].includes(row.vendorStateCode);
        selected = row;
        vendorChoices.clear();
        vendorAvailabilityWindowKey = null;
        if (el('wfVendorSelected')) el('wfVendorSelected').value = '';
        if (manager) {
            // Re-selecting the same row invalidates any older vendor response.
            vendorGeneration++;
            vendorPage = 1;
            el('wfVendorPanel').classList.toggle('d-none', !vendorEligible);
            el('wfVendorRows').replaceChildren();
            el('wfVendorPage').textContent = '';
        }
        el('wfSelected').textContent = '#' + row.id + ' — ' + row.summary;
        el('wfDetails').textContent = [
            t('Campus', 'ক্যাম্পাস') + ': ' + row.campusId,
            t('Request', 'অনুরোধ') + ': ' + row.stateCode,
            t('Work order', 'ওয়ার্ক অর্ডার') + ': ' + (row.workOrderState || '—'),
            'SLA: ' + (row.slaDueAtUtc || '—'),
            t('Technician', 'টেকনিশিয়ান') + ': ' + (row.technicianUserId || '—'),
            t('Vendor booking', 'সরবরাহকারী বুকিং') + ': ' + (row.vendorStateCode || '—') +
                (row.vendorSupplierId ? ' #' + row.vendorSupplierId : ''),
            t('Vendor work evidence', 'সরবরাহকারীর কাজের প্রমাণ') + ': ' +
                vendorEvidenceLabel(row.vendorAcceptanceStateCode),
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
        partsPage = 1;
        el('wfPartsRefresh').disabled = !row.workOrderId;
        parts();
        history();
    }
    function toLocal(iso) {
        const d = new Date(iso);
        return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
    }
    async function suggestSla(rowId) {
        const generation = ++slaGeneration;
        try {
            const policy = await api('/' + rowId + '/sla');
            if (generation !== slaGeneration || !sessionCurrent() || selected?.id !== rowId ||
                el('wfAction').value !== 'triage' || el('wfDue').value) return;
            // Never overwrite an operator-entered deadline or invent a default.
            // The server recomputes the canonical deadline at actual triage.
            if (policy.configuredHours && policy.recommendedDueUtc)
                el('wfDue').value = toLocal(policy.recommendedDueUtc);
        } catch (error) {
            if (generation === slaGeneration && selected?.id === rowId &&
                el('wfAction').value === 'triage' && !blocked)
                notice(error.message);
        }
    }
    function toggleFields() {
        const action = el('wfAction').value;
        if (action === 'triage' && selected && manager)
            suggestSla(selected.id);
        else
            ++slaGeneration;
        for (const [id, enabled] of [
            ['wfDue', action === 'triage'], ['wfTech', action === 'schedule'],
            ['wfVendorSelected', action === 'vendorpropose'],
            ['wfStart', action === 'schedule' || action === 'vendorpropose'],
            ['wfEnd', action === 'schedule' || action === 'vendorpropose']
        ]) {
            const input = el(id);
            input.disabled = !enabled; input.required = enabled;
            input.closest('.col-12, .col-sm-6')?.classList.toggle('d-none', !enabled);
        }
        const scheduling = action === 'schedule';
        const rescheduling = scheduling && selected?.workOrderState === 'Scheduled';
        el('wfTechnicianLookup').classList.toggle('d-none', !scheduling);
        if (scheduling && selected) {
            technicianPage = 1;
            loadTechnicians();
        } else {
            invalidateTechnicians();
        }
        el('wfNote').required = rescheduling || ['reject', 'pause', 'complete', 'verify', 'return', 'unschedule', 'escalate', 'cancelrequest', 'cancelapprove', 'cancelreject'].includes(action) || action.startsWith('vendor');
        el('wfNote').minLength = rescheduling || ['return', 'escalate', 'cancelrequest', 'cancelapprove', 'cancelreject'].includes(action) ? 10 : action === 'unschedule' ? 5 : action.startsWith('vendor') ? 10 : 0;
        el('wfNote').placeholder = rescheduling
            ? t('Reason for rescheduling (10-500 characters)', 'সময়সূচি পরিবর্তনের কারণ (১০-৫০০ অক্ষর)')
            : '';
        el('wfNote').maxLength = ['schedule', 'unschedule', 'escalate', 'return', 'cancelrequest', 'cancelapprove', 'cancelreject'].includes(action) ? 500 : action.startsWith('vendor') ? 500 : 1000;
        if (manager) {
            const vendorAction = action === 'vendorpropose' && selected?.stateCode === 'Approved' &&
                ['Approved', 'Scheduled'].includes(selected?.workOrderState) &&
                selected?.requesterUserId !== currentUserId &&
                !['Proposed', 'Approved'].includes(selected?.vendorStateCode);
            el('wfVendorPanel').classList.toggle('d-none', !vendorAction);
            if (vendorAction) { vendorPage = 1; loadVendors(); }
            else invalidateVendors();
        }
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
    // The server is authoritative; this guard prevents misleading lookups and
    // stale choices when the two independently approved windows cannot intersect.
    function overlapsWindow(from, until) {
        const first = new Date(el('wfStart').value).getTime();
        const last = new Date(el('wfEnd').value).getTime();
        const begin = Date.parse(from), end = Date.parse(until);
        return Number.isFinite(first) && Number.isFinite(last) &&
            Number.isFinite(begin) && Number.isFinite(end) &&
            first < end && last > begin;
    }
    async function loadTechnicians() {
        if (blocked || !selected || el('wfAction').value !== 'schedule') return;
        const generation = ++technicianGeneration;
        const requestId = selected.id, campusId = selected.campusId;
        const currentWindowKey = windowKey();
        technicianChoices = new Map();
        availabilityWindowKey = null;
        el('wfTech').value = '';
        const picker = el('wfTechSelect');
        picker.replaceChildren();
        picker.disabled = true;
        el('wfTechPrev').disabled = true;
        el('wfTechNext').disabled = true;
        const startValue = el('wfStart').value, endValue = el('wfEnd').value;
        const start = new Date(startValue), end = new Date(endValue);
        if (!startValue || !endValue || !Number.isFinite(start.getTime()) ||
            !Number.isFinite(end.getTime()) || start >= end ||
            start.getTime() <= Date.now() ||
            !Number.isSafeInteger(Number(selected.workOrderId)) || Number(selected.workOrderId) <= 0) {
            el('wfTechPage').textContent = t(
                'Set a valid start and end time before finding available technicians.',
                'উপলব্ধ টেকনিশিয়ান খোঁজার আগে কাজের সঠিক শুরু ও শেষ সময় দিন।');
            return;
        }
        if (selected.vendorStateCode === 'Approved' &&
            !overlapsWindow(selected.vendorStartUtc, selected.vendorEndUtc)) {
            el('wfTechPage').textContent = t(
                'Schedule must overlap the approved vendor booking. Release or rebook first.',
                'সময়সূচি অনুমোদিত সরবরাহকারী বুকিংয়ের সঙ্গে মিলতে হবে। আগে বুকিং মুক্ত বা পুনর্নির্ধারণ করুন।');
            return;
        }
        el('wfTechPage').textContent = t('Checking availability…', 'উপলব্ধতা যাচাই হচ্ছে…');
        try {
            const q = new URLSearchParams({
                campusId: String(campusId), page: String(technicianPage), pageSize: '25',
                startUtc: start.toISOString(), endUtc: end.toISOString(),
                workOrderId: String(selected.workOrderId)
            });
            const search = el('wfTechSearch').value.trim();
            if (search) q.set('search', search);
            const data = await api('/technicians?' + q);
            if (generation !== technicianGeneration || blocked || selected?.id !== requestId ||
                el('wfAction').value !== 'schedule' || windowKey() !== currentWindowKey) return;
            availabilityWindowKey = currentWindowKey;
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
                (technicianChoices.size ? '' : t(' — no available technicians for this window',
                    ' — এই সময়ে উপলব্ধ টেকনিশিয়ান নেই'));
            el('wfTechPrev').disabled = technicianPage <= 1;
            el('wfTechNext').disabled = technicianPage >= technicianPages;
        } catch (error) {
            if (generation !== technicianGeneration || blocked || selected?.id !== requestId ||
                windowKey() !== currentWindowKey || error.message === 'session-changed') return;
            picker.replaceChildren();
            picker.disabled = true;
            el('wfTechPage').textContent = t('Lookup failed. Retry search.', 'খোঁজা ব্যর্থ। আবার চেষ্টা করুন।');
            notice(error.message);
        }
    }
    // The bounded supplier preview is advisory. Only a choice returned for
    // the current selected order may be proposed; server revalidates eligibility.
    async function loadVendors() {
        if (!manager || blocked || !selected || !selected.workOrderId ||
            el('wfAction').value !== 'vendorpropose' ||
            selected.stateCode !== 'Approved' ||
            !['Approved', 'Scheduled'].includes(selected.workOrderState) ||
            selected.requesterUserId === currentUserId) return;
        const generation = ++vendorGeneration;
        const requestId = selected.id, campusId = selected.campusId;
        const workOrderId = selected.workOrderId, currentWindowKey = windowKey();
        const rows = el('wfVendorRows');
        vendorChoices.clear();
        vendorAvailabilityWindowKey = null;
        el('wfVendorSelected').value = '';
        rows.replaceChildren();
        el('wfVendorPrev').disabled = true;
        el('wfVendorNext').disabled = true;
        const startValue = el('wfStart').value, endValue = el('wfEnd').value;
        const start = new Date(startValue), end = new Date(endValue);
        if (!startValue || !endValue || !Number.isFinite(start.getTime()) ||
            !Number.isFinite(end.getTime()) || start >= end || start.getTime() <= Date.now()) {
            el('wfVendorPage').textContent = t(
                'Set a valid future booking window before searching.',
                'খোঁজার আগে বুকিংয়ের সঠিক ভবিষ্যৎ সময়সীমা দিন।');
            return;
        }
        if (selected.workOrderState === 'Scheduled' &&
            !overlapsWindow(selected.scheduledStartUtc, selected.scheduledEndUtc)) {
            el('wfVendorPage').textContent = t(
                'Vendor booking must overlap the technician schedule. Change the booking window.',
                'সরবরাহকারী বুকিং টেকনিশিয়ানের সময়সূচির সঙ্গে মিলতে হবে। বুকিংয়ের সময় পরিবর্তন করুন।');
            return;
        }
        el('wfVendorPage').textContent = t('Checking available vendors…', 'উপলব্ধ সরবরাহকারী খোঁজা হচ্ছে…');
        try {
            const q = new URLSearchParams({ campusId: String(campusId),
                workOrderId: String(workOrderId), page: String(vendorPage), pageSize: '25',
                startUtc: start.toISOString(), endUtc: end.toISOString() });
            const search = el('wfVendorSearch').value.trim();
            if (search) q.set('search', search);
            const data = await api('/vendors?' + q);
            if (generation !== vendorGeneration || blocked ||
                selected?.id !== requestId || selected?.campusId !== campusId ||
                selected?.workOrderId !== workOrderId ||
                el('wfAction').value !== 'vendorpropose' ||
                windowKey() !== currentWindowKey) return;
            vendorAvailabilityWindowKey = currentWindowKey;
            vendorPages = Math.max(1, data.totalPages || 1);
            for (const vendor of data.items || []) {
                const item = document.createElement('li');
                item.className = 'list-group-item px-0';
                const supplierId = Number(vendor.supplierId);
                if (!Number.isSafeInteger(supplierId) || supplierId <= 0) continue;
                vendorChoices.set(supplierId, vendor);
                const choose = document.createElement('button');
                choose.type = 'button';
                choose.className = 'btn btn-outline-secondary btn-sm text-start';
                choose.textContent = (vendor.supplierName || '') + ' (' +
                    (vendor.supplierCode || '') + ')';
                choose.addEventListener('click', () => {
                    if (!sessionCurrent() || selected?.id !== requestId ||
                        el('wfAction').value !== 'vendorpropose' ||
                        vendorAvailabilityWindowKey !== windowKey() ||
                        !vendorChoices.has(supplierId)) return;
                    el('wfVendorSelected').value = String(supplierId);
                    notice(t('Vendor selected. Submit a proposal for independent approval.',
                        'সরবরাহকারী নির্বাচিত। স্বতন্ত্র অনুমোদনের জন্য প্রস্তাব জমা দিন।'));
                });
                item.append(choose);
                rows.append(item);
            }
            if (!rows.children.length) {
                const item = document.createElement('li');
                item.className = 'list-group-item px-0 text-muted';
                item.textContent = t('No available approved vendors for this booking window.',
                    'এই বুকিং সময়ে উপলব্ধ অনুমোদিত সরবরাহকারী নেই।');
                rows.append(item);
            }
            el('wfVendorPage').textContent = vendorPage + ' / ' + vendorPages;
            el('wfVendorPrev').disabled = vendorPage <= 1;
            el('wfVendorNext').disabled = vendorPage >= vendorPages;
        } catch (error) {
            if (generation !== vendorGeneration || blocked ||
                selected?.id !== requestId || selected?.workOrderId !== workOrderId ||
                windowKey() !== currentWindowKey ||
                error.message === 'session-changed') return;
            rows.replaceChildren();
            el('wfVendorPage').textContent = t('Vendor lookup failed; retry.',
                'সরবরাহকারী খোঁজা ব্যর্থ; আবার চেষ্টা করুন।');
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
    async function parts() {
        if (!selected || blocked) return;
        const generation = ++partsGeneration, requestId = selected.id, requestedPage = partsPage;
        const box = el('wfParts');
        box.textContent = t('Loading parts ledger…', 'যন্ত্রাংশের হিসাব লোড হচ্ছে…');
        try {
            const data = await api('/' + requestId + '/parts?page=' + requestedPage + '&pageSize=10');
            if (generation !== partsGeneration || selected?.id !== requestId || blocked) return;
            box.replaceChildren();
            if (!data.items?.length) {
                box.textContent = t('No parts issued for this work order.', 'এই কাজের জন্য কোনো যন্ত্রাংশ ইস্যু নেই।');
                return;
            }
            const list = document.createElement('ol');
            list.className = 'list-group list-group-numbered';
            for (const move of data.items) {
                const entry = document.createElement('li');
                entry.className = 'list-group-item py-2';
                const cost = Number(move.netCost ?? 0).toFixed(2);
                const items = (move.lines || []).map(l =>
                    '#' + l.inventoryItemId + ' × ' + l.quantity + (l.lotOrBatchNo ? ' [' + l.lotOrBatchNo + ']' : '')).join(', ');
                entry.textContent = move.movementNumber + ' | ' + move.stateCode + ' | ' +
                    t('Actual net cost', 'প্রকৃত নিট খরচ') + ': ' + cost + ' | Finance: ' + move.financeState +
                    ' | ' + items;
                list.append(entry);
            }
            box.append(list);
            if (requestedPage > 1) {
                const prev = document.createElement('button');
                prev.type = 'button'; prev.className = 'btn btn-outline-secondary btn-sm mt-2 me-2';
                prev.textContent = t('Previous', 'আগের');
                prev.addEventListener('click', () => { partsPage--; parts(); });
                box.append(prev);
            }
            if (data.hasNextPage) {
                const next = document.createElement('button');
                next.type = 'button'; next.className = 'btn btn-outline-secondary btn-sm mt-2';
                next.textContent = t('Next', 'পরের');
                next.addEventListener('click', () => { partsPage++; parts(); });
                box.append(next);
            }
        } catch (error) {
            if (generation !== partsGeneration || selected?.id !== requestId || blocked || error.message === 'session-changed') return;
            box.textContent = error.message;
        }
    }
    function clearSelection() {
        vendorGeneration++;
        vendorAvailabilityWindowKey = null;
        const vendorPanel = el('wfVendorPanel');
        if (vendorPanel) {
            vendorPanel.classList.add('d-none');
            el('wfVendorRows').replaceChildren();
            el('wfVendorPage').textContent = '';
        }
        selected = null;
        vendorChoices.clear();
        if (el('wfVendorSelected')) el('wfVendorSelected').value = '';
        historyGeneration++;
        partsGeneration++;
        technicianGeneration++;
        technicianChoices.clear();
        el('wfTech').value = '';
        el('wfTechSelect').replaceChildren();
        el('wfTechSelect').disabled = true;
        el('wfSelected').textContent = t('Select a request', 'কাজ নির্বাচন করুন');
        el('wfDetails').textContent = '';
        el('wfTransition').classList.add('d-none');
        el('wfHistory').replaceChildren();
        el('wfParts').replaceChildren();
        el('wfPartsRefresh').disabled = true;
    }
    el('wfPartsRefresh').addEventListener('click', () => {
        if (!blocked && selected) { partsPage = 1; parts(); }
    });
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
    if (manager) {
        el('wfVendorFind').addEventListener('click', () => {
            if (!blocked) { vendorPage = 1; loadVendors(); }
        });
        el('wfVendorSearch').addEventListener('input', invalidateVendors);
        el('wfVendorSearch').addEventListener('keydown', e => {
            if (e.key === 'Enter') { e.preventDefault(); vendorPage = 1; loadVendors(); }
        });
        el('wfVendorPrev').addEventListener('click', () => {
            if (!blocked && vendorPage > 1) { vendorPage--; loadVendors(); }
        });
        el('wfVendorNext').addEventListener('click', () => {
            if (!blocked && vendorPage < vendorPages) { vendorPage++; loadVendors(); }
        });
    }
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
        el('wfTech').value = availabilityWindowKey === windowKey() &&
            technicianChoices.has(id) ? String(id) : '';
    });
    for (const id of ['wfStart', 'wfEnd']) {
        el(id).addEventListener('input', () => {
            invalidateTechnicians();
            invalidateVendors();
        });
        el(id).addEventListener('change', () => {
            if (blocked || !selected) return;
            if (el('wfAction').value === 'schedule') {
                technicianPage = 1;
                loadTechnicians();
            } else if (el('wfAction').value === 'vendorpropose') {
                vendorPage = 1;
                loadVendors();
            }
        });
    }
    el('workflowSearch').addEventListener('submit', e => { e.preventDefault(); page = 1; clearSelection(); load(); });
    el('wfPrev').addEventListener('click', () => { if (page > 1) { page--; clearSelection(); load(); } });
    el('wfNext').addEventListener('click', () => { if (page < pages) { page++; clearSelection(); load(); } });
    // A direct location.assign download cannot validate the response tenant or
    // cookie-session reference. Fetch and verify the bounded CSV before release.
    let exporting = false;
    el('facilityExport').addEventListener('click', async () => {
        if (blocked || exporting) return;
        exporting = true;
        const exportButton = el('facilityExport');
        exportButton.disabled = true;
        try {
            await verifySession();
            const q = new URLSearchParams();
            if (el('wfCampusFilter').value) q.set('campusId', el('wfCampusFilter').value);
            if (el('wfStateFilter').value) q.set('state', el('wfStateFilter').value);
            if (el('wfOverdueOnly').checked) q.set('overdueOnly', 'true');
            const response = await fetch(base + '/export?' + q, {
                method: 'GET', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'text/csv' }
            });
            if (response.status === 401 || response.status === 403) {
                lockPage();
                throw new Error('session-changed');
            }
            await verifySession();
            if (!responseMatchesSession(response)) {
                lockPage();
                throw new Error('session-changed');
            }
            if (!response.ok || !(response.headers.get('content-type') || '').includes('text/csv'))
                throw new Error(t('Export denied or invalid; narrow the filters and retry.',
                    'এক্সপোর্ট প্রত্যাখ্যাত বা ভুল; ফিল্টার কমিয়ে আবার চেষ্টা করুন।'));
            const blob = await response.blob();
            await verifySession();
            if (!responseMatchesSession(response)) {
                lockPage();
                throw new Error('session-changed');
            }
            const href = URL.createObjectURL(blob);
            const anchor = document.createElement('a');
            anchor.href = href;
            anchor.download = 'facilities-work-orders.csv';
            document.body.append(anchor);
            anchor.click();
            anchor.remove();
            setTimeout(() => URL.revokeObjectURL(href), 1000);
            notice(t('Scoped work-order CSV downloaded.', 'অনুমোদিত কাজের CSV ডাউনলোড হয়েছে।'), true);
        } catch (error) {
            if (!blocked) notice(error.message);
        } finally {
            exporting = false;
            if (!blocked) exportButton.disabled = false;
        }
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
        if ((el('wfAction').value === 'vendorpropose' &&
             selected.workOrderState === 'Scheduled' &&
             !overlapsWindow(selected.scheduledStartUtc, selected.scheduledEndUtc)) ||
            (el('wfAction').value === 'schedule' &&
             selected.vendorStateCode === 'Approved' &&
             !overlapsWindow(selected.vendorStartUtc, selected.vendorEndUtc))) {
            notice(t('Technician and approved vendor windows must overlap; review the schedule and booking.',
                'টেকনিশিয়ান ও অনুমোদিত সরবরাহকারীর সময়সীমা মিলতে হবে; সময়সূচি ও বুকিং যাচাই করুন।'));
            return;
        }
        if (el('wfAction').value === 'vendorpropose' &&
            (vendorAvailabilityWindowKey !== windowKey() ||
             !vendorChoices.has(Number(el('wfVendorSelected').value)) ||
             new Date(el('wfStart').value).getTime() <= Date.now())) {
            notice(t('Check the booking window and choose an available approved vendor.',
                'বুকিংয়ের সময়সীমা যাচাই করে উপলব্ধ অনুমোদিত সরবরাহকারী নির্বাচন করুন।'));
            return;
        }
        if (el('wfAction').value === 'schedule' &&
            (availabilityWindowKey !== windowKey() ||
             !technicianChoices.has(Number(el('wfTech').value)))) {
            notice(t('Check the time window and choose an available technician.',
                'সময়সীমা যাচাই করে উপলব্ধ টেকনিশিয়ান নির্বাচন করুন।'));
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
            scheduledEndUtc: action === 'schedule' ? date('wfEnd') : null,
            vendorSupplierId: action === 'vendorpropose' ? Number(el('wfVendorSelected').value) : null,
            vendorStartUtc: action === 'vendorpropose' ? date('wfStart') : null,
            vendorEndUtc: action === 'vendorpropose' ? date('wfEnd') : null
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
            if (!blocked) notice(action === 'start' && error.status === 409
                ? t('Schedule or approved vendor booking changed or expired. Ask an authorized manager to review and rebook, then reload.',
                    'কাজের সময়সূচি বা অনুমোদিত সরবরাহকারীর বুকিং পরিবর্তিত হয়েছে অথবা মেয়াদ শেষ। অনুমোদিত ম্যানেজারকে যাচাই ও পুনরায় বুকিং করতে বলুন, তারপর পুনরায় লোড করুন।')
                : error.message);
        } finally {
            transitioning = false;
            if (!blocked && submit) submit.disabled = false;
        }
    });
    load();
    loadLocations();
})();
