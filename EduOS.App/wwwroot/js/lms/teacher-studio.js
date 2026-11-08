(() => {
    'use strict';
    const root = document.getElementById('teacherStudio');
    if (!root) return;
    const el = id => document.getElementById(id);
    const author = root.dataset.canAuthor === 'true';
    const reviewer = root.dataset.canReview === 'true';
    const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const tr = (en, bd) => bn ? bd : en;
    const initialSession = document.body?.dataset.authSessionReference || '';
    const initialTenant = document.body?.dataset.authTenantId || '';
    const active = new Set();
    const session = () => /^[A-F0-9]{64}$/.test(initialSession) &&
        document.body?.dataset.authSessionReference === initialSession &&
        document.body?.dataset.authTenantId === initialTenant &&
        window.EduOSSessionTransitionPending?.() !== true;
    const stale = () => Error(tr('Session changed. Sign in and reload.',
        'সেশন পরিবর্তিত হয়েছে। আবার লগইন করে পৃষ্ঠা খুলুন।'));
    let page = 1, totalPages = 1, sequence = 0, resources = [], optionsReady = false;
    let requestId = null, replacing = null;
    const csrf = () => document.querySelector('meta[name="request-verification-token"]')?.content ||
        document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    function notice(message, success = false) {
        if (!session()) return;
        const target = el('studioAlert');
        target.className = 'alert alert-' + (success ? 'success' : 'danger');
        target.textContent = message;
        target.focus();
    }
    async function api(url, method = 'GET', data = null) {
        if (!session()) throw stale();
        const controller = new AbortController(); active.add(controller);
        const headers = { Accept: 'application/json' };
        if (method !== 'GET') {
            if (!csrf()) throw Error(tr('Security token missing. Reload.', 'নিরাপত্তা টোকেন নেই। পুনরায় খুলুন।'));
            headers.RequestVerificationToken = csrf();
            if (!(data instanceof FormData)) headers['Content-Type'] = 'application/json';
        }
        try {
            const response = await fetch(url, { method, cache: 'no-store', credentials: 'same-origin',
                headers, signal: controller.signal, ...(data === null ? {} :
                    { body: data instanceof FormData ? data : JSON.stringify(data) }) });
            if (!session()) throw stale();
            const payload = await response.json().catch(() => null);
            if (!session()) throw stale();
            if (!response.ok || !payload?.success)
                throw Error(payload?.message || tr('Operation failed.', 'অনুরোধ ব্যর্থ হয়েছে।'));
            return payload.data;
        } finally { active.delete(controller); }
    }
    const option = (value, label) => {
        const x = document.createElement('option'); x.value = String(value); x.textContent = label; return x;
    };
    function setOptions(id, values, placeholder) {
        const select = el(id); if (!select) return;
        select.replaceChildren(option('', placeholder));
        for (const item of values) select.append(option(item.reference || item.id, item.name));
    }
    async function loadOptions() {
        const data = await api('/api/lms/teacher-resources/options');
        if (!author) return;
        setOptions('studioCampus', data.campuses || [], tr('Choose campus', 'ক্যাম্পাস নির্বাচন'));
        setOptions('studioSubject', data.subjects || [], tr('Any subject', 'যেকোনো বিষয়'));
        setOptions('studioLevel', data.levels || [], tr('Any level', 'যেকোনো শ্রেণি'));
        setOptions('studioPlan', data.lessonPlans || [], tr('No lesson link', 'পাঠ পরিকল্পনার লিংক নেই'));
        optionsReady = (data.campuses || []).length > 0;
        el('studioCampus').disabled = !optionsReady;
        el('studioSave').disabled = !optionsReady;
        if (!optionsReady) notice(tr('No active campus assigned to this teacher.',
            'এই শিক্ষকের সক্রিয় ক্যাম্পাস দায়িত্ব নেই।'));
    }
    const cell = value => {
        const x = document.createElement('td');
        x.textContent = value == null || value === '' ? '—' : String(value);
        return x;
    };
    function action(label, handler) {
        const button = document.createElement('button');
        button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary me-1 mb-1';
        button.textContent = label; button.addEventListener('click', handler);
        return button;
    }
    function render() {
        const tbody = el('studioRows'); tbody.replaceChildren();
        if (!resources.length) {
            const row = document.createElement('tr'), td = cell(tr('No resources found.', 'কোনো রিসোর্স নেই।'));
            td.colSpan = 5; td.className = 'text-muted text-center p-3'; row.append(td); tbody.append(row);
        }
        for (const item of resources) {
            const row = document.createElement('tr'), controls = cell(''); controls.textContent = '';
            controls.append(action(tr('File', 'ফাইল'), () => {
                if (session()) window.open('/api/lms/teacher-resources/' + encodeURIComponent(item.reference) +
                    '/download', '_blank', 'noopener,noreferrer');
            }));
            if (author && item.canModify && item.stateCode === 'Draft')
                controls.append(action(tr('Submit', 'জমা দিন'), () => change(item, 'submit')));
            if (author && item.canModify && item.stateCode === 'Approved')
                controls.append(action(tr('Revise', 'নতুন সংস্করণ'), () => startRevision(item)));
            if ((item.canModify || reviewer) && ['Draft', 'Rejected', 'Approved'].includes(item.stateCode))
                controls.append(action(tr('Withdraw', 'প্রত্যাহার'), () => change(item, 'withdraw')));
            if (reviewer && item.stateCode === 'Submitted') {
                controls.append(action(tr('Approve', 'অনুমোদন'), () => change(item, 'review', true)));
                controls.append(action(tr('Reject', 'প্রত্যাখ্যান'), () => change(item, 'review', false)));
            }
            row.append(cell(item.title), cell(item.kindCode), cell(item.versionNo),
                cell(item.stateCode), controls);
            tbody.append(row);
        }
        el('studioPage').textContent = page + ' / ' + totalPages;
        el('studioPrev').disabled = page <= 1;
        el('studioNext').disabled = page >= totalPages;
    }
    async function load() {
        const seq = ++sequence;
        const query = new URLSearchParams({ page: String(page), pageSize: '20',
            search: el('studioSearch').value.trim(), state: el('studioState').value });
        const result = await api('/api/lms/teacher-resources?' + query);
        if (seq !== sequence || !session()) return;
        resources = result.items || [];
        totalPages = Math.max(1, Math.ceil((result.totalCount || 0) / 20));
        render();
    }
    async function change(item, kind, approve) {
        const note = kind === 'withdraw' || kind === 'review' && !approve ?
            window.prompt(tr('Reason (required)', 'কারণ লিখুন (আবশ্যক)'), '') :
            kind === 'review' ? window.prompt(tr('Review note (optional)', 'রিভিউ মন্তব্য (ঐচ্ছিক)'), '') : '';
        if (note === null || (kind === 'withdraw' || kind === 'review' && !approve) && !note.trim()) return;
        if (!window.confirm(tr('Confirm this resource action?', 'এই কাজ নিশ্চিত করবেন?'))) return;
        try {
            await api('/api/lms/teacher-resources/' + encodeURIComponent(item.reference) + '/' + kind +
                (kind === 'review' ? '?approve=' + (approve ? 'true' : 'false') : ''),
                'POST', { rowVersion: item.rowVersion, note: note || null });
            notice(tr('Resource updated.', 'রিসোর্স আপডেট হয়েছে।'), true);
            await load();
        } catch (e) { notice(e.message); }
    }
    function startRevision(item) {
        if (!author || !optionsReady) return;
        replacing = { reference: item.reference, rowVersion: item.rowVersion };
        requestId = null;
        el('studioTitle').value = item.title;
        el('studioCampus').value = String(item.campusId);
        el('studioKind').value = item.kindCode;
        el('studioScope').value = item.scopeCode;
        el('studioSubject').value = item.subjectId || '';
        el('studioLevel').value = item.academicLevelId || '';
        el('studioTopic').value = item.topic || '';
        el('studioLanguage').value = item.languageCode;
        el('studioSource').value = item.sourceUrl || '';
        el('studioRights').value = item.rightsHolder;
        el('studioLicence').value = item.licenceCode;
        el('studioEvidence').value = item.licenceEvidence;
        el('studioCancelRevision').classList.remove('d-none');
        el('studioFormTitle').textContent = tr('New revision', 'নতুন সংস্করণ');
        el('studioTitle').focus();
    }
    function clearRevision() {
        replacing = null; requestId = null;
        el('studioCancelRevision')?.classList.add('d-none');
        if (el('studioFormTitle')) el('studioFormTitle').textContent =
            tr('Create teacher resource', 'নতুন শিক্ষাসামগ্রী');
    }
    async function save(event) {
        event.preventDefault();
        if (!author || !optionsReady || !session()) return;
        const file = el('studioFile').files[0];
        if (!file || file.size < 1 || file.size > 10485760) {
            notice(tr('Choose a file up to 10 MB.', '১০ এমবি পর্যন্ত ফাইল নির্বাচন করুন।')); return;
        }
        if (!requestId) requestId = crypto.randomUUID();
        const data = new FormData();
        for (const [name, id] of [
            ['Title','studioTitle'],['CampusId','studioCampus'],['KindCode','studioKind'],
            ['ScopeCode','studioScope'],['Topic','studioTopic'],['LanguageCode','studioLanguage'],
            ['SourceUrl','studioSource'],['RightsHolder','studioRights'],
            ['LicenceCode','studioLicence'],['LicenceEvidence','studioEvidence'],
            ['SubjectId','studioSubject'],['AcademicLevelId','studioLevel'],
            ['LessonPlanReference','studioPlan']]) {
            const value = el(id).value.trim(); if (value) data.append(name, value);
        }
        data.append('RequestId', requestId);
        if (replacing) {
            data.append('SupersedesReference', replacing.reference);
            data.append('SupersedesRowVersion', replacing.rowVersion);
        }
        data.append('file', file, file.name);
        const button = el('studioSave'); button.disabled = true;
        try {
            await api('/api/lms/teacher-resources', 'POST', data);
            if (!session()) return;
            notice(tr('Draft saved. Submit it from the list.', 'Draft সংরক্ষিত। তালিকা থেকে জমা দিন।'), true);
            el('studioUpload').reset(); clearRevision(); await load();
        } catch (e) { notice(e.message); }
        finally { button.disabled = !optionsReady || !session(); }
    }
    el('studioFilter').addEventListener('submit', event => {
        event.preventDefault(); page = 1; load().catch(e => notice(e.message));
    });
    el('studioPrev').addEventListener('click', () => {
        if (page > 1) { --page; load().catch(e => notice(e.message)); }
    });
    el('studioNext').addEventListener('click', () => {
        if (page < totalPages) { ++page; load().catch(e => notice(e.message)); }
    });
    if (author) {
        el('studioUpload').addEventListener('submit', save);
        el('studioUpload').addEventListener('input', () => { requestId = null; });
        el('studioCancelRevision').addEventListener('click', clearRevision);
    }
    window.addEventListener('pagehide', () => { for (const request of active) request.abort(); });
    Promise.all([loadOptions(), load()]).catch(e => notice(e.message));
})();
