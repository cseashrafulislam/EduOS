(() => {
    'use strict';
    const root = document.getElementById('alumniWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const bn = document.documentElement.lang.toLowerCase().startsWith('bn');
    const strings = {
        heading: ['Alumni & Graduate Directory', 'প্রাক্তন শিক্ষার্থী ও স্নাতক তালিকা'],
        intro: ['Only verified graduates can register. Directory sharing is voluntary and revocable.', 'শুধু যাচাইকৃত উত্তীর্ণ শিক্ষার্থীরা নিবন্ধন করতে পারবেন। তথ্য প্রকাশ ঐচ্ছিক এবং প্রত্যাহারযোগ্য।'],
        privacy: ['My alumni privacy preferences', 'আমার তথ্যের গোপনীয়তা'],
        graduate: ['Verified graduation', 'যাচাইকৃত উত্তীর্ণ শিক্ষার্থী'],
        email: ['Contact email (optional)', 'যোগাযোগের ইমেইল (ঐচ্ছিক)'],
        phone: ['Contact phone (optional)', 'যোগাযোগের ফোন (ঐচ্ছিক)'],
        optin: ['I consent to show my name and contact details in my institution\'s restricted alumni directory.', 'আমি আমার প্রতিষ্ঠানের সীমিত প্রাক্তন শিক্ষার্থী তালিকায় নাম ও যোগাযোগের তথ্য প্রকাশে সম্মতি দিচ্ছি।'],
        emailInvites: ['I separately agree to receive alumni event invitations by email.', 'আমি আলাদাভাবে ইমেইলে প্রাক্তন শিক্ষার্থীদের অনুষ্ঠানের আমন্ত্রণ পেতে সম্মতি দিচ্ছি।'],
        smsInvites: ['I separately agree to receive alumni event invitations by SMS.', 'আমি আলাদাভাবে SMS-এ প্রাক্তন শিক্ষার্থীদের অনুষ্ঠানের আমন্ত্রণ পেতে সম্মতি দিচ্ছি।'],
        privacyNote: ['Each consent can be revoked separately. Unneeded contact data is erased; graduation evidence stays.', 'প্রতিটি সম্মতি আলাদাভাবে প্রত্যাহার করা যায়। অপ্রয়োজনীয় যোগাযোগের তথ্য মুছে যাবে; উত্তীর্ণ হওয়ার প্রমাণ থাকবে।'],
        save: ['Save preferences', 'পছন্দ সংরক্ষণ'],
        directory: ['Opted-in alumni directory', 'সম্মতিপ্রাপ্ত প্রাক্তন শিক্ষার্থী'],
        search: ['Search', 'খুঁজুন'], export: ['Export current page CSV', 'বর্তমান পৃষ্ঠা CSV'],
        name: ['Name', 'নাম'], code: ['Code', 'কোড'], contact: ['Contact', 'যোগাযোগ'],
        completed: ['Completed', 'উত্তীর্ণ'], loading: ['Loading…', 'লোড হচ্ছে…'],
        previous: ['Previous', 'আগের'], next: ['Next', 'পরের'],
        events: ['Alumni events & engagement', 'প্রাক্তন শিক্ষার্থীদের অনুষ্ঠান ও অংশগ্রহণ'],
        eventsPrivacy: ['RSVP requires active directory consent. Revoking consent clears active RSVPs.', 'অংশগ্রহণ জানাতে তথ্য প্রকাশের সম্মতি প্রয়োজন। সম্মতি প্রত্যাহার করলে অংশগ্রহণের তথ্য মুছে যাবে।'],
        eventsCsv: ['Export page engagement CSV', 'অংশগ্রহণের CSV রপ্তানি'],
        eventCampus: ['Campus', 'ক্যাম্পাস'], eventTitle: ['Event title', 'অনুষ্ঠানের শিরোনাম'],
        eventStart: ['Starts (local time)', 'শুরু (স্থানীয় সময়)'],
        eventEnd: ['Ends (local time)', 'শেষ (স্থানীয় সময়)'],
        eventDescription: ['Details (optional)', 'বিবরণ (ঐচ্ছিক)'],
        createDraft: ['Create draft for approval', 'অনুমোদনের জন্য খসড়া তৈরি'],
        makerChecker: ['A different administrator must publish the draft.', 'অন্য একজন প্রশাসক প্রকাশের অনুমোদন দেবেন।'],
        attendanceNote: ['During a published event, authorized staff can check in consenting attendees. No contact details are shown.', 'প্রকাশিত অনুষ্ঠান চলাকালে অনুমোদিত প্রশাসক সম্মতিপ্রাপ্ত অংশগ্রহণকারীদের উপস্থিতি নিবন্ধন করবেন। যোগাযোগের তথ্য দেখানো হবে না।']
    };
    for (const node of root.querySelectorAll('[data-i18n]')) {
        const values = strings[node.dataset.i18n];
        if (values) node.textContent = values[bn ? 1 : 0];
    }
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { graduates: [], selected: null, page: 1, total: 0, busy: false, requestId: null,
        eventPage: 1, eventTotal: 0, eventBusy: false, eventCreateRequest: null, rsvpRequests: new Map(), eventRequestSerial: 0,
        directoryRequestSerial: 0, attendanceRequests: new Map() };
    const notice = (message, error = false) => {
        const box = el('alumniNotice');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = message;
    };
    async function api(url, options = {}) {
        const response = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store', ...options,
            headers: { Accept: 'application/json',
                ...(options.body ? { 'Content-Type': 'application/json' } : {}),
                ...(options.method === 'POST' ? { RequestVerificationToken: token } : {}) }
        });
        const body = await response.json().catch(() => null);
        if (!response.ok || body?.success !== true)
            throw new Error(body?.message || (bn ? 'অনুরোধ ব্যর্থ হয়েছে।' : 'Request failed.'));
        return body.data;
    }
    function selectedGraduate() {
        return state.graduates.find(x => x.studentReference === el('alumniGraduate').value);
    }
    function showSelected() {
        state.selected = selectedGraduate();
        state.requestId = null;
        // Retry keys and in-flight event pages belong to a single graduate identity.
        state.rsvpRequests.clear();
        state.eventRequestSerial++;
        // Old graduate cards must not remain actionable during an identity switch.
        state.eventPage = 1;
        el('alumniEventCards').replaceChildren();
        el('alumniEventPage').textContent = '';
        el('alumniEventPrev').disabled = true;
        el('alumniEventNext').disabled = true;
        const item = state.selected;
        el('alumniOptIn').checked = Boolean(item?.directoryOptIn);
        el('alumniEmailInvites').checked = Boolean(item?.invitationEmailOptIn);
        el('alumniSmsInvites').checked = Boolean(item?.invitationSmsOptIn);
        el('alumniEmail').value = item?.contactEmail || '';
        el('alumniPhone').value = item?.contactPhone || '';
        el('alumniSave').disabled = !item;
        syncContactInputs();
    }
    function syncContactInputs() {
        const directory = el('alumniOptIn').checked;
        el('alumniEmail').disabled = !(directory || el('alumniEmailInvites').checked);
        el('alumniPhone').disabled = !(directory || el('alumniSmsInvites').checked);
        el('alumniEmail').required = el('alumniEmailInvites').checked;
        el('alumniPhone').required = el('alumniSmsInvites').checked;
    }
    async function loadMine() {
        const graduates = await api('/api/alumni/me');
        state.graduates = graduates || [];
        const select = el('alumniGraduate');
        select.replaceChildren();
        if (!state.graduates.length) {
            const option = new Option(bn ? 'যাচাইকৃত উত্তীর্ণ তথ্য পাওয়া যায়নি' : 'No verified graduation for this account', '');
            select.add(option);
            el('alumniSave').disabled = true;
            return;
        }
        for (const item of state.graduates) select.add(new Option(item.fullName + ' — ' + item.studentCode, item.studentReference));
        showSelected();
    }
    async function save(event) {
        event.preventDefault();
        if (state.busy || !state.selected) return;
        state.busy = true; el('alumniSave').disabled = true;
        if (!state.requestId) state.requestId = crypto.randomUUID();
        try {
            const saved = await api('/api/alumni/consent', { method: 'POST', body: JSON.stringify({
                clientRequestId: state.requestId,
                studentReference: state.selected.studentReference,
                directoryOptIn: el('alumniOptIn').checked,
                invitationEmailOptIn: el('alumniEmailInvites').checked,
                invitationSmsOptIn: el('alumniSmsInvites').checked,
                contactEmail: el('alumniEmail').value.trim() || null,
                contactPhone: el('alumniPhone').value.trim() || null,
                rowVersion: state.selected.rowVersion || null
            }) });
            state.graduates = state.graduates.map(x => x.studentReference === saved.studentReference ? saved : x);
            state.requestId = null; showSelected();
            notice(bn ? 'গোপনীয়তার পছন্দ সংরক্ষিত হয়েছে।' : 'Privacy preferences saved.');
            if (root.dataset.admin === 'true') await loadDirectory();
            await loadEvents();
        } catch (error) {
            notice(error.message, true);
        } finally { state.busy = false; el('alumniSave').disabled = !state.selected; }
    }
    async function loadDirectory() {
        if (root.dataset.admin !== 'true') return;
        // A newer search, page or consent refresh owns the visible contacts and CSV link.
        const serial = ++state.directoryRequestSerial;
        const pageNumber = state.page;
        const query = new URLSearchParams({ page: pageNumber, pageSize: 20,
            search: el('alumniSearch').value.trim() });
        const rows = el('alumniRows');
        rows.replaceChildren();
        const loadingCell = rows.insertRow().insertCell();
        loadingCell.colSpan = 4;
        loadingCell.textContent = bn ? 'লোড হচ্ছে…' : 'Loading…';
        el('alumniPage').textContent = loadingCell.textContent;
        el('alumniPrev').disabled = true;
        el('alumniNext').disabled = true;
        el('alumniExport').removeAttribute('href');
        try {
            const page = await api('/api/alumni/directory?' + query);
            if (serial !== state.directoryRequestSerial) return;
            state.total = page.totalCount;
            rows.replaceChildren();
            if (!page.items.length) {
                const row = rows.insertRow();
                const cell = row.insertCell(); cell.colSpan = 4;
                cell.textContent = bn ? 'কোনো তথ্য পাওয়া যায়নি' : 'No opted-in graduates found.';
            }
            for (const item of page.items) {
                const row = rows.insertRow();
                for (const value of [item.fullName, item.studentCode,
                    [item.contactEmail, item.contactPhone].filter(Boolean).join(' / '),
                    new Date(item.completedAtUtc).toLocaleDateString(bn ? 'bn-BD' : 'en-BD')]) {
                    row.insertCell().textContent = value || '—';
                }
            }
            el('alumniPage').textContent = pageNumber + ' / ' + Math.max(1, Math.ceil(state.total / 20));
            el('alumniPrev').disabled = pageNumber <= 1;
            el('alumniNext').disabled = pageNumber * 20 >= state.total;
            el('alumniExport').href = '/api/alumni/directory.csv?' + query;
        } catch (error) {
            if (serial !== state.directoryRequestSerial) return;
            rows.replaceChildren();
            const cell = rows.insertRow().insertCell();
            cell.colSpan = 4;
            cell.textContent = bn ? 'তালিকা লোড হয়নি। আবার চেষ্টা করুন।' :
                'Directory could not be loaded. Please retry.';
            el('alumniPage').textContent = '';
            throw error;
        }
    }
    el('alumniGraduate').addEventListener('change', () => {
        showSelected();
        loadEvents().catch(e => notice(e.message, true));
    });
    el('alumniOptIn').addEventListener('change', syncContactInputs);
    el('alumniEmailInvites').addEventListener('change', syncContactInputs);
    el('alumniSmsInvites').addEventListener('change', syncContactInputs);
    el('alumniConsentForm').addEventListener('submit', save);
    if (root.dataset.admin === 'true') {
        el('alumniSearchForm').addEventListener('submit', event => {
            event.preventDefault(); state.page = 1; loadDirectory().catch(e => notice(e.message, true));
        });
        el('alumniPrev').addEventListener('click', () => {
            if (state.page > 1) { state.page--; loadDirectory().catch(e => notice(e.message, true)); }
        });
        el('alumniNext').addEventListener('click', () => {
            if (state.page * 20 < state.total) { state.page++; loadDirectory().catch(e => notice(e.message, true)); }
        });
    }

    const eventAdmin = root.dataset.admin === 'true';
    const eventLabel = (english, bangla) => bn ? bangla : english;
    const localDate = value => new Date(value).toLocaleString(bn ? 'bn-BD' : 'en-BD',
        { dateStyle: 'medium', timeStyle: 'short' });
    const eventStatus = value => value === 1 ? eventLabel('Draft', 'খসড়া')
        : value === 2 ? eventLabel('Published', 'প্রকাশিত') : eventLabel('Cancelled', 'বাতিল');

    function eventButton(label, style, onClick) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-sm ' + style;
        button.textContent = label;
        button.addEventListener('click', onClick);
        return button;
    }
    async function loadEventCampuses() {
        if (!eventAdmin) return;
        const campuses = await api('/api/alumni/event-campuses');
        const select = el('alumniEventCampus');
        select.replaceChildren();
        select.add(new Option(campuses.length
            ? eventLabel('Choose campus', 'ক্যাম্পাস নির্বাচন')
            : eventLabel('No campus grant — ask your institution administrator', 'ক্যাম্পাসের অনুমতি নেই — প্রতিষ্ঠানের প্রশাসকের কাছে অনুমতি নিন'), ''));
        for (const campus of campuses) select.add(new Option(campus.name, campus.id));
        el('alumniEventCreate').disabled = campuses.length === 0;
    }
    async function loadEvents() {
        const serial = ++state.eventRequestSerial;
        const selectedReference = state.selected?.studentReference || null;
        const query = new URLSearchParams({ page: state.eventPage, pageSize: 20 });
        if (!eventAdmin && selectedReference) query.set('studentReference', selectedReference);
        const target = el('alumniEventCards');
        // Invalidate actionable cards, paging and CSV before a new response arrives.
        target.replaceChildren();
        const pending = document.createElement('p');
        pending.className = 'text-muted';
        pending.textContent = eventLabel('Loading events…', 'অনুষ্ঠান লোড হচ্ছে…');
        target.append(pending);
        el('alumniEventPage').textContent = '';
        el('alumniEventPrev').disabled = true;
        el('alumniEventNext').disabled = true;
        if (eventAdmin) el('alumniEventsExport').removeAttribute('href');
        const isStale = () => serial !== state.eventRequestSerial ||
            (!eventAdmin && (state.selected?.studentReference || null) !== selectedReference);
        let page;
        try {
            page = await api('/api/alumni/events?' + query);
        } catch (error) {
            if (isStale()) return;
            target.replaceChildren();
            const failed = document.createElement('p');
            failed.className = 'text-danger';
            failed.textContent = eventLabel('Events could not be loaded. Please retry.',
                'অনুষ্ঠান লোড হয়নি। আবার চেষ্টা করুন।');
            target.append(failed);
            throw error;
        }
        if (isStale()) return;
        // A fresh server snapshot supersedes ambiguous attendance attempts from old cards.
        state.attendanceRequests.clear();
        state.eventTotal = page.totalCount;
        target.replaceChildren();
        if (!page.items.length) {
            const empty = document.createElement('p');
            empty.className = 'text-muted';
            empty.textContent = eventLabel('No events available.', 'কোনো অনুষ্ঠান পাওয়া যায়নি।');
            target.append(empty);
        }
        for (const item of page.items) {
            const col = document.createElement('div');
            col.className = 'col-md-6 col-xl-4';
            const card = document.createElement('article');
            card.className = 'border rounded p-3 h-100 d-flex flex-column gap-2';
            const title = document.createElement('h3');
            title.className = 'h6 fw-semibold mb-0';
            title.textContent = item.title;
            const date = document.createElement('p');
            date.className = 'small text-muted mb-0';
            date.textContent = localDate(item.startsAtUtc) + ' — ' + localDate(item.endsAtUtc);
            const description = document.createElement('p');
            description.className = 'small mb-0';
            description.textContent = item.description || '';
            const status = document.createElement('p');
            status.className = 'small mb-0';
            status.textContent = eventStatus(item.status) + (eventAdmin
                ? ' · ' + eventLabel('Going', 'আসবেন') + ': ' + item.goingCount +
                  ' · ' + eventLabel('Declined', 'আসবেন না') + ': ' + item.declinedCount + ' · ' + eventLabel('Checked in', 'উপস্থিত') + ': ' + item.checkedInCount
                : item.myResponse ? ' · ' + (item.myResponse === 1
                    ? eventLabel('Going', 'আসবেন') : eventLabel('Not going', 'আসবেন না')) : '');
            const actions = document.createElement('div');
            actions.className = 'd-flex flex-wrap gap-2 mt-auto';
            if (eventAdmin && item.status === 1) {
                actions.append(eventButton(eventLabel('Publish', 'প্রকাশ'), 'btn-outline-primary',
                    () => changeEventStatus(item, 'publish')));
            }
            if (eventAdmin && item.status !== 3) {
                actions.append(eventButton(eventLabel('Cancel', 'বাতিল'), 'btn-outline-danger',
                    () => changeEventStatus(item, 'cancel')));
            }
            if (!eventAdmin && item.status === 2 && new Date(item.startsAtUtc) > new Date()
                && state.selected?.directoryOptIn) {
                actions.append(eventButton(eventLabel('Going', 'আসব'), 'btn-outline-success',
                    () => saveRsvp(item, 1, selectedReference)));
                actions.append(eventButton(eventLabel('Not going', 'আসব না'), 'btn-outline-secondary',
                    () => saveRsvp(item, 2, selectedReference)));
            }
            const attendanceHost = document.createElement('div');
            if (eventAdmin && item.status === 2) {
                actions.append(eventButton(eventLabel('Attendance', 'উপস্থিতি'), 'btn-outline-info',
                    () => loadAttendance(item, attendanceHost, 1).catch(e => notice(e.message, true))));
            }
            card.append(title, date, description, status, actions, attendanceHost);
            col.append(card);
            target.append(col);
        }
        el('alumniEventPage').textContent = state.eventPage + ' / ' +
            Math.max(1, Math.ceil(state.eventTotal / 20));
        el('alumniEventPrev').disabled = state.eventPage <= 1;
        el('alumniEventNext').disabled = state.eventPage * 20 >= state.eventTotal;
        if (eventAdmin) el('alumniEventsExport').href = '/api/alumni/events/report.csv?' + query;
    }

    // Roster responses are scoped to the current host/page generation. Detached
    // event cards must not receive late attendee identities.
    const attendanceSerial = new WeakMap();
    // No contacts, no unbounded export, and no unsafe HTML.
    async function loadAttendance(item, host, page) {
        const serial = (attendanceSerial.get(host) || 0) + 1;
        attendanceSerial.set(host, serial);
        const query = new URLSearchParams({ page, pageSize: 20 });
        host.replaceChildren();
        const pending = document.createElement('p');
        pending.className = 'text-muted';
        pending.textContent = eventLabel('Loading attendees…', 'অংশগ্রহণকারী লোড হচ্ছে…');
        host.append(pending);
        let data;
        try {
            data = await api('/api/alumni/events/' + encodeURIComponent(item.reference) +
                '/attendance?' + query);
        } catch (error) {
            if (attendanceSerial.get(host) !== serial || !host.isConnected) return;
            host.replaceChildren();
            const failed = document.createElement('p');
            failed.className = 'text-danger';
            failed.textContent = eventLabel('Attendance could not be loaded. Please retry.',
                'উপস্থিতির তালিকা লোড হয়নি। আবার চেষ্টা করুন।');
            host.append(failed);
            throw error;
        }
        if (attendanceSerial.get(host) !== serial || !host.isConnected) return;
        host.replaceChildren();
        const heading = document.createElement('p');
        heading.className = 'small fw-semibold';
        heading.textContent = eventLabel('Consented attendees', 'সম্মতিপ্রাপ্ত অংশগ্রহণকারী') +
            ' · ' + data.totalCount;
        host.append(heading);
        const wrapper = document.createElement('div');
        wrapper.className = 'table-responsive';
        const table = document.createElement('table');
        table.className = 'table table-sm align-middle';
        const header = table.createTHead().insertRow();
        for (const title of [eventLabel('Graduate', 'উত্তীর্ণ শিক্ষার্থী'),
            eventLabel('Status', 'অবস্থা'), eventLabel('Action', 'করণীয়')]) {
            const th = document.createElement('th');
            th.scope = 'col'; th.textContent = title; header.append(th);
        }
        const body = table.createTBody();
        const active = item.status === 2 && new Date(item.startsAtUtc) <= new Date() &&
            new Date(item.endsAtUtc) >= new Date();
        for (const attendee of data.items) {
            const row = body.insertRow();
            row.insertCell().textContent = attendee.fullName + ' · ' + attendee.studentCode;
            row.insertCell().textContent = attendee.checkedInAtUtc
                ? eventLabel('Present', 'উপস্থিত') + ' · ' + localDate(attendee.checkedInAtUtc)
                : eventLabel('Not checked in', 'উপস্থিতি হয়নি');
            const action = row.insertCell();
            if (active) action.append(eventButton(attendee.checkedInAtUtc
                ? eventLabel('Undo', 'বাতিল করুন') : eventLabel('Check in', 'উপস্থিতি নিন'),
                attendee.checkedInAtUtc ? 'btn-outline-secondary' : 'btn-outline-success',
                () => {
                    if (host.isConnected && attendanceSerial.get(host) === serial)
                        markAttendance(item, attendee, !attendee.checkedInAtUtc);
                }));
        }
        if (!data.items.length) {
            const cell = body.insertRow().insertCell();
            cell.colSpan = 3;
            cell.textContent = eventLabel('No consenting attendees marked as going.',
                'সম্মতিপ্রাপ্ত অংশগ্রহণকারী পাওয়া যায়নি।');
        }
        wrapper.append(table); host.append(wrapper);
        const pager = document.createElement('div');
        pager.className = 'd-flex justify-content-between align-items-center gap-2';
        const previous = eventButton(eventLabel('Previous', 'আগের'), 'btn-outline-secondary',
            () => loadAttendance(item, host, page - 1).catch(e => notice(e.message, true)));
        previous.disabled = page <= 1;
        const indicator = document.createElement('span');
        indicator.className = 'small'; indicator.setAttribute('aria-live', 'polite');
        indicator.textContent = page + ' / ' + Math.max(1, Math.ceil(data.totalCount / 20));
        const next = eventButton(eventLabel('Next', 'পরের'), 'btn-outline-secondary',
            () => loadAttendance(item, host, page + 1).catch(e => notice(e.message, true)));
        next.disabled = page * 20 >= data.totalCount;
        pager.append(previous, indicator, next); host.append(pager);
    }
    async function markAttendance(item, attendee, present) {
        if (state.eventBusy) return;
        state.eventBusy = true;
        // Keep the same command ID after an ambiguous network failure so the
        // server can safely replay a completed check-in instead of double-writing.
        const key = item.reference + ':' + attendee.studentReference + ':' + present + ':' +
            (attendee.rowVersion || '');
        if (!state.attendanceRequests.has(key))
            state.attendanceRequests.set(key, crypto.randomUUID());
        try {
            await api('/api/alumni/events/' + encodeURIComponent(item.reference) + '/attendance',
                { method: 'POST', body: JSON.stringify({
                    clientRequestId: state.attendanceRequests.get(key),
                    studentReference: attendee.studentReference, present,
                    rowVersion: attendee.rowVersion
                }) });
            state.attendanceRequests.delete(key);
            notice(eventLabel('Attendance updated.', 'উপস্থিতি হালনাগাদ হয়েছে।'));
            await loadEvents();
        } catch (error) { notice(error.message, true); }
        finally { state.eventBusy = false; }
    }
    async function changeEventStatus(item, action) {
        if (state.eventBusy) return;
        state.eventBusy = true;
        try {
            await api('/api/alumni/events/' + encodeURIComponent(item.reference) + '/' + action,
                { method: 'POST', body: JSON.stringify({ rowVersion: item.rowVersion }) });
            notice(eventLabel('Event updated.', 'অনুষ্ঠান হালনাগাদ হয়েছে।'));
            await loadEvents();
        } catch (error) { notice(error.message, true); }
        finally { state.eventBusy = false; }
    }
    async function saveRsvp(item, response, cardGraduateReference) {
        // A detached card cannot submit another graduate's RSVP/rowversion.
        if (state.eventBusy || !state.selected || !state.selected.directoryOptIn ||
            state.selected.studentReference !== cardGraduateReference) return;
        state.eventBusy = true;
        const studentReference = state.selected.studentReference;
        const key = item.reference + ':' + studentReference + ':' + response;
        if (!state.rsvpRequests.has(key)) state.rsvpRequests.set(key, crypto.randomUUID());
        try {
            await api('/api/alumni/events/rsvp', { method: 'POST', body: JSON.stringify({
                clientRequestId: state.rsvpRequests.get(key), eventReference: item.reference,
                studentReference, response,
                rowVersion: item.myRowVersion || null
            }) });
            state.rsvpRequests.delete(key);
            notice(eventLabel('RSVP saved.', 'অংশগ্রহণের উত্তর সংরক্ষিত হয়েছে।'));
            await loadEvents();
        } catch (error) { notice(error.message, true); }
        finally { state.eventBusy = false; }
    }
    async function createEvent(event) {
        event.preventDefault();
        if (state.eventBusy) return;
        state.eventBusy = true;
        el('alumniEventCreate').disabled = true;
        if (!state.eventCreateRequest) state.eventCreateRequest = crypto.randomUUID();
        try {
            await api('/api/alumni/events', { method: 'POST', body: JSON.stringify({
                clientRequestId: state.eventCreateRequest,
                campusId: Number(el('alumniEventCampus').value),
                title: el('alumniEventTitle').value.trim(),
                description: el('alumniEventDescription').value.trim() || null,
                startsAtUtc: new Date(el('alumniEventStart').value).toISOString(),
                endsAtUtc: new Date(el('alumniEventEnd').value).toISOString()
            }) });
            state.eventCreateRequest = null;
            el('alumniEventForm').reset();
            notice(eventLabel('Draft created. Another administrator must publish it.',
                'খসড়া তৈরি হয়েছে। অন্য প্রশাসক প্রকাশ করবেন।'));
            state.eventPage = 1;
            await loadEvents();
        } catch (error) { notice(error.message, true); }
        finally { state.eventBusy = false; el('alumniEventCreate').disabled = false; }
    }
    el('alumniEventPrev').addEventListener('click', () => {
        if (state.eventPage > 1) { state.eventPage--; loadEvents().catch(e => notice(e.message, true)); }
    });
    el('alumniEventNext').addEventListener('click', () => {
        if (state.eventPage * 20 < state.eventTotal) {
            state.eventPage++; loadEvents().catch(e => notice(e.message, true));
        }
    });
    if (eventAdmin) {
        el('alumniEventForm').addEventListener('submit', createEvent);
        loadEventCampuses().catch(e => notice(e.message, true));
    }

    loadMine().then(loadEvents).catch(e => notice(e.message, true));
    loadDirectory().catch(e => notice(e.message, true));
})();
