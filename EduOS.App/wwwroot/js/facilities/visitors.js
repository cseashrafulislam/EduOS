(() => {
    'use strict';
    const root = document.getElementById('facilityVisitors');
    if (!root) return;
    const bn = root.dataset.bn === 'true', manager = root.dataset.manager === 'true';
    const actor = Number(root.dataset.userId || 0);
    const $ = id => document.getElementById(id);
    const label = (en, bangla) => bn ? bangla : en;
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const status = message => { $('fvStatus').textContent = message; };
    const api = async (url, method = 'GET', data) => {
        const response = await fetch(url, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers: { 'Accept': 'application/json', ...(method !== 'GET' ? {
                'Content-Type': 'application/json', 'RequestVerificationToken': token } : {}) },
            ...(data === undefined ? {} : { body: JSON.stringify(data) })
        });
        const payload = await response.json().catch(() => null);
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
        const reason = $('fvReason').value.trim();
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
    load();
})();
