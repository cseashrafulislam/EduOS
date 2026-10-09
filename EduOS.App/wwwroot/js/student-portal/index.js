(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('studentPortalTitle')) return;
    const select = el('studentPortalStudent'), tabs = el('studentPortalTabs');
    let section = 'timetable', latestRequest = 0;
    const columns = {
        timetable: [['dayOfWeek','Day'],['startTime','Start'],['endTime','End'],['subjectName','Subject'],['teacherName','Teacher'],['roomNo','Room']],
        attendance: [['date','Date'],['status','Status'],['inTime','In'],['outTime','Out'],['remarks','Remarks']],
        results: [['examName','Exam'],['totalMark','Marks'],['totalFullMark','Full marks'],['percentage','Percent'],['grade','Grade'],['position','Position']],
        transport: [['routeName','Route'],['vehicleNo','Vehicle'],['pickupPoint','Pickup'],['startDate','Start'],['monthlyFare','Monthly fare'],['isActive','Active']],
        homework: [['subjectName','Subject'],['title','Title'],['assignedDate','Assigned'],['dueDate','Due'],['description','Description']],
        assignments: [['courseTitle','Course'],['title','Assignment'],['totalMark','Full marks'],['dueDate','Due'],['description','Instructions']],
        fees: [['invoiceNo','Invoice'],['billedAmount','Billed'],['paidAmount','Paid'],['dueAmount','Due'],['dueDate','Due date'],['status','Status']]
    };
    function notify(message, kind = 'danger') {
        const box = el('studentPortalAlert'); box.className = 'alert alert-' + kind; box.textContent = message; box.focus();
    }
    function clearAlert() { el('studentPortalAlert').className = 'd-none'; el('studentPortalAlert').textContent = ''; }
    async function api(url) {
        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
        const data = await response.json().catch(() => null);
        if (!response.ok || !data?.success) throw new Error(data?.message || 'The requested section is unavailable.');
        return data.data;
    }
    function cell(value) {
        const td = document.createElement('td');
        if (typeof value === 'boolean') td.textContent = value ? 'Yes' : 'No';
        else if (value === null || value === undefined || value === '') td.textContent = '—';
        else td.textContent = String(value);
        return td;
    }
    function display(value, key) {
        if (value === null || value === undefined) return '—';
        if (['date', 'startDate', 'dueDate', 'assignedDate'].includes(key)) return String(value).slice(0,10);
        if (['startTime', 'endTime', 'inTime', 'outTime'].includes(key)) return String(value).slice(0,5);
        return value;
    }
    function render(items, mapping, noData = 'No records in this section.') {
        const head = el('studentPortalHeaders'), body = el('studentPortalRows');
        head.replaceChildren(); body.replaceChildren();
        const header = document.createElement('tr');
        mapping.forEach(([,label]) => { const th = document.createElement('th'); th.scope = 'col'; th.textContent = label; header.append(th); });
        head.append(header);
        if (!Array.isArray(items) || !items.length) {
            const tr = document.createElement('tr'), td = cell(noData); td.colSpan = mapping.length;
            td.className = 'text-center text-muted py-3'; tr.append(td); body.append(tr); return;
        }
        for (const item of items) {
            const tr = document.createElement('tr');
            mapping.forEach(([key]) => tr.append(cell(display(item[key], key))));
            body.append(tr);
        }
    }
    function localDate(value) {
        const d = new Date(value);
        return [d.getFullYear(), String(d.getMonth()+1).padStart(2,'0'), String(d.getDate()).padStart(2,'0')].join('-');
    }
    async function loadStudents() {
        try {
            const linked = await api('/api/portal/students');
            select.replaceChildren();
            const opt = document.createElement('option'); opt.value = ''; opt.textContent = 'Select student'; select.append(opt);
            for (const student of linked || []) {
                const item = document.createElement('option'); item.value = student.reference;
                item.textContent = student.name + ' · ' + student.studentCode + (student.roll ? ' · Roll ' + student.roll : '');
                select.append(item);
            }
            if (linked?.length === 1) { select.value = linked[0].reference; loadCurrent(); }
            if (!linked?.length) notify('No student is linked to your account.', 'warning');
        } catch (error) { notify(error.message); }
    }
    async function loadCurrent() {
        const student = select.value;
        const token = ++latestRequest;
        clearAlert(); el('studentPortalExtra').replaceChildren(); el('studentPortalSummary').textContent = '';
        el('studentPortalSectionTitle').textContent = section.charAt(0).toUpperCase() + section.slice(1);
        if (!student) { render([], columns[section], 'Choose a student to continue.'); return; }
        try {
            let url = '/api/portal/students/' + encodeURIComponent(student) + '/' + section;
            if (section === 'attendance') {
                const from = el('studentPortalFrom').value, to = el('studentPortalTo').value;
                if (!from || !to || from > to) throw new Error('Choose a valid attendance date range.');
                url += '?' + new URLSearchParams({ fromDate: from, toDate: to });
            }
            const data = await api(url);
            if (token !== latestRequest) return;
            if (section === 'fees') {
                render(data?.invoices || [], columns.fees);
                el('studentPortalSummary').textContent = 'Total billed ' + (data?.totalBilled ?? 0) +
                    ' · Paid ' + (data?.totalPaid ?? 0) + ' · Due ' + (data?.totalDue ?? 0);
                const extra = el('studentPortalExtra'), h = document.createElement('h4');
                h.className = 'h6'; h.textContent = 'Payments'; extra.append(h);
                const list = document.createElement('ul'); list.className = 'list-group';
                (data?.payments || []).forEach(x => {
                    const li = document.createElement('li'); li.className = 'list-group-item';
                    li.textContent = (x.receiptNo || 'Receipt') + ' · ' + x.amount + ' · ' + String(x.paymentDate || '').slice(0,10);
                    list.append(li);
                });
                if (!(data?.payments || []).length) {
                    const li = document.createElement('li'); li.className = 'list-group-item text-muted'; li.textContent = 'No payments recorded.'; list.append(li);
                }
                extra.append(list);
            } else render(data || [], columns[section]);
        } catch (error) {
            if (token !== latestRequest) return;
            render([], columns[section], 'This section cannot be loaded.');
            notify(error.message);
        }
    }
    select.addEventListener('change', loadCurrent);
    tabs.addEventListener('click', event => {
        const button = event.target.closest('button[data-portal-tab]');
        if (!button) return;
        section = button.dataset.portalTab;
        tabs.querySelectorAll('button[data-portal-tab]').forEach(b => { b.className = b === button ? 'btn btn-primary' : 'btn btn-outline-primary'; });
        loadCurrent();
    });
    el('studentPortalRefresh').addEventListener('click', loadCurrent);
    el('studentPortalFrom').addEventListener('change', () => { if (section === 'attendance') loadCurrent(); });
    el('studentPortalTo').addEventListener('change', () => { if (section === 'attendance') loadCurrent(); });
    el('studentPortalTo').value = localDate(Date.now());
    el('studentPortalFrom').value = localDate(Date.now() - 30 * 86400000);
    loadStudents();
})();
