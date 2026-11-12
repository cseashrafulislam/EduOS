(() => {
    'use strict';
    const root = document.getElementById('student360');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const bn = root.dataset.bn === 'true';
    const msg = (en, bd) => bn ? bd : en;
    const reference = root.dataset.reference;
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(reference)) return;
    const tenant = document.body?.dataset.authTenantId;
    const session = document.body?.dataset.authSessionReference;
    const sameSession = () => tenant === document.body?.dataset.authTenantId &&
        session === document.body?.dataset.authSessionReference &&
        window.EduOSSessionTransitionPending?.() !== true;
    const configs = {
        enrollment: {
            url: '/api/academic-enrollments/students/' + encodeURIComponent(reference) + '/history',
            columns: [['enrollmentDate', 'Date', 'তারিখ'], ['batchName', 'Batch', 'ব্যাচ'],
                ['rollNo', 'Roll', 'রোল'], ['state', 'State', 'অবস্থা'], ['isCurrent', 'Current', 'বর্তমান']]
        },
        lifecycle: {
            url: '/api/students/' + encodeURIComponent(reference) + '/lifecycle-timeline',
            columns: [['occurredAt', 'When', 'সময়'], ['eventType', 'Event', 'ঘটনা'],
                ['fromStatus', 'From', 'আগের অবস্থা'], ['toStatus', 'To', 'নতুন অবস্থা'],
                ['reason', 'Reason', 'কারণ']]
        },
        subjects: {
            url: '/api/student-subjects/students/' + encodeURIComponent(reference) + '/history',
            columns: [['registeredAt', 'Registered', 'নিবন্ধনের সময়'],
                ['subjectCode', 'Subject code', 'বিষয় কোড'], ['subjectName', 'Subject', 'বিষয়'],
                ['rollNo', 'Enrollment roll', 'ভর্তির রোল'],
                ['registrationState', 'Registration', 'নিবন্ধন অবস্থা'],
                ['creditHoursSnapshot', 'Credits at registration', 'নিবন্ধনকালীন ক্রেডিট'],
                ['approvedAt', 'Approved', 'অনুমোদনের সময়']]
        },
        guardian: {
            url: '/api/student-guardians/students/' + encodeURIComponent(reference) + '/history',
            columns: [['occurredAt', 'When', 'সময়'], ['linkId', 'Link', 'সংযোগ'],
                ['action', 'Action', 'পরিবর্তন'], ['isSuccess', 'Success', 'সফল'],
                ['reason', 'Reason', 'কারণ']]
        }
    };
    let tab = 'enrollment', page = 1, totalPages = 1, busy = false, token = 0, controller = null;
    let currentRows = [], currentTab = null, profileReady = false, historyReady = false;
    let profileController = null, historyFailed = false;
    const node = (tag, value) => { const e = document.createElement(tag); e.textContent = value == null || value === '' ? '—' : String(value); return e; };
    const alert = value => { $('s360Alert').className = 'alert alert-danger'; $('s360Alert').textContent = value; };
    function controls() {
        $('s360Previous').disabled = busy || !historyReady || !sameSession() || page <= 1;
        $('s360Next').disabled = busy || !historyReady || !sameSession() || page >= totalPages;
        $('s360Page').textContent = page + ' / ' + totalPages;
        const canExport = !busy && profileReady && historyReady && currentTab === tab && currentRows.length > 0 && sameSession();
        $('s360Export').disabled = !canExport;
        $('s360Print').disabled = !canExport;
        $('s360Retry').disabled = busy || !profileReady || !sameSession();
    }
    function render(items) {
        const cfg = configs[tab];
        const head = node('tr', '');
        head.textContent = '';
        cfg.columns.forEach(col => { const th = node('th', bn ? col[2] : col[1]); th.scope = 'col'; head.append(th); });
        $('s360Head').replaceChildren(head);
        const rows = $('s360Rows');
        rows.replaceChildren();
        if (!items.length) {
            const tr = document.createElement('tr'), td = node('td', msg('No records for this page.', 'এই পৃষ্ঠায় কোনো তথ্য নেই।'));
            td.colSpan = cfg.columns.length; tr.append(td); rows.append(tr); return;
        }
        for (const item of items) {
            const tr = document.createElement('tr');
            for (const [field] of cfg.columns) {
                let value = item[field];
                if (typeof value === 'boolean') value = value ? msg('Yes', 'হ্যাঁ') : msg('No', 'না');
                if (['occurredAt', 'registeredAt', 'approvedAt'].includes(field) && value) {
                    const date = new Date(value);
                    value = Number.isNaN(date.getTime()) ? '—' : date.toLocaleString(bn ? 'bn-BD' : 'en-BD');
                }
                tr.append(node('td', value));
            }
            rows.append(tr);
        }
    }
    async function load() {
        controller?.abort();
        const local = new AbortController();
        controller = local;
        const ticket = ++token, selected = tab;
        busy = true; historyReady = false; historyFailed = false; currentRows = []; currentTab = null; controls();
        $('s360Status').textContent = msg('Loading history…', 'ইতিহাস লোড হচ্ছে…');
        $('s360Rows').replaceChildren();
        try {
            const params = new URLSearchParams({page: String(page), pageSize: '20'});
            const response = await fetch(configs[selected].url + '?' + params, {
                credentials: 'same-origin', cache: 'no-store', signal: local.signal,
                headers: {Accept: 'application/json'}
            });
            const json = await response.json().catch(() => null);
            if (ticket !== token || selected !== tab || !sameSession()) return;
            if (!response.ok || json?.success !== true || !json.data)
                throw new Error(json?.message || msg('History unavailable.', 'ইতিহাস পাওয়া যায়নি।'));
            const data = json.data;
            const items = selected === 'lifecycle' ? data.events : data.items;
            if (!Array.isArray(items) || items.length > 20 || items.some(item => !item || typeof item !== 'object' || Array.isArray(item)))
                throw new Error('Invalid history page.');
            if (selected === 'lifecycle') {
                if (typeof data.hasMore !== 'boolean' || (data.hasMore && items.length === 0))
                    throw new Error('Invalid lifecycle pagination.');
                totalPages = data.hasMore ? page + 1 : page;
            } else {
                if (!Number.isSafeInteger(data.totalCount) || data.totalCount < 0)
                    throw new Error('Invalid history count.');
                totalPages = Math.max(1, Math.ceil(data.totalCount / 20));
                if ((page < totalPages && items.length === 0) || items.length > data.totalCount)
                    throw new Error('Invalid history count or page.');
                if (page > totalPages) { page = totalPages; load(); return; }
            }
            currentRows = items.map(item => ({...item}));
            currentTab = selected;
            historyReady = true;
            render(items);
            $('s360Alert').className = 'd-none';
            $('s360Status').textContent = items.length
                ? msg('Authorized historical records', 'অনুমোদিত ঐতিহাসিক তথ্য')
                : msg('No history recorded.', 'কোনো ইতিহাস সংরক্ষিত নেই।');
        } catch (error) {
            if (ticket !== token || local.signal.aborted || !sameSession()) return;
            $('s360Rows').replaceChildren();
            totalPages = page;
            historyReady = false;
            historyFailed = true;
            currentRows = [];
            alert(error.message || msg('Could not load history.', 'ইতিহাস লোড করা যায়নি।'));
            $('s360Status').textContent = msg('Unavailable (not zero).', 'অনুপলব্ধ (শূন্য নয়)।');
        } finally {
            if (ticket === token) { busy = false; controls(); }
        }
    }
    async function profile() {
        profileController?.abort();
        const local = new AbortController();
        profileController = local;
        try {
            const response = await fetch('/api/students/' + encodeURIComponent(reference),
                {credentials: 'same-origin', cache: 'no-store', signal: local.signal, headers: {Accept: 'application/json'}});
            const json = await response.json().catch(() => null);
            if (!sameSession() || local.signal.aborted) return;
            if (!response.ok || json?.success !== true || !json.data || typeof json.data !== 'object') throw Error('Profile unavailable.');
            const d = json.data, host = $('s360Identity');
            const body = document.createElement('div'); body.className = 'card-body';
            const title = node('h2', d.fullNameBangla || d.fullName); title.className = 'h5';
            const summary = node('p', [d.studentCode, d.academicLevelName, d.academicBatchName, d.status]
                .filter(Boolean).join(' · ')); summary.className = 'mb-0 text-muted';
            body.append(title, summary); host.replaceChildren(body);
            profileReady = true;
            load();
        } catch {
            if (!sameSession() || local.signal.aborted) return;
            $('s360Identity').textContent = msg('Student unavailable or access denied.', 'শিক্ষার্থীর তথ্য অনুপলব্ধ অথবা অনুমতি নেই।');
            alert(msg('Cannot open Student 360.', 'Student 360 খোলা যায়নি।'));
        }
    }
    document.querySelectorAll('[data-s360-tab]').forEach(button => button.addEventListener('click', () => {
        if (!sameSession()) return;
        tab = button.dataset.s360Tab; page = 1; totalPages = 1;
        historyReady = false; currentRows = []; currentTab = null;
        document.querySelectorAll('[data-s360-tab]').forEach(b => {
            const active = b === button;
            b.className = active ? 'btn btn-primary' : 'btn btn-outline-primary';
            b.setAttribute('aria-pressed', String(active));
        });
        load();
    }));
    $('s360Previous').addEventListener('click', () => { if (!busy && historyReady && sameSession() && page > 1) { page--; load(); } });
    $('s360Next').addEventListener('click', () => { if (!busy && historyReady && sameSession() && page < totalPages) { page++; load(); } });
    $('s360Retry').addEventListener('click', () => { if (!busy && profileReady && sameSession()) load(); });
    function authorizedPage() {
        return !busy && profileReady && historyReady && currentTab === tab &&
            currentRows.length > 0 && sameSession() ? currentRows : null;
    }
    function pageCells() {
        const rows = authorizedPage();
        if (!rows) return null;
        const cols = configs[tab].columns;
        const headers = cols.map(col => bn ? col[2] : col[1]);
        const body = rows.map(item => cols.map(([field]) => {
            const value = item[field];
            if (typeof value === 'boolean') return value ? msg('Yes', 'হ্যাঁ') : msg('No', 'না');
            if (value == null) return '';
            if (['occurredAt', 'registeredAt', 'approvedAt'].includes(field)) {
                const d = new Date(value);
                return Number.isNaN(d.getTime()) ? '' : d.toLocaleString(bn ? 'bn-BD' : 'en-BD');
            }
            return String(value);
        }));
        return {headers, body};
    }
    function csvCell(value) {
        // Quoting alone does not neutralize spreadsheet formulas after ignored prefixes.
        const text = String(value);
        const safe = /^[\s\uFEFF\u200B-\u200D\u2060]*[=+@-]/u.test(text) || /^[\t\r\n]/.test(text)
            ? "'" + text : text;
        return '"' + safe.replace(/"/g, '""') + '"';
    }
    $('s360Export').addEventListener('click', () => {
        const cells = pageCells();
        if (!cells) return;
        const content = '\uFEFF' + [cells.headers, ...cells.body].map(row => row.map(csvCell).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob([content], {type: 'text/csv;charset=utf-8'}));
        const a = document.createElement('a');
        a.href = url;
        a.download = 'student-history-' + tab + '-page-' + page + '.csv';
        document.body.append(a);
        a.click();
        a.remove();
        URL.revokeObjectURL(url);
    });
    $('s360Print').addEventListener('click', () => {
        const cells = pageCells();
        if (!cells) return;
        // Print only the currently displayed authorized projection, never a broader report.
        const w = window.open('', '_blank');
        if (!w) { alert(msg('Pop-up blocked. Allow pop-ups to print.', 'প্রিন্ট পপ-আপ বন্ধ আছে।')); return; }
        w.opener = null;
        const doc = w.document;
        const title = doc.createElement('h1');
        title.textContent = msg('Student history — ', 'শিক্ষার্থীর ইতিহাস — ') + tab + ' (' + page + ')';
        const table = doc.createElement('table');
        table.style.borderCollapse = 'collapse';
        const tr = doc.createElement('tr');
        cells.headers.forEach(value => { const th = doc.createElement('th'); th.textContent = value; th.style.border = '1px solid #888'; tr.append(th); });
        table.append(tr);
        cells.body.forEach(row => {
            const r = doc.createElement('tr');
            row.forEach(value => { const td = doc.createElement('td'); td.textContent = value; td.style.border = '1px solid #888'; r.append(td); });
            table.append(r);
        });
        doc.body.append(title, table);
        w.print();
    });
    window.addEventListener('pagehide', () => { controller?.abort(); profileController?.abort();
        ++token; busy = false; historyReady = false; currentRows = []; currentTab = null; controls(); });
    profile();
})();
