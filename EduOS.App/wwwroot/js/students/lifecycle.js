(() => {
    'use strict';
    const root = typeof document === 'undefined' ? null : document.getElementById('studentLifecycle');
    const bn = root?.dataset.bn === 'true';
    const tr = (en, bd) => bn ? bd : en;
    function statusLabel(value, bangla = bn) {
        if (!bangla) return value;
        return { Active: 'সক্রিয়', TC: 'স্থানান্তরিত', Passed: 'উত্তীর্ণ',
            Dropout: 'পড়াশোনা ত্যাগ', Transfer: 'স্থানান্তর', Completed: 'শিক্ষা সমাপ্তি' }[value] || value;
    }
    function localizedError(message, status, bangla = bn) {
        if (!bangla) return message || 'Request failed. Reload and retry.';
        const known = {
            'Student not found.': 'শিক্ষার্থী পাওয়া যায়নি।',
            'Student exit permission required.': 'শিক্ষার্থী প্রস্থানের অনুমতি নেই।',
            'Student exit request is invalid.': 'প্রস্থানের তথ্য সঠিক নয়।',
            'Only an active student can exit.': 'শুধু সক্রিয় শিক্ষার্থীর প্রস্থান করা যাবে।',
            'Student changed. Reload and retry.': 'শিক্ষার্থীর তথ্য পরিবর্তিত হয়েছে। রিলোড করে আবার চেষ্টা করুন।',
            'An active current enrollment is required.': 'সক্রিয় বর্তমান ভর্তি থাকতে হবে।',
            'Student has an existing final exit.': 'এই ভর্তির চূড়ান্ত প্রস্থান আগেই হয়েছে।',
            'Outstanding fees must be settled.': 'আগে সব বকেয়া ফি পরিশোধ করুন।',
            'Request ID is already used for a different exit.': 'একই অনুরোধ নম্বর অন্য প্রস্থানে ব্যবহৃত হয়েছে।',
            'Exit conflicts with an existing transaction.': 'আগের লেনদেনের সঙ্গে সংঘাত হয়েছে। রিলোড করুন।',
            'Exit transaction conflicted with another update.': 'অন্য পরিবর্তনের সঙ্গে সংঘাত হয়েছে। রিলোড করুন।',
            'Student exit could not be processed.': 'শিক্ষার্থী প্রস্থান সম্পন্ন হয়নি। আবার চেষ্টা করুন।',
            'Exit type must be Transfer, Completed or Dropout.': 'স্থানান্তর, শিক্ষা সমাপ্তি বা পড়াশোনা ত্যাগ নির্বাচন করুন।',
            'Transfer reason is required.': 'স্থানান্তরের কারণ প্রয়োজন।',
            'Exit comments are too long.': 'মন্তব্য নির্ধারিত সীমার বেশি হয়েছে।'
        };
        return known[message] || (status === 403 ? 'এই কাজের অনুমতি নেই।'
            : status === 409 ? 'তথ্যে সংঘাত হয়েছে। বর্তমান অবস্থা যাচাই করে রিলোড করুন।'
            : 'অনুরোধ ব্যর্থ হয়েছে। বর্তমান অবস্থা যাচাই করে আবার চেষ্টা করুন।');
    }
    if (typeof module !== 'undefined' && module.exports)
        module.exports = { statusLabel, localizedError };
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { selected: null, requestId: null, saving: false };
    const value = id => el(id).value.trim();
    function alert(message, error = false) {
        const box = el('lifecycleAlert');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = message;
        box.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
    async function api(url, options = {}) {
        const response = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store', ...options,
            headers: {
                Accept: 'application/json',
                ...(options.body ? { 'Content-Type': 'application/json' } : {}),
                ...(options.method && options.method !== 'GET' ? { RequestVerificationToken: token } : {}),
                ...(options.headers || {})
            }
        });
        const json = await response.json().catch(() => null);
        if (!response.ok || json?.success !== true)
            throw new Error(localizedError(json?.message, response.status));
        return json.data;
    }
    function requestId() {
        if (!state.requestId) {
            if (typeof crypto.randomUUID === 'function') state.requestId = crypto.randomUUID();
            else {
                const bytes = new Uint8Array(16);
                crypto.getRandomValues(bytes);
                bytes[6] = bytes[6] & 15 | 64; bytes[8] = bytes[8] & 63 | 128;
                const h = [...bytes].map(x => x.toString(16).padStart(2, '0')).join('');
                state.requestId = [h.slice(0, 8), h.slice(8, 12), h.slice(12, 16),
                    h.slice(16, 20), h.slice(20)].join('-');
            }
        }
        return state.requestId;
    }
    function canProcess() {
        return !state.saving && state.selected?.status === 'Active' &&
            Boolean(state.selected.rowVersion) && Boolean(value('lifecycleReason')) &&
            Boolean(value('lifecycleExitType'));
    }
    function refresh() { el('lifecycleSubmit').disabled = !canProcess(); }
    async function search(event) {
        event?.preventDefault();
        const query = value('lifecycleSearchText');
        if (query.length < 2) return alert(tr('Enter at least two characters to search.', 'খুঁজতে অন্তত দুইটি অক্ষর লিখুন।'), true);
        const box = el('lifecycleMatches');
        box.textContent = tr('Searching...', 'খোঁজা হচ্ছে...');
        try {
            const page = await api('/api/students?' + new URLSearchParams({ search: query, page: 1, pageSize: 20 }));
            box.replaceChildren();
            if (!page?.items?.length) { box.textContent = tr('No matching students.', 'মিলে যাওয়া শিক্ষার্থী নেই।'); return; }
            for (const item of page.items) {
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'list-group-item list-group-item-action';
                button.textContent = item.fullName + ' — ' + item.studentCode + ' (' + statusLabel(item.status) + ')';
                button.addEventListener('click', () => selectStudent(item.reference));
                box.append(button);
            }
        } catch (e) { box.textContent = ''; alert(e.message, true); }
    }
    async function selectStudent(reference) {
        state.selected = null; state.requestId = null; refresh();
        try {
            const details = await api('/api/students/' + encodeURIComponent(reference));
            const history = await api('/api/students/exit/' + encodeURIComponent(reference) + '/history');
            if (details.reference !== reference) throw new Error(tr('Unexpected student reference returned.', 'অপ্রত্যাশিত শিক্ষার্থী রেফারেন্স পাওয়া গেছে।'));
            state.selected = details;
            el('lifecycleStudent').classList.remove('d-none');
            el('lifecycleName').textContent = details.fullName;
            el('lifecycleCode').textContent = details.studentCode;
            el('lifecycleStatus').textContent = statusLabel(details.status);
            el('lifecycleCurrent').textContent = (details.enrollments || []).length +
                tr(' enrollment history rows (up to 100).', 'টি ভর্তির ইতিহাস (সর্বোচ্চ ১০০)।');
            renderHistory(history || []);
            refresh();
        } catch (e) { alert(e.message, true); }
    }
    function renderHistory(items) {
        const body = el('lifecycleHistory');
        body.replaceChildren();
        if (!items.length) {
            const row = document.createElement('tr'), cell = document.createElement('td');
            cell.colSpan = 5; cell.textContent = tr('No final exit recorded.', 'কোনো চূড়ান্ত প্রস্থানের ইতিহাস নেই।');
            row.append(cell); body.append(row); return;
        }
        for (const item of items) {
            const row = document.createElement('tr');
            for (const [index, text] of [
                item.processedAtUtc, item.exitType, item.finalStatus,
                item.certificateNo || '—', String(item.dueAtExit ?? 0)
            ].entries()) {
                const cell = document.createElement('td');
                if (index === 3 && item.certificateNo) {
                    const link = document.createElement('a');
                    link.href = '/StudentLifecycle/Certificate?' + new URLSearchParams({
                        studentReference: item.studentReference,
                        certificateNumber: item.certificateNo
                    });
                    link.target = '_blank';
                    link.rel = 'noopener noreferrer';
                    link.textContent = item.certificateNo + tr(' · Print', ' · প্রিন্ট');
                    cell.append(link);
                } else cell.textContent = (index === 1 || index === 2) ? statusLabel(text) : (text ?? '');
                row.append(cell);
            }
            body.append(row);
        }
    }
    async function submit(event) {
        event.preventDefault();
        if (!canProcess()) return alert(tr('Select an active student and provide a reason.', 'সক্রিয় শিক্ষার্থী নির্বাচন করে কারণ লিখুন।'), true);
        const chosen = state.selected;
        const reason = value('lifecycleReason'), conductRemark = value('lifecycleConduct') || null;
        const exitType = value('lifecycleExitType');
        if (!confirm(tr('Finalize ', 'চূড়ান্ত করুন: ') + statusLabel(exitType) + tr(' for ', ' — ') + chosen.studentCode +
            tr('? This closes the current enrollment and cannot be silently undone.', '? বর্তমান ভর্তি বন্ধ হবে; নীরবে ফিরিয়ে আনা যাবে না।'))) return;
        const payload = {
            clientRequestId: requestId(), studentReference: chosen.reference,
            exitType, reason, conductRemark, studentRowVersion: chosen.rowVersion
        };
        state.saving = true; refresh();
        try {
            const result = await api('/api/students/exit', { method: 'POST', body: JSON.stringify(payload) });
            state.requestId = null;
            await selectStudent(chosen.reference);
            alert(tr('Student exit completed.', 'শিক্ষার্থী প্রস্থান সম্পন্ন হয়েছে।') + (result.certificateNo ? tr(' Transfer certificate: ', ' স্থানান্তর সনদ: ') + result.certificateNo : ''));
        } catch (e) { alert(e.message, true); }
        finally { state.saving = false; refresh(); }
    }
    el('lifecycleStudentSearch').addEventListener('submit', search);
    el('lifecycleExitForm').addEventListener('submit', submit);
    for (const id of ['lifecycleExitType', 'lifecycleReason', 'lifecycleConduct']) {
        el(id).addEventListener('input', () => { state.requestId = null; refresh(); });
        el(id).addEventListener('change', () => { state.requestId = null; refresh(); });
    }
    const preselected = new URLSearchParams(location.search).get('studentReference');
    if (preselected && /^[0-9a-f-]{36}$/i.test(preselected)) selectStudent(preselected);
})();
