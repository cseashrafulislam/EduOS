(() => {
    'use strict';
    const root = document.getElementById('careerOpportunityWorkspace');
    if (!root) return;
    const admin = root.dataset.admin === 'true';
    const bn = document.documentElement.lang.toLowerCase().startsWith('bn');
    const el = id => document.getElementById(id);
    const t = (a, b) => bn ? b : a;
    const words = {
        heading: ['Career opportunities & applications', 'ক্যারিয়ার সুযোগ ও আবেদন'], create: ['Create opportunity draft', 'নতুন সুযোগের খসড়া'],
        employerSearch: ['Find verified employer', 'যাচাইকৃত নিয়োগদাতা খুঁজুন'], title: ['Opportunity title', 'পদের নাম'],
        employer: ['Employer', 'নিয়োগদাতা'], kind: ['Type', 'ধরন'], job: ['Job', 'চাকরি'], internship: ['Internship', 'ইন্টার্নশিপ'],
        deadline: ['Apply until (local time)', 'আবেদনের শেষ সময় (স্থানীয়)'], description: ['Description', 'বিবরণ'],
        save: ['Save draft', 'খসড়া সংরক্ষণ'], list: ['Opportunities', 'সুযোগসমূহ'], all: ['All', 'সব'], draft: ['Draft', 'খসড়া'],
        published: ['Published', 'প্রকাশিত'], closed: ['Closed', 'বন্ধ'], refresh: ['Refresh', 'রিফ্রেশ'],
        status: ['Status', 'অবস্থা'], actions: ['Actions', 'কাজ'], previous: ['Previous', 'আগের'], next: ['Next', 'পরের'],
        myApplications: ['My applications', 'আমার আবেদন']
    };
    for (const node of root.querySelectorAll('[data-i18n]')) {
        const v = words[node.dataset.i18n]; if (v) node.textContent = v[bn ? 1 : 0];
    }
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const pending = new Map();
    let page = 1, total = 0, myPage = 1, myTotal = 0, revision = 0, myRevision = 0;
    let employersRevision = 0, employerTimer, createRequest = null, students = [], saving = false;
    // A stale HTTP 200 is not evidence of current cookie-session or tenant ownership.
    const session = {
        reference: document.body?.dataset.authSessionReference || '',
        tenant: document.body?.dataset.authTenantId || ''
    };
    const shield = document.getElementById('eduosSessionShield');
    let locked = false;
    const sessionError = () => new Error(t(
        'Institution session changed. Reload this page.',
        'প্রতিষ্ঠানের সেশন পরিবর্তিত হয়েছে। পেজটি আবার লোড করুন।'));
    function lockPage() {
        if (locked) return;
        locked = true;
        ++revision; ++myRevision; ++employersRevision;
        students = []; pending.clear(); createRequest = null;
        clearTimeout(employerTimer);
        for (const id of ['careerOpportunityRows', 'careerMyRows', 'careerEmployer']) el(id)?.replaceChildren();
        for (const node of root.querySelectorAll('button, input, select, textarea')) node.disabled = true;
        for (const id of ['careerOpportunityRefresh', 'careerOpportunityPrev', 'careerOpportunityNext',
            'careerMyRefresh', 'careerMyPrev', 'careerMyNext', 'careerOpportunitySave'])
            if (el(id)) el(id).disabled = true;
        const message = el('careerOpportunityNotice');
        if (message) { message.textContent = sessionError().message; message.className = 'alert alert-danger'; }
        void window.EduOSVerifySessionContext?.();
    }
    function sessionCurrent() {
        return !locked && /^[0-9A-F]{64}$/.test(session.reference) &&
            /^[1-9][0-9]*$/.test(session.tenant) &&
            document.body?.dataset.authSessionReference === session.reference &&
            document.body?.dataset.authTenantId === session.tenant &&
            !window.EduOSSessionTransitionPending?.();
    }
    async function verifySession(response) {
        if (!sessionCurrent() || !shield || typeof window.EduOSVerifySessionContext !== 'function') {
            lockPage(); throw sessionError();
        }
        try { await window.EduOSVerifySessionContext(); }
        catch { lockPage(); throw sessionError(); }
        if (!sessionCurrent() || shield.hidden !== true ||
            (response && (response.headers?.get('X-EduOS-Session-Reference') !== session.reference ||
                response.headers?.get('X-EduOS-Tenant-Id') !== session.tenant))) {
            lockPage(); throw sessionError();
        }
    }
    const notice = (message, error = false) => {
        const target = el('careerOpportunityNotice');
        target.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        target.textContent = message;
    };
    async function api(path, body) {
        const write = body !== undefined;
        if (!sessionCurrent()) { lockPage(); throw sessionError(); }
        if (write) {
            if (!token) throw new Error(t('Security token missing. Reload.', 'নিরাপত্তা টোকেন নেই। রিলোড করুন।'));
            await verifySession();
        }
        const res = await fetch(path, { method: write ? 'POST' : 'GET', credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json', ...(write ? { 'Content-Type': 'application/json', RequestVerificationToken: token } : {}) },
            ...(write ? { body: JSON.stringify(body) } : {}) });
        // Check before parsing private JSON and again after an asynchronous parse.
        await verifySession(res);
        const json = await res.json().catch(() => null);
        await verifySession(res);
        if (!res.ok || json?.success !== true) {
            const err = new Error(json?.message || t('Request failed. Refresh and retry.', 'অনুরোধ ব্যর্থ। রিফ্রেশ করে আবার চেষ্টা করুন।'));
            err.status = res.status; throw err;
        }
        return json.data;
    }
    const cell = (tr, value) => {
        const td = document.createElement('td'); td.textContent = value == null ? '' : String(value); tr.appendChild(td); return td;
    };
    const empty = (tbody, columns, message) => {
        tbody.replaceChildren(); const tr = document.createElement('tr'); const td = cell(tr, message);
        td.colSpan = columns; tbody.appendChild(tr);
    };
    const button = (label, action, style = 'btn-outline-primary') => {
        const b = document.createElement('button'); b.type = 'button';
        b.className = 'btn btn-sm ' + style + ' me-1 mb-1'; b.textContent = label;
        b.addEventListener('click', async () => {
            if (locked) return;
            b.disabled = true;
            try { await action(); }
            catch (err) { notice(err.message, true); }
            finally { if (!locked) b.disabled = false; }
        });
        return b;
    };
    const status = value => ({ 1: t('Draft', 'খসড়া'), 2: t('Published', 'প্রকাশিত'), 3: t('Closed', 'বন্ধ'),
        4: t('Withdrawn', 'প্রত্যাহার'), 5: t('Rejected', 'প্রত্যাখ্যাত') })[value] || '—';
    const keyFor = item => item.reference + ':' + item.rowVersion;
    async function transition(item, action) {
        const key = action + ':' + keyFor(item);
        if (!confirm(action === 'publish' ? t('Publish this opportunity?', 'সুযোগটি প্রকাশ করবেন?') :
            t('Close this opportunity to new applications?', 'নতুন আবেদন বন্ধ করবেন?'))) return;
        if (!pending.has(key)) pending.set(key, crypto.randomUUID());
        try {
            await api('/api/alumni/career/opportunities/' + encodeURIComponent(item.reference) + '/' + action,
                { clientRequestId: pending.get(key), rowVersion: item.rowVersion });
            pending.delete(key); notice(t('Opportunity updated.', 'সুযোগের অবস্থা হালনাগাদ হয়েছে।')); await load();
        } catch (err) {
            if (err.status === 403 || err.status === 409 || err.status === 400) { pending.delete(key); await load(); }
            throw err;
        }
    }
    async function apply(item, studentReference) {
        const key = 'apply:' + item.reference + ':' + studentReference;
        if (!pending.has(key)) pending.set(key, crypto.randomUUID());
        try {
            await api('/api/alumni/career/opportunities/' + encodeURIComponent(item.reference) + '/apply',
                { clientRequestId: pending.get(key), studentReference });
            pending.delete(key); notice(t('Application submitted.', 'আবেদন জমা হয়েছে।')); await loadMine();
        } catch (err) {
            if ([400, 403, 404, 409].includes(err.status)) { pending.delete(key); await load(); await loadMine(); }
            throw err;
        }
    }
    async function load() {
        if (locked) return;
        const id = ++revision;
        const rows = el('careerOpportunityRows');
        empty(rows, 6, t('Loading…', 'লোড হচ্ছে…'));
        el('careerOpportunityRefresh').disabled = true;
        try {
            const filter = admin ? Number(el('careerOpportunityStatus').value) : 2;
            const data = await api('/api/alumni/career/opportunities?page=' + page + '&pageSize=20&status=' + filter);
            if (id !== revision) return;
            total = data.totalCount; rows.replaceChildren();
            if (!data.items.length) empty(rows, 6, t('No opportunities in this scope.', 'এই পরিসরে সুযোগ নেই।'));
            for (const item of data.items) {
                const tr = document.createElement('tr');
                cell(tr, item.title); cell(tr, item.employerName);
                cell(tr, item.kind === 1 ? words.job[bn ? 1 : 0] : words.internship[bn ? 1 : 0]);
                cell(tr, new Date(item.applyUntilUtc).toLocaleString(bn ? 'bn-BD' : 'en-BD'));
                cell(tr, status(item.status)); const actions = cell(tr, '');
                if (admin) {
                    if (item.canPublish) actions.appendChild(button(t('Publish', 'প্রকাশ করুন'), () => transition(item, 'publish')));
                    if (item.status === 2) actions.appendChild(button(t('Close', 'বন্ধ করুন'), () => transition(item, 'close'), 'btn-outline-danger'));
                } else if (item.canApply) {
                    const eligible = students.filter(s => s.campusId === item.campusId);
                    if (eligible.length === 1) actions.appendChild(button(t('Apply', 'আবেদন'), () => apply(item, eligible[0].studentReference)));
                    if (eligible.length > 1) {
                        const select = document.createElement('select'); select.className = 'form-select form-select-sm mb-1';
                        select.setAttribute('aria-label', t('Student identity', 'শিক্ষার্থী পরিচয়'));
                        for (const s of eligible) { const o = document.createElement('option'); o.value = s.studentReference; o.textContent = s.studentCode + ' — ' + s.fullName; select.appendChild(o); }
                        actions.appendChild(select); actions.appendChild(button(t('Apply', 'আবেদন'), () => apply(item, select.value)));
                    }
                }
                rows.appendChild(tr);
            }
            el('careerOpportunityPage').textContent = page + ' / ' + Math.max(1, Math.ceil(total / 20));
        } catch (err) { if (id === revision) { empty(rows, 6, t('Unable to load. Refresh.', 'লোড হয়নি। রিফ্রেশ করুন।')); notice(err.message, true); } }
        finally {
            if (id === revision) { el('careerOpportunityRefresh').disabled = false; el('careerOpportunityPrev').disabled = page <= 1;
                el('careerOpportunityNext').disabled = page * 20 >= total; }
        }
    }
    async function loadMine() {
        if (admin || locked) return;
        const id = ++myRevision; const rows = el('careerMyRows'); empty(rows, 3, t('Loading…', 'লোড হচ্ছে…'));
        try {
            const data = await api('/api/alumni/career/applications?page=' + myPage + '&pageSize=20');
            if (id !== myRevision) return;
            myTotal = data.totalCount; rows.replaceChildren();
            if (!data.items.length) empty(rows, 3, t('No applications yet.', 'এখনো আবেদন নেই।'));
            for (const item of data.items) {
                const tr = document.createElement('tr'); cell(tr, item.opportunityTitle);
                cell(tr, ({ 1: t('Pending', 'বিচারাধীন'), 2: t('Rejected', 'প্রত্যাখ্যাত'), 3: t('Placed', 'প্লেসমেন্ট'), 4: t('Withdrawn', 'প্রত্যাহার') })[item.status]);
                const actions = cell(tr, '');
                if (item.status === 1) actions.appendChild(button(t('Withdraw', 'প্রত্যাহার'), async () => {
                    if (!confirm(t('Withdraw this application?', 'এই আবেদন প্রত্যাহার করবেন?'))) return;
                    const key = 'withdraw:' + keyFor(item); if (!pending.has(key)) pending.set(key, crypto.randomUUID());
                    try { await api('/api/alumni/career/applications/' + encodeURIComponent(item.reference) + '/withdraw',
                        { clientRequestId: pending.get(key), rowVersion: item.rowVersion });
                        pending.delete(key); notice(t('Application withdrawn.', 'আবেদন প্রত্যাহার হয়েছে।')); await loadMine(); }
                    catch (err) { if ([400, 403, 409].includes(err.status)) pending.delete(key); throw err; }
                }, 'btn-outline-danger'));
                rows.appendChild(tr);
            }
            el('careerMyPage').textContent = myPage + ' / ' + Math.max(1, Math.ceil(myTotal / 20));
        } catch (err) { if (id === myRevision) { empty(rows, 3, t('Unable to load applications.', 'আবেদন লোড হয়নি।')); notice(err.message, true); } }
        finally { if (id === myRevision) { el('careerMyPrev').disabled = myPage <= 1; el('careerMyNext').disabled = myPage * 20 >= myTotal; } }
    }
    async function loadEmployers() {
        if (!admin || locked) return;
        const id = ++employersRevision;
        const select = el('careerEmployer'), save = el('careerOpportunitySave');
        select.replaceChildren();
        select.disabled = true;
        save.disabled = true;
        try {
            const filter = el('careerEmployerSearch').value.trim();
            const data = await api('/api/alumni/career/employers?page=1&pageSize=50&status=2&search=' + encodeURIComponent(filter));
            if (id !== employersRevision) return;
            for (const e of data.items) { const option = document.createElement('option'); option.value = e.reference; option.textContent = e.name + ' (' + e.campusId + ')'; select.appendChild(option); }
            select.disabled = !data.items.length;
            save.disabled = saving || !data.items.length;
            if (!data.items.length) notice(t('No verified employer found.', 'যাচাইকৃত নিয়োগদাতা পাওয়া যায়নি।'), true);
        } catch (err) { if (id === employersRevision) { el('careerOpportunitySave').disabled = true; notice(err.message, true); } }
    }
    if (admin) {
        for (const id of ['careerEmployer', 'careerOpportunityTitle', 'careerOpportunityDescription', 'careerOpportunityKind', 'careerOpportunityDeadline']) {
            el(id).addEventListener('input', () => { createRequest = null; });
        }
        el('careerEmployerSearch').addEventListener('input', () => {
            createRequest = null;
            clearTimeout(employerTimer);
            // Invalidate both displayed options and in-flight results before debounce.
            ++employersRevision;
            el('careerEmployer').replaceChildren();
            el('careerEmployer').disabled = true;
            el('careerOpportunitySave').disabled = true;
            employerTimer = setTimeout(loadEmployers, 300);
        });
        el('careerOpportunityForm').addEventListener('submit', async ev => {
            ev.preventDefault();
            const form = ev.currentTarget, save = el('careerOpportunitySave');
            const selectedEmployer = el('careerEmployer').value;
            if (locked || saving || save.disabled || !selectedEmployer || !form.reportValidity()) return;
            const until = new Date(el('careerOpportunityDeadline').value);
            if (!Number.isFinite(until.getTime())) { notice(t('Invalid deadline.', 'সঠিক সময় দিন।'), true); return; }
            if (!createRequest) createRequest = crypto.randomUUID();
            saving = true;
            const controls = [...form.querySelectorAll('input, select, textarea, button')];
            for (const control of controls) control.disabled = true;
            try {
                await api('/api/alumni/career/opportunities', { clientRequestId: createRequest,
                    employerReference: selectedEmployer, title: el('careerOpportunityTitle').value,
                    description: el('careerOpportunityDescription').value || null,
                    kind: Number(el('careerOpportunityKind').value), applyUntilUtc: until.toISOString() });
                createRequest = null; el('careerOpportunityTitle').value = ''; el('careerOpportunityDescription').value = '';
                notice(t('Draft saved for independent publication.', 'স্বতন্ত্র অনুমোদনের জন্য খসড়া সংরক্ষিত।')); page = 1; await load();
            } catch (err) { if ([400, 403, 409].includes(err.status)) createRequest = null; notice(err.message, true); }
            finally {
                saving = false;
                if (!locked) {
                    for (const control of controls) control.disabled = false;
                    save.disabled = !el('careerEmployer').value;
                    el('careerEmployer').disabled = !el('careerEmployer').value;
                }
            }
        });
        el('careerOpportunityStatus').addEventListener('change', () => { page = 1; load(); });
        loadEmployers();
    } else {
        el('careerMyRefresh').addEventListener('click', loadMine);
        el('careerMyPrev').addEventListener('click', () => { if (myPage > 1) { myPage--; loadMine(); } });
        el('careerMyNext').addEventListener('click', () => { if (myPage * 20 < myTotal) { myPage++; loadMine(); } });
    }
    el('careerOpportunityRefresh').addEventListener('click', load);
    el('careerOpportunityPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
    el('careerOpportunityNext').addEventListener('click', () => { if (page * 20 < total) { page++; load(); } });
    if (!admin) api('/api/alumni/career/students/me').then(data => { students = data; load(); })
        .catch(err => { notice(err.message, true); load(); });
    else load();
    loadMine();
})();
