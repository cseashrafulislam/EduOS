(() => {
    'use strict';
    const root = document.getElementById('smartSetupWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const bn = root.dataset.language === 'bn-BD';
    const tr = (en, bd) => bn ? bd : en;
    const identity = [document.body?.dataset.authTenantId || '',
        document.body?.dataset.authSessionReference || ''];
    const sameSession = () => identity[0] && identity[1] &&
        identity[0] === (document.body?.dataset.authTenantId || '') &&
        identity[1] === (document.body?.dataset.authSessionReference || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { preview: null, selected: new Set(), busy: false, revision: 0,
        requestId: null, fingerprint: null, readiness: new Map() };
    const message = (value, error = false) => {
        const target = el('smartSetupNotice');
        target.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        target.textContent = value;
        target.focus();
    };
    const clearMessage = () => {
        el('smartSetupNotice').textContent = '';
        el('smartSetupNotice').className = 'alert d-none';
    };
    const node = (tag, className, label) => {
        const x = document.createElement(tag);
        if (className) x.className = className;
        if (label != null) x.textContent = label;
        return x;
    };
    const request = async (url, init = {}) => {
        const res = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store', ...init,
            headers: {
                Accept: 'application/json',
                ...(init.body ? { 'Content-Type': 'application/json' } : {}),
                ...(init.method === 'POST' ? { RequestVerificationToken: token } : {}),
                ...(init.headers || {})
            }
        });
        const body = await res.json().catch(() => null);
        if (!res.ok || body?.success !== true)
            throw new Error(body?.message || tr('Operation failed. Refresh and retry.', 'অনুরোধ ব্যর্থ হয়েছে। আবার চেষ্টা করুন।'));
        return body.data;
    };
    const guid = () => {
        if (crypto.randomUUID) return crypto.randomUUID();
        const b = new Uint8Array(16);
        crypto.getRandomValues(b);
        b[6] = (b[6] & 15) | 64;
        b[8] = (b[8] & 63) | 128;
        const h = [...b].map(x => x.toString(16).padStart(2, '0')).join('');
        return [h.slice(0,8),h.slice(8,12),h.slice(12,16),h.slice(16,20),h.slice(20)].join('-');
    };
    const safePath = value => typeof value === 'string' && /^\/[a-z0-9][a-z0-9/\-]*$/i.test(value)
        ? value : '/TenantSettings/Index';
    const setupLink = (path, module) => {
        const url = new URL(safePath(path), window.location.origin);
        url.searchParams.set('smartSetup', module);
        return url.pathname + url.search;
    };
    // Embedded Settings are the original, tenant-authorized pages. No duplicate
    // create/edit/delete API or shadow master table exists in Smart Setup.
    const openEmbeddedSetup = (module, path, caption) => {
        if (!sameSession() || !state.preview?.modules.some(m => m.code === module && m.isAvailable)) return;
        const destination = safePath(path);
        if (destination === '/TenantSettings/Index' && path !== destination) return;
        if (destination.toLowerCase().startsWith('/smartsetup/')) return;
        const panel = el('smartSetupInline');
        const frame = el('smartSetupFrame');
        if (!panel || !frame) return;
        const address = new URL(destination, window.location.origin);
        address.searchParams.set('smartSetupEmbedded', '1');
        el('smartSetupInlineTitle').textContent = caption;
        panel.hidden = false;
        frame.src = address.pathname + address.search;
        panel.scrollIntoView({ behavior: 'smooth', block: 'start' });
    };
    const closeEmbeddedSetup = () => {
        const panel = el('smartSetupInline'), frame = el('smartSetupFrame');
        if (!panel || !frame) return;
        panel.hidden = true;
        frame.removeAttribute('src');
    };
    const entryKey = (module, entry) => module + '|' + entry.kind + '|' + entry.code;
    const selectedRows = () => (state.preview?.modules || []).flatMap(m =>
        !m.isAvailable || !m.supportsSafeApply ? [] : (m.entries || [])
            .filter(e => e.status === 'Missing' && state.selected.has(entryKey(m.code, e)))
            .map(e => ({ module: m.code, kind: e.kind, code: e.code })));
    const validSelection = () => selectedRows().length > 0 && !!state.preview?.institutionTypeId;
    function updateSubmit() {
        const count = selectedRows().length;
        el('smartSetupSelection').textContent = count
            ? tr(count + ' checked record(s) will be saved', count + 'টি চিহ্নিত তথ্য সংরক্ষিত হবে')
            : tr('Tick the records you want to create', 'যে তথ্যগুলো সংরক্ষণ করবেন, সেগুলোতে টিক দিন');
        el('smartSetupApply').disabled = state.busy || !sameSession() || !validSelection() ||
            !el('smartSetupConfirm').checked;
        el('smartSetupReload').disabled = state.busy || !sameSession();
        for (const cb of el('smartSetupModules').querySelectorAll('input[type=checkbox]'))
            cb.disabled = state.busy || !sameSession();
    }
    function render() {
        const p = state.preview;
        if (!p || !sameSession()) return;
        el('smartSetupInstitution').textContent = p.institutionTypeName;
        el('smartSetupVersion').textContent = p.institutionTypeCode + ' · ' + p.countryCode + ' · ' +
            tr('Template version ', 'টেমপ্লেট সংস্করণ ') + p.templateVersion;
        const host = el('smartSetupModules');
        host.replaceChildren();
        const requested = new URLSearchParams(window.location.search).get('module')?.toUpperCase();
        const ordered = p.modules.slice().sort((a, b) =>
            (b.code === requested ? 1 : 0) - (a.code === requested ? 1 : 0));
        const allChecks = [...state.readiness.values()].filter(x => x.isAvailable && x.hasChecklist);
        const ready = allChecks.filter(x => x.prerequisitesConfigured).length;
        let missingTotal = 0, guidedTotal = 0, shown = 0;
        const makeField = (value, label) => {
            const field = node('input', 'form-control form-control-sm bg-light');
            field.type = 'text';
            field.value = value || '';
            field.readOnly = true;
            field.setAttribute('aria-label', label);
            field.title = value || '';
            return field;
        };
        for (const m of ordered) {
            if (!m.isAvailable) continue;
            const entries = (m.entries || []).filter(e => e.status === 'Missing');
            const conflicts = (m.entries || []).filter(e => e.status === 'Conflict');
            const readiness = state.readiness.get(m.code);
            const todo = readiness?.isAvailable
                ? (readiness.requirements || []).filter(x => !x.configured) : [];
            if (!entries.length && !conflicts.length && !todo.length) continue;
            missingTotal += entries.length;
            guidedTotal += todo.length;
            shown++;
            const wrapper = node('div', 'col-12');
            const card = node('section', 'card border-0 shadow-sm' +
                (requested === m.code ? ' border border-primary' : ''));
            const body = node('div', 'card-body');
            const header = node('div', 'd-flex flex-wrap align-items-center justify-content-between gap-2 mb-2');
            const heading = node('div');
            heading.append(node('h3', 'h6 fw-bold mb-1', bn ? m.nameBangla : m.name));
            heading.append(node('span', 'small text-muted', m.code));
            header.append(heading);
            if (entries.length)
                header.append(node('span', 'small text-muted',
                    tr(entries.length + ' pre-filled record(s)', entries.length + 'টি তথ্য আগে থেকে পূরণ করা')));
            body.append(header);
            if (entries.length) {
                const hasDetail = entries.some(item => !!item.detail);
                body.append(node('p', 'small text-muted mb-2',
                    tr('Review each automatically filled column, tick the rows you want, then save below. Values come from the approved institution template.',
                        'স্বয়ংক্রিয়ভাবে পূরণ করা তথ্যগুলো যাচাই করুন, প্রয়োজনীয় Row-তে টিক দিন, তারপর নিচে Save করুন।')));
                const responsive = node('div', 'table-responsive');
                const table = node('table', 'table table-sm table-bordered align-middle mb-2');
                const thead = node('thead', 'table-light');
                const headerRow = node('tr');
                const selectCell = node('th');
                selectCell.scope = 'col';
                const selectAll = node('input', 'form-check-input');
                selectAll.type = 'checkbox';
                selectAll.setAttribute('aria-label', tr('Select all missing records in ' + m.name,
                    m.nameBangla + ' এর সব অনুপস্থিত তথ্য নির্বাচন'));
                selectAll.setAttribute('data-smart-all', m.code);
                selectCell.append(selectAll, node('span', 'small ms-2', tr('All', 'সব')));
                headerRow.append(selectCell);
                for (const name of [tr('Type', 'ধরন'), tr('Code', 'কোড'), tr('Name', 'নাম'),
                    ...(hasDetail ? [tr('Detail', 'বিবরণ')] : [])]) {
                    const th = node('th', '', name);
                    th.scope = 'col';
                    headerRow.append(th);
                }
                thead.append(headerRow);
                const tbody = node('tbody');
                const boxes = [];
                const syncAll = () => {
                    const checked = boxes.filter(x => x.checked).length;
                    selectAll.checked = checked === boxes.length;
                    selectAll.indeterminate = checked > 0 && checked < boxes.length;
                };
                for (const item of entries) {
                    const key = entryKey(m.code, item);
                    const row = node('tr');
                    const checkCell = node('td');
                    const check = node('input', 'form-check-input');
                    check.type = 'checkbox';
                    check.checked = state.selected.has(key);
                    check.setAttribute('data-smart-row', key);
                    check.setAttribute('aria-label', tr('Save ' + item.name, item.name + ' সংরক্ষণ করুন'));
                    check.addEventListener('change', () => {
                        check.checked ? state.selected.add(key) : state.selected.delete(key);
                        state.requestId = null;
                        state.fingerprint = null;
                        syncAll();
                        updateSubmit();
                    });
                    checkCell.append(check);
                    row.append(checkCell);
                    const labels = {
                        Medium: tr('Medium', 'মাধ্যম'),
                        OrganizationUnit: tr('Department', 'বিভাগ'),
                        Designation: tr('Designation', 'পদবি'),
                        FeeHead: tr('Fee head', 'ফি-এর ধরন'),
                        AssetCategory: tr('Asset category', 'সম্পদের ধরন'),
                        BookCategory: tr('Book category', 'বইয়ের ধরন'),
                        NoticeCategory: tr('Notice category', 'নোটিশের ধরন'),
                        DocumentType: tr('Document type', 'ডকুমেন্টের ধরন')
                    };
                    row.append(node('td', 'small', labels[item.kind] || item.kind));
                    const columns = [
                        [item.code, tr('Code', 'কোড')],
                        [item.name, tr('Name', 'নাম')]
                    ];
                    if (hasDetail) columns.push([item.detail || '', tr('Detail', 'বিবরণ')]);
                    for (const [value, fieldName] of columns) {
                        const td = node('td');
                        td.append(makeField(value, fieldName + ': ' + item.name));
                        row.append(td);
                    }
                    tbody.append(row);
                    boxes.push(check);
                }
                selectAll.addEventListener('change', () => {
                    for (let i = 0; i < entries.length; i++) {
                        boxes[i].checked = selectAll.checked;
                        const key = entryKey(m.code, entries[i]);
                        selectAll.checked ? state.selected.add(key) : state.selected.delete(key);
                    }
                    state.requestId = null;
                    state.fingerprint = null;
                    syncAll();
                    updateSubmit();
                });
                syncAll();
                table.append(thead, tbody);
                responsive.append(table);
                body.append(responsive);
            }
            if (conflicts.length) {
                const warning = node('p', 'small text-danger mb-2',
                    tr(conflicts.length + ' conflicting existing record(s) need manual review; nothing will be overwritten.',
                        conflicts.length + 'টি বিদ্যমান তথ্যের দ্বন্দ্ব আছে; কোনো তথ্য পরিবর্তন হবে না।'));
                body.append(warning);
                const list = node('ul', 'small mb-3');
                for (const item of conflicts)
                    list.append(node('li', '', item.kind + ': ' + item.name + ' (' + item.code + ')'));
                body.append(list);
            }
            if (todo.length) {
                body.append(node('p', 'small fw-semibold mb-1',
                    tr('Institution-specific setup still needed:', 'প্রতিষ্ঠানের নিজস্ব তথ্য এখনো প্রয়োজন:')));
                const list = node('ul', 'list-unstyled small mb-2');
                const destinations = new Map();
                for (const task of todo) {
                    const li = node('li', 'small py-1', bn ? task.labelBangla : task.label);
                    list.append(li);
                    const url = safePath(task.actionUrl);
                    if (!destinations.has(url)) destinations.set(url, []);
                    destinations.get(url).push(task);
                }
                body.append(list);
                for (const [url, tasks] of destinations) {
                    const actions = node('div', 'd-flex flex-wrap align-items-center gap-2 mb-2');
                    const inlineButton = node('button', 'btn btn-sm btn-outline-primary',
                        tr('Add here (' + tasks.length + ' setup item(s))',
                            'এখানেই যোগ করুন (' + tasks.length + 'টি সেটিং)'));
                    inlineButton.type = 'button';
                    inlineButton.setAttribute('data-smart-setup-inline', m.code + ':' + tasks[0].code);
                    inlineButton.addEventListener('click', () =>
                        openEmbeddedSetup(m.code, url, bn ? m.nameBangla : m.name));
                    actions.append(inlineButton);
                    const link = node('a', 'small', tr('Full Settings', 'মূল সেটিংস'));
                    link.href = url;
                    link.target = '_blank';
                    link.rel = 'noopener noreferrer';
                    actions.append(link);
                    body.append(actions);
                }
            }
            if (m.code === 'FINANCE' && todo.length) {
                const opening = node('div', 'border-top pt-2 mt-2 small');
                opening.append(node('span', 'me-2',
                    tr('Verified opening balances (balanced journal + approval):',
                        'যাচাইকৃত Opening Balance (সমন্বিত জার্নাল ও অনুমোদন):')));
                const button = node('button', 'btn btn-sm btn-outline-primary',
                    tr('Enter opening journal here', 'এখানে Opening Journal দিন'));
                button.type = 'button';
                button.setAttribute('data-smart-setup-inline', 'FINANCE:OPENING_JOURNAL');
                button.addEventListener('click', () => openEmbeddedSetup('FINANCE',
                    '/AccountingJournals/Index', tr('Verified opening journal', 'যাচাইকৃত Opening Journal')));
                opening.append(button);
                body.append(opening);
            }
            if (m.nextStepUrl) {
                const manage = node('a', 'small', tr('Manage ' + m.name + ' in original settings',
                    m.nameBangla + ' এর মূল সেটিংস'));
                manage.href = safePath(m.nextStepUrl);
                manage.target = '_blank';
                manage.rel = 'noopener noreferrer';
                body.append(manage);
            }
            card.append(body);
            wrapper.append(card);
            host.append(wrapper);
        }
        el('smartSetupReadinessSummary').textContent = tr(
            missingTotal + ' pre-filled safe record(s) available; ' + guidedTotal +
            ' institution-specific prerequisite(s) need review. ' + ready + '/' + allChecks.length +
            ' checked module prerequisites configured. No financial or operational approval is implied.',
            missingTotal + 'টি প্রস্তুত তথ্য; ' + guidedTotal + 'টি প্রতিষ্ঠানের নিজস্ব সেটআপ বাকি। ' +
            allChecks.length + 'টির মধ্যে ' + ready + 'টি মডিউলের প্রাথমিক তথ্য সম্পন্ন।');
        if (!shown) {
            const wrapper = node('div', 'col-12');
            const complete = node('div', 'card card-body border-0 shadow-sm');
            complete.append(node('p', 'mb-0 text-success',
                tr('All reviewed safe defaults and checked setup prerequisites are present. No new records are needed.',
                    'যাচাইকৃত Safe Defaults ও প্রাথমিক তথ্য সম্পন্ন। নতুন কিছু যোগ করার প্রয়োজন নেই।')));
            wrapper.append(complete);
            host.append(wrapper);
        }
        updateSubmit();
    }
    async function load() {
        if (!sameSession() || state.busy) return;
        state.busy = true;
        const version = ++state.revision;
        updateSubmit();
        try {
            const [preview, readiness] = await Promise.all([
                request('/api/smart-setup/preview'),
                request('/api/smart-setup/readiness').catch(() => null)
            ]);
            if (version !== state.revision || !sameSession()) return;
            state.preview = preview;
            state.readiness = new Map((readiness || []).map(x => [x.moduleCode, x]));
            state.selected.clear();
            el('smartSetupConfirm').checked = false;
            clearMessage();
            if (!preview.institutionTypeId) {
                message(tr('Select an active institution type in Institution Profile before applying defaults.',
                    'প্রথমে প্রতিষ্ঠান প্রোফাইলে সক্রিয় প্রতিষ্ঠানের ধরন নির্বাচন করুন।'), true);
            }
            render();
        } catch (err) {
            if (version === state.revision && sameSession()) message(err.message, true);
        } finally {
            if (version === state.revision) { state.busy = false; updateSubmit(); }
        }
    }
    async function apply(event) {
        event.preventDefault();
        if (state.busy || !sameSession() || !validSelection() ||
            !el('smartSetupConfirm').checked || !state.preview?.institutionTypeId) return;
        const p = state.preview;
        const entries = selectedRows();
        const names = [...new Set(entries.map(x => x.module))].sort();
        const fingerprint = p.institutionTypeId + '|' + p.templateVersion + '|' +
            entries.map(x => entryKey(x.module, x)).sort().join(',');
        if (state.fingerprint !== fingerprint || !state.requestId) state.requestId = guid();
        state.fingerprint = fingerprint;
        state.busy = true;
        updateSubmit();
        try {
            const result = await request('/api/smart-setup/apply', {
                method: 'POST',
                body: JSON.stringify({
                    clientRequestId: state.requestId,
                    institutionTypeId: p.institutionTypeId,
                    templateVersion: p.templateVersion,
                    modules: names,
                    entries: entries,
                    confirmed: true
                })
            });
            if (!sameSession()) return;
            state.requestId = null;
            state.fingerprint = null;
            state.selected.clear();
            el('smartSetupConfirm').checked = false;
            const groups = new Map();
            for (const record of result.created || []) {
                const kind = record.kind || 'Master';
                groups.set(kind, (groups.get(kind) || 0) + 1);
            }
            const details = [...groups].map(([kind, count]) => kind + ': ' + count).join(' · ');
            message(tr('Saved ' + result.createdCount + ' new master record(s); ' +
                result.existingCount + ' already existed.',
                result.createdCount + 'টি নতুন তথ্য সংরক্ষিত; ' +
                result.existingCount + 'টি আগে থেকেই ছিল।') +
                (details ? ' — ' + details : ''));
            // Reload only the non-destructive preview. A successful POST remains
            // successful even if this follow-up GET fails.
            const [fresh, readiness] = await Promise.all([
                request('/api/smart-setup/preview').catch(() => null),
                request('/api/smart-setup/readiness').catch(() => null)
            ]);
            if (fresh && sameSession()) {
                state.preview = fresh;
                if (readiness) state.readiness = new Map(readiness.map(x => [x.moduleCode, x]));
                render();
            }
        } catch (err) {
            if (sameSession()) message(err.message, true);
        } finally {
            state.busy = false;
            updateSubmit();
        }
    }
    el('smartSetupReload').addEventListener('click', load);
    el('smartSetupSelectAll')?.addEventListener('click', () => {
        if (!sameSession() || state.busy || !state.preview) return;
        state.selected.clear();
        for (const m of state.preview.modules) {
            if (!m.isAvailable || !m.supportsSafeApply) continue;
            for (const entry of m.entries || []) {
                if (entry.status === 'Missing') state.selected.add(entryKey(m.code, entry));
            }
        }
        state.requestId = null;
        state.fingerprint = null;
        render();
    });
    el('smartSetupClearSelected')?.addEventListener('click', () => {
        if (!sameSession() || state.busy) return;
        state.selected.clear();
        state.requestId = null;
        state.fingerprint = null;
        el('smartSetupConfirm').checked = false;
        render();
    });
    el('smartSetupInlineClose')?.addEventListener('click', () => {
        closeEmbeddedSetup();
        load();
    });
    el('smartSetupInlineRefresh')?.addEventListener('click', load);
    el('smartSetupConfirm').addEventListener('change', updateSubmit);
    el('smartSetupForm').addEventListener('submit', apply);
    document.addEventListener('visibilitychange', () => {
        if (!sameSession()) closeEmbeddedSetup();
    });
    load();
})();
