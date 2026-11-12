(() => {
    'use strict';
    const root = document.getElementById('facilitySpaces');
    if (!root) return;
    const bn = root.dataset.bn === 'true';
    const manage = root.dataset.manage === 'true';
    const el = id => document.getElementById(id);
    const tr = (en, bd) => bn ? bd : en;
    const token = document.querySelector('meta[name="request-verification-token"]')?.content || '';
    const session = {
        reference: document.body?.dataset.authSessionReference || '',
        tenant: document.body?.dataset.authTenantId || ''
    };
    const shield = el('eduosSessionShield');
    const searchForm = el('facilitySearchForm');
    const editor = manage ? el('facilityForm') : null;
    const saveButton = editor?.querySelector('button[type="submit"]');
    const resetButton = el('facilityReset');
    let page = 1, pages = 1, rows = [], requestNumber = 0, listController = null;
    let saving = false, dirty = false, blocked = false;

    const messages = {
        'Facilities access denied.': ['Facilities access denied.', 'সুবিধা ব্যবস্থাপনার অনুমতি নেই।'],
        'Invalid page or filter.': ['Invalid page or filter.', 'পৃষ্ঠা বা ফিল্টার সঠিক নয়।'],
        'Valid space details required.': ['Enter valid space details.', 'সঠিক স্থানের তথ্য দিন।'],
        'Invalid code, name or address.': ['Check the code, name and address.', 'কোড, নাম ও ঠিকানা যাচাই করুন।'],
        'Space not found.': ['Space not found. Refresh the list.', 'স্থান পাওয়া যায়নি। তালিকা নতুন করে দেখুন।'],
        'Space changed; reload.': ['Space changed. Reload before editing again.', 'স্থান পরিবর্তিত হয়েছে। আবার সম্পাদনার আগে তালিকা লোড করুন।'],
        'Space code already exists.': ['This campus already uses that space code.', 'এই ক্যাম্পাসে কোডটি আগে থেকেই আছে।'],
        'Space code or campus conflict.': ['Space code or campus conflict. Refresh before retrying.', 'কোড বা ক্যাম্পাসে সংঘাত হয়েছে। পুনরায় চেষ্টার আগে তালিকা লোড করুন।']
    };
    function notice(message, ok = false) {
        const target = el('facilityAlert');
        target.className = 'alert alert-' + (ok ? 'success' : 'danger');
        target.textContent = message;
    }
    function clearNotice() {
        el('facilityAlert').className = 'd-none';
        el('facilityAlert').textContent = '';
    }
    function messageFor(message, status) {
        if (status === 401) return tr('Sign in again to continue.', 'চালিয়ে যেতে আবার সাইন ইন করুন।');
        if (status === 403) return tr('You do not have access to this campus or action.', 'এই ক্যাম্পাস বা কাজের অনুমতি নেই।');
        if (status === 409) return (messages[message] || [
            'Information changed. Refresh the list before retrying.',
            'তথ্য পরিবর্তিত হয়েছে। আবার চেষ্টার আগে তালিকা লোড করুন।'
        ])[bn ? 1 : 0];
        return messages[message]?.[bn ? 1 : 0] || tr(
            'Request failed. Check your connection and retry.',
            'অনুরোধ ব্যর্থ হয়েছে। সংযোগ যাচাই করে আবার চেষ্টা করুন।');
    }
    function lockPage() {
        blocked = true;
        requestNumber++;
        listController?.abort();
        rows = [];
        el('facilityRows').replaceChildren();
        root.setAttribute('aria-busy', 'false');
        dirty = false;
        if (editor) {
            editor.reset();
            el('facilityId').value = el('facilityVersion').value = '';
        }
        el('facilityPrev').disabled = el('facilityNext').disabled = true;
        if (editor) {
            for (const control of editor.querySelectorAll('input, button')) control.disabled = true;
        }
        notice(tr('Institution session changed. Reload this page.', 'প্রতিষ্ঠানের সেশন পরিবর্তিত হয়েছে। পেজটি আবার লোড করুন।'));
        window.EduOSVerifySessionContext?.();
    }
    function sessionCurrent() {
        return !blocked && !window.EduOSSessionTransitionPending?.() &&
            document.body?.dataset.authSessionReference === session.reference &&
            document.body?.dataset.authTenantId === session.tenant;
    }
    async function verifySession() {
        if (!sessionCurrent() || !window.EduOSVerifySessionContext || !shield) {
            lockPage();
            throw new Error('session-changed');
        }
        await window.EduOSVerifySessionContext();
        if (!sessionCurrent() || shield.hidden !== true) {
            lockPage();
            throw new Error('session-changed');
        }
    }
    async function api(path, method = 'GET', body, signal) {
        if (method !== 'GET' && !token) throw new Error(tr(
            'Security token missing. Reload before saving.', 'নিরাপত্তা টোকেন নেই। সংরক্ষণের আগে পেজ রিলোড করুন।'));
        if (!sessionCurrent()) {
            lockPage();
            throw new Error('session-changed');
        }
        const headers = { Accept: 'application/json' };
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        if (method !== 'GET') headers.RequestVerificationToken = token;
        const response = await fetch('/api/facilities/spaces' + path, {
            method, credentials: 'same-origin', cache: 'no-store', headers, signal,
            ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        // The spaces API has no per-response session identity header. Reconcile
        // with the existing server session endpoint before displaying any payload.
        await verifySession();
        const data = await response.json().catch(() => null);
        if (!sessionCurrent() || shield.hidden !== true) {
            lockPage();
            throw new Error('session-changed');
        }
        if (!response.ok || !data?.success) {
            const error = new Error(messageFor(data?.message, response.status));
            error.status = response.status;
            throw error;
        }
        return data.data;
    }
    function cell(value) {
        const td = document.createElement('td');
        td.textContent = value == null || value === '' ? '—' : String(value);
        return td;
    }
    function setLoading(loading) {
        root.setAttribute('aria-busy', String(loading));
        el('facilityPrev').disabled = loading || page <= 1;
        el('facilityNext').disabled = loading || page >= pages;
        if (loading) el('facilityPage').textContent = tr('Loading spaces…', 'স্থান লোড হচ্ছে…');
    }
    function render() {
        const target = el('facilityRows');
        target.replaceChildren();
        for (const item of rows) {
            const line = document.createElement('tr');
            [item.campusId, item.code, item.name, item.address,
                item.isActive ? tr('Active', 'সক্রিয়') : tr('Inactive', 'নিষ্ক্রিয়')].forEach(v => line.append(cell(v)));
            if (manage) {
                const td = document.createElement('td'), button = document.createElement('button');
                button.type = 'button';
                button.className = 'btn btn-outline-primary btn-sm';
                button.textContent = tr('Edit', 'সম্পাদনা');
                button.setAttribute('aria-label', tr('Edit space ', 'স্থান সম্পাদনা ') + item.code);
                button.addEventListener('click', () => {
                    if (saving || blocked || (dirty && !window.confirm(tr(
                        'Discard unsaved space changes?', 'সংরক্ষণ না করা পরিবর্তন বাদ দেবেন?')))) return;
                    el('facilityId').value = item.id;
                    el('facilityVersion').value = item.rowVersion;
                    el('facilityCampus').value = item.campusId;
                    el('facilityCode').value = item.code;
                    el('facilityName').value = item.name;
                    el('facilityAddress').value = item.address || '';
                    el('facilityActive').checked = item.isActive;
                    dirty = false;
                    el('facilityForm').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
                    el('facilityName').focus();
                });
                td.append(button); line.append(td);
            }
            target.append(line);
        }
        if (!rows.length) {
            const line = document.createElement('tr');
            const td = cell(tr('No spaces found. Try another authorized campus or search.', 'কোনো স্থান পাওয়া যায়নি। অন্য অনুমোদিত ক্যাম্পাস বা শব্দ দিয়ে খুঁজুন।'));
            td.colSpan = manage ? 6 : 5;
            line.append(td);
            target.append(line);
        }
        el('facilityPage').textContent = tr('Page ', 'পৃষ্ঠা ') + page + ' / ' + pages;
        setLoading(false);
    }
    async function load() {
        if (blocked) return false;
        const seq = ++requestNumber;
        listController?.abort();
        listController = new AbortController();
        const query = new URLSearchParams({
            page: String(page), pageSize: '25', search: el('facilitySearch').value.trim()
        });
        const campus = el('facilityCampusFilter').value.trim();
        if (campus && (!/^[1-9]\d*$/.test(campus) || !Number.isSafeInteger(Number(campus)))) {
            rows = []; pages = 1; render();
            notice(tr('Enter a valid campus ID.', 'সঠিক ক্যাম্পাস আইডি দিন।'));
            return false;
        }
        if (query.get('search').length > 100) {
            rows = []; pages = 1; render();
            notice(tr('Search must be 100 characters or fewer.', 'খোঁজার শব্দ ১০০ অক্ষরের মধ্যে রাখুন।'));
            return false;
        }
        if (campus) query.set('campusId', campus);
        setLoading(true);
        try {
            const result = await api('?' + query.toString(), 'GET', undefined, listController.signal);
            if (seq !== requestNumber || blocked) return false;
            rows = Array.isArray(result?.items) ? result.items : [];
            pages = Math.max(1, Number(result?.totalPages) || 1);
            if (page > pages) {
                page = pages;
                return await load();
            }
            clearNotice();
            render();
            return true;
        } catch (error) {
            if (seq !== requestNumber || blocked || error.name === 'AbortError' || error.message === 'session-changed') return false;
            rows = [];
            render();
            notice(error.message);
            return false;
        }
    }
    searchForm.addEventListener('submit', event => {
        event.preventDefault();
        page = 1;
        void load();
    });
    el('facilityPrev').addEventListener('click', () => { if (page > 1 && !blocked) { page--; void load(); } });
    el('facilityNext').addEventListener('click', () => { if (page < pages && !blocked) { page++; void load(); } });
    if (editor) {
        editor.addEventListener('input', () => { dirty = true; });
        editor.addEventListener('change', () => { dirty = true; });
        resetButton.addEventListener('click', () => {
            if (saving || blocked) return;
            if (dirty && !window.confirm(tr('Discard unsaved space changes?', 'সংরক্ষণ না করা পরিবর্তন বাদ দেবেন?'))) return;
            editor.reset();
            el('facilityId').value = '';
            el('facilityVersion').value = '';
            dirty = false;
            el('facilityCode').focus();
        });
        editor.addEventListener('submit', async event => {
            event.preventDefault();
            if (saving || blocked || !editor.reportValidity()) return;
            if (!token) {
                notice(tr('Security token missing. Reload before saving.', 'নিরাপত্তা টোকেন নেই। সংরক্ষণের আগে পেজ রিলোড করুন।'));
                return;
            }
            const id = el('facilityId').value;
            const body = {
                campusId: Number(el('facilityCampus').value), code: el('facilityCode').value.trim(),
                name: el('facilityName').value.trim(), address: el('facilityAddress').value.trim() || null,
                isActive: el('facilityActive').checked, rowVersion: el('facilityVersion').value || null
            };
            saving = true;
            saveButton.disabled = resetButton.disabled = true;
            try {
                await verifySession();
                await api(id ? '/' + encodeURIComponent(id) : '', id ? 'PUT' : 'POST', body);
                dirty = false;
                editor.reset();
                el('facilityId').value = el('facilityVersion').value = '';
                page = 1;
                const refreshed = await load();
                if (!blocked) notice(refreshed
                    ? tr('Space saved.', 'স্থান সংরক্ষিত হয়েছে।')
                    : tr('Space saved, but the list could not be refreshed. Search again; do not resubmit.',
                        'স্থান সংরক্ষিত হয়েছে, কিন্তু তালিকা লোড হয়নি। আবার খুঁজুন; পুনরায় জমা দেবেন না।'), true);
            } catch (error) {
                if (!blocked && error.message !== 'session-changed') notice(error.message + ' ' + tr(
                    'Check the list before retrying an uncertain save.',
                    'সংরক্ষণের ফল অনিশ্চিত হলে আবার চেষ্টার আগে তালিকা যাচাই করুন।'));
            } finally {
                saving = false;
                if (!blocked) saveButton.disabled = resetButton.disabled = false;
            }
        });
    }
    window.addEventListener('beforeunload', event => {
        if (!dirty || blocked) return;
        event.preventDefault();
        event.returnValue = '';
    });
    window.addEventListener('pageshow', event => {
        if (event.persisted) void verifySession().then(() => load()).catch(() => {});
    });
    void load();
})();