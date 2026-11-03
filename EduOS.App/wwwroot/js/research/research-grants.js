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
    let outputPage = 1, outputTotal = 0, outputRetry = null, outputReview = null, reviewRetry = null;
    let outputGeneration = 0, reportGeneration = 0;
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
        outputGeneration++; reportGeneration++; outputPage = 1; outputTotal = 0;
        outputRetry = null; outputReview = null; reviewRetry = null;
        get('grantDetail').classList.add('d-none');
        get('grantSummary').replaceChildren(); get('grantMilestones').replaceChildren();
        get('grantHistory').replaceChildren(); get('grantAction').replaceChildren();
        get('grantOutputRows').replaceChildren(); get('grantOutputsPage').textContent = '';
        get('grantOutputForm').classList.add('d-none');
        get('grantOutputReviewForm').classList.add('d-none');
        get('grantOutcomeReportRows')?.replaceChildren();
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
        get('grantOutputSubmit').disabled = busy || !selected;
        get('grantOutputReviewConfirm').disabled = busy || !selected || !outputReview;
        get('grantOutputsPrev').disabled = busy || !selected || outputPage <= 1;
        get('grantOutputsNext').disabled = busy || !selected || outputPage * 20 >= outputTotal;
        if (get('grantOutcomeReportRun')) get('grantOutcomeReportRun').disabled = busy || !selected;
    }
    async function loadOutputs(reference, generation) {
        const current = ++outputGeneration;
        const data = await api('/api/research/grants/' + encodeURIComponent(reference) +
            '/outputs?page=' + outputPage + '&pageSize=20');
        if (generation !== detailGeneration || current !== outputGeneration || !sessionValid()) return;
        outputTotal = data.totalCount;
        const target = get('grantOutputRows'); target.replaceChildren();
        data.items.forEach(item => {
            const row = document.createElement('div'); row.className = 'py-2 border-bottom';
            const title = document.createElement('strong'); title.textContent = item.kindCode + ': ' + item.title;
            row.appendChild(title);
            textRow(row, [item.stateCode, String(item.occurredOn).slice(0, 10), item.summary]);
            if (item.referenceUrl) {
                try {
                    const uri = new URL(item.referenceUrl);
                    if (uri.protocol === 'https:' && uri.hostname.includes('.')) {
                        const link = document.createElement('a'); link.href = uri.href;
                        link.target = '_blank'; link.rel = 'noopener noreferrer';
                        link.textContent = t('Open evidence reference', 'প্রমাণের রেফারেন্স খুলুন');
                        row.appendChild(link);
                    }
                } catch { /* Invalid legacy URL is never linked. */ }
            }
            if (item.decisionReason) textRow(row, [t('Decision note', 'সিদ্ধান্তের মন্তব্য'), item.decisionReason]);
            if (item.canReview) {
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary ms-2';
                button.textContent = t('Review', 'পর্যালোচনা');
                button.addEventListener('click', () => {
                    if (!selected || selected.reference !== reference || !sessionValid()) return;
                    outputReview = item; reviewRetry = null;
                    get('grantOutputReviewTitle').textContent = item.title;
                    get('grantOutputReviewForm').reset();
                    get('grantOutputReviewForm').classList.remove('d-none'); controls();
                });
                row.appendChild(button);
            }
            target.appendChild(row);
        });
        if (!data.items.length) textRow(target, [t('No outcomes submitted.', 'কোনো গবেষণা ফলাফল জমা নেই।')]);
        get('grantOutputsPage').textContent = outputPage + ' / ' + Math.max(1, Math.ceil(outputTotal / 20));
        controls();
    }
    get('grantOutputsPrev').addEventListener('click', async () => {
        if (!selected || outputPage <= 1) return;
        outputPage--;
        try { await loadOutputs(selected.reference, detailGeneration); } catch (e) { notice(e.message, true); }
    });
    get('grantOutputsNext').addEventListener('click', async () => {
        if (!selected || outputPage * 20 >= outputTotal) return;
        outputPage++;
        try { await loadOutputs(selected.reference, detailGeneration); } catch (e) { notice(e.message, true); }
    });
    get('grantOutputForm').addEventListener('input', () => { outputRetry = null; });
    get('grantOutputForm').addEventListener('change', () => { outputRetry = null; });
    get('grantOutputForm').addEventListener('submit', async event => {
        event.preventDefault(); if (busy || !selected || !sessionValid()) return;
        const reference = selected.reference, generation = detailGeneration;
        const date = get('grantOutputDate').value;
        if (!date) return;
        const data = {
            clientRequestId: outputRetry ||= crypto.randomUUID(),
            kindCode: get('grantOutputType').value, title: get('grantOutputTitle').value.trim(),
            summary: get('grantOutputSummary').value.trim(),
            referenceUrl: get('grantOutputUrl').value.trim() || null,
            milestoneId: get('grantOutputMilestone').value ? Number(get('grantOutputMilestone').value) : null,
            occurredOn: date + 'T00:00:00Z'
        };
        if (!data.referenceUrl && !data.milestoneId) {
            notice(t('Choose a completed milestone or HTTPS reference.', 'সম্পন্ন মাইলস্টোন বা HTTPS রেফারেন্স দিন।'), true); return;
        }
        busy = true; controls();
        try {
            await api('/api/research/grants/' + encodeURIComponent(reference) + '/outputs', data);
            if (generation !== detailGeneration) return;
            outputRetry = null; get('grantOutputForm').reset(); outputPage = 1;
            await loadOutputs(reference, generation);
            notice(t('Outcome submitted for independent review.', 'স্বতন্ত্র পর্যালোচনার জন্য ফলাফল জমা হয়েছে।'));
        } catch (e) { if (generation === detailGeneration) notice(e.message, true); }
        finally { busy = false; controls(); }
    });
    get('grantOutputReviewForm').addEventListener('input', () => { reviewRetry = null; });
    get('grantOutputReviewForm').addEventListener('change', () => { reviewRetry = null; });
    get('grantOutputReviewForm').addEventListener('submit', async event => {
        event.preventDefault(); if (busy || !selected || !outputReview || !sessionValid()) return;
        const reference = selected.reference, generation = detailGeneration, item = outputReview;
        const data = {
            clientRequestId: reviewRetry ||= crypto.randomUUID(), rowVersion: item.rowVersion,
            decision: get('grantOutputDecision').value, reason: get('grantOutputReason').value.trim(),
            declaresNoConflict: get('grantOutputNoConflict').checked
        };
        busy = true; controls();
        try {
            await api('/api/research/grants/' + encodeURIComponent(reference) +
                '/outputs/' + encodeURIComponent(item.reference) + '/review', data);
            if (generation !== detailGeneration) return;
            outputReview = null; reviewRetry = null;
            get('grantOutputReviewForm').classList.add('d-none');
            await loadOutputs(reference, generation);
            notice(t('Independent review recorded.', 'স্বতন্ত্র পর্যালোচনার সিদ্ধান্ত সংরক্ষিত।'));
        } catch (e) { if (generation === detailGeneration) notice(e.message, true); }
        finally { busy = false; controls(); }
    });
    get('grantOutcomeReportRun')?.addEventListener('click', async () => {
        if (!selected || busy) return;
        const generation = ++reportGeneration, reference = selected.reference;
        const from = get('grantOutputReportFrom').value, to = get('grantOutputReportTo').value;
        if (!from || !to) { notice(t('Choose both dates.', 'শুরু ও শেষ তারিখ দিন।'), true); return; }
        busy = true; controls();
        try {
            const rows = await api('/api/research/grants/reports/outcomes?campusId=' +
                encodeURIComponent(selected.campusId) + '&from=' + encodeURIComponent(from) +
                '&to=' + encodeURIComponent(to));
            if (generation !== reportGeneration || !selected || selected.reference !== reference) return;
            const target = get('grantOutcomeReportRows'); target.replaceChildren();
            rows.forEach(x => textRow(target, [x.kindCode, t('Total', 'মোট') + ': ' + x.total,
                t('Verified', 'যাচাইকৃত') + ': ' + x.verified,
                t('Pending', 'অপেক্ষমাণ') + ': ' + x.submitted,
                t('Rejected', 'প্রত্যাখ্যাত') + ': ' + x.rejected]));
            if (!rows.length) textRow(target, [t('No outcomes in this period.', 'এই সময়ে কোনো ফলাফল নেই।')]);
        } catch (e) { if (generation === reportGeneration) notice(e.message, true); }
        finally { busy = false; controls(); }
    });
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
            get('grantOutputForm').classList.toggle('d-none',
                !(data.stateCode === 'Active' && data.allowedActions.includes('milestone-add')));
            if (get('grantOutputReportTo')) {
                get('grantOutputReportTo').value = new Date().toISOString().slice(0, 10);
                get('grantOutputReportFrom').value = new Date(Date.now() - 365 * 86400000).toISOString().slice(0, 10);
            }
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
            const linked = get('grantOutputMilestone'); linked.replaceChildren();
            option(linked, '', t('Choose completed milestone (optional)', 'সম্পন্ন মাইলস্টোন নির্বাচন (ঐচ্ছিক)'));
            milestones.forEach(m => {
                textRow(list, [m.title, String(m.dueDate).slice(0, 10), m.completedAtUtc ? t('Done', 'সম্পন্ন') : t('Pending', 'অপেক্ষমাণ'), m.outcome]);
                if (!m.completedAtUtc) option(target, m.id, m.title);
                if (m.completedAtUtc) option(linked, m.id, m.title);
            });
            if (!milestones.length) textRow(list, [t('No milestones.', 'মাইলস্টোন নেই।')]);
            const audit = get('grantHistory'); audit.replaceChildren();
            history.items.forEach(h => textRow(audit, [h.actionCode, h.fromState + ' → ' + h.toState, h.reason, String(h.occurredAtUtc).slice(0, 16)]));
            await loadOutputs(reference, generation);
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
