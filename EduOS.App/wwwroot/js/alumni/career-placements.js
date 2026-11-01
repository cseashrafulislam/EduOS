(() => {
    'use strict';
    const root = document.getElementById('careerPlacementWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const bn = document.documentElement.lang.toLowerCase().startsWith('bn');
    const t = (a, b) => bn ? b : a;
    const words = {
        heading: ['Placement decisions and reports', 'প্লেসমেন্ট সিদ্ধান্ত ও রিপোর্ট'],
        applications: ['Applications and independent approvals', 'আবেদন ও স্বতন্ত্র অনুমোদন'],
        all: ['All', 'সব'], pending: ['Pending', 'বিচারাধীন'], rejected: ['Rejected', 'প্রত্যাখ্যাত'],
        placed: ['Placed', 'প্লেসমেন্ট'], withdrawn: ['Withdrawn', 'প্রত্যাহার'],
        refresh: ['Refresh', 'রিফ্রেশ'], title: ['Opportunity', 'পদ'], student: ['Student', 'শিক্ষার্থী'],
        campus: ['Campus', 'ক্যাম্পাস'], status: ['Status', 'অবস্থা'], proposed: ['Proposed outcome', 'প্রস্তাবিত ফলাফল'],
        actions: ['Actions', 'কাজ'], previous: ['Previous', 'আগের'], next: ['Next', 'পরের'],
        report: ['Placement outcome report', 'প্লেসমেন্ট ফলাফল রিপোর্ট'], export: ['Export page CSV', 'পৃষ্ঠার CSV'],
        employer: ['Employer', 'নিয়োগদাতা'], total: ['Applications', 'আবেদন']
    };
    for (const node of root.querySelectorAll('[data-i18n]')) {
        const pair = words[node.dataset.i18n]; if (pair) node.textContent = pair[bn ? 1 : 0];
    }
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const pending = new Map();
    let page = 1, total = 0, reportPage = 1, reportTotal = 0, revision = 0, reportRevision = 0;
    const notice = (message, error = false) => {
        const node = el('careerPlacementNotice'); node.textContent = message;
        node.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
    };
    async function api(url, body) {
        const write = body !== undefined;
        const response = await fetch(url, { method: write ? 'POST' : 'GET', cache: 'no-store', credentials: 'same-origin',
            headers: { Accept: 'application/json', ...(write ? { 'Content-Type': 'application/json', RequestVerificationToken: token } : {}) },
            ...(write ? { body: JSON.stringify(body) } : {}) });
        const json = await response.json().catch(() => null);
        if (!response.ok || json?.success !== true) {
            const error = new Error(json?.message || t('Request failed. Reload and retry.', 'অনুরোধ ব্যর্থ। রিলোড করে আবার চেষ্টা করুন।'));
            error.status = response.status; throw error;
        }
        return json.data;
    }
    const td = (tr, text) => {
        const cell = document.createElement('td'); cell.textContent = String(text ?? ''); tr.appendChild(cell); return cell;
    };
    const empty = (body, span, label) => {
        body.replaceChildren(); const tr = document.createElement('tr'); const cell = td(tr, label); cell.colSpan = span; body.appendChild(tr);
    };
    const status = value => ({ 1: words.pending, 2: words.rejected, 3: words.placed, 4: words.withdrawn })[value]?.[bn ? 1 : 0] || '—';
    const makeButton = (label, click, color = 'btn-outline-primary') => {
        const button = document.createElement('button'); button.type = 'button';
        button.className = 'btn btn-sm me-1 mb-1 ' + color; button.textContent = label;
        button.addEventListener('click', async () => {
            button.disabled = true;
            try { await click(); } catch (error) { notice(error.message, true); }
            finally { button.disabled = false; }
        });
        return button;
    };
    async function decision(item, action, outcome) {
        const key = action + ':' + item.reference + ':' + item.rowVersion + ':' + outcome;
        if (!confirm(action === 'approve'
            ? t('Approve this placement outcome as the independent reviewer?', 'স্বতন্ত্র অনুমোদনকারী হিসেবে ফলাফল অনুমোদন করবেন?')
            : t('Propose this outcome for independent approval?', 'স্বতন্ত্র অনুমোদনের জন্য ফলাফল প্রস্তাব করবেন?'))) return;
        if (!pending.has(key)) pending.set(key, crypto.randomUUID());
        try {
            await api('/api/alumni/career/applications/' + encodeURIComponent(item.reference) + '/' + action,
                { clientRequestId: pending.get(key), rowVersion: item.rowVersion,
                    ...(action === 'propose' ? { outcome } : {}) });
            pending.delete(key);
            notice(t('Decision recorded.', 'সিদ্ধান্ত সংরক্ষিত হয়েছে।'));
            await Promise.all([loadApplications(), loadReport()]);
        } catch (error) {
            if ([400, 403, 409].includes(error.status)) {
                pending.delete(key); await loadApplications();
            }
            throw error;
        }
    }
    async function loadApplications() {
        const id = ++revision; const body = el('careerPlacementRows'); empty(body, 6, t('Loading…', 'লোড হচ্ছে…'));
        el('careerPlacementRefresh').disabled = true;
        try {
            const data = await api('/api/alumni/career/applications?page=' + page + '&pageSize=20&status=' + el('careerPlacementFilter').value);
            if (id !== revision) return;
            total = data.totalCount; body.replaceChildren();
            if (!data.items.length) empty(body, 6, t('No applications in this scope.', 'এই পরিসরে আবেদন নেই।'));
            for (const item of data.items) {
                const tr = document.createElement('tr');
                td(tr, item.opportunityTitle); td(tr, item.studentCode + ' — ' + item.fullName);
                td(tr, item.campusId); td(tr, status(item.status));
                td(tr, item.proposedOutcome ? status(item.proposedOutcome) : '—');
                const actions = td(tr, '');
                if (item.status === 1 && item.proposedOutcome == null) {
                    actions.appendChild(makeButton(t('Propose placed', 'প্লেসমেন্ট প্রস্তাব'), () => decision(item, 'propose', 3)));
                    actions.appendChild(makeButton(t('Propose rejected', 'প্রত্যাখ্যান প্রস্তাব'), () => decision(item, 'propose', 2), 'btn-outline-danger'));
                }
                if (item.canApprove) actions.appendChild(makeButton(t('Approve outcome', 'ফলাফল অনুমোদন'), () => decision(item, 'approve', 0)));
                body.appendChild(tr);
            }
            el('careerPlacementPage').textContent = page + ' / ' + Math.max(1, Math.ceil(total / 20));
        } catch (error) {
            if (id === revision) { empty(body, 6, t('Unable to load. Refresh.', 'লোড হয়নি। রিফ্রেশ করুন।')); notice(error.message, true); }
        } finally {
            if (id === revision) { el('careerPlacementRefresh').disabled = false;
                el('careerPlacementPrev').disabled = page <= 1; el('careerPlacementNext').disabled = page * 20 >= total; }
        }
    }
    async function loadReport() {
        const id = ++reportRevision; const body = el('careerReportRows');
        empty(body, 8, t('Loading…', 'লোড হচ্ছে…'));
        const link = el('careerReportCsv'); link.removeAttribute('href'); link.classList.add('disabled'); link.setAttribute('aria-disabled', 'true');
        try {
            const data = await api('/api/alumni/career/reports?page=' + reportPage + '&pageSize=20');
            if (id !== reportRevision) return;
            reportTotal = data.totalCount; body.replaceChildren();
            if (!data.items.length) empty(body, 8, t('No report data in this scope.', 'এই পরিসরে রিপোর্ট নেই।'));
            for (const item of data.items) {
                const tr = document.createElement('tr');
                for (const value of [item.employerName, item.opportunityTitle, item.campusId,
                    item.applications, item.pending, item.rejected, item.placed, item.withdrawn]) td(tr, value);
                body.appendChild(tr);
            }
            el('careerReportPage').textContent = reportPage + ' / ' + Math.max(1, Math.ceil(reportTotal / 20));
            if (data.items.length) {
                link.href = '/api/alumni/career/reports.csv?page=' + reportPage + '&pageSize=20';
                link.classList.remove('disabled'); link.setAttribute('aria-disabled', 'false');
            }
        } catch (error) {
            if (id === reportRevision) { empty(body, 8, t('Unable to load report.', 'রিপোর্ট লোড হয়নি।')); notice(error.message, true); }
        } finally {
            if (id === reportRevision) { el('careerReportPrev').disabled = reportPage <= 1;
                el('careerReportNext').disabled = reportPage * 20 >= reportTotal; }
        }
    }
    el('careerPlacementFilter').addEventListener('change', () => { page = 1; loadApplications(); });
    el('careerPlacementRefresh').addEventListener('click', loadApplications);
    el('careerPlacementPrev').addEventListener('click', () => { if (page > 1) { page--; loadApplications(); } });
    el('careerPlacementNext').addEventListener('click', () => { if (page * 20 < total) { page++; loadApplications(); } });
    el('careerReportRefresh').addEventListener('click', loadReport);
    el('careerReportPrev').addEventListener('click', () => { if (reportPage > 1) { reportPage--; loadReport(); } });
    el('careerReportNext').addEventListener('click', () => { if (reportPage * 20 < reportTotal) { reportPage++; loadReport(); } });
    loadApplications(); loadReport();
})();
