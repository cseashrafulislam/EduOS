(() => {
    'use strict';
    const node = id => document.getElementById(id);
    if (!node('hostelApplicationSelf') && !node('hostelApplicationQueue')) return;
    const bn = document.documentElement.lang.toLowerCase().startsWith('bn');
    const words = {
        requestTitle: ['My hostel applications', 'আমার হোস্টেল আবেদন'],
        chooseHostel: ['Eligible hostel', 'উপযুক্ত হোস্টেল'], start: ['Arrival', 'প্রবেশ'],
        end: ['Checkout (optional)', 'ছাড়ার তারিখ (ঐচ্ছিক)'], note: ['Request note (optional)', 'আবেদনের মন্তব্য (ঐচ্ছিক)'],
        submit: ['Submit application', 'আবেদন জমা দিন'], hostel: ['Hostel', 'হোস্টেল'],
        period: ['Period', 'সময়কাল'], state: ['Status', 'অবস্থা'], offerExpiry: ['Offer expiry (UTC)', 'অফারের মেয়াদ (UTC)'],
        actions: ['Actions', 'কাজ'], previous: ['Previous', 'পূর্ববর্তী'], next: ['Next', 'পরবর্তী'],
        queueTitle: ['Application & waitlist queue', 'আবেদন ও অপেক্ষমাণ তালিকা'],
        exportPage: ['Export current page', 'বর্তমান পৃষ্ঠা এক্সপোর্ট'], search: ['Search', 'খুঁজুন'],
        student: ['Student', 'শিক্ষার্থী'], submitted: ['Submitted (UTC)', 'আবেদনের সময় (UTC)'],
        offeredBed: ['Bed for non-binding offer', 'শর্তসাপেক্ষ অফারের বেড'], hours: ['Valid hours', 'মেয়াদ (ঘণ্টা)'],
        decisionReason: ['Decision reason', 'সিদ্ধান্তের কারণ'], cancel: ['Cancel', 'বাতিল'],
        confirm: ['Confirm decision', 'সিদ্ধান্ত নিশ্চিত']
    };
    const t = key => words[key]?.[bn ? 1 : 0] || key;
    document.querySelectorAll('[data-hostel-i18n]').forEach(el => { el.textContent = t(el.dataset.hostelI18n); });
    const states = ['Unknown', 'Submitted', 'Waitlisted', 'Offered', 'Accepted', 'Rejected', 'Withdrawn', 'Expired'];
    const stateName = value => typeof value === 'number' ? states[value] || 'Unknown' : value;
    const today = () => {
        const d = new Date();
        return [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'), String(d.getDate()).padStart(2, '0')].join('-');
    };
    const td = text => { const cell = document.createElement('td'); cell.textContent = text ?? '—'; return cell; };
    const button = (label, fn, style = 'outline-primary') => {
        const item = document.createElement('button'); item.type = 'button';
        item.className = 'btn btn-' + style + ' btn-sm me-1 mb-1'; item.textContent = label;
        item.addEventListener('click', async () => { item.disabled = true; try { await fn(); } finally { item.disabled = false; } });
        return item;
    };
    const status = (id, message, error = false) => {
        const item = node(id); if (!item) return;
        item.className = 'small ' + (error ? 'text-danger' : 'text-muted'); item.textContent = message;
    };
    async function api(url, method = 'GET', data) {
        const headers = { Accept: 'application/json' };
        if (data !== undefined) {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload this page.');
            headers.RequestVerificationToken = token;
            headers['Content-Type'] = 'application/json';
        }
        const response = await fetch(url, { method, cache: 'no-store', credentials: 'same-origin', headers,
            ...(data === undefined ? {} : { body: JSON.stringify(data) }) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Request failed.');
        return result.data;
    }
    const period = item => item.startDate + ' – ' + (item.endDate || 'Open');
    function showEmpty(target, columns, message) {
        target.replaceChildren(); const tr = document.createElement('tr'), cell = td(message);
        cell.colSpan = columns; tr.append(cell); target.append(tr);
    }
    let myPage = 1, myTotal = 1, myRequestId = crypto.randomUUID(), queuePage = 1, queueTotal = 1;
    let myLookup = 0, queueLookup = 0, decision = null, bedPage = 1;

    async function loadOptions() {
        if (!node('hostelApplicationOption')) return;
        try {
            const result = await api('/api/hostel/applications/options?page=1&pageSize=50');
            const selector = node('hostelApplicationOption'); selector.replaceChildren();
            const placeholder = document.createElement('option'); placeholder.value = '';
            placeholder.textContent = bn ? 'হোস্টেল নির্বাচন করুন' : 'Select eligible hostel'; selector.append(placeholder);
            (result.items || []).forEach(item => {
                const choice = document.createElement('option'); choice.value = item.enrollmentReference + '|' + item.hostelReference;
                choice.textContent = item.campusName + ' · ' + item.hostelName; selector.append(choice);
            });
            if (!result.items?.length) status('hostelApplicationStatus', bn ? 'সক্রিয় শিক্ষার্থী তালিকায় নেই।' : 'No eligible active student enrollment.');
            if (result.totalPages > 1) status('hostelApplicationStatus', bn ? 'প্রথম ৫০টি হোস্টেল দেখানো হচ্ছে।' : 'Showing first 50 eligible hostels.');
        } catch (error) { status('hostelApplicationStatus', error.message, true); }
    }

    async function loadMine() {
        if (!node('hostelApplicationMineRows')) return;
        const token = ++myLookup;
        try {
            const result = await api('/api/hostel/applications/my?page=' + myPage + '&pageSize=20');
            if (token !== myLookup) return;
            myTotal = Math.max(1, result.totalPages || 1);
            const target = node('hostelApplicationMineRows'); target.replaceChildren();
            if (!(result.items || []).length) showEmpty(target, 5, bn ? 'কোনো আবেদন নেই' : 'No applications yet.');
            for (const item of result.items || []) {
                const tr = document.createElement('tr');
                [item.hostelName, period(item), stateName(item.state), item.offerExpiresAtUtc || '—'].forEach(v => tr.append(td(v)));
                const actions = document.createElement('td');
                if (stateName(item.state) === 'Offered')
                    actions.append(button(bn ? 'গ্রহণ' : 'Accept offer', () => accept(item), 'success'));
                if (['Submitted', 'Waitlisted', 'Offered'].includes(stateName(item.state)))
                    actions.append(button(bn ? 'প্রত্যাহার' : 'Withdraw', () => change(item, 'withdraw'), 'outline-danger'));
                actions.append(button(bn ? 'ইতিহাস' : 'History', () => history(item)));
                tr.append(actions); target.append(tr);
            }
            node('hostelApplicationMinePage').textContent = myPage + ' / ' + myTotal;
            node('hostelApplicationMinePrev').disabled = myPage <= 1;
            node('hostelApplicationMineNext').disabled = myPage >= myTotal;
        } catch (error) { status('hostelApplicationStatus', error.message, true); }
    }

    function queueParams(includePage = true) {
        const p = new URLSearchParams({ pageSize: '25' });
        if (includePage) p.set('page', String(queuePage));
        const term = node('hostelApplicationSearch').value.trim();
        if (term) p.set('search', term);
        const state = node('hostelApplicationState').value;
        if (state) p.set('state', state);
        return p;
    }
    async function loadQueue() {
        if (!node('hostelApplicationQueueRows')) return;
        const token = ++queueLookup;
        try {
            const result = await api('/api/hostel/applications/queue?' + queueParams());
            if (token !== queueLookup) return;
            queueTotal = Math.max(1, result.totalPages || 1);
            const target = node('hostelApplicationQueueRows'); target.replaceChildren();
            if (!(result.items || []).length) showEmpty(target, 6, bn ? 'কোনো আবেদন নেই' : 'No matching applications.');
            for (const item of result.items || []) {
                const tr = document.createElement('tr');
                [item.studentName + ' (' + item.studentCode + ')', item.hostelName, period(item),
                    stateName(item.state), item.submittedAtUtc].forEach(v => tr.append(td(v)));
                const actions = document.createElement('td'), state = stateName(item.state);
                if (state === 'Submitted')
                    actions.append(button(bn ? 'অপেক্ষমাণ' : 'Waitlist', () => change(item, 'waitlist')));
                if (state === 'Submitted' || state === 'Waitlisted')
                    actions.append(button(bn ? 'অফার' : 'Offer', () => chooseDecision(item, 'offer'), 'outline-success'));
                if (['Submitted', 'Waitlisted', 'Offered'].includes(state))
                    actions.append(button(bn ? 'প্রত্যাখ্যান' : 'Reject', () => chooseDecision(item, 'reject'), 'outline-danger'));
                actions.append(button(bn ? 'ইতিহাস' : 'History', () => history(item)));
                tr.append(actions); target.append(tr);
            }
            node('hostelApplicationQueuePage').textContent = queuePage + ' / ' + queueTotal;
            node('hostelApplicationQueuePrev').disabled = queuePage <= 1;
            node('hostelApplicationQueueNext').disabled = queuePage >= queueTotal;
        } catch (error) { status('hostelApplicationQueueStatus', error.message, true); }
    }

    async function change(item, action, extra = {}) {
        const own = action === 'withdraw';
        const target = own ? 'hostelApplicationStatus' : 'hostelApplicationQueueStatus';
        try {
            await api('/api/hostel/applications/' + item.id + '/' + action, 'POST',
                { rowVersion: item.rowVersion, ...extra });
            status(target, bn ? 'সফলভাবে হালনাগাদ হয়েছে।' : 'Application updated.');
            await Promise.all([loadMine(), loadQueue()]);
        } catch (error) { status(target, error.message, true); }
    }

    async function accept(item) {
        try {
            await api('/api/hostel/applications/' + item.id + '/accept', 'POST', { rowVersion: item.rowVersion });
            status('hostelApplicationStatus', bn ? 'বেড বরাদ্দ হয়েছে।' : 'Bed allocated. Check your stay history.');
            await Promise.all([loadMine(), loadQueue()]);
        } catch (error) { status('hostelApplicationStatus', error.message, true); }
    }

    async function history(item) {
        const target = node('hostelApplicationQueue') ? 'hostelApplicationQueueStatus' : 'hostelApplicationStatus';
        try {
            const entries = await api('/api/hostel/applications/' + item.id + '/events');
            const summary = entries.map(e => stateName(e.toState) + (e.reason ? ': ' + e.reason : '')).join(' | ');
            status(target, summary || (bn ? 'ইতিহাস নেই' : 'No history recorded.'));
        } catch (error) { status(target, error.message, true); }
    }

    async function chooseDecision(item, action) {
        decision = { item, action }; bedPage = 1;
        const form = node('hostelApplicationDecision'); form.classList.remove('d-none');
        node('hostelApplicationDecisionTitle').textContent = action === 'offer' ?
            (bn ? 'শর্তসাপেক্ষ বেড অফার' : 'Non-binding bed offer') : (bn ? 'আবেদন প্রত্যাখ্যান' : 'Reject application');
        node('hostelApplicationOfferedBed').closest('.col-md-5').classList.toggle('d-none', action !== 'offer');
        node('hostelApplicationOfferHours').closest('.col-md-2').classList.toggle('d-none', action !== 'offer');
        node('hostelApplicationReason').value = '';
        node('hostelApplicationReason').required = action === 'reject';
        if (action === 'offer') {
            try {
                const params = new URLSearchParams({ page: '1', pageSize: '100', search: item.hostelName,
                    startDate: item.startDate });
                const response = await api('/api/hostel/available-beds?' + params);
                const selector = node('hostelApplicationOfferedBed'); selector.replaceChildren();
                const empty = document.createElement('option'); empty.value = ''; empty.textContent = 'Choose bed'; selector.append(empty);
                (response.items || []).filter(b => b.hostelName === item.hostelName).forEach(b => {
                    const opt = document.createElement('option'); opt.value = b.bedId;
                    opt.textContent = b.hostelName + ' · ' + b.roomNumber + ' / ' + b.bedNumber; selector.append(opt);
                });
                if (response.totalPages > 1) status('hostelApplicationQueueStatus',
                    bn ? 'প্রথম ১০০টি বেড দেখানো হয়েছে; প্রয়োজন হলে বর্তমান Allocation page থেকে বেড নির্বাচন করুন।' :
                        'First 100 matching beds shown; narrow hostel inventory if needed.');
            } catch (error) { status('hostelApplicationQueueStatus', error.message, true); }
        }
        form.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    async function submitDecision(event) {
        event.preventDefault(); if (!decision) return;
        const { item, action } = decision;
        const extra = { reason: node('hostelApplicationReason').value.trim() || null };
        if (action === 'offer') {
            extra.offeredBedId = Number(node('hostelApplicationOfferedBed').value);
            extra.offerHours = Number(node('hostelApplicationOfferHours').value);
            if (!extra.offeredBedId || extra.offerHours < 1 || extra.offerHours > 168) {
                status('hostelApplicationQueueStatus', 'Choose an active bed and valid expiry.', true); return;
            }
        }
        await change(item, action, extra);
        decision = null; node('hostelApplicationDecision').classList.add('d-none');
    }

    if (node('hostelApplicationSelf')) {
        node('hostelApplicationStart').value = today();
        node('hostelApplicationSubmit').addEventListener('submit', async event => {
            event.preventDefault(); if (!event.currentTarget.reportValidity()) return;
            const selected = node('hostelApplicationOption').value.split('|');
            const start = node('hostelApplicationStart').value, end = node('hostelApplicationEnd').value;
            if (selected.length !== 2 || (end && end < start) ||
                (end && (Date.parse(end + 'T00:00:00Z') - Date.parse(start + 'T00:00:00Z')) / 86400000 > 89)) {
                status('hostelApplicationStatus', 'Choose a hostel and valid stay of up to 90 days.', true); return;
            }
            const submit = node('hostelApplicationSubmitButton'); submit.disabled = true;
            try {
                await api('/api/hostel/applications', 'POST', { clientRequestId: myRequestId,
                    studentEnrollmentReference: selected[0], hostelReference: selected[1],
                    startDate: start, endDate: end || null, note: node('hostelApplicationNote').value.trim() || null });
                myRequestId = crypto.randomUUID(); node('hostelApplicationNote').value = '';
                status('hostelApplicationStatus', bn ? 'আবেদন জমা হয়েছে।' : 'Application submitted.');
                myPage = 1; await loadMine();
            } catch (error) { status('hostelApplicationStatus', error.message, true); }
            finally { submit.disabled = false; }
        });
        node('hostelApplicationMinePrev').addEventListener('click', () => { if (myPage > 1) { myPage--; loadMine(); } });
        node('hostelApplicationMineNext').addEventListener('click', () => { if (myPage < myTotal) { myPage++; loadMine(); } });
        loadOptions(); loadMine();
    }
    if (node('hostelApplicationQueue')) {
        node('hostelApplicationQueueFilter').addEventListener('submit', event => { event.preventDefault(); queuePage = 1; loadQueue(); });
        node('hostelApplicationQueuePrev').addEventListener('click', () => { if (queuePage > 1) { queuePage--; loadQueue(); } });
        node('hostelApplicationQueueNext').addEventListener('click', () => { if (queuePage < queueTotal) { queuePage++; loadQueue(); } });
        node('hostelApplicationDecision').addEventListener('submit', submitDecision);
        node('hostelApplicationDecisionCancel').addEventListener('click', () => {
            decision = null; node('hostelApplicationDecision').classList.add('d-none');
        });
        node('hostelApplicationCsv').addEventListener('click', () => {
            const p = queueParams(); p.delete('pageSize');
            window.open('/api/hostel/applications/queue/export?' + p, '_blank', 'noopener,noreferrer');
        });
        loadQueue();
    }
})();
