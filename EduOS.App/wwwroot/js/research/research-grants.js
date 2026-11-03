(() => {
    'use strict';
    const root = document.getElementById('researchGrantWorkspace');
    if (!root) return;
    const get = id => document.getElementById(id);
    const bn = () => document.documentElement.lang.toLowerCase().startsWith('bn');
    const t = (en, bd) => bn() ? bd : en;
    root.querySelectorAll('[data-g-en]').forEach(node => { node.textContent = bn() ? node.dataset.gBn : node.dataset.gEn; });
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const tenant = document.body.dataset.authTenantId;
    const session = document.body.dataset.authSessionReference;
    let page = 1, total = 0, selected = null, listGeneration = 0, detailGeneration = 0, busy = false;
    let createRetry = null, actionRetry = null, schemeRetry = null;
    const names = {
        submit: ['Submit application', 'আবেদন জমা'], award: ['Approve award', 'অনুদান অনুমোদন'],
        reject: ['Reject application', 'আবেদন প্রত্যাখ্যান'], 'request-amend': ['Request budget change', 'বাজেট পরিবর্তন অনুরোধ'],
        'approve-amend': ['Approve budget change', 'বাজেট পরিবর্তন অনুমোদন'],
        'reject-amend': ['Reject budget change', 'বাজেট পরিবর্তন প্রত্যাখ্যান'],
        'milestone-add': ['Add milestone', 'মাইলস্টোন যোগ'], 'milestone-complete': ['Complete milestone', 'মাইলস্টোন সম্পন্ন'],
        'request-close': ['Request closure', 'সমাপ্তির অনুরোধ'], close: ['Approve closure', 'সমাপ্তি অনুমোদন'],
        withdraw: ['Withdraw draft', 'আবেদন প্রত্যাহার']
    };
    function sessionValid() {
        return Boolean(tenant && session && tenant === document.body.dataset.authTenantId &&
            session === document.body.dataset.authSessionReference);
    }
    function clearDetail() {
        detailGeneration++; selected = null; actionRetry = null;
        get('grantDetail').classList.add('d-none');
        get('grantSummary').replaceChildren(); get('grantMilestones').replaceChildren();
        get('grantHistory').replaceChildren(); get('grantAction').replaceChildren();
    }
    async function api(url, data) {
        if (!sessionValid()) { clearDetail(); throw Error(t('Session changed; sign in again.', 'সেশন বদলেছে; আবার লগইন করুন।')); }
        if (typeof window.EduOSVerifySessionContext === 'function') await window.EduOSVerifySessionContext();
        const res = await fetch(url, {
            method: data === undefined ? 'GET' : 'POST', credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json', ...(data === undefined ? {} :
                { 'Content-Type': 'application/json', RequestVerificationToken: token }) },
            ...(data === undefined ? {} : { body: JSON.stringify(data) })
        });
        if (!sessionValid() || res.headers.get('X-EduOS-Tenant-Id') !== tenant ||
            res.headers.get('X-EduOS-Session-Reference') !== session) {
            clearDetail(); throw Error(t('Session verification failed.', 'সেশন যাচাই ব্যর্থ।'));
        }
        const body = await res.json().catch(() => null);
        if (!res.ok || body?.success !== true) {
            const err = Error(res.status === 409 ? t('Record changed; refresh.', 'রেকর্ড বদলেছে; রিফ্রেশ করুন।') :
                res.status === 403 || res.status === 404 ? t('Access unavailable.', 'অধিকার নেই।') :
                    body?.message || t('Request failed.', 'অনুরোধ ব্যর্থ।'));
            err.status = res.status; throw err;
        }
        return body.data;
    }
    function notice(message, error = false) {
        get('grantNotice').className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        get('grantNotice').textContent = message;
    }
    function option(target, value, title) {
        const item = document.createElement('option'); item.value = String(value); item.textContent = title;
        target.appendChild(item);
    }
    function textRow(target, values) {
        const row = document.createElement('div'); row.className = 'py-1 border-bottom';
        row.textContent = values.filter(x => x !== null && x !== undefined).join(' · ');
        target.appendChild(row);
    }
    function controls() {
        get('grantCreateButton').disabled = busy || !get('grantScheme').value;
        get('grantRefresh').disabled = busy;
        get('grantPrevious').disabled = busy || page === 1;
        get('grantNext').disabled = busy || page * 20 >= total;
        get('grantConfirm').disabled = busy || !selected || !get('grantAction').value;
    }
    async function schemes() {
        try {
            const list = await api('/api/research/grants/schemes');
            const select = get('grantScheme'); select.replaceChildren();
            option(select, '', t('Choose scheme', 'স্কিম নির্বাচন'));
            list.forEach(s => option(select, s.id, `${s.name} (${s.currencyCode}, max ${s.maxAward})`));
            controls();
        } catch (e) { notice(e.message, true); }
    }
    async function loadList() {
        clearDetail(); const generation = ++listGeneration;
        get('grantRows').replaceChildren();
        try {
            const data = await api('/api/research/grants?page=' + page + '&pageSize=20');
            if (generation !== listGeneration || !sessionValid()) return;
            total = data.totalCount;
            data.items.forEach(item => {
                const tr = document.createElement('tr');
                for (const value of [item.title, item.stateCode, `${item.currencyCode} ${item.approvedAmount ?? item.requestedAmount}`]) {
                    const td = document.createElement('td'); td.textContent = value; tr.appendChild(td);
                }
                const td = document.createElement('td'); const btn = document.createElement('button');
                btn.className = 'btn btn-sm btn-outline-primary'; btn.type = 'button';
                btn.textContent = t('Open', 'খুলুন'); btn.addEventListener('click', () => open(item.reference));
                td.appendChild(btn); tr.appendChild(td); get('grantRows').appendChild(tr);
            });
            if (!data.items.length) textRow(get('grantRows'), [t('No grants available.', 'কোনো অনুদান নেই।')]);
            get('grantPage').textContent = `${page} / ${Math.max(1, Math.ceil(total / 20))}`;
        } catch (e) { if (generation === listGeneration) notice(e.message, true); }
        finally { controls(); }
    }
    async function open(reference) {
        clearDetail(); const generation = ++detailGeneration;
        try {
            const data = await api('/api/research/grants/' + encodeURIComponent(reference));
            if (generation !== detailGeneration || !sessionValid()) return;
            selected = data;
            get('grantDetail').classList.remove('d-none');
            get('grantSummary').textContent = `${data.title} · ${data.stateCode} · ${data.currencyCode} ${data.approvedAmount ?? data.requestedAmount} · ${data.reference}`;
            const actions = get('grantAction'); actions.replaceChildren(); option(actions, '', t('Choose action', 'কার্যক্রম নির্বাচন'));
            data.allowedActions.forEach(a => option(actions, a, t(...(names[a] || [a, a]))));
            changeAction();
            const [milestones, history] = await Promise.all([
                api('/api/research/grants/' + encodeURIComponent(reference) + '/milestones'),
                api('/api/research/grants/' + encodeURIComponent(reference) + '/history?page=1&pageSize=20')
            ]);
            if (generation !== detailGeneration || !sessionValid()) return;
            const list = get('grantMilestones'); list.replaceChildren();
            const target = get('grantMilestoneId'); target.replaceChildren();
            option(target, '', t('Choose milestone', 'মাইলস্টোন নির্বাচন'));
            milestones.forEach(m => {
                textRow(list, [m.title, String(m.dueDate).slice(0, 10), m.completedAtUtc ? t('Done', 'সম্পন্ন') : t('Pending', 'অপেক্ষমাণ'), m.outcome]);
                if (!m.completedAtUtc) option(target, m.id, m.title);
            });
            if (!milestones.length) textRow(list, [t('No milestones.', 'মাইলস্টোন নেই।')]);
            const audit = get('grantHistory'); audit.replaceChildren();
            history.items.forEach(h => textRow(audit, [h.actionCode, h.fromState + ' → ' + h.toState, h.reason, String(h.occurredAtUtc).slice(0, 16)]));
        } catch (e) { if (generation === detailGeneration) { clearDetail(); notice(e.message, true); } }
        finally { controls(); }
    }
    function changeAction() {
        actionRetry = null;
        const a = get('grantAction').value;
        get('grantAmountBox').classList.toggle('d-none', !['award', 'request-amend'].includes(a));
        get('grantMilestoneBox').classList.toggle('d-none', a !== 'milestone-add');
        get('grantOutcomeBox').classList.toggle('d-none', a !== 'milestone-complete');
        controls();
    }
    get('grantAction').addEventListener('change', changeAction);
    get('grantRefresh').addEventListener('click', () => { page = 1; loadList(); });
    get('grantPrevious').addEventListener('click', () => { if (page > 1) { page--; loadList(); } });
    get('grantNext').addEventListener('click', () => { if (page * 20 < total) { page++; loadList(); } });
    get('grantCreateForm').addEventListener('input', () => { createRetry = null; });
    get('grantCreateForm').addEventListener('change', () => { createRetry = null; });
    get('grantCreateForm').addEventListener('submit', async event => {
        event.preventDefault(); if (busy) return;
        const body = {
            clientRequestId: createRetry ||= crypto.randomUUID(),
            proposalReference: get('grantProposal').value.trim(), schemeId: Number(get('grantScheme').value),
            title: get('grantTitle').value.trim(), requestedAmount: Number(get('grantRequested').value)
        };
        busy = true; controls();
        try {
            const created = await api('/api/research/grants', body);
            createRetry = null; get('grantCreateForm').reset(); page = 1; await loadList();
            await open(created.reference); notice(t('Grant draft saved.', 'অনুদানের খসড়া সংরক্ষিত।'));
        } catch (e) { notice(e.message, true); }
        finally { busy = false; controls(); }
    });
    get('grantActionForm').addEventListener('input', () => { actionRetry = null; });
    get('grantActionForm').addEventListener('submit', async event => {
        event.preventDefault(); if (busy || !selected) return;
        const reference = selected.reference, action = get('grantAction').value;
        if (!action || !window.confirm(t('Confirm this grant action?', 'অনুদানের কার্যক্রম নিশ্চিত করবেন?'))) return;
        const body = {
            clientRequestId: actionRetry ||= crypto.randomUUID(), rowVersion: selected.rowVersion,
            amount: ['award', 'request-amend'].includes(action) ? Number(get('grantAmount').value) : null,
            milestoneTitle: action === 'milestone-add' ? get('grantMilestoneTitle').value.trim() : null,
            dueDate: action === 'milestone-add' && get('grantDueDate').value ? get('grantDueDate').value + 'T00:00:00Z' : null,
            milestoneId: action === 'milestone-complete' ? Number(get('grantMilestoneId').value) : null,
            outcome: action === 'milestone-complete' ? get('grantOutcome').value.trim() : null,
            reason: get('grantReason').value.trim()
        };
        busy = true; controls();
        try {
            await api('/api/research/grants/' + encodeURIComponent(reference) + '/' + encodeURIComponent(action), body);
            actionRetry = null; await loadList(); await open(reference);
            notice(t('Grant action completed.', 'অনুদানের কার্যক্রম সম্পন্ন।'));
        } catch (e) { if (e.status === 409 || e.status === 403 || e.status === 404) clearDetail(); notice(e.message, true); }
        finally { busy = false; controls(); }
    });
    get('grantSchemeForm')?.addEventListener('input', () => { schemeRetry = null; });
    get('grantSchemeForm')?.addEventListener('submit', async event => {
        event.preventDefault(); if (busy) return;
        busy = true; controls();
        try {
            await api('/api/research/grants/schemes', {
                clientRequestId: schemeRetry ||= crypto.randomUUID(),
                code: get('grantSchemeCode').value.trim().toUpperCase(), name: get('grantSchemeName').value.trim(),
                currencyCode: get('grantCurrency').value.trim().toUpperCase(), maxAward: Number(get('grantSchemeMax').value)
            });
            schemeRetry = null; get('grantSchemeForm').reset(); await schemes(); notice(t('Scheme created.', 'স্কিম তৈরি হয়েছে।'));
        } catch (e) { notice(e.message, true); }
        finally { busy = false; controls(); }
    });
    schemes(); loadList();
})();
