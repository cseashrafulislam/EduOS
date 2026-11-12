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
    const selected = () => (state.preview?.modules || []).filter(x => state.selected.has(x.code));
    const validSelection = () => selected().length > 0 && selected().every(m =>
        m.supportsSafeApply && m.isAvailable && m.entries.every(e => e.status !== 'Conflict'));
    function updateSubmit() {
        const count = selected().length;
        el('smartSetupSelection').textContent = count
            ? tr(count + ' module(s) selected', count + 'টি মডিউল নির্বাচিত')
            : tr('Select a module with missing safe defaults', 'অনুপস্থিত Safe Default আছে এমন মডিউল নির্বাচন করুন');
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
        el('smartSetupVersion').textContent =
            p.institutionTypeCode + ' · ' + p.countryCode + ' · ' +
            tr('Template version ', 'টেমপ্লেট সংস্করণ ') + p.templateVersion;
        const host = el('smartSetupModules');
        host.replaceChildren();
        const requested = new URLSearchParams(window.location.search).get('module')?.toUpperCase();
        const ordered = p.modules.slice().sort((a, b) =>
            (b.code === requested ? 1 : 0) - (a.code === requested ? 1 : 0));
        const checks = [...state.readiness.values()].filter(x => x.isAvailable && x.hasChecklist);
        const completed = checks.filter(x => x.prerequisitesConfigured).length;
        el('smartSetupReadinessSummary').textContent = tr(
            completed + ' / ' + checks.length + ' enabled modules have their checked prerequisites. This is not an operational or financial approval.',
            checks.length + 'টি চালু মডিউলের মধ্যে ' + completed + 'টির যাচাইকৃত প্রাথমিক সেটআপ আছে। এটি পরিচালন বা আর্থিক অনুমোদন নয়।');
        for (const m of ordered) {
            const wrapper = node('div', 'col-12 col-xl-6');
            const card = node('section', 'card h-100 border-0 shadow-sm' +
                (m.code === requested ? ' border border-primary' : ''));
            const body = node('div', 'card-body');
            const top = node('div', 'd-flex align-items-start justify-content-between gap-2 mb-2');
            const heading = node('div');
            heading.append(node('h3', 'h6 fw-bold mb-1', bn ? m.nameBangla : m.name));
            heading.append(node('span', 'small text-muted', m.code));
            top.append(heading);
            const badge = node('span', 'badge ' + (m.isAvailable ? 'bg-primary' : 'bg-secondary'),
                !m.isAvailable ? tr('Unavailable', 'চালু নেই') :
                m.supportsSafeApply ? tr('Safe defaults', 'নিরাপদ ডিফল্ট') :
                    tr('Guided setup', 'নির্দেশিত সেটআপ'));
            top.append(badge);
            body.append(top);
            const guidance = node('p', 'small text-muted', m.guidance || '');
            body.append(guidance);
            const readiness = state.readiness.get(m.code);
            if (readiness?.isAvailable && readiness.requirements?.length) {
                const tasks = readiness.requirements;
                const passed = tasks.filter(x => x.configured).length;
                const headline = node('p', 'fw-semibold small mb-1',
                    tr('Required setup: ' + passed + ' / ' + tasks.length + ' configured',
                        'প্রাথমিক সেটআপ: ' + passed + ' / ' + tasks.length + ' সম্পন্ন'));
                body.append(headline);
                const list = node('ul', 'list-unstyled small mb-3');
                for (const task of tasks) {
                    const li = node('li', 'd-flex gap-2 align-items-start py-1');
                    const status = node('span', task.configured ? 'text-success' : 'text-warning-emphasis',
                        task.configured ? '✓' : '○');
                    const label = node('span', 'flex-grow-1', bn ? task.labelBangla : task.label);
                    li.append(status, label);
                    if (!task.configured) {
                        const link = node('a', 'small text-nowrap', tr('Configure', 'সেট করুন'));
                        link.href = setupLink(task.actionUrl, m.code);
                        link.target = '_blank';
                        link.rel = 'noopener noreferrer';
                        li.append(link);
                    }
                    list.append(li);
                }
                body.append(list);
            } else if (readiness?.isAvailable && !readiness.hasChecklist) {
                body.append(node('p', 'small text-muted',
                    tr('No mandatory reference setup; review applicable policies before operating.',
                        'বাধ্যতামূলক প্রাথমিক তথ্য নেই; কাজের আগে প্রযোজ্য নীতিমালা যাচাই করুন।')));
            }
            if (m.entries?.length) {
                const list = node('ul', 'list-unstyled small mb-3');
                for (const item of m.entries) {
                    const li = node('li', 'd-flex justify-content-between border-bottom py-1 gap-2');
                    li.append(node('span', '', item.name + ' (' + item.code + ')'));
                    const status = item.status === 'Missing' ? tr('To add', 'যোগ হবে') :
                        item.status === 'Existing' ? tr('Already exists', 'আগেই আছে') :
                        item.status === 'Conflict' ? tr('Review conflict', 'দ্বন্দ্ব যাচাই') :
                            tr('Module unavailable', 'মডিউল চালু নেই');
                    li.append(node('span', 'fw-semibold text-nowrap', status));
                    list.append(li);
                }
                body.append(list);
            }
            const missing = (m.entries || []).filter(e => e.status === 'Missing').length;
            const conflict = (m.entries || []).some(e => e.status === 'Conflict');
            if (m.supportsSafeApply && m.isAvailable) {
                const label = node('label', 'form-check mb-2');
                const input = node('input', 'form-check-input me-2');
                input.type = 'checkbox';
                input.checked = state.selected.has(m.code);
                input.disabled = conflict || missing === 0;
                input.addEventListener('change', () => {
                    input.checked ? state.selected.add(m.code) : state.selected.delete(m.code);
                    state.requestId = null;
                    state.fingerprint = null;
                    updateSubmit();
                });
                label.append(input, node('span', 'form-check-label',
                    conflict ? tr('Resolve conflicts first', 'আগে দ্বন্দ্ব দূর করুন') :
                    missing ? tr('Apply ' + missing + ' missing item(s)', missing + 'টি তথ্য যোগ করুন') :
                    tr('Already configured', 'সেটআপ আগে থেকেই আছে')));
                body.append(label);
            }
            const link = node('a', 'btn btn-sm btn-outline-secondary', tr('Open setup page', 'সেটআপ পেজ খুলুন'));
            link.href = setupLink(m.nextStepUrl, m.code);
            link.target = '_blank';
            link.rel = 'noopener noreferrer';
            body.append(link);
            card.append(body);
            wrapper.append(card);
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
        const names = selected().map(x => x.code).sort();
        const fingerprint = p.institutionTypeId + '|' + p.templateVersion + '|' + names.join(',');
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
                    confirmed: true
                })
            });
            if (!sameSession()) return;
            state.requestId = null;
            state.fingerprint = null;
            state.selected.clear();
            el('smartSetupConfirm').checked = false;
            message(tr('Saved ' + result.createdCount + ' new master record(s); ' +
                result.existingCount + ' already existed.',
                result.createdCount + 'টি নতুন তথ্য সংরক্ষিত; ' +
                result.existingCount + 'টি আগে থেকেই ছিল।'));
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
    el('smartSetupConfirm').addEventListener('change', updateSubmit);
    el('smartSetupForm').addEventListener('submit', apply);
    load();
})();
