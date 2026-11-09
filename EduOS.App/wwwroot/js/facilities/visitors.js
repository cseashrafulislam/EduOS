(() => {
    'use strict';
    const root = document.getElementById('facilityVisitors');
    if (!root) return;
    const bn = root.dataset.bn === 'true', manager = root.dataset.manager === 'true';
    const privacyReviewer = root.dataset.privacyReviewer === 'true';
    const actor = Number(root.dataset.userId || 0);
    const $ = id => document.getElementById(id);
    const label = (en, bangla) => bn ? bangla : en;
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const originalSession = {
        tenant: document.body?.dataset.authTenantId || '',
        reference: document.body?.dataset.authSessionReference || ''
    };
    const sessionError = () => label('Institution session changed; reload or sign in again.',
        'প্রতিষ্ঠানের সেশন পরিবর্তিত; পুনরায় লোড বা লগইন করুন।');
    const matchesSession = (session, response) =>
        /^[0-9A-F]{64}$/.test(session.reference) &&
        /^[1-9][0-9]*$/.test(session.tenant) &&
        document.body?.dataset.authTenantId === session.tenant &&
        document.body?.dataset.authSessionReference === session.reference &&
        window.EduOSSessionTransitionPending?.() !== true &&
        document.getElementById('eduosSessionShield')?.hidden !== false &&
        (!response || (response.headers?.get('X-EduOS-Session-Reference') === session.reference &&
            response.headers?.get('X-EduOS-Tenant-Id') === session.tenant));
    const status = message => { $('fvStatus').textContent = message; };
    // On revocation, erase rendered visitor PII and invalidate in-flight reads.
    const invalidateAuthorization = () => {
        ++requestEpoch; ++hostEpoch; ++occupancyEpoch; ++exportEpoch; ++policyEpoch;
        $('fvRows')?.replaceChildren();
        $('fvHost')?.replaceChildren();
        $('fvOccupancyResult')?.replaceChildren();
        $('fvPolicyRows')?.replaceChildren();
        $('fvPrivacyAuditResult')?.replaceChildren();
        root.querySelectorAll('form, #fvRefresh, #fvBadge, #fvReason').forEach(element => {
            element.hidden = true;
            element.querySelectorAll('input, select, button').forEach(control => {
                control.disabled = true;
                if ('value' in control) control.value = '';
            });
        });
        status(label('Visitor access revoked; reload or sign in again.',
            'ভিজিটর প্রবেশাধিকার বাতিল হয়েছে; পুনরায় লগইন বা রিলোড করুন।'));
    };
    const verifySession = async session => {
        if (!matchesSession(session) || typeof window.EduOSVerifySessionContext !== 'function') {
            invalidateAuthorization();
            throw new Error(sessionError());
        }
        await window.EduOSVerifySessionContext();
        if (!matchesSession(session)) {
            invalidateAuthorization();
            throw new Error(sessionError());
        }
    };
    const api = async (url, method = 'GET', data) => {
        const session = originalSession;
        await verifySession(session);
        const response = await fetch(url, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers: { 'Accept': 'application/json', ...(method !== 'GET' ? {
                'Content-Type': 'application/json', 'RequestVerificationToken': token } : {}) },
            ...(data === undefined ? {} : { body: JSON.stringify(data) })
        });
        if (response.status === 401 || response.status === 403) {
            invalidateAuthorization();
            throw new Error(sessionError());
        }
        // Do not parse private JSON from a successful response with stale scope headers.
        if (!matchesSession(session, response)) {
            invalidateAuthorization();
            throw new Error(sessionError());
        }
        const payload = await response.json().catch(() => null);
        await verifySession(session);
        if (!response.ok || !payload?.success)
            throw new Error(payload?.message || label('Request denied or stale; refresh.', 'অনুরোধ প্রত্যাখ্যাত বা তথ্য পুরোনো; রিফ্রেশ করুন।'));
        return payload.data;
    };
    const utc = id => {
        const value = $(id).value;
        return value ? new Date(value).toISOString() : null;
    };
    let requestEpoch = 0;
    let pendingCreateId = crypto.randomUUID();
    const commandIds = new Map();
    const inFlightActions = new Set();
    const createForm = $('fvCreate'), policyForm = $('fvPolicyRequest');
    const createGuard = createForm && window.EduOSFormGuard?.create(createForm);
    const policyGuard = policyForm && window.EduOSFormGuard?.create(policyForm);
    let creating = false, requestingPolicy = false, hostReady = false;
    let lastCreateFingerprint = '', lastPolicyFingerprint = '';
    const button = (name, action, row, handler) => {
        const b = document.createElement('button');
        b.type = 'button'; b.className = 'btn btn-outline-primary btn-sm';
        b.textContent = name;
        b.addEventListener('click', async () => {
            if (b.disabled) return;
            b.disabled = true;
            try { await handler(row, action); }
            finally { b.disabled = false; }
        });
        return b;
    };
    const act = async (row, action) => {
        const reason = action === 'redactrequest' ? 'Visitor retention period expired' :
            action === 'redactapprove' ? 'Independent retention approval' :
            $('fvReason').value.trim();
        const badgeCode = $('fvBadge').value.trim().toUpperCase();
        if (action === 'checkin' && !/^[A-Z0-9-]{4,20}$/.test(badgeCode)) {
            status(label('Enter a valid badge code.', 'সঠিক ব্যাজ কোড দিন।')); return;
        }
        const reasonedAction = ['hostreject', 'incident', 'incidentresolve', 'incidentclose',
            'holdrequest', 'holdapprove', 'holdreject', 'holdreleaserequest', 'holdrelease'];
        if (reasonedAction.includes(action) && (reason.length < 10 || reason.length > 200)) {
            status(label('A 10–200 character case reason is required.',
                'ঘটনা বা আইনগত সিদ্ধান্তের জন্য ১০–২০০ অক্ষরের কারণ লিখুন।')); return;
        }
        const key = [row.id, action, row.rowVersion, badgeCode, reason].join('|');
        if (inFlightActions.has(key)) return;
        if (!commandIds.has(key)) commandIds.set(key, crypto.randomUUID());
        inFlightActions.add(key);
        try {
            await api('/api/facilities/visitors/' + row.id + '/' + action, 'POST', {
                clientCommandId: commandIds.get(key), rowVersion: row.rowVersion,
                badgeCode: action === 'checkin' ? badgeCode : null,
                reason: reason || null
            });
            commandIds.delete(key);
            $('fvReason').value = ''; $('fvBadge').value = '';
            await load();
        } catch (e) { status(e.message); }
        finally { inFlightActions.delete(key); }
    };
    const history = async (row, card) => {
        const existing = card.querySelector('[data-visitor-history]');
        if (existing) { existing.remove(); return; }
        const epoch = requestEpoch;
        const panel = document.createElement('div');
        panel.dataset.visitorHistory = 'true';
        panel.className = 'small border-top mt-2 pt-2';
        panel.textContent = label('Loading event history…', 'ঘটনার ইতিহাস লোড হচ্ছে…');
        card.append(panel);
        try {
            const data = await api('/api/facilities/visitors/' + row.id + '/history?page=1&pageSize=50');
            if (epoch !== requestEpoch || !card.isConnected) return;
            panel.replaceChildren();
            for (const event of data.items || []) {
                const line = document.createElement('p');
                line.className = 'mb-1';
                line.textContent = event.actionCode + ' · ' + new Date(event.occurredAtUtc).toLocaleString() +
                    (manager && event.reason ? ' · ' + event.reason : '');
                panel.append(line);
            }
            if (!panel.childElementCount) panel.textContent = label('No events.', 'কোনো ঘটনা নেই।');
        } catch (e) {
            if (epoch === requestEpoch && card.isConnected) panel.textContent = e.message;
        }
    };
    const load = async () => {
        const epoch = ++requestEpoch;
        try {
            const data = await api('/api/facilities/visitors?page=1&pageSize=50');
            if (epoch !== requestEpoch) return;
            const holder = $('fvRows'); holder.replaceChildren();
            for (const row of data.items || []) {
                const card = document.createElement('article');
                card.className = 'border rounded p-2';
                const heading = document.createElement('div');
                heading.className = 'fw-semibold';
                heading.textContent = row.visitorName + ' — ' + row.stateCode;
                const detail = document.createElement('p');
                detail.className = 'small text-muted mb-2';
                detail.textContent = label('Campus ', 'ক্যাম্পাস ') + row.campusId +
                    label(' · Host ', ' · হোস্ট ') + row.hostUserId + ' · ' + row.purpose +
                    ' · ' + new Date(row.visitStartUtc).toLocaleString() +
                    ' – ' + new Date(row.visitEndUtc).toLocaleString() +
                    (row.badgeCode ? ' · ' + row.badgeCode : '');
                card.append(heading, detail);
                if (row.incidentStateCode !== 'None' || row.legalHoldStateCode !== 'None') {
                    const caseLine = document.createElement('p');
                    caseLine.className = 'small text-muted mb-2';
                    caseLine.textContent = label('Incident: ', 'ঘটনা: ') + (row.incidentStateCode || 'None') +
                        ' · ' + label('Legal hold: ', 'আইনগত হোল্ড: ') + (row.legalHoldStateCode || 'None');
                    card.append(caseLine);
                }
                const actions = document.createElement('div');
                actions.className = 'd-flex flex-wrap gap-2';
                if (row.hostUserId === actor && row.stateCode === 'PendingHost') {
                    actions.append(button(label('Approve', 'অনুমোদন'), 'hostapprove', row, act),
                        button(label('Reject', 'প্রত্যাখ্যান'), 'hostreject', row, act));
                }
                if (manager && row.stateCode === 'Approved')
                    actions.append(button(label('Check in', 'প্রবেশ'), 'checkin', row, act));
                if (manager && row.stateCode === 'CheckedIn')
                    actions.append(button(label('Check out', 'প্রস্থান'), 'checkout', row, act));
                if (manager && (row.stateCode === 'CheckedIn' || row.stateCode === 'CheckedOut') &&
                    !row.privacyRedacted)
                    actions.append(button(label('Report incident', 'ঘটনা নথিভুক্ত'), 'incident', row, act));
                // Case/hold actions use the same server-validated rowversion and
                // command-id workflow; the UI never authorizes independently.
                if (manager && !row.privacyRedacted) {
                    if (row.stateCode === 'CheckedOut' && row.incidentStateCode === 'Open')
                        actions.append(button(label('Request incident closure', 'ঘটনা সমাধানের অনুরোধ'),
                            'incidentresolve', row, act));
                    if (privacyReviewer && row.incidentStateCode === 'PendingReview')
                        actions.append(button(label('Approve incident closure', 'ঘটনা নিষ্পত্তি অনুমোদন'),
                            'incidentclose', row, act));
                    if (row.legalHoldStateCode === 'None' || row.legalHoldStateCode === 'Clear')
                        actions.append(button(label('Request legal hold', 'আইনগত হোল্ড অনুরোধ'),
                            'holdrequest', row, act));
                    if (privacyReviewer && row.legalHoldStateCode === 'Pending')
                        actions.append(
                            button(label('Approve hold', 'হোল্ড অনুমোদন'), 'holdapprove', row, act),
                            button(label('Reject hold', 'হোল্ড প্রত্যাখ্যান'), 'holdreject', row, act));
                    if (row.legalHoldStateCode === 'Active')
                        actions.append(button(label('Request hold release', 'হোল্ড ছাড়ের অনুরোধ'),
                            'holdreleaserequest', row, act));
                    if (privacyReviewer && row.legalHoldStateCode === 'ReleasePending')
                        actions.append(button(label('Approve hold release', 'হোল্ড ছাড় অনুমোদন'),
                            'holdrelease', row, act));
                }
                // Retention requires an administrator-configured 30–3650 day
                // policy, no legal hold/incident, and a separate live checker.
                const caseBlocksRedaction = ['Open', 'PendingReview'].includes(row.incidentStateCode) ||
                    ['Pending', 'Active', 'ReleasePending'].includes(row.legalHoldStateCode);
                if (manager && (row.stateCode === 'CheckedOut' || row.stateCode === 'Rejected') &&
                    !row.privacyRedacted && !caseBlocksRedaction) {
                    if (row.redactionRequested && privacyReviewer)
                        actions.append(button(label('Approve redaction', 'তথ্য অপসারণ অনুমোদন'),
                            'redactapprove', row, act));
                    else if (!row.redactionRequested)
                        actions.append(button(label('Request redaction', 'তথ্য অপসারণ অনুরোধ'),
                            'redactrequest', row, act));
                }
                actions.append(button(label('History', 'ইতিহাস'), 'history', row, () => history(row, card)));
                card.append(actions); holder.append(card);
            }
            status(label('Authorized visits loaded.', 'অনুমোদিত ভিজিট লোড হয়েছে।'));
        } catch (e) { if (epoch === requestEpoch) status(e.message); }
    };
    let hostEpoch = 0, lastHostCampus = null;
    const hosts = async () => {
        const campus = Number($('fvCampus')?.value || 0);
        const select = $('fvHost');
        if (!select) return;
        // Duplicate change/blur events for the same campus must not erase
        // a host already chosen from an authoritative lookup.
        if (campus === lastHostCampus && hostReady && select.options.length > 1) return;
        lastHostCampus = campus;
        const epoch = ++hostEpoch;
        hostReady = false;
        select.disabled = true;
        select.replaceChildren(new Option(label('Select host', 'হোস্ট নির্বাচন'), ''));
        if (!Number.isSafeInteger(campus) || campus <= 0) return;
        try {
            const data = await api('/api/facilities/visitors/hosts?campusId=' + campus);
            if (epoch !== hostEpoch || Number($('fvCampus').value) !== campus) return;
            for (const h of data.items || [])
                select.add(new Option(h.displayName + ' (#' + h.userId + ')', String(h.userId)));
            hostReady = select.options.length > 1;
            select.disabled = !hostReady;
        } catch (e) { if (epoch === hostEpoch) status(e.message); }
    };
    $('fvCampus')?.addEventListener('change', hosts);
    $('fvRefresh').addEventListener('click', load);
    $('fvCreate')?.addEventListener('submit', async e => {
        e.preventDefault();
        if (creating || !hostReady || !createForm.reportValidity()) return;
        const start = utc('fvStart'), end = utc('fvEnd');
        if (!start || !end || new Date(end) <= new Date(start)) {
            status(label('Visit end must be later than start.', 'ভিজিটের শেষ সময় শুরুর পরে হতে হবে।'));
            return;
        }
        const payload = {
            campusId: Number($('fvCampus').value), hostUserId: Number($('fvHost').value),
            visitorName: $('fvName').value.trim(), purpose: $('fvPurpose').value.trim(),
            visitStartUtc: start, visitEndUtc: end
        };
        const fingerprint = JSON.stringify(payload);
        if (fingerprint !== lastCreateFingerprint) {
            lastCreateFingerprint = fingerprint;
            pendingCreateId = crypto.randomUUID();
        }
        creating = true; createForm.inert = true;
        try {
            await api('/api/facilities/visitors', 'POST', {
                clientRequestId: pendingCreateId, ...payload
            });
            pendingCreateId = crypto.randomUUID(); lastCreateFingerprint = '';
            $('fvName').value = ''; $('fvPurpose').value = '';
            createGuard?.markClean();
            await load();
        } catch (err) { status(err.message); }
        finally { creating = false; createForm.inert = false; }
    });

    // A PII-free campus occupancy snapshot is desk-only and server authorized.
    let occupancyEpoch = 0;
    $('fvOccupancy')?.addEventListener('submit', async event => {
        event.preventDefault();
        const campusId = Number($('fvOccupancyCampus').value);
        const epoch = ++occupancyEpoch;
        const result = $('fvOccupancyResult');
        result.textContent = label('Loading occupancy…', 'উপস্থিতি লোড হচ্ছে…');
        if (!Number.isSafeInteger(campusId) || campusId <= 0) {
            result.textContent = label('Choose a valid campus.', 'সঠিক ক্যাম্পাস নির্বাচন করুন।');
            return;
        }
        try {
            const snapshot = await api('/api/facilities/visitors/occupancy?campusId=' + campusId);
            if (epoch !== occupancyEpoch) return;
            result.textContent = label('Checked in: ', 'উপস্থিত: ') + snapshot.checkedInCount +
                ' / ' + (snapshot.maxConcurrent == null
                    ? label('No configured ceiling', 'সীমা কনফিগার করা নেই') : snapshot.maxConcurrent) +
                ' · ' + label('Overdue: ', 'নির্ধারিত সময় পার: ') + snapshot.overdueCount;
        } catch (error) { if (epoch === occupancyEpoch) result.textContent = error.message; }
    });

    let policyEpoch = 0, pendingPolicyId = crypto.randomUUID();
    const policyReviewIds = new Map();
    const loadPolicies = async () => {
        const campusId = Number($('fvPolicyCampus')?.value || 0);
        if (!Number.isSafeInteger(campusId) || campusId <= 0) {
            status(label('Choose a campus to review policies.', 'নীতিমালা দেখতে ক্যাম্পাস নির্বাচন করুন।'));
            return;
        }
        const epoch = ++policyEpoch;
        try {
            const result = await api('/api/facilities/visitors/policies?campusId=' + campusId);
            if (epoch !== policyEpoch) return;
            const holder = $('fvPolicyRows'); holder.replaceChildren();
            for (const row of result.items || []) {
                const card = document.createElement('div');
                card.className = 'border rounded p-2';
                const detail = document.createElement('span');
                detail.textContent = '#' + row.id + ' · ' + row.policyCode + ': ' +
                    (row.previousValue ?? label('(not set)', '(নেই)')) + ' → ' +
                    row.proposedValue + ' · ' + row.stateCode +
                    ' · ' + label('Requested by ', 'আবেদনকারী ') + row.requestedByUserId +
                    ' · ' + row.requestReason;
                card.append(detail);
                if (privacyReviewer && row.stateCode === 'Submitted' && row.requestedByUserId !== actor) {
                    const controls = document.createElement('div');
                    controls.className = 'd-flex gap-2 mt-2';
                    for (const decision of ['approve', 'reject']) {
                        const btn = document.createElement('button');
                        btn.type = 'button'; btn.className = 'btn btn-outline-primary btn-sm';
                        btn.textContent = decision === 'approve'
                            ? label('Approve', 'অনুমোদন') : label('Reject', 'প্রত্যাখ্যান');
                        btn.addEventListener('click', async () => {
                            if (btn.disabled) return;
                            const reason = $('fvPolicyReviewReason')?.value.trim() || '';
                            if (reason.length < 10) {
                                status(label('Enter a 10-character independent review reason.',
                                    'পর্যালোচনার কমপক্ষে ১০ অক্ষরের কারণ দিন।')); return;
                            }
                            const reviewKey = ['policy', row.id, decision, row.rowVersion, reason].join('|');
                            if (inFlightActions.has(reviewKey)) return;
                            if (!policyReviewIds.has(reviewKey)) policyReviewIds.set(reviewKey, crypto.randomUUID());
                            const commandId = policyReviewIds.get(reviewKey);
                            inFlightActions.add(reviewKey);
                            btn.disabled = true;
                            try {
                                await api('/api/facilities/visitors/policies/' + row.id + '/' + decision,
                                    'POST', { clientCommandId: commandId, rowVersion: row.rowVersion, reason });
                                policyReviewIds.delete(reviewKey);
                                $('fvPolicyReviewReason').value = '';
                                await loadPolicies();
                            } catch (error) { status(error.message); }
                            finally { btn.disabled = false; inFlightActions.delete(reviewKey); }
                        });
                        controls.append(btn);
                    }
                    card.append(controls);
                }
                holder.append(card);
            }
            if (!holder.childElementCount)
                holder.textContent = label('No requests for this campus.', 'এই ক্যাম্পাসে কোনো অনুরোধ নেই।');
        } catch (error) { if (epoch === policyEpoch) status(error.message); }
    };
    $('fvPolicyLoad')?.addEventListener('click', loadPolicies);
    $('fvPolicyRequest')?.addEventListener('submit', async event => {
        event.preventDefault();
        if (requestingPolicy || !policyForm.reportValidity()) return;
        const payload = {
            campusId: Number($('fvPolicyCampus').value), policyCode: $('fvPolicyCode').value,
            proposedValue: $('fvPolicyValue').value.trim(), reason: $('fvPolicyReason').value.trim()
        };
        const fingerprint = JSON.stringify(payload);
        if (fingerprint !== lastPolicyFingerprint) {
            lastPolicyFingerprint = fingerprint;
            pendingPolicyId = crypto.randomUUID();
        }
        requestingPolicy = true; policyForm.inert = true;
        try {
            await api('/api/facilities/visitors/policies', 'POST', {
                clientRequestId: pendingPolicyId, ...payload
            });
            pendingPolicyId = crypto.randomUUID(); lastPolicyFingerprint = '';
            $('fvPolicyReason').value = '';
            policyGuard?.markClean();
            await loadPolicies();
        } catch (error) { status(error.message); }
        finally { requestingPolicy = false; policyForm.inert = false; }
    });

    // Never release a CSV after tenant/session change, or after a newer export request.
    let exportEpoch = 0;
    $('fvExport')?.addEventListener('submit', async event => {
        event.preventDefault();
        const button = $('fvExportButton');
        const campusId = Number($('fvExportCampus').value);
        const fromUtc = utc('fvExportFrom'), toUtc = utc('fvExportTo');
        if (!Number.isSafeInteger(campusId) || campusId <= 0 || !fromUtc || !toUtc ||
            new Date(toUtc) <= new Date(fromUtc) ||
            new Date(toUtc) - new Date(fromUtc) > 31 * 86400000) {
            status(label('Choose a campus and a window of at most 31 days.',
                'ক্যাম্পাস ও সর্বোচ্চ ৩১ দিনের সময়সীমা নির্বাচন করুন।'));
            return;
        }
        const epoch = ++exportEpoch;
        const session = originalSession;
        const verify = async () => {
            await verifySession(session);
            if (epoch !== exportEpoch) throw new Error(sessionError());
        };
        button.disabled = true;
        try {
            await verify();
            // Export writes a scoped access audit; require antiforgery and POST.
            const response = await fetch('/api/facilities/visitors/export', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'text/csv', 'Content-Type': 'application/json',
                    RequestVerificationToken: token },
                body: JSON.stringify({ campusId, fromUtc, toUtc })
            });
            if (!response.ok) {
                if (response.status === 401 || response.status === 403) invalidateAuthorization();
                const payload = await response.json().catch(() => null);
                throw new Error(payload?.message || label('Export denied; narrow filters or reload.',
                    'এক্সপোর্ট প্রত্যাখ্যাত; সময়সীমা কমান বা রিলোড করুন।'));
            }
            if (!matchesSession(session, response)) {
                invalidateAuthorization();
                throw new Error(sessionError());
            }
            if (!(response.headers.get('content-type') || '').includes('text/csv'))
                throw new Error(label('Unexpected export response.', 'অপ্রত্যাশিত এক্সপোর্ট ফলাফল।'));
            const blob = await response.blob();
            await verify();
            const href = URL.createObjectURL(blob);
            const anchor = document.createElement('a');
            anchor.href = href; anchor.download = 'facility-visitor-access-summary.csv';
            document.body.append(anchor); anchor.click(); anchor.remove();
            setTimeout(() => URL.revokeObjectURL(href), 1000);
            status(label('PII-minimized CSV exported.', 'পরিচয়বিহীন সীমিত CSV এক্সপোর্ট হয়েছে।'));
        } catch (error) { if (epoch === exportEpoch) status(error.message); }
        finally { if (epoch === exportEpoch) button.disabled = false; }
    });
    // Security posture is a tenant-only aggregate report, never a raw
    // export of old names, badge codes, incident narratives or JSON payloads.
    let privacyAuditEpoch = 0;
    $('fvPrivacyAudit')?.addEventListener('click', async () => {
        const epoch = ++privacyAuditEpoch;
        const result = $('fvPrivacyAuditResult');
        result.textContent = label('Checking stored audit payload counts…',
            'সংরক্ষিত অডিট তথ্যের সংখ্যা যাচাই করা হচ্ছে…');
        try {
            const data = await api('/api/facilities/visitors/privacy-audit');
            if (epoch !== privacyAuditEpoch || !matchesSession(originalSession)) return;
            const remaining = data.legacyVisitAuditPayloadRows + data.legacyEventAuditPayloadRows;
            result.textContent =
                label('Total visits: ', 'মোট ভিজিট: ') + data.visitorVisits + ' · ' +
                label('Redacted visits: ', 'তথ্য অপসারিত ভিজিট: ') + data.fullyRedactedVisits + ' · ' +
                label('Legacy private audit rows remaining: ', 'পুরোনো ব্যক্তিগত অডিট সারি বাকি: ') +
                remaining + ' · ' +
                label('Redacted visits still with private audit: ',
                    'তথ্য অপসারিত ভিজিটেও ব্যক্তিগত অডিট বাকি: ') +
                data.fullyRedactedVisitsWithLegacyPayloads;
        } catch (error) {
            if (epoch === privacyAuditEpoch) status(error.message);
        }
    });
    load();
    const workCampusText = new URLSearchParams(window.location?.search || '').get('campusId');
    if (/^[1-9]\d{0,14}$/.test(workCampusText || '') && Number.isSafeInteger(Number(workCampusText)) &&
        $('fvPolicyCampus')) {
        $('fvPolicyCampus').value = workCampusText;
        loadPolicies();
    }
})();
