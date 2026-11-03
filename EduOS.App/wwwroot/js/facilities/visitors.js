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
        ++requestEpoch; ++hostEpoch; ++occupancyEpoch; ++exportEpoch;
        $('fvRows')?.replaceChildren();
        $('fvHost')?.replaceChildren();
        $('fvOccupancyResult')?.replaceChildren();
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
    const button = (name, action, row, handler) => {
        const b = document.createElement('button');
        b.type = 'button'; b.className = 'btn btn-outline-primary btn-sm';
        b.textContent = name; b.addEventListener('click', () => handler(row, action)); return b;
    };
    const act = async (row, action) => {
        const reason = action === 'redactrequest' ? 'Visitor retention period expired' :
            action === 'redactapprove' ? 'Independent retention approval' :
            $('fvReason').value.trim();
        const badgeCode = $('fvBadge').value.trim().toUpperCase();
        if (action === 'checkin' && !/^[A-Z0-9-]{4,20}$/.test(badgeCode)) {
            status(label('Enter a valid badge code.', 'সঠিক ব্যাজ কোড দিন।')); return;
        }
        if ((action === 'hostreject' || action === 'incident') && reason.length < 10) {
            status(label('Rejection or incident needs a 10-character reason.', 'প্রত্যাখ্যান বা ঘটনায় কমপক্ষে ১০ অক্ষরের কারণ দিন।')); return;
        }
        const key = [row.id, action, row.rowVersion, badgeCode, reason].join('|');
        if (!commandIds.has(key)) commandIds.set(key, crypto.randomUUID());
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
                if (manager && (row.stateCode === 'CheckedIn' || row.stateCode === 'CheckedOut'))
                    actions.append(button(label('Report incident', 'ঘটনা নথিভুক্ত'), 'incident', row, act));
                // Retention requires an administrator-configured 30–3650 day
                // policy, no legal hold/incident, and a separate live checker.
                if (manager && (row.stateCode === 'CheckedOut' || row.stateCode === 'Rejected') &&
                    !row.privacyRedacted) {
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
        if (campus === lastHostCampus && select.options.length > 1) return;
        lastHostCampus = campus;
        const epoch = ++hostEpoch;
        select.replaceChildren(new Option(label('Select host', 'হোস্ট নির্বাচন'), ''));
        if (!campus) return;
        try {
            const data = await api('/api/facilities/visitors/hosts?campusId=' + campus);
            if (epoch !== hostEpoch) return;
            for (const h of data.items || [])
                select.add(new Option(h.displayName + ' (#' + h.userId + ')', String(h.userId)));
        } catch (e) { if (epoch === hostEpoch) status(e.message); }
    };
    $('fvCampus')?.addEventListener('change', hosts);
    $('fvRefresh').addEventListener('click', load);
    $('fvCreate')?.addEventListener('submit', async e => {
        e.preventDefault();
        try {
            await api('/api/facilities/visitors', 'POST', {
                clientRequestId: pendingCreateId,
                campusId: Number($('fvCampus').value),
                hostUserId: Number($('fvHost').value),
                visitorName: $('fvName').value.trim(),
                purpose: $('fvPurpose').value.trim(),
                visitStartUtc: utc('fvStart'), visitEndUtc: utc('fvEnd')
            });
            pendingCreateId = crypto.randomUUID();
            $('fvName').value = ''; $('fvPurpose').value = '';
            await load();
        } catch (err) { status(err.message); }
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
    load();
})();
