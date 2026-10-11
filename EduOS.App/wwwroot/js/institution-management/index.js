(() => {
    'use strict';
    const root = document.getElementById('institutionManagementApp');
    if (!root) return;
    const el = id => document.getElementById(id);
    const state = { profile: null, campuses: [], editing: null };
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

    async function loadCampuses() {
        state.campuses = await api('/api/institution-onboarding/campus-list');
        const tbody = el('institutionCampusesBody');
        tbody.replaceChildren();
        if (!state.campuses.length) {
            const row = make('tr', '');
            const td = make('td', 'No campuses configured.');
            td.colSpan = 5; row.append(td); tbody.append(row);
        }
        for (const campus of state.campuses) {
            const row = make('tr', '');
            const name = make('td', campus.name);
            name.append(make('div', campus.address || '', 'small text-muted'));
            row.append(name, make('td', campus.code),
                make('td', campus.email || campus.phone || '—'),
                make('td', campus.isHeadOffice ? 'Head Office' : 'Campus'));
            const actions = make('td', '');
            const edit = make('button', 'Edit', 'btn btn-sm btn-outline-primary me-2');
            edit.type = 'button';
            edit.addEventListener('click', () => editCampus(campus));
            const archive = make('button', 'Archive', 'btn btn-sm btn-outline-danger');
            archive.type = 'button';
            archive.addEventListener('click', () => archiveCampus(campus, archive));
            actions.append(edit, archive); row.append(actions); tbody.append(row);
        }
        el('institutionCampusesCount').textContent = state.campuses.length + ' campuses shown';
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
        el('campusHeadOffice').checked = campus?.isHeadOffice === true || state.campuses.length === 0;
        el('campusHeadOffice').disabled = campus?.isHeadOffice === true || state.campuses.length === 0;
        el('campusEditorHeading').textContent = campus ? 'Edit campus' : 'Add campus';
        el('campusEditor').classList.remove('d-none');
        el('campusEditor').scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    async function archiveCampus(campus, button) {
        if (!confirm('Archive ' + campus.name + '? Campus data or assigned users may prevent archiving.')) return;
        button.disabled = true;
        try {
            await api('/api/institution-onboarding/campus/' + encodeURIComponent(campus.id) +
                '?rowVersion=' + encodeURIComponent(campus.rowVersion), { method: 'DELETE' });
            if (state.editing?.id === campus.id) resetCampusEditor();
            await loadCampuses();
            notify('Campus archived.');
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
            notify('Institution profile updated.');
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
                    rowVersion: state.profile.rowVersion
                })
            });
            setProfile(result);
            notify('Regional settings saved.');
        });
    }

    async function saveSubdomain(event) {
        event.preventDefault();
        await submit(event.submitter, async () => {
            if (!state.profile) throw new Error('Reload the institution profile first.');
            const subdomain = value('institutionSubdomain').toLowerCase();
            if (subdomain !== state.profile.subdomain) {
                if (!confirm('Changing the subdomain may affect bookmarks and integrations. Continue?')) return;
                const available = await api('/api/tenant-profile/subdomain/check?subdomain=' +
                    encodeURIComponent(subdomain));
                if (!available?.isAvailable) throw new Error(available?.reason || 'Subdomain is unavailable.');
            }
            const result = await api('/api/tenant-profile/subdomain', {
                method: 'PUT', body: JSON.stringify({ subdomain, rowVersion: state.profile.rowVersion })
            });
            setProfile(result);
            notify('Subdomain updated.');
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
                    isActive: true
                })
            });
            resetCampusEditor(); await loadCampuses();
            notify('Campus saved.');
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
            await loadProfile();
            notify('Institution ' + type + ' uploaded.');
        });
    }

    async function removeBranding(type, button) {
        if (!state.profile) return notify('Reload the profile first.', true);
        if (!confirm('Remove this institution ' + type + '?')) return;
        await submit(button, async () => {
            await api('/api/tenant-profile/' + type + '?rowVersion=' +
                encodeURIComponent(state.profile.rowVersion), { method: 'DELETE' });
            await loadProfile();
            notify('Institution ' + type + ' removed.');
        });
    }

    function showTab(campuses) {
        el('institutionProfileSection').classList.toggle('d-none', campuses);
        el('institutionCampusSection').classList.toggle('d-none', !campuses);
        el('institutionProfileTab').classList.toggle('active', !campuses);
        el('institutionCampusTab').classList.toggle('active', campuses);
    }

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
    el('reloadInstitution').addEventListener('click', async () => {
        try { await Promise.all([loadProfile(), loadCampuses()]); notify('Institution data refreshed.'); }
        catch (error) { notify(error.message, true); }
    });

    (async () => {
        try { await Promise.all([loadProfile(), loadCampuses()]); }
        catch (error) { notify(error.message, true); }
    })();
})();
