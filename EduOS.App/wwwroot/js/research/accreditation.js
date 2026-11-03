(() => {
    'use strict';
    const root = document.getElementById('accreditationWorkspace');
    if (!root) return;
    const get = id => document.getElementById(id);
    const bn = () => document.documentElement.lang.toLowerCase().startsWith('bn');
    const t = (en, bd) => bn() ? bd : en;
    root.querySelectorAll('[data-a-en]').forEach(node => { node.textContent = bn() ? node.dataset.aBn : node.dataset.aEn; });
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const tenant = document.body.dataset.authTenantId;
    const session = document.body.dataset.authSessionReference;
    let selected = null, frameworks = [], total = 0, page = 1, busy = false;
    let listGeneration = 0, detailGeneration = 0, standardsGeneration = 0, programGeneration = 0, reportGeneration = 0;
    let createRetry = null, actionRetry = null, frameworkRetry = null, standardRetry = null, publishRetry = null;
    let fileRetry = null, filePage = 1, fileTotal = 0;
    const actions = {
        edit: ['Edit draft', 'খসড়া সংশোধন'], submit: ['Submit evidence', 'প্রমাণ জমা'],
        assign: ['Assign reviewer', 'পর্যালোচক নিযুক্ত'], approve: ['Approve evidence', 'প্রমাণ অনুমোদন'],
        reject: ['Reject evidence', 'প্রমাণ প্রত্যাখ্যান'],
        'request-revision': ['Request changes', 'সংশোধন অনুরোধ'], revise: ['Submit revised evidence', 'সংশোধিত প্রমাণ জমা'],
        withdraw: ['Withdraw draft', 'খসড়া প্রত্যাহার']
    };
    const sessionValid = () => Boolean(tenant && session && tenant === document.body.dataset.authTenantId &&
        session === document.body.dataset.authSessionReference);
    function clearReport() {
        reportGeneration++; get('accReportRows').replaceChildren();
        get('accCsv').classList.add('d-none'); get('accCsv').removeAttribute('href');
        get('accPrint').classList.add('d-none');
    }
    function clearDetail() {
        detailGeneration++; selected = null; actionRetry = null;
        get('accDetail').classList.add('d-none'); get('accSummary').replaceChildren();
        get('accSensitive').replaceChildren(); get('accAction').replaceChildren(); get('accHistory').replaceChildren();
        get('accFileRows').replaceChildren(); get('accFilesPage').textContent = '';
        get('accFileForm').classList.add('d-none'); fileRetry = null; filePage = 1; fileTotal = 0;
    }
    async function api(url, body) {
        if (!sessionValid()) { clearDetail(); clearReport(); throw Error(t('Session changed.', 'সেশন পরিবর্তিত।')); }
        if (typeof window.EduOSVerifySessionContext === 'function') await window.EduOSVerifySessionContext();
        const res = await fetch(url, {
            method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json', ...(body === undefined ? {} :
                { 'Content-Type': 'application/json', RequestVerificationToken: token }) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        if (!sessionValid() || res.headers.get('X-EduOS-Tenant-Id') !== tenant ||
            res.headers.get('X-EduOS-Session-Reference') !== session) {
            clearDetail(); clearReport(); throw Error(t('Session verification failed.', 'সেশন যাচাই ব্যর্থ।'));
        }
        const data = await res.json().catch(() => null);
        if (!res.ok || data?.success !== true) {
            const err = Error(res.status === 409 ? t('Record changed; refresh.', 'রেকর্ড বদলেছে; রিফ্রেশ করুন।') :
                res.status === 403 || res.status === 404 ? t('Access unavailable.', 'অধিকার নেই।') :
                    data?.message || t('Request failed.', 'অনুরোধ ব্যর্থ।'));
            err.status = res.status; throw err;
        }
        return data.data;
    }
    function notice(text, error = false) {
        get('accreditationNotice').className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        get('accreditationNotice').textContent = text;
    }
    function option(target, value, title) {
        const o = document.createElement('option'); o.value = String(value); o.textContent = title;
        target.appendChild(o);
    }
    function td(tr, content) {
        const c = document.createElement('td'); c.textContent = content == null ? '—' : String(content);
        tr.appendChild(c); return c;
    }
    function historyRow(item) {
        const p = document.createElement('p'); p.className = 'border-bottom py-1 mb-1';
        p.textContent = [item.actionCode, item.fromState + ' → ' + item.toState,
            'v' + item.revisionNo, item.reason || '', String(item.occurredAtUtc).slice(0, 16)].join(' · ');
        get('accHistory').appendChild(p);
    }
    function controls() {
        get('accCreateButton').disabled = busy || !get('accCampus').value || !get('accStandard').value;
        get('accConfirm').disabled = busy || !selected || !get('accAction').value;
        get('accRefresh').disabled = busy;
        get('accPrevious').disabled = busy || page === 1;
        get('accNext').disabled = busy || page * 20 >= total;
        get('accReportRun').disabled = busy;
        get('accFileUpload').disabled = busy || !selected;
        get('accFilesPrev').disabled = busy || !selected || filePage <= 1;
        get('accFilesNext').disabled = busy || !selected || filePage * 25 >= fileTotal;
        if (get('accFrameworkPublish')) get('accFrameworkPublish').disabled = busy || !get('accFrameworkManage').value;
        if (get('accStandardSave')) get('accStandardSave').disabled = busy || !get('accFrameworkManage').value;
    }
    async function loadFiles(reference, generation) {
        const data = await api('/api/accreditation/assessments/' + encodeURIComponent(reference) +
            '/files?page=' + filePage + '&pageSize=25');
        if (generation !== detailGeneration || !sessionValid()) return;
        fileTotal = data.totalCount;
        const container = get('accFileRows'); container.replaceChildren();
        data.items.forEach(item => {
            const row = document.createElement('div'); row.className = 'py-1 border-bottom';
            const link = document.createElement('a'); link.setAttribute('download', '');
            link.href = '/api/accreditation/assessments/' + encodeURIComponent(reference) +
                '/files/' + encodeURIComponent(item.reference) + '/download';
            link.textContent = item.fileName;
            row.append(link, document.createTextNode(' · v' + item.revisionNo + ' · ' +
                Math.ceil(item.sizeBytes / 1024) + ' KB')); container.appendChild(row);
        });
        if (!data.items.length) container.textContent = t('No private evidence files.', 'কোনো ব্যক্তিগত প্রমাণ ফাইল নেই।');
        get('accFilesPage').textContent = filePage + ' / ' + Math.max(1, Math.ceil(fileTotal / 25));
        controls();
    }
    get('accFilesPrev').addEventListener('click', async () => {
        if (!selected || filePage <= 1) return;
        filePage--;
        try { await loadFiles(selected.reference, detailGeneration); } catch (e) { notice(e.message, true); }
    });
    get('accFilesNext').addEventListener('click', async () => {
        if (!selected || filePage * 25 >= fileTotal) return;
        filePage++;
        try { await loadFiles(selected.reference, detailGeneration); } catch (e) { notice(e.message, true); }
    });
    get('accFileInput').addEventListener('change', () => { fileRetry = null; });
    get('accFileForm').addEventListener('submit', async event => {
        event.preventDefault(); if (busy || !selected || !sessionValid()) return;
        const file = get('accFileInput').files?.[0]; if (!file) return;
        if (file.size < 1 || file.size > 5242880) { notice(t('File must be 5 MB or smaller.', 'ফাইল ৫ এমবির মধ্যে হতে হবে।'), true); return; }
        const reference = selected.reference, generation = detailGeneration;
        const data = new FormData(); data.append('file', file); data.append('requestId', fileRetry ||= crypto.randomUUID());
        busy = true; controls();
        try {
            if (typeof window.EduOSVerifySessionContext === 'function') await window.EduOSVerifySessionContext();
            const res = await fetch('/api/accreditation/assessments/' + encodeURIComponent(reference) + '/files', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { RequestVerificationToken: token, Accept: 'application/json' }, body: data
            });
            if (!sessionValid() || res.headers.get('X-EduOS-Tenant-Id') !== tenant ||
                res.headers.get('X-EduOS-Session-Reference') !== session)
                throw Error(t('Session verification failed.', 'সেশন যাচাই ব্যর্থ।'));
            const result = await res.json().catch(() => null);
            if (!res.ok || result?.success !== true) throw Error(result?.message || t('Upload failed.', 'আপলোড ব্যর্থ।'));
            if (generation !== detailGeneration) return;
            fileRetry = null; get('accFileForm').reset(); filePage = 1;
            await loadFiles(reference, generation);
            notice(t('Scanned evidence saved.', 'স্ক্যান করা প্রমাণ সংরক্ষিত।'));
        } catch (e) { if (generation === detailGeneration) notice(e.message, true); }
        finally { busy = false; controls(); }
    });
    async function loadFrameworks() {
        try {
            frameworks = await api('/api/accreditation/frameworks');
            const published = get('accFramework'), draft = get('accFrameworkManage');
            published.replaceChildren(); option(published, '', t('Choose framework', 'কাঠামো নির্বাচন'));
            draft?.replaceChildren(); if (draft) option(draft, '', t('Choose draft', 'খসড়া নির্বাচন'));
            frameworks.forEach(f => {
                const label = `${f.name} (${f.code} v${f.versionNo})`;
                if (f.stateCode === 'Published') option(published, f.id, label);
                if (draft && f.stateCode === 'Draft') option(draft, f.id, label);
            });
            await loadStandards(); controls();
        } catch (e) { notice(e.message, true); }
    }
    async function loadStandards() {
        const frameworkId = get('accFramework').value, generation = ++standardsGeneration;
        const target = get('accStandard'); target.replaceChildren();
        option(target, '', t('Choose standard', 'মানদণ্ড নির্বাচন'));
        if (!frameworkId) { controls(); return; }
        try {
            const standards = await api('/api/accreditation/frameworks/' + encodeURIComponent(frameworkId) + '/standards');
            if (generation !== standardsGeneration || !sessionValid()) return;
            standards.forEach(s => option(target, s.id, `${s.code} · ${s.title}`));
        } catch (e) { if (generation === standardsGeneration) notice(e.message, true); }
        controls();
    }
    async function loadCampuses() {
        try {
            const campuses = await api('/api/accreditation/campuses');
            for (const id of ['accCampus', 'accReportCampus']) {
                const select = get(id); select.replaceChildren();
                option(select, '', t('Choose campus', 'ক্যাম্পাস নির্বাচন'));
                campuses.forEach(c => option(select, c.id, c.name));
            }
            controls();
        } catch (e) { notice(e.message, true); }
    }
    async function loadPrograms() {
        const campus = get('accCampus').value, generation = ++programGeneration;
        const target = get('accProgram'); target.replaceChildren();
        option(target, '', t('Institute-wide / no program', 'সম্পূর্ণ প্রতিষ্ঠান / প্রোগ্রাম নেই'));
        if (!campus) { controls(); return; }
        try {
            const programs = await api('/api/accreditation/programs?campusId=' + encodeURIComponent(campus));
            if (generation !== programGeneration || !sessionValid()) return;
            programs.forEach(p => option(target, p.id, p.name));
        } catch (e) { if (generation === programGeneration) notice(e.message, true); }
        controls();
    }
    async function loadList() {
        const generation = ++listGeneration; clearDetail(); get('accRows').replaceChildren();
        try {
            const data = await api('/api/accreditation/assessments?page=' + page + '&pageSize=20');
            if (generation !== listGeneration || !sessionValid()) return;
            total = data.totalCount;
            data.items.forEach(item => {
                const tr = document.createElement('tr');
                td(tr, item.frameworkCode + ' v' + item.frameworkVersion + ' · ' + item.standardCode);
                td(tr, `${String(item.periodStart).slice(0, 10)} – ${String(item.periodEnd).slice(0, 10)}`);
                td(tr, item.stateCode);
                const cell = td(tr, ''); const button = document.createElement('button');
                button.className = 'btn btn-sm btn-outline-primary'; button.type = 'button';
                button.textContent = t('Open', 'খুলুন'); button.addEventListener('click', () => open(item.reference));
                cell.appendChild(button); get('accRows').appendChild(tr);
            });
            if (!data.items.length) { const row = document.createElement('tr'); td(row, t('No evidence assessments.', 'কোনো প্রমাণ মূল্যায়ন নেই।')).colSpan = 4; get('accRows').appendChild(row); }
            get('accPage').textContent = `${page} / ${Math.max(1, Math.ceil(total / 20))}`;
        } catch (e) { if (generation === listGeneration) notice(e.message, true); }
        finally { controls(); }
    }
    async function open(reference) {
        clearDetail(); const generation = ++detailGeneration;
        try {
            const detail = await api('/api/accreditation/assessments/' + encodeURIComponent(reference));
            if (generation !== detailGeneration || !sessionValid()) return;
            selected = detail; get('accDetail').classList.remove('d-none');
            get('accSummary').textContent = `${detail.frameworkCode} v${detail.frameworkVersion} · ${detail.standardCode} · ${detail.stateCode} · v${detail.currentRevision} · ${detail.reference}` +
                ` · ${t('Independent review', 'স্বতন্ত্র মূল্যায়ন')}: ${detail.approvalsReceived}/${detail.requiredReviewerCount}`;
            get('accSensitive').textContent = detail.evidenceSummary || t('Evidence visible only to its owner or assigned reviewer.', 'প্রমাণ শুধু মালিক বা নিযুক্ত পর্যালোচক দেখতে পারবেন।');
            get('accEditEvidence').value = detail.evidenceSummary || '';
            const action = get('accAction'); action.replaceChildren(); option(action, '', t('Choose action', 'কার্যক্রম নির্বাচন'));
            detail.allowedActions.forEach(a => option(action, a, t(...(actions[a] || [a, a]))));
            changeAction();
            get('accFileForm').classList.toggle('d-none',
                !(['Draft', 'ChangesRequested'].includes(detail.stateCode) &&
                    detail.allowedActions.some(a => ['edit', 'submit', 'revise'].includes(a))));
            const audit = await api('/api/accreditation/assessments/' + encodeURIComponent(reference) + '/history?page=1&pageSize=20');
            if (generation !== detailGeneration || !sessionValid()) return;
            get('accHistory').replaceChildren(); audit.items.forEach(historyRow);
            await loadFiles(reference, generation);
        } catch (e) { if (generation === detailGeneration) { clearDetail(); notice(e.message, true); } }
        finally { controls(); }
    }
    async function changeAction() {
        actionRetry = null;
        const action = get('accAction').value;
        get('accEditBox').classList.toggle('d-none', !['edit', 'revise'].includes(action));
        get('accReviewerBox').classList.toggle('d-none', action !== 'assign');
        get('accReviewer').required = action === 'assign';
        get('accConflictLabel').classList.toggle('d-none', !['approve', 'reject', 'request-revision'].includes(action));
        if (action === 'assign' && selected) {
            const reference = selected.reference, generation = detailGeneration;
            const target = get('accReviewer'); target.replaceChildren();
            try {
                const reviewers = await api('/api/accreditation/reviewers?campusId=' + selected.campusId);
                if (generation !== detailGeneration || selected?.reference !== reference || get('accAction').value !== 'assign') return;
                reviewers.filter(x => x.userId !== selected.ownerUserId).forEach(x => option(target, x.userId, x.name));
            } catch (e) { if (generation === detailGeneration) notice(e.message, true); }
        }
        controls();
    }
    get('accAction').addEventListener('change', changeAction);
    get('accCampus').addEventListener('change', () => { createRetry = null; loadPrograms(); });
    get('accFramework').addEventListener('change', () => { createRetry = null; loadStandards(); });
    get('accFrameworkManage')?.addEventListener('change', () => { standardRetry = null; publishRetry = null; controls(); });
    get('accRefresh').addEventListener('click', () => { page = 1; loadList(); });
    get('accPrevious').addEventListener('click', () => { if (page > 1) { page--; loadList(); } });
    get('accNext').addEventListener('click', () => { if (page * 20 < total) { page++; loadList(); } });
    get('accCreateForm').addEventListener('input', () => { createRetry = null; });
    get('accCreateForm').addEventListener('submit', async event => {
        event.preventDefault(); if (busy) return;
        const date = value => value ? value + 'T00:00:00Z' : null;
        const body = {
            clientRequestId: createRetry ||= crypto.randomUUID(), campusId: Number(get('accCampus').value),
            academicProgramId: get('accProgram').value ? Number(get('accProgram').value) : null,
            standardId: Number(get('accStandard').value), periodStart: date(get('accPeriodFrom').value),
            periodEnd: date(get('accPeriodTo').value), evidenceSummary: get('accEvidence').value.trim()
        };
        busy = true; controls();
        try {
            const data = await api('/api/accreditation/assessments', body);
            createRetry = null; get('accCreateForm').reset(); page = 1; await loadList(); await open(data.reference);
            notice(t('Assessment draft saved.', 'মূল্যায়নের খসড়া সংরক্ষিত।'));
        } catch (e) { notice(e.message, true); }
        finally { busy = false; controls(); }
    });
    get('accActionForm').addEventListener('input', () => { actionRetry = null; });
    get('accActionForm').addEventListener('submit', async event => {
        event.preventDefault(); if (busy || !selected) return;
        const action = get('accAction').value, reference = selected.reference;
        if (!action || !window.confirm(t('Confirm this accreditation decision?', 'স্বীকৃতির এই সিদ্ধান্ত নিশ্চিত করবেন?'))) return;
        const body = {
            clientRequestId: actionRetry ||= crypto.randomUUID(), rowVersion: selected.rowVersion,
            reviewerUserId: null,
            reviewerUserIds: action === 'assign' ? Array.from(get('accReviewer').selectedOptions, x => Number(x.value)) : null,
            evidenceSummary: ['edit', 'revise'].includes(action) ? get('accEditEvidence').value.trim() : null,
            reason: get('accReason').value.trim(),
            declaresNoConflict: ['approve', 'reject', 'request-revision'].includes(action) ? get('accConflict').checked : null
        };
        if (action === 'assign' && body.reviewerUserIds.length !== selected.requiredReviewerCount) {
            actionRetry = null;
            notice(t(`Select exactly ${selected.requiredReviewerCount} independent reviewers.`,
                `ঠিক ${selected.requiredReviewerCount} জন স্বতন্ত্র পর্যালোচক নির্বাচন করুন।`), true);
            return;
        }
        busy = true; controls();
        try {
            await api('/api/accreditation/assessments/' + encodeURIComponent(reference) + '/' + encodeURIComponent(action), body);
            actionRetry = null; await loadList(); await open(reference);
            notice(t('Decision recorded.', 'সিদ্ধান্ত সংরক্ষিত।'));
        } catch (e) { if ([403, 404, 409].includes(e.status)) clearDetail(); notice(e.message, true); }
        finally { busy = false; controls(); }
    });
    get('accFrameworkForm')?.addEventListener('input', () => { frameworkRetry = null; });
    get('accFrameworkForm')?.addEventListener('submit', async event => {
        event.preventDefault(); if (busy) return;
        busy = true; controls();
        try {
            await api('/api/accreditation/frameworks', {
                clientRequestId: frameworkRetry ||= crypto.randomUUID(), code: get('accFrameworkCode').value.trim().toUpperCase(),
                name: get('accFrameworkName').value.trim(), versionNo: Number(get('accFrameworkVersion').value)
            });
            frameworkRetry = null; get('accFrameworkForm').reset(); await loadFrameworks();
            notice(t('Framework draft saved.', 'কাঠামোর খসড়া সংরক্ষিত।'));
        } catch (e) { notice(e.message, true); }
        finally { busy = false; controls(); }
    });
    get('accStandardForm')?.addEventListener('input', () => { standardRetry = null; });
    get('accStandardForm')?.addEventListener('submit', async event => {
        event.preventDefault(); if (busy) return;
        busy = true; controls();
        try {
            await api('/api/accreditation/standards', {
                clientRequestId: standardRetry ||= crypto.randomUUID(), frameworkId: Number(get('accFrameworkManage').value),
                code: get('accStandardCode').value.trim().toUpperCase(), title: get('accStandardTitle').value.trim(),
                rubric: get('accStandardRubric').value.trim(), requiredEvidence: get('accStandardRequired').value.trim(),
                validityDays: Number(get('accStandardValidity').value),
                requiredReviewerCount: Number(get('accStandardQuorum').value)
            });
            standardRetry = null; get('accStandardForm').reset();
            notice(t('Standard added to the draft.', 'খসড়ায় মানদণ্ড যোগ হয়েছে।'));
        } catch (e) { notice(e.message, true); }
        finally { busy = false; controls(); }
    });
    get('accFrameworkPublish')?.addEventListener('click', async () => {
        const id = Number(get('accFrameworkManage').value), framework = frameworks.find(f => f.id === id);
        if (!framework || busy || !window.confirm(t('Publish this frozen framework version?', 'এই অপরিবর্তনীয় কাঠামো সংস্করণ প্রকাশ করবেন?'))) return;
        busy = true; controls();
        try {
            await api('/api/accreditation/frameworks/' + id + '/publish', {
                clientRequestId: publishRetry ||= crypto.randomUUID(), rowVersion: framework.rowVersion
            });
            publishRetry = null; await loadFrameworks(); notice(t('Framework published.', 'কাঠামো প্রকাশিত।'));
        } catch (e) { notice(e.message, true); }
        finally { busy = false; controls(); }
    });
    for (const id of ['accReportCampus', 'accReportFrom', 'accReportTo']) {
        get(id).addEventListener('change', clearReport);
    }
    get('accReportForm').addEventListener('submit', async event => {
        event.preventDefault(); clearReport(); const generation = reportGeneration;
        const query = new URLSearchParams({ campusId: get('accReportCampus').value,
            from: get('accReportFrom').value, to: get('accReportTo').value });
        try {
            const rows = await api('/api/accreditation/reports?' + query);
            if (generation !== reportGeneration || !sessionValid()) return;
            rows.forEach(r => {
                const tr = document.createElement('tr');
                [r.frameworkCode + ' v' + r.frameworkVersion, r.standardCode,
                    r.total, r.approved, r.pending, r.expired].forEach(x => td(tr, x));
                get('accReportRows').appendChild(tr);
            });
            if (!rows.length) { const tr = document.createElement('tr'); td(tr, t('No summary rows.', 'কোনো সারসংক্ষেপ নেই।')).colSpan = 6; get('accReportRows').appendChild(tr); }
            const csv = get('accCsv'); csv.href = '/api/accreditation/reports.csv?' + query.toString();
            csv.classList.remove('d-none'); get('accPrint').classList.remove('d-none');
        } catch (e) { if (generation === reportGeneration) notice(e.message, true); }
    });
    get('accPrint').addEventListener('click', () => {
        if (!sessionValid()) { clearReport(); return; }
        window.print();
    });
    const today = new Date().toISOString().slice(0, 10);
    get('accReportTo').value = today; get('accReportFrom').value = today.slice(0, 4) + '-01-01';
    loadCampuses(); loadFrameworks(); loadList();
})();
