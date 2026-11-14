(() => {
    'use strict';
    const root = document.getElementById('institutionManagementApp');
    if (!root) return;
    const el = id => document.getElementById(id);
    const bn = root.dataset.bangla === 'true';
    const t = (en, bangla) => bn ? bangla : en;
    const locale = bn ? 'bn-BD' : 'en-BD';
    const state = { profile: null, campuses: [], editing: null, historyPage: 1 };
    const token = () => root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';

    function notify(message, failed = false) {
        const alert = el('institutionMessage');
        alert.className = 'alert ' + (failed ? 'alert-danger' : 'alert-success');
        alert.textContent = message;
        alert.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    async function api(url, options = {}) {
        const response = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store', ...options,
            headers: {
                Accept: 'application/json',
                ...(options.body && !(options.body instanceof FormData)
                    ? { 'Content-Type': 'application/json' } : {}),
                ...(options.method && options.method !== 'GET' ? { RequestVerificationToken: token() } : {}),
                ...(options.headers || {})
            }
        });
        const payload = await response.json().catch(() => null);
        if (!response.ok || payload?.success !== true)
            throw new Error(payload?.message || 'Unable to complete request. Please reload.');
        return payload.data;
    }

    const value = id => el(id).value.trim();
    const set = (id, text) => { el(id).value = text ?? ''; };
    const make = (tag, text, className) => {
        const node = document.createElement(tag);
        node.textContent = String(text ?? '');
        if (className) node.className = className;
        return node;
    };

    function setProfile(data) {
        state.profile = data;
        set('institutionName', data.name);
        set('institutionEmail', data.email);
        set('institutionPhone', data.phone);
        set('institutionCountry', data.countryCode);
        set('institutionAddress', data.address);
        set('institutionCurrency', data.currencyCode);
        set('institutionTimezone', data.timeZoneId);
        set('institutionDateFormat', data.dateFormat);
        const language = el('institutionLanguage');
        if ([...language.options].some(x => x.value === data.defaultLanguage))
            language.value = data.defaultLanguage;
        else {
            const option = new Option(data.defaultLanguage || 'Other', data.defaultLanguage || 'en');
            language.add(option);
            language.value = option.value;
        }
        set('institutionSubdomain', data.subdomain);
        for (const [field, url, removeButton] of [
            ['institutionLogoPreview', data.logoUrl, 'removeInstitutionLogo'],
            ['institutionFaviconPreview', data.faviconUrl, 'removeInstitutionFavicon']
        ]) {
            const preview = el(field);
            preview.classList.toggle('d-none', !url);
            if (url) preview.src = url;
            else preview.removeAttribute('src');
            el(removeButton).disabled = !url;
        }
    }

    async function loadProfile() {
        const profile = await api('/api/tenant-profile');
        setProfile(profile);
    }


    async function loadHistory(page = 1) {
        const history = await api('/api/tenant-profile/history?page=' +
            encodeURIComponent(page) + '&pageSize=10');
        state.historyPage = history.page;
        const body = el('institutionHistoryBody');
        body.replaceChildren();
        if (!history.items?.length) {
            const row = make('tr', '');
            const cell = make('td', t('No recorded changes on this page.', 'এই পৃষ্ঠায় পরিবর্তনের ইতিহাস নেই।'));
            cell.colSpan = 4; row.append(cell); body.append(row);
        }
        for (const entry of history.items || []) {
            const row = make('tr', '');
            const date = new Date(entry.occurredAt);
            row.append(make('td', Number.isNaN(date.getTime()) ? '—' : date.toLocaleString(locale)),
                make('td', entry.action),
                make('td', entry.changedFields || '—'),
                make('td', entry.actorName || ('User #' + (entry.actorId || 'unknown'))));
            body.append(row);
        }
        el('institutionHistoryPrevious').disabled = !history.hasPreviousPage;
        el('institutionHistoryNext').disabled = !history.hasNextPage;
        el('institutionHistoryPage').textContent =
            t('Page ', 'পৃষ্ঠা ') + history.page + t(' of ', ' / ') + Math.max(1, history.totalPages) +
            ' · ' + history.totalCount + ' changes';
    }

    async function loadCampuses() {
        state.campuses = await api('/api/institution-onboarding/campus-list');
        const tbody = el('institutionCampusesBody');
        tbody.replaceChildren();
        if (!state.campuses.length) {
            const row = make('tr', '');
            const td = make('td', t('No campuses configured.', 'কোনো ক্যাম্পাস নেই।'));
            td.colSpan = 5; row.append(td); tbody.append(row);
        }
        for (const campus of state.campuses) {
            const row = make('tr', '');
            const name = make('td', campus.name);
            name.append(make('div', campus.address || '', 'small text-muted'));
            row.append(name, make('td', campus.code),
                make('td', campus.email || campus.phone || '—'),
                make('td', (campus.isHeadOffice ? t('Head Office', 'প্রধান ক্যাম্পাস') : t('Campus', 'ক্যাম্পাস')) +
                    (campus.isOnlineCampus ? ' · Online' : '') +
                    (campus.timeZoneId ? ' · ' + campus.timeZoneId : '')));
            const actions = make('td', '');
            const edit = make('button', 'Edit', 'btn btn-sm btn-outline-primary me-2');
            edit.type = 'button';
            edit.addEventListener('click', () => editCampus(campus));
            const archive = make('button', 'Archive', 'btn btn-sm btn-outline-danger');
            archive.type = 'button';
            archive.addEventListener('click', () => archiveCampus(campus, archive));
            actions.append(edit, archive); row.append(actions); tbody.append(row);
        }
        el('institutionCampusesCount').textContent = state.campuses.length + t(' campuses shown', ' টি ক্যাম্পাস');
    }

    function resetCampusEditor() {
        el('institutionCampusForm').reset();
        state.editing = null;
        el('campusEditor').classList.add('d-none');
    }

    function editCampus(campus = null) {
        state.editing = campus;
        el('institutionCampusForm').reset();
        set('campusName', campus?.name);
        set('campusCode', campus?.code);
        set('campusPhone', campus?.phone);
        set('campusEmail', campus?.email);
        set('campusHeadName', campus?.headName);
        set('campusAddress', campus?.address);
        set('campusTimeZone', campus?.timeZoneId);
        el('campusOnline').checked = campus?.isOnlineCampus === true;
        el('campusHeadOffice').checked = campus?.isHeadOffice === true || state.campuses.length === 0;
        el('campusHeadOffice').disabled = campus?.isHeadOffice === true || state.campuses.length === 0;
        el('campusEditorHeading').textContent = campus ? t('Edit campus', 'ক্যাম্পাস সম্পাদনা') : t('Add campus', 'ক্যাম্পাস যোগ করুন');
        el('campusEditor').classList.remove('d-none');
        el('campusEditor').scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    async function archiveCampus(campus, button) {
        button.disabled = true;
        try {
            const preview = await api('/api/institution-onboarding/campus/' +
                encodeURIComponent(campus.id) + '/archive-impact');
            if (!preview.canArchive) {
                const blockers = Array.isArray(preview.blockers) ? preview.blockers : [];
                notify(t('Campus cannot be archived: ', 'ক্যাম্পাস আর্কাইভ করা যাবে না: ') + blockers.join(' '), true);
                return;
            }
            if (!confirm(t('Archive ', 'আর্কাইভ করবেন: ') + campus.name + t('? This action preserves history but removes the campus from active use.', '? ইতিহাস থাকবে, কিন্তু সক্রিয় ব্যবহার থেকে সরবে।'))) return;
            await api('/api/institution-onboarding/campus/' + encodeURIComponent(campus.id) +
                '?rowVersion=' + encodeURIComponent(campus.rowVersion), { method: 'DELETE' });
            if (state.editing?.id === campus.id) resetCampusEditor();
            await loadCampuses();
            notify(t('Campus archived.', 'ক্যাম্পাস আর্কাইভ করা হয়েছে।'));
        } catch (error) { notify(error.message, true); }
        finally { button.disabled = false; }
    }

    async function submit(button, operation) {
        button.disabled = true;
        try { await operation(); }
        catch (error) { notify(error.message, true); }
        finally { button.disabled = false; }
    }

    async function saveProfile(event) {
        event.preventDefault();
        await submit(event.submitter, async () => {
            const current = state.profile;
            if (!current) throw new Error('Reload the institution profile first.');
            const result = await api('/api/tenant-profile', {
                method: 'PUT', body: JSON.stringify({
                    name: value('institutionName'), email: current.email,
                    institutionTypeDefinitionId: current.institutionTypeDefinitionId,
                    phone: value('institutionPhone') || null,
                    address: value('institutionAddress') || null,
                    countryCode: value('institutionCountry').toUpperCase(),
                    rowVersion: current.rowVersion
                })
            });
            setProfile(result);
            await loadHistory(1);
            notify(t('Institution profile updated.', 'প্রতিষ্ঠানের প্রোফাইল সংরক্ষিত হয়েছে।'));
        });
    }

    async function saveRegion(event) {
        event.preventDefault();
        await submit(event.submitter, async () => {
            if (!state.profile) throw new Error('Reload the institution profile first.');
            const result = await api('/api/tenant-profile/regional-settings', {
                method: 'PUT', body: JSON.stringify({
                    currencyCode: value('institutionCurrency').toUpperCase(),
                    timeZoneId: value('institutionTimezone'),
                    defaultLanguage: value('institutionLanguage'),
                    dateFormat: value('institutionDateFormat'),
                    rowVersion: state.profile.rowVersion
                })
            });
            setProfile(result);
            await loadHistory(1);
            notify(t('Regional settings saved.', 'আঞ্চলিক সেটিংস সংরক্ষিত হয়েছে।'));
        });
    }

    async function saveSubdomain(event) {
        event.preventDefault();
        await submit(event.submitter, async () => {
            if (!state.profile) throw new Error('Reload the institution profile first.');
            const subdomain = value('institutionSubdomain').toLowerCase();
            if (subdomain !== state.profile.subdomain) {
                if (!confirm(t('Changing the subdomain may affect bookmarks and integrations. Continue?', 'সাবডোমেইন বদলালে আগের বুকমার্ক ও ইন্টিগ্রেশন প্রভাবিত হতে পারে। এগোবেন?'))) return;
                const available = await api('/api/tenant-profile/subdomain/check?subdomain=' +
                    encodeURIComponent(subdomain));
                if (!available?.isAvailable) throw new Error(available?.reason || 'Subdomain is unavailable.');
            }
            const result = await api('/api/tenant-profile/subdomain', {
                method: 'PUT', body: JSON.stringify({ subdomain, rowVersion: state.profile.rowVersion })
            });
            setProfile(result);
            await loadHistory(1);
            notify(t('Subdomain updated.', 'সাবডোমেইন সংরক্ষিত হয়েছে।'));
        });
    }

    async function saveCampus(event) {
        event.preventDefault();
        await submit(event.submitter, async () => {
            const current = state.editing;
            await api('/api/institution-onboarding/campus', {
                method: 'POST', body: JSON.stringify({
                    id: current?.id || null,
                    rowVersion: current?.rowVersion || null,
                    name: value('campusName'), code: value('campusCode').toUpperCase(),
                    phone: value('campusPhone') || null, email: value('campusEmail') || null,
                    headName: value('campusHeadName') || null,
                    address: value('campusAddress') || null,
                    isHeadOffice: el('campusHeadOffice').checked,
                    isOnlineCampus: el('campusOnline').checked,
                    timeZoneId: value('campusTimeZone'),
                    isActive: true
                })
            });
            resetCampusEditor(); await loadCampuses();
            notify(t('Campus saved.', 'ক্যাম্পাস সংরক্ষিত হয়েছে।'));
        });
    }

    async function uploadBranding(type, button) {
        await submit(button, async () => {
            const field = el(type === 'logo' ? 'institutionLogoFile' : 'institutionFaviconFile');
            const file = field.files?.[0];
            if (!file) throw new Error('Select an image file.');
            if (file.size <= 0 || file.size > 5 * 1024 * 1024)
                throw new Error('Image size must be at most 5 MB.');
            const supported = type === 'logo'
                ? ['image/png', 'image/jpeg', 'image/webp']
                : ['image/png', 'image/jpeg'];
            if (!supported.includes(file.type)) throw new Error('Unsupported image type.');
            const data = new FormData();
            data.append('file', file);
            await api('/api/tenant-profile/' + type, { method: 'POST', body: data });
            field.value = '';
            await Promise.all([loadProfile(), loadHistory(1)]);
            notify(t('Institution ', 'প্রতিষ্ঠানের ') + type + t(' uploaded.', ' আপলোড হয়েছে।'));
        });
    }

    async function removeBranding(type, button) {
        if (!state.profile) return notify(t('Reload the profile first.', 'আগে প্রোফাইল আবার খুলুন।'), true);
        if (!confirm(t('Remove this institution ', 'প্রতিষ্ঠানের ') + type + t('?', ' সরাবেন?'))) return;
        await submit(button, async () => {
            await api('/api/tenant-profile/' + type + '?rowVersion=' +
                encodeURIComponent(state.profile.rowVersion), { method: 'DELETE' });
            await Promise.all([loadProfile(), loadHistory(1)]);
            notify(t('Institution ', 'প্রতিষ্ঠানের ') + type + t(' removed.', ' সরানো হয়েছে।'));
        });
    }

    function showTab(campuses) {
        el('institutionProfileSection').classList.toggle('d-none', campuses);
        el('institutionCampusSection').classList.toggle('d-none', !campuses);
        el('institutionProfileTab').classList.toggle('active', !campuses);
        el('institutionCampusTab').classList.toggle('active', campuses);
    }

    // Focused URLs select an initial workspace; the old Index keeps its tabs.
    showTab(document.getElementById('institutionManagementApp')?.dataset.institutionFeature === 'campuses');

    el('institutionProfileTab').addEventListener('click', () => showTab(false));
    el('institutionCampusTab').addEventListener('click', () => showTab(true));
    el('newCampus').addEventListener('click', () => editCampus());
    el('cancelCampus').addEventListener('click', resetCampusEditor);
    el('institutionProfileForm').addEventListener('submit', saveProfile);
    el('institutionRegionForm').addEventListener('submit', saveRegion);
    el('institutionDomainForm').addEventListener('submit', saveSubdomain);
    el('institutionCampusForm').addEventListener('submit', saveCampus);
    el('uploadInstitutionLogo').addEventListener('click', e => uploadBranding('logo', e.currentTarget));
    el('removeInstitutionLogo').addEventListener('click', e => removeBranding('logo', e.currentTarget));
    el('uploadInstitutionFavicon').addEventListener('click', e => uploadBranding('favicon', e.currentTarget));
    el('removeInstitutionFavicon').addEventListener('click', e => removeBranding('favicon', e.currentTarget));

    el('institutionHistoryPrevious').addEventListener('click', async () => {
        try { await loadHistory(Math.max(1, state.historyPage - 1)); }
        catch (error) { notify(error.message, true); }
    });
    el('institutionHistoryNext').addEventListener('click', async () => {
        try { await loadHistory(state.historyPage + 1); }
        catch (error) { notify(error.message, true); }
    });

    (async () => {
        try { await Promise.all([loadProfile(), loadCampuses(), loadHistory(1)]); }
        catch (error) { notify(error.message, true); }
    })();
})();
