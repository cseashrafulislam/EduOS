(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const root = el('learnerRescrutiny');
    if (!root) return;
    const bn = root.dataset.lang === 'bn';
    const t = (en, bd) => bn ? bd : en;
    const student = el('learnerRescrutinyStudent');
    const publication = el('learnerRescrutinyPublication');
    const form = el('learnerRescrutinyForm');
    const reason = el('learnerRescrutinyReason');
    const submit = el('learnerRescrutinySubmit');
    const rows = el('learnerRescrutinyRows');
    let resultPage = 1, resultTotal = 0, cursors = [null], casePage = 0;
    let pendingKey = null, generation = 0, caseGeneration = 0;
    const token = document.querySelector('#learnerRescrutiny input[name="__RequestVerificationToken"]')?.value;
    const notify = (message, error = true) => {
        const box = el('learnerRescrutinyNotice');
        box.className = 'alert alert-' + (error ? 'danger' : 'success');
        box.textContent = message;
    };
    const endpoint = () => '/api/portal/students/' + encodeURIComponent(student.value) + '/rescrutiny';
    async function api(url, options = {}) {
        const response = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store',
            ...options,
            headers: { Accept: 'application/json', ...(options.headers || {}) }
        });
        const body = await response.json().catch(() => null);
        if (!response.ok || !body?.success) throw new Error(body?.message || t('Request failed.', 'অনুরোধ ব্যর্থ হয়েছে।'));
        return body.data;
    }
    function option(value, label) {
        const item = document.createElement('option');
        item.value = String(value); item.textContent = label; return item;
    }
    function status(value) {
        return Number(value) === 2 ? t('Rejected', 'প্রত্যাখ্যাত') :
            Number(value) === 3 ? t('Accepted for correction review', 'সংশোধন পর্যালোচনার জন্য গৃহীত') :
                t('Pending', 'অপেক্ষমাণ');
    }
    function empty(message) {
        rows.replaceChildren();
        const tr = document.createElement('tr'), td = document.createElement('td');
        td.colSpan = 4; td.className = 'text-muted py-3 text-center'; td.textContent = message;
        tr.append(td); rows.append(tr);
    }
    function resetCases() {
        cursors = [null]; casePage = 0; ++caseGeneration;
        el('learnerRescrutinyCasesPrev').disabled = true;
        el('learnerRescrutinyCasesNext').disabled = true;
        el('learnerRescrutinyPage').textContent = '';
        empty(t('Select a published result.', 'প্রকাশিত ফলাফল নির্বাচন করুন।'));
    }
    async function loadCases() {
        const id = publication.value, ref = student.value, version = ++caseGeneration;
        if (!id || !ref) { resetCases(); return; }
        try {
            const params = new URLSearchParams({ publicationId: id });
            if (cursors[casePage]) params.set('beforeId', String(cursors[casePage]));
            const data = await api(endpoint() + '?' + params);
            if (version !== caseGeneration || student.value !== ref || publication.value !== id) return;
            rows.replaceChildren();
            if (!data.items?.length) empty(t('No applications.', 'কোনো আবেদন নেই।'));
            for (const item of data.items || []) {
                const tr = document.createElement('tr');
                const values = [item.requestedAt?.slice(0, 16) || '—',
                    status(item.status), item.requestReason || '—', item.decisionReason || '—'];
                for (const value of values) {
                    const td = document.createElement('td');
                    td.textContent = value; tr.append(td);
                }
                rows.append(tr);
            }
            el('learnerRescrutinyCasesPrev').disabled = casePage === 0;
            el('learnerRescrutinyCasesNext').disabled = !data.hasMore;
            if (data.hasMore) cursors[casePage + 1] = data.nextBeforeId;
            el('learnerRescrutinyPage').textContent = t('Page ', 'পৃষ্ঠা ') + (casePage + 1);
        } catch (error) {
            if (version === caseGeneration) { empty(t('Could not load applications.', 'আবেদন লোড করা যায়নি।')); notify(error.message); }
        }
    }
    async function loadResults() {
        const ref = student.value, version = ++generation;
        publication.replaceChildren(option('', t('Select result', 'ফলাফল নির্বাচন করুন')));
        publication.disabled = true; submit.disabled = true; resetCases();
        if (!ref) return;
        try {
            const data = await api('/api/portal/students/' + encodeURIComponent(ref) +
                '/results?' + new URLSearchParams({ page: String(resultPage), pageSize: '25' }));
            if (version !== generation || student.value !== ref) return;
            resultTotal = data.totalCount || 0;
            for (const item of data.items || []) {
                publication.append(option(item.resultPublicationId,
                    (item.assessmentName || t('Assessment', 'মূল্যায়ন')) +
                    ' · v' + item.publicationVersionNo + ' · ' +
                    (item.publishedAt || '').slice(0, 10)));
            }
            publication.disabled = !data.items?.length;
            el('learnerRescrutinyResultsPrev').disabled = resultPage <= 1;
            el('learnerRescrutinyResultsNext').disabled = resultPage * 25 >= resultTotal;
        } catch (error) { if (version === generation) notify(error.message); }
    }
    async function loadStudents() {
        try {
            const data = await api('/api/portal/students');
            student.replaceChildren(option('', t('Select student', 'শিক্ষার্থী নির্বাচন করুন')));
            for (const item of data || []) student.append(option(item.reference,
                item.name + ' · ' + item.studentCode));
            if (data?.length === 1) { student.value = data[0].reference; await loadResults(); }
        } catch (error) { notify(error.message); }
    }
    student.addEventListener('change', () => { resultPage = 1; pendingKey = null; loadResults(); });
    publication.addEventListener('change', () => {
        pendingKey = null; resetCases(); submit.disabled = !publication.value;
        if (publication.value) loadCases();
    });
    el('learnerRescrutinyResultsPrev').addEventListener('click', () => {
        if (resultPage > 1) { --resultPage; loadResults(); }
    });
    el('learnerRescrutinyResultsNext').addEventListener('click', () => {
        if (resultPage * 25 < resultTotal) { ++resultPage; loadResults(); }
    });
    el('learnerRescrutinyCasesPrev').addEventListener('click', () => {
        if (casePage > 0) { --casePage; loadCases(); }
    });
    el('learnerRescrutinyCasesNext').addEventListener('click', () => {
        if (cursors[casePage + 1]) { ++casePage; loadCases(); }
    });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (!student.value || !publication.value || !form.reportValidity() || submit.disabled) return;
        if (!pendingKey) pendingKey = crypto.randomUUID();
        const selectedStudent = student.value, selectedPublication = publication.value;
        submit.disabled = true;
        try {
            await api(endpoint(), {
                method: 'POST', headers: { 'Content-Type': 'application/json', RequestVerificationToken: token || '' },
                body: JSON.stringify({
                    clientRequestId: pendingKey, publicationId: Number(selectedPublication),
                    studentReference: selectedStudent, reason: reason.value.trim()
                })
            });
            pendingKey = null; reason.value = '';
            notify(t('Application submitted.', 'আবেদন জমা হয়েছে।'), false);
            cursors = [null]; casePage = 0;
            await loadCases();
        } catch (error) { notify(error.message); }
        finally { submit.disabled = !student.value || !publication.value; }
    });
    el('learnerRescrutinyPrint').addEventListener('click', () => window.print());
    loadStudents();
})();
