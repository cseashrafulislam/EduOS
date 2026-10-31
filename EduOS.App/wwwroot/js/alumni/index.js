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
        privacyNote: ['Unchecking and saving revokes directory access and clears alumni contact details. Graduation evidence is retained.', 'টিক তুলে সংরক্ষণ করলে তালিকা থেকে তথ্য সরবে এবং যোগাযোগের তথ্য মুছে যাবে। উত্তীর্ণ হওয়ার প্রমাণ সংরক্ষিত থাকবে।'],
        save: ['Save preferences', 'পছন্দ সংরক্ষণ'],
        directory: ['Opted-in alumni directory', 'সম্মতিপ্রাপ্ত প্রাক্তন শিক্ষার্থী'],
        search: ['Search', 'খুঁজুন'], export: ['Export current page CSV', 'বর্তমান পৃষ্ঠা CSV'],
        name: ['Name', 'নাম'], code: ['Code', 'কোড'], contact: ['Contact', 'যোগাযোগ'],
        completed: ['Completed', 'উত্তীর্ণ'], loading: ['Loading…', 'লোড হচ্ছে…'],
        previous: ['Previous', 'আগের'], next: ['Next', 'পরের']
    };
    for (const node of root.querySelectorAll('[data-i18n]')) {
        const values = strings[node.dataset.i18n];
        if (values) node.textContent = values[bn ? 1 : 0];
    }
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { graduates: [], selected: null, page: 1, total: 0, busy: false, requestId: null };
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
        const item = state.selected;
        el('alumniOptIn').checked = Boolean(item?.directoryOptIn);
        el('alumniEmail').value = item?.contactEmail || '';
        el('alumniPhone').value = item?.contactPhone || '';
        el('alumniSave').disabled = !item;
        syncContactInputs();
    }
    function syncContactInputs() {
        const sharing = el('alumniOptIn').checked;
        el('alumniEmail').disabled = !sharing;
        el('alumniPhone').disabled = !sharing;
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
                contactEmail: el('alumniEmail').value.trim() || null,
                contactPhone: el('alumniPhone').value.trim() || null,
                rowVersion: state.selected.rowVersion || null
            }) });
            state.graduates = state.graduates.map(x => x.studentReference === saved.studentReference ? saved : x);
            state.requestId = null; showSelected();
            notice(bn ? 'গোপনীয়তার পছন্দ সংরক্ষিত হয়েছে।' : 'Privacy preferences saved.');
            if (root.dataset.admin === 'true') await loadDirectory();
        } catch (error) {
            notice(error.message, true);
        } finally { state.busy = false; el('alumniSave').disabled = !state.selected; }
    }
    async function loadDirectory() {
        if (root.dataset.admin !== 'true') return;
        const query = new URLSearchParams({ page: state.page, pageSize: 20, search: el('alumniSearch').value.trim() });
        const page = await api('/api/alumni/directory?' + query);
        state.total = page.totalCount;
        const rows = el('alumniRows');
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
        el('alumniPage').textContent = state.page + ' / ' + Math.max(1, Math.ceil(state.total / 20));
        el('alumniPrev').disabled = state.page <= 1;
        el('alumniNext').disabled = state.page * 20 >= state.total;
        el('alumniExport').href = '/api/alumni/directory.csv?' + query;
    }
    el('alumniGraduate').addEventListener('change', showSelected);
    el('alumniOptIn').addEventListener('change', syncContactInputs);
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
    loadMine().catch(e => notice(e.message, true));
    loadDirectory().catch(e => notice(e.message, true));
})();
