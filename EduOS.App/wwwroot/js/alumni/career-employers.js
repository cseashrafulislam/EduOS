(() => {
    'use strict';
    const root = document.getElementById('careerEmployerWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const bn = document.documentElement.lang.toLowerCase().startsWith('bn');
    const labels = {
        heading: ['Career employer verification', 'ক্যারিয়ার নিয়োগদাতা যাচাই'],
        createHeading: ['Register employer for verification', 'যাচাইয়ের জন্য নিয়োগদাতা নিবন্ধন'],
        listHeading: ['Employer verification queue', 'নিয়োগদাতা যাচাইয়ের তালিকা'],
        campus: ['Campus', 'ক্যাম্পাস'], name: ['Employer name', 'নিয়োগদাতার নাম'],
        website: ['HTTPS website (optional)', 'HTTPS ওয়েবসাইট (ঐচ্ছিক)'],
        save: ['Save draft', 'খসড়া সংরক্ষণ'], filter: ['Status', 'অবস্থা'],
        all: ['All employers', 'সব নিয়োগদাতা'], draft: ['Awaiting verification', 'যাচাই বাকি'],
        verified: ['Verified', 'যাচাইকৃত'], refresh: ['Refresh', 'রিফ্রেশ'],
        export: ['Export verified page CSV', 'যাচাইকৃত পৃষ্ঠার CSV'],
        actions: ['Actions', 'কাজ'], loading: ['Loading…', 'লোড হচ্ছে…'],
        previous: ['Previous', 'আগের'], next: ['Next', 'পরের']
    };
    const t = (en, bd) => bn ? bd : en;
    for (const node of root.querySelectorAll('[data-i18n]')) {
        const pair = labels[node.dataset.i18n];
        if (pair) node.textContent = pair[bn ? 1 : 0];
    }
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    let page = 1, total = 0, busy = false, serial = 0, createRequest = null, hasCampus = false;
    const verifyRequests = new Map();
    const notice = (message, error = false) => {
        const n = el('careerEmployerNotice');
        n.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        n.textContent = message;
    };
    async function api(url, body) {
        const response = await fetch(url, {
            method: body ? 'POST' : 'GET', credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json',
                ...(body ? { 'Content-Type': 'application/json', RequestVerificationToken: token } : {}) },
            ...(body ? { body: JSON.stringify(body) } : {})
        });
        const json = await response.json().catch(() => null);
        if (!response.ok || json?.success !== true) {
            const error = new Error(json?.message || t('Request failed. Reload and retry.', 'অনুরোধ ব্যর্থ। রিলোড করে আবার চেষ্টা করুন।'));
            error.status = response.status;
            throw error;
        }
        return json.data;
    }
    const setBusy = value => {
        busy = value;
        el('careerCreateButton').disabled = value || !hasCampus;
        el('careerRefresh').disabled = value;
        el('careerStatus').disabled = value;
        el('careerPrevious').disabled = value || page <= 1;
        el('careerNext').disabled = value || page * 20 >= total;
    };
    const clearRows = message => {
        const rows = el('careerEmployerRows');
        rows.replaceChildren();
        const tr = document.createElement('tr');
        const td = document.createElement('td');
        td.colSpan = 5; td.textContent = message; tr.appendChild(td); rows.appendChild(tr);
    };
    const exportLink = enabled => {
        const link = el('careerExport');
        link.removeAttribute('href');
        link.classList.add('disabled');
        link.setAttribute('aria-disabled', 'true');
        if (enabled) {
            link.href = '/api/alumni/career/employers.csv?page=' + page + '&pageSize=20&status=2';
            link.classList.remove('disabled');
            link.setAttribute('aria-disabled', 'false');
        }
    };
    async function load() {
        const generation = ++serial;
        setBusy(true); exportLink(false); total = 0;
        clearRows(t('Loading…', 'লোড হচ্ছে…'));
        el('careerPage').textContent = '';
        try {
            const data = await api('/api/alumni/career/employers?page=' + page +
                '&pageSize=20&status=' + el('careerStatus').value);
            if (generation !== serial) return;
            total = data.totalCount;
            const rows = el('careerEmployerRows');
            rows.replaceChildren();
            if (!data.items.length) clearRows(t('No employers in this scope.', 'এই পরিসরে কোনো নিয়োগদাতা নেই।'));
            for (const item of data.items) {
                const tr = document.createElement('tr');
                const cell = value => {
                    const td = document.createElement('td');
                    td.textContent = value == null ? '' : String(value);
                    tr.appendChild(td); return td;
                };
                cell(item.name); cell(item.campusId); cell(item.website || '—');
                cell(item.status === 2 ? labels.verified[bn ? 1 : 0] : labels.draft[bn ? 1 : 0]);
                const actions = cell('');
                if (item.canVerify) {
                    const button = document.createElement('button');
                    button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
                    button.textContent = t('Verify', 'যাচাই করুন');
                    // Server rechecks independent approval; the button never grants access.
                    button.addEventListener('click', async () => {
                        const key = item.reference + ':' + item.rowVersion;
                        if (!verifyRequests.has(key)) verifyRequests.set(key, crypto.randomUUID());
                        button.disabled = true;
                        try {
                            await api('/api/alumni/career/employers/' + encodeURIComponent(item.reference) + '/verify',
                                { clientRequestId: verifyRequests.get(key), rowVersion: item.rowVersion });
                            verifyRequests.delete(key);
                            notice(t('Employer verified.', 'নিয়োগদাতা যাচাই হয়েছে।'));
                            await load();
                        } catch (error) {
                            if (error.status === 403 || error.status === 409) {
                                verifyRequests.delete(key);
                                await load();
                            }
                            notice(error.message, true);
                            button.disabled = false;
                        }
                    });
                    actions.appendChild(button);
                }
                rows.appendChild(tr);
            }
            el('careerPage').textContent = page + ' / ' + Math.max(1, Math.ceil(total / 20));
            exportLink(data.items.some(item => item.status === 2) && Number(el('careerStatus').value) === 2);
        } catch (error) {
            if (generation !== serial) return;
            clearRows(t('Unable to load employers. Select Refresh.', 'নিয়োগদাতা লোড হয়নি। রিফ্রেশ চাপুন।'));
            notice(error.message, true);
        } finally {
            if (generation === serial) setBusy(false);
        }
    }
    async function campuses() {
        try {
            const data = await api('/api/alumni/event-campuses');
            const select = el('careerCampus');
            select.replaceChildren();
            for (const campus of data) {
                const option = document.createElement('option');
                option.value = campus.id; option.textContent = campus.name;
                select.appendChild(option);
            }
            hasCampus = data.length > 0;
            setBusy(busy);
            if (!data.length) {
                notice(t('No authorized campus. Contact your administrator.', 'অনুমোদিত ক্যাম্পাস নেই। প্রশাসকের সঙ্গে যোগাযোগ করুন।'), true);
            }
        } catch (error) {
            hasCampus = false;
            setBusy(busy);
            notice(error.message, true);
        }
    }
    for (const id of ['careerCampus', 'careerName', 'careerWebsite']) {
        el(id).addEventListener('input', () => { createRequest = null; });
    }
    el('careerEmployerCreate').addEventListener('submit', async event => {
        event.preventDefault();
        if (busy || !event.currentTarget.reportValidity()) return;
        if (!createRequest) createRequest = crypto.randomUUID();
        setBusy(true);
        try {
            await api('/api/alumni/career/employers', {
                clientRequestId: createRequest, campusId: Number(el('careerCampus').value),
                name: el('careerName').value, website: el('careerWebsite').value || null
            });
            createRequest = null;
            el('careerName').value = ''; el('careerWebsite').value = '';
            notice(t('Draft saved; another administrator must verify.', 'খসড়া সংরক্ষিত; অন্য প্রশাসক যাচাই করবেন।'));
            page = 1; await load();
        } catch (error) {
            if (error.status === 403 || error.status === 409 || error.status === 400) createRequest = null;
            notice(error.message, true);
        } finally { setBusy(false); }
    });
    el('careerStatus').addEventListener('change', () => { page = 1; load(); });
    el('careerRefresh').addEventListener('click', load);
    el('careerPrevious').addEventListener('click', () => { if (!busy && page > 1) { page--; load(); } });
    el('careerNext').addEventListener('click', () => { if (!busy && page * 20 < total) { page++; load(); } });
    exportLink(false); clearRows(t('Loading…', 'লোড হচ্ছে…'));
    campuses(); load();
})();
