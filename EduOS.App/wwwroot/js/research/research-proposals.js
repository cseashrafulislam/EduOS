(() => {
    'use strict';
    const root = document.getElementById('researchWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const bn = () => document.documentElement.lang.toLowerCase().startsWith('bn');
    const t = (en, bd) => bn() ? bd : en;
    const labels = {
        create: ['New research proposal', 'নতুন গবেষণা প্রস্তাব'],
        campus: ['Campus', 'ক্যাম্পাস'], program: ['Academic program (optional)', 'একাডেমিক প্রোগ্রাম (ঐচ্ছিক)'],
        title: ['Title', 'শিরোনাম'], abstract: ['Abstract', 'সারসংক্ষেপ'], protocol: ['Protocol', 'গবেষণাপদ্ধতি'],
        save: ['Save draft', 'খসড়া সংরক্ষণ'], queue: ['Proposal and ethics queue', 'প্রস্তাব ও নৈতিক পর্যালোচনার তালিকা'],
        refresh: ['Refresh', 'রিফ্রেশ'], status: ['Status', 'অবস্থা'], revision: ['Revision', 'সংস্করণ'],
        actions: ['Actions', 'কার্যক্রম'], previous: ['Previous', 'আগের'], next: ['Next', 'পরের'],
        detail: ['Selected proposal', 'নির্বাচিত প্রস্তাব'], close: ['Close', 'বন্ধ'],
        action: ['Available action', 'অনুমোদিত কার্যক্রম'], supervisor: ['Supervisor', 'তত্ত্বাবধায়ক'],
        reviewer: ['Independent reviewer', 'স্বতন্ত্র পর্যালোচক'], risk: ['Risk category', 'ঝুঁকির ধরন'],
        low: ['Low', 'কম'], moderate: ['Moderate', 'মাঝারি'], high: ['High', 'বেশি'],
        conflict: ['I declare no conflict of interest', 'আমার স্বার্থের সংঘাত নেই বলে ঘোষণা করছি'],
        reason: ['Decision / correction reason', 'সিদ্ধান্ত / সংশোধনের কারণ'],
        confirm: ['Confirm action', 'কার্যক্রম নিশ্চিত করুন'], history: ['Immutable command history', 'অপরিবর্তনীয় কার্যক্রমের ইতিহাস'],
        files: ['Private research evidence files', 'গোপন গবেষণা প্রমাণ ফাইল'],
        filerules: ['Only scanned PDF/images; up to five files per revision. Downloads require current authorization.', 'শুধু স্ক্যান করা PDF/ছবি; প্রতি সংস্করণে সর্বোচ্চ পাঁচটি ফাইল। ডাউনলোডের সময় বর্তমান অনুমতি যাচাই হবে।'],
        filelabel: ['Supporting evidence (5 MB maximum)', 'প্রমাণ ফাইল (সর্বোচ্চ ৫ এমবি)'],
        fileupload: ['Upload scanned evidence', 'স্ক্যান করা প্রমাণ আপলোড'],
        filesprev: ['Previous files', 'আগের ফাইল'], filesnext: ['Next files', 'পরের ফাইল']
    };
    const actionNames = {
        edit: ['Edit draft', 'খসড়া সংশোধন'], submit: ['Submit for review', 'পর্যালোচনার জন্য জমা'],
        revise: ['Submit revised version', 'সংশোধিত সংস্করণ জমা'], assign: ['Assign reviewers', 'পর্যালোচক নিযুক্তি'],
        'supervisor-approve': ['Supervisor approves', 'তত্ত্বাবধায়কের অনুমোদন'],
        'supervisor-revise': ['Supervisor requests changes', 'তত্ত্বাবধায়কের সংশোধন অনুরোধ'],
        'review-recommend': ['Recommend ethics review', 'নৈতিক পর্যালোচনার সুপারিশ'],
        'review-revise': ['Reviewer requests changes', 'পর্যালোচকের সংশোধন অনুরোধ'],
        'ethics-approve': ['Ethics approval', 'নৈতিক অনুমোদন'],
        'ethics-reject': ['Ethics rejection', 'নৈতিক প্রত্যাখ্যান'],
        withdraw: ['Withdraw proposal', 'প্রস্তাব প্রত্যাহার']
    };
    function localize() {
        for (const node of root.querySelectorAll('[data-r-i18n]')) {
            const pair = labels[node.dataset.rI18n];
            if (pair) node.textContent = pair[bn() ? 1 : 0];
        }
    }
    localize();
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const initialTenant = document.body.dataset.authTenantId;
    const initialSession = document.body.dataset.authSessionReference;
    let listGeneration = 0, detailGeneration = 0, selected = null, page = 1, total = 0, busy = false;
    let createRetry = null, actionRetry = null, fileRetry = null, filePage = 1, fileTotal = 0;
    function clearDetail() {
        ++detailGeneration;
        selected = null; actionRetry = null;
        el('researchDetail').classList.add('d-none');
        el('researchSummary').replaceChildren(); el('researchSensitive').replaceChildren();
        el('researchHistory').replaceChildren();
        el('researchAction').replaceChildren();
        el('researchFileRows').replaceChildren(); el('researchFilesPage').textContent = '';
        el('researchFileForm').classList.add('d-none'); fileRetry = null; filePage = 1; fileTotal = 0;
    }
    function currentSession() {
        if (!initialTenant || !initialSession || document.body.dataset.authTenantId !== initialTenant ||
            document.body.dataset.authSessionReference !== initialSession) {
            clearDetail(); el('researchRows').replaceChildren(); ++listGeneration;
            return false;
        }
        return true;
    }
    async function api(path, payload) {
        if (!currentSession()) throw new Error(t('Session changed; sign in again.', 'সেশন বদলেছে; আবার লগইন করুন।'));
        if (typeof window.EduOSVerifySessionContext === 'function') await window.EduOSVerifySessionContext();
        const response = await fetch(path, {
            method: payload === undefined ? 'GET' : 'POST', credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json', ...(payload === undefined ? {} :
                { 'Content-Type': 'application/json', RequestVerificationToken: token }) },
            ...(payload === undefined ? {} : { body: JSON.stringify(payload) })
        });
        if (!currentSession() || response.headers.get('X-EduOS-Tenant-Id') !== initialTenant ||
            response.headers.get('X-EduOS-Session-Reference') !== initialSession)
            throw new Error(t('Session verification failed.', 'সেশন যাচাই ব্যর্থ।'));
        const json = await response.json().catch(() => null);
        if (!response.ok || json?.success !== true) {
            const error = new Error(response.status === 403 || response.status === 404
                ? t('Access unavailable; refresh or ask an administrator.', 'অধিকার নেই; রিফ্রেশ করুন বা প্রশাসকের সাহায্য নিন।')
                : response.status === 409
                    ? t('Record changed; refresh before trying again.', 'রেকর্ড পরিবর্তিত; আবার চেষ্টা করার আগে রিফ্রেশ করুন।')
                    : json?.message || t('Unable to complete request.', 'অনুরোধ সম্পন্ন হয়নি।'));
            error.status = response.status; throw error;
        }
        return json.data;
    }
    function notice(message, error = false) {
        const n = el('researchNotice');
        n.className = 'alert ' + (error ? 'alert-danger' : 'alert-success'); n.textContent = message;
    }
    function option(select, value, label) {
        const node = document.createElement('option'); node.value = String(value); node.textContent = label;
        select.appendChild(node);
    }
    function cell(tr, value) {
        const td = document.createElement('td'); td.textContent = value == null ? '—' : String(value);
        tr.appendChild(td); return td;
    }
    function emptyRows(message) {
        const tbody = el('researchRows'); tbody.replaceChildren();
        const tr = document.createElement('tr'); const td = cell(tr, message); td.colSpan = 5; tbody.appendChild(tr);
    }
    function controls() {
        el('researchCreateButton').disabled = busy || !el('researchCampus').value;
        el('researchRefresh').disabled = busy;
        el('researchPrevious').disabled = busy || page <= 1;
        el('researchNext').disabled = busy || page * 20 >= total;
        el('researchActButton').disabled = busy || !selected || !el('researchAction').value;
        el('researchFileUpload').disabled = busy || !selected;
        el('researchFilesPrev').disabled = busy || !selected || filePage <= 1;
        el('researchFilesNext').disabled = busy || !selected || filePage * 25 >= fileTotal;
    }
    async function loadFiles(reference, generation) {
        const data = await api('/api/research/proposals/' + encodeURIComponent(reference) +
            '/files?page=' + filePage + '&pageSize=25');
        if (generation !== detailGeneration || !currentSession()) return;
        fileTotal = data.totalCount;
        const container = el('researchFileRows'); container.replaceChildren();
        for (const item of data.items) {
            const row = document.createElement('div'); row.className = 'py-1 border-bottom';
            const link = document.createElement('a');
            link.href = '/api/research/proposals/' + encodeURIComponent(reference) +
                '/files/' + encodeURIComponent(item.reference) + '/download';
            link.textContent = item.fileName; link.setAttribute('download', '');
            row.append(link, document.createTextNode(' · v' + item.revisionNo + ' · ' +
                Math.ceil(item.sizeBytes / 1024) + ' KB'));
            container.appendChild(row);
        }
        if (!data.items.length) container.textContent = t('No private evidence files.', 'কোনো ব্যক্তিগত প্রমাণ ফাইল নেই।');
        el('researchFilesPage').textContent = filePage + ' / ' + Math.max(1, Math.ceil(fileTotal / 25));
        controls();
    }
    el('researchFilesPrev').addEventListener('click', async () => {
        if (!selected || filePage <= 1) return;
        const ref = selected.reference, generation = detailGeneration; filePage--;
        try { await loadFiles(ref, generation); } catch (error) { notice(error.message, true); }
    });
    el('researchFilesNext').addEventListener('click', async () => {
        if (!selected || filePage * 25 >= fileTotal) return;
        const ref = selected.reference, generation = detailGeneration; filePage++;
        try { await loadFiles(ref, generation); } catch (error) { notice(error.message, true); }
    });
    el('researchFileInput').addEventListener('change', () => { fileRetry = null; });
    el('researchFileForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (busy || !selected || !currentSession()) return;
        const file = el('researchFileInput').files?.[0]; if (!file) return;
        if (file.size < 1 || file.size > 5242880) { notice(t('File must be 5 MB or smaller.', 'ফাইল ৫ এমবির মধ্যে হতে হবে।'), true); return; }
        const ref = selected.reference, generation = detailGeneration;
        const data = new FormData(); data.append('file', file); data.append('requestId', fileRetry ||= crypto.randomUUID());
        busy = true; controls();
        try {
            if (typeof window.EduOSVerifySessionContext === 'function') await window.EduOSVerifySessionContext();
            const response = await fetch('/api/research/proposals/' + encodeURIComponent(ref) + '/files', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { RequestVerificationToken: token, Accept: 'application/json' }, body: data
            });
            if (!currentSession() || response.headers.get('X-EduOS-Tenant-Id') !== initialTenant ||
                response.headers.get('X-EduOS-Session-Reference') !== initialSession)
                throw Error(t('Session verification failed.', 'সেশন যাচাই ব্যর্থ।'));
            const result = await response.json().catch(() => null);
            if (!response.ok || result?.success !== true) throw Error(result?.message || t('Upload failed.', 'আপলোড ব্যর্থ।'));
            if (generation !== detailGeneration) return;
            fileRetry = null; el('researchFileForm').reset(); filePage = 1;
            await loadFiles(ref, generation);
            notice(t('Scanned evidence saved to this version.', 'স্ক্যান করা প্রমাণ ফাইল সংস্করণে সংরক্ষিত।'));
        } catch (error) { if (generation === detailGeneration) notice(error.message, true); }
        finally { busy = false; controls(); }
    });
    async function programs() {
        const campus = el('researchCampus').value;
        const select = el('researchProgram'); select.replaceChildren();
        option(select, '', t('No program', 'প্রোগ্রাম নেই'));
        if (!campus) return;
        const generation = ++listGeneration;
        try {
            const list = await api('/api/research/proposals/programs?campusId=' + encodeURIComponent(campus));
            if (generation !== listGeneration) return;
            for (const item of list) option(select, item.id, item.name);
        } catch (error) { if (generation === listGeneration) notice(error.message, true); }
    }
    async function campuses() {
        const list = await api('/api/research/proposals/campuses');
        const select = el('researchCampus'); select.replaceChildren();
        option(select, '', t('Select campus', 'ক্যাম্পাস বেছে নিন'));
        for (const item of list) option(select, item.id, item.name);
        controls();
    }
    async function load() {
        const generation = ++listGeneration;
        clearDetail(); busy = true; total = 0; controls();
        emptyRows(t('Loading…', 'লোড হচ্ছে…')); el('researchPage').textContent = '';
        try {
            const data = await api('/api/research/proposals?page=' + page + '&pageSize=20');
            if (generation !== listGeneration) return;
            total = data.totalCount; const tbody = el('researchRows'); tbody.replaceChildren();
            if (!data.items.length) emptyRows(t('No authorized proposals.', 'অনুমোদিত কোনো প্রস্তাব নেই।'));
            for (const item of data.items) {
                const tr = document.createElement('tr');
                cell(tr, item.title); cell(tr, item.campusId); cell(tr, item.stateCode); cell(tr, item.currentRevision);
                const td = cell(tr, ''); const button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
                button.textContent = t('Open', 'খুলুন'); button.addEventListener('click', () => { void detail(item.reference); });
                td.appendChild(button); tbody.appendChild(tr);
            }
            el('researchPage').textContent = page + ' / ' + Math.max(1, Math.ceil(total / 20));
        } catch (error) {
            if (generation !== listGeneration) return;
            emptyRows(t('Unable to load. Select Refresh.', 'লোড হয়নি। রিফ্রেশ করুন।'));
            notice(error.message, true);
        } finally { if (generation === listGeneration) { busy = false; controls(); } }
    }
    async function reviewerOptions(proposal, generation) {
        el('researchSupervisor').replaceChildren(); el('researchReviewer').replaceChildren();
        const list = await api('/api/research/proposals/reviewers?campusId=' + proposal.campusId);
        if (generation !== detailGeneration) return;
        option(el('researchSupervisor'), '', t('Select supervisor', 'তত্ত্বাবধায়ক বেছে নিন'));
        option(el('researchReviewer'), '', t('Select reviewer', 'পর্যালোচক বেছে নিন'));
        for (const item of list) {
            if (item.canSupervise && item.userId !== proposal.ownerUserId)
                option(el('researchSupervisor'), item.userId, item.name);
            if (item.canReview && item.userId !== proposal.ownerUserId)
                option(el('researchReviewer'), item.userId, item.name);
        }
    }
    function updateActionFields() {
        const action = el('researchAction').value;
        el('researchContentEdit').classList.toggle('d-none', !['edit', 'revise'].includes(action));
        el('researchAssign').classList.toggle('d-none', action !== 'assign');
        el('researchEthics').classList.toggle('d-none', !action.startsWith('ethics-'));
        el('researchReason').required = ['revise', 'withdraw', 'supervisor-revise', 'review-revise', 'ethics-approve', 'ethics-reject'].includes(action);
        controls();
    }
    async function detail(reference) {
        clearDetail(); const generation = detailGeneration;
        try {
            const proposal = await api('/api/research/proposals/' + encodeURIComponent(reference));
            if (generation !== detailGeneration) return;
            selected = proposal;
            const summary = el('researchSummary'); summary.replaceChildren();
            const strong = document.createElement('strong'); strong.textContent = proposal.title;
            summary.appendChild(strong);
            const state = document.createElement('p'); state.className = 'text-muted mb-2';
            state.textContent = proposal.stateCode + ' · ' + t('Revision ', 'সংস্করণ ') + proposal.currentRevision;
            summary.appendChild(state);
            const sensitive = el('researchSensitive'); sensitive.replaceChildren();
            for (const pair of [[t('Abstract', 'সারসংক্ষেপ'), proposal.abstract], [t('Protocol', 'গবেষণাপদ্ধতি'), proposal.protocol]]) {
                if (pair[1] == null) continue;
                const heading = document.createElement('h3'); heading.className = 'h6'; heading.textContent = pair[0];
                const body = document.createElement('p'); body.className = 'small'; body.textContent = pair[1];
                sensitive.append(heading, body);
            }
            const action = el('researchAction'); action.replaceChildren();
            option(action, '', t('Select action', 'কার্যক্রম বেছে নিন'));
            for (const code of proposal.allowedActions || []) {
                const pair = actionNames[code]; if (pair) option(action, code, pair[bn() ? 1 : 0]);
            }
            el('researchEditTitle').value = proposal.title;
            el('researchEditAbstract').value = proposal.abstract || '';
            el('researchEditProtocol').value = proposal.protocol || '';
            el('researchReason').value = ''; el('researchConflict').checked = false;
            el('researchDetail').classList.remove('d-none'); updateActionFields(); controls();
            el('researchFileForm').classList.toggle('d-none',
                !(['Draft', 'ChangesRequested'].includes(proposal.stateCode) &&
                    (proposal.allowedActions || []).some(a => ['edit', 'submit', 'revise'].includes(a))));
            if (proposal.allowedActions?.includes('assign')) await reviewerOptions(proposal, generation);
            const history = await api('/api/research/proposals/' + encodeURIComponent(reference) + '/history?page=1&pageSize=25');
            if (generation !== detailGeneration) return;
            const tbody = el('researchHistory'); tbody.replaceChildren();
            for (const h of history.items) {
                const tr = document.createElement('tr');
                cell(tr, h.actionCode); cell(tr, h.fromState + ' → ' + h.toState);
                cell(tr, h.revisionNo); cell(tr, h.reason); tbody.appendChild(tr);
            }
            await loadFiles(reference, generation);
        } catch (error) {
            if (generation !== detailGeneration) return;
            clearDetail(); notice(error.message, true);
        }
    }
    el('researchCreateForm').addEventListener('submit', async event => {
        event.preventDefault(); if (busy || !currentSession()) return;
        const draft = { campusId: Number(el('researchCampus').value),
            academicProgramId: el('researchProgram').value ? Number(el('researchProgram').value) : null,
            title: el('researchTitle').value.trim(), abstract: el('researchAbstract').value.trim(),
            protocol: el('researchProtocol').value.trim() };
        const payload = JSON.stringify(draft);
        if (!createRetry || createRetry.payload !== payload)
            createRetry = { payload, id: crypto.randomUUID() };
        busy = true; controls();
        try {
            await api('/api/research/proposals', { ...draft, clientRequestId: createRetry.id });
            createRetry = null; el('researchCreateForm').reset();
            notice(t('Draft saved.', 'খসড়া সংরক্ষিত হয়েছে।')); await load();
        } catch (error) {
            if ([400, 403, 404, 409].includes(error.status)) createRetry = null;
            notice(error.message, true);
        } finally { busy = false; controls(); }
    });
    el('researchActionForm').addEventListener('submit', async event => {
        event.preventDefault(); if (busy || !selected || !currentSession()) return;
        const reference = selected.reference, action = el('researchAction').value;
        if (!action) return;
        if (!window.confirm(t('Confirm this audited action?', 'এই নিরীক্ষাযোগ্য কার্যক্রম নিশ্চিত করবেন?'))) return;
        const command = { rowVersion: selected.rowVersion,
            reason: el('researchReason').value.trim() || null,
            ...(action === 'assign' ? { supervisorUserId: Number(el('researchSupervisor').value),
                reviewerUserId: Number(el('researchReviewer').value) } : {}),
            ...(['edit', 'revise'].includes(action) ? {
                title: el('researchEditTitle').value.trim(), abstract: el('researchEditAbstract').value.trim(),
                protocol: el('researchEditProtocol').value.trim() } : {}),
            ...(action.startsWith('ethics-') ? { riskCode: el('researchRisk').value,
                declaresNoConflict: el('researchConflict').checked } : {}) };
        const payload = JSON.stringify(command);
        if (!actionRetry || actionRetry.key !== reference + action + payload)
            actionRetry = { key: reference + action + payload, id: crypto.randomUUID() };
        busy = true; controls();
        try {
            await api('/api/research/proposals/' + encodeURIComponent(reference) + '/' + action,
                { ...command, clientRequestId: actionRetry.id });
            actionRetry = null; notice(t('Action recorded.', 'কার্যক্রম সংরক্ষিত হয়েছে।'));
            await load();
        } catch (error) {
            if ([400, 403, 404, 409].includes(error.status)) {
                actionRetry = null; clearDetail(); await load();
            }
            notice(error.message, true);
        } finally { busy = false; controls(); }
    });
    el('researchAction').addEventListener('change', () => { actionRetry = null; updateActionFields(); });
    el('researchClose').addEventListener('click', clearDetail);
    el('researchRefresh').addEventListener('click', () => { void load(); });
    el('researchPrevious').addEventListener('click', () => { if (page > 1) { page--; void load(); } });
    el('researchNext').addEventListener('click', () => { if (page * 20 < total) { page++; void load(); } });
    el('researchCampus').addEventListener('change', () => { createRetry = null; void programs(); controls(); });
    document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible' && !currentSession()) clearDetail(); });
    window.addEventListener('pageshow', currentSession);
    void campuses().then(load).catch(error => { emptyRows(t('Access unavailable.', 'অধিকার নেই।')); notice(error.message, true); });
})();
