(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('studentPortalTitle')) return;
    const select = el('studentPortalStudent'), tabs = el('studentPortalTabs');
    let section = 'timetable', latestRequest = 0, page = 1, paymentPage = 1;
    const pageSize = 25, paged = new Set(['attendance','results','transport','homework','assignments','enrollments','subjects']);
    const columns = {
        enrollments: [['rollNo','Original roll'],['registrationNo','Registration'],['campusId','Campus ID'],['academicYearId','Year ID'],['academicLevelId','Level ID'],['academicBatchId','Batch ID'],['enrollmentDate','Enrolled'],['endDate','Ended'],['state','State'],['isCurrentEnrollment','Current']],
        subjects: [['rollNo','Original roll'],['campusId','Campus ID'],['subjectCode','Subject code'],['subjectName','Current subject label'],['creditHoursSnapshot','Registered credits'],['registrationState','Status'],['registeredAt','Registered'],['approvedAt','Approved']],
        timetable: [['dayOfWeek','Day'],['startTime','Start'],['endTime','End'],['subjectName','Subject'],['teacherName','Teacher'],['roomNo','Room']],
        attendance: [['attendanceDate','Date'],['state','Status'],['checkInTime','In'],['checkOutTime','Out'],['remarks','Remarks']],
        results: [['assessmentName','Assessment'],['obtainedMarks','Marks'],['totalMarks','Full marks'],['percentage','Percent'],['gradeLetter','Grade'],['meritPosition','Position'],['publishedAt','Published']],
        fees: [['invoiceNumber','Invoice'],['invoiceDate','Issued'],['totalAmount','Billed'],['paidAmount','Paid'],['dueAmount','Due'],['dueDate','Due date'],['state','Status']],
        transport: [['routeName','Route'],['vehicleNumber','Vehicle'],['pickupPoint','Pickup'],['startDate','Start'],['endDate','End'],['monthlyFare','Monthly fare'],['state','Status']],
        homework: [['subjectName','Subject'],['title','Title'],['opensAt','Opens'],['dueAt','Due'],['instructions','Instructions']],
        assignments: [['courseTitle','Course'],['title','Assignment'],['maxMarks','Full marks'],['dueAt','Due'],['instructions','Instructions']],
        documents: [['documentTypeCode','Type'],['title','Title'],['fileName','File'],['expiresAt','Expires']]
    };
    const paymentColumns = [['receiptNumber','Receipt'],['paymentDate','Date'],['amount','Amount'],['paymentMethod','Method'],['state','Status']];
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
        if (['attendanceDate','invoiceDate','paymentDate','startDate','endDate','enrollmentDate','registeredAt','approvedAt','opensAt','dueAt','publishedAt','expiresAt'].includes(key)) return String(value).slice(0,10);
        if (['startTime','endTime','checkInTime','checkOutTime'].includes(key)) return String(value).slice(0,5);
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
    function renderDocuments(items, student) {
        render(items, columns.documents, 'No available student documents.');
        const th = document.createElement('th'); th.scope = 'col'; th.textContent = 'Action';
        el('studentPortalHeaders').firstElementChild.append(th);
        const rows = el('studentPortalRows').children;
        if (!items.length) {
            rows[0].firstElementChild.colSpan = 5;
            document.dispatchEvent(new CustomEvent('portal:documents-loaded', { detail: { student } }));
            return;
        }
        items.forEach((item, index) => {
            const td = document.createElement('td');
            const link = document.createElement('a');
            link.className = 'btn btn-sm btn-outline-primary';
            link.textContent = 'Download';
            link.rel = 'noreferrer';
            link.href = '/api/portal/students/' + encodeURIComponent(student) +
                '/documents/' + encodeURIComponent(item.reference) + '/download';
            td.append(link); rows[index].append(td);
        });
        document.dispatchEvent(new CustomEvent('portal:documents-loaded', { detail: { student } }));
    }
    function queryPage(n) { return '?' + new URLSearchParams({ page: String(n), pageSize: String(pageSize) }); }
    function showPaging(main, payments = false) {
        el('studentPortalPagination').classList.toggle('d-none', !main);
        el('studentPortalPaymentsPagination').classList.toggle('d-none', !payments);
    }
    function pageInfo(data, current, prefix) {
        const total = Math.max(0, Number(data?.totalCount) || 0);
        const size = Math.max(1, Number(data?.pageSize) || pageSize);
        const last = Math.max(1, Math.ceil(total / size));
        el(prefix + 'Previous').disabled = current <= 1;
        el(prefix + 'Next').disabled = current >= last;
        el(prefix + 'Page').textContent = 'Page ' + current + ' of ' + last + ' · ' + total + ' records';
    }
    function renderPayments(items) {
        const extra = el('studentPortalExtra');
        const heading = document.createElement('h4'); heading.className = 'h6'; heading.textContent = 'Payments';
        const table = document.createElement('table'); table.className = 'table table-sm align-middle';
        const head = document.createElement('thead'), body = document.createElement('tbody');
        const tr = document.createElement('tr');
        paymentColumns.forEach(([,title]) => {
            const th = document.createElement('th'); th.scope = 'col'; th.textContent = title; tr.append(th);
        });
        head.append(tr);
        if (!items.length) {
            const row = document.createElement('tr'), td = cell('No payments recorded.');
            td.colSpan = paymentColumns.length; row.append(td); body.append(row);
        } else items.forEach(item => {
            const row = document.createElement('tr');
            paymentColumns.forEach(([key]) => row.append(cell(display(item[key], key))));
            body.append(row);
        });
        table.append(head, body); extra.append(heading, table);
    }
    function resetPages() { page = 1; paymentPage = 1; }
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
                item.textContent = student.name + ' · ' + student.studentCode + (student.rollNo ? ' · Roll ' + student.rollNo : '');
                select.append(item);
            }
            if (linked?.length === 1) { select.value = linked[0].reference; loadCurrent(); }
            document.dispatchEvent(new CustomEvent('portal:student-selected', { detail: { student: select.value } }));
            if (!linked?.length) notify('No student is linked to your account.', 'warning');
        } catch (error) { notify(error.message); }
    }
    async function loadCurrent() {
        const student = select.value, token = ++latestRequest, activeSection = section;
        clearAlert(); showPaging(false);
        el('studentPortalExtra').replaceChildren(); el('studentPortalSummary').textContent = '';
        el('studentPortalSectionTitle').textContent = section.charAt(0).toUpperCase() + section.slice(1);
        if (!student) { render([], columns[section], 'Choose a student to continue.'); return; }
        try {
            const base = '/api/portal/students/' + encodeURIComponent(student) + '/';
            if (activeSection === 'fees') {
                const [ledger, invoices, payments] = await Promise.all([
                    api(base + 'fees'), api(base + 'invoices' + queryPage(page)),
                    api(base + 'payments' + queryPage(paymentPage))
                ]);
                if (token !== latestRequest) return;
                render(invoices.items || [], columns.fees);
                el('studentPortalSummary').textContent = 'Total billed ' + ledger.totalBilled +
                    ' · Paid ' + ledger.totalPaid + ' · Due ' + ledger.totalDue;
                renderPayments(payments.items || []);
                showPaging(true, true);
                pageInfo(invoices, page, 'studentPortal');
                pageInfo(payments, paymentPage, 'studentPortalPayments');
                return;
            }
            let url = base + activeSection;
            if (activeSection === 'attendance') {
                const from = el('studentPortalFrom').value, to = el('studentPortalTo').value;
                if (!from || !to || from > to || (Date.parse(to) - Date.parse(from)) / 86400000 > 366)
                    throw new Error('Choose a valid attendance date range of at most 367 days.');
                url += '?' + new URLSearchParams({ fromDate: from, toDate: to,
                    page: String(page), pageSize: String(pageSize) });
            } else if (paged.has(activeSection)) url += queryPage(page);
            const data = await api(url);
            if (token !== latestRequest) return;
            if (activeSection === 'documents') {
                const rows = Array.isArray(data) ? data : [];
                renderDocuments(rows, student);
                el('studentPortalSummary').textContent =
                    'Available non-sensitive documents: ' + rows.length +
                    '. Verification-required files are handled by the institution.';
            } else if (paged.has(activeSection)) {
                render(data.items || [], columns[activeSection]);
                showPaging(true);
                pageInfo(data, page, 'studentPortal');
                if (activeSection === 'subjects')
                    el('studentPortalSummary').textContent = 'Original roll and credit snapshots; subject labels reflect the current catalog.';
                if (activeSection === 'enrollments')
                    el('studentPortalSummary').textContent = 'Original enrollment identity and dates, including previous placements.';
            } else render(Array.isArray(data) ? data : [], columns[activeSection]);
        } catch (error) {
            if (token !== latestRequest) return;
            render([], columns[activeSection], 'This section cannot be loaded.');
            notify(error.message);
        }
    }
    select.addEventListener('change', () => {
        resetPages(); loadCurrent();
        document.dispatchEvent(new CustomEvent('portal:student-selected', { detail: { student: select.value } }));
    });
    tabs.addEventListener('click', event => {
        const button = event.target.closest('button[data-portal-tab]');
        if (!button) return;
        section = button.dataset.portalTab;
        resetPages();
        tabs.querySelectorAll('button[data-portal-tab]').forEach(b => { b.className = b === button ? 'btn btn-primary' : 'btn btn-outline-primary'; });
        loadCurrent();
    });
    el('studentPortalPrevious').addEventListener('click', () => { if (page > 1) { page--; loadCurrent(); } });
    el('studentPortalNext').addEventListener('click', () => { page++; loadCurrent(); });
    el('studentPortalPaymentsPrevious').addEventListener('click', () => { if (paymentPage > 1) { paymentPage--; loadCurrent(); } });
    el('studentPortalPaymentsNext').addEventListener('click', () => { paymentPage++; loadCurrent(); });
    el('studentPortalRefresh').addEventListener('click', loadCurrent);
    el('studentPortalFrom').addEventListener('change', () => { if (section === 'attendance') { resetPages(); loadCurrent(); } });
    el('studentPortalTo').addEventListener('change', () => { if (section === 'attendance') { resetPages(); loadCurrent(); } });
    el('studentPortalTo').value = localDate(Date.now());
    el('studentPortalFrom').value = localDate(Date.now() - 30 * 86400000);
    loadStudents();
})();
