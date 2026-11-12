(() => {
    'use strict';
    const body = document.body;
    const main = document.getElementById('main-content');
    if (!main || !body?.dataset.authTenantId || !body?.dataset.authSessionReference) return;
    const path = location.pathname.toLowerCase();
    const routes = [
        ['/academicmanagement', 'ACADEMIC'],
        ['/academicroutines', 'ATTENDANCE'],
        ['/students', 'STUDENT'],
        ['/studentguardians', 'STUDENT'],
        ['/studentimports', 'STUDENT'],
        ['/enrollmentmanagement', 'STUDENT'],
        ['/admissions', 'ADMISSION'],
        ['/assessmentadministration', 'ASSESSMENT'],
        ['/feeoperations', 'FEES'],
        ['/accountingsetup', 'FINANCE'],
        ['/payrolloperations', 'PAYROLL'],
        ['/payrollsalarycomponents', 'PAYROLL'],
        ['/payrollsalarystructures', 'PAYROLL'],
        ['/hrsetup', 'HR'],
        ['/inventorycatalog', 'INVENTORY'],
        ['/library', 'LIBRARY'],
        ['/hosteloperations', 'HOSTEL'],
        ['/transportoperations', 'TRANSPORT'],
        ['/learning', 'LMS'],
        ['/communicationadministration', 'COMMUNICATION'],
        ['/communicationworkspace', 'COMMUNICATION'],
        ['/documentadministration', 'DOCUMENTS'],
        ['/facilityspaces', 'FACILITIES']
    ];
    const match = routes.find(([prefix]) => path === prefix ||
        path.startsWith(prefix + '/'));
    if (!match) return;
    // The explicit setup handoff renders a richer, editable preview on this page.
    if (new URLSearchParams(location.search).get('smartSetup')?.toUpperCase() === match[1]) return;
    const session = body.dataset.authSessionReference;
    const tenant = body.dataset.authTenantId;
    const valid = () => session === body.dataset.authSessionReference &&
        tenant === body.dataset.authTenantId &&
        window.EduOSSessionTransitionPending?.() !== true;
    const bn = document.documentElement.lang.toLowerCase().startsWith('bn');
    const phrase = (en, bd) => bn ? bd : en;
    const module = match[1];
    fetch('/api/smart-setup/readiness?module=' + encodeURIComponent(module), {
        cache: 'no-store', credentials: 'same-origin', headers: { Accept: 'application/json' }
    }).then(res => res.ok ? res.json() : null).then(data => {
        if (!valid() || !data?.success || !Array.isArray(data.data)) return;
        const status = data.data.find(x => x.moduleCode === module);
        if (!status?.isAvailable || status.prerequisitesConfigured) return;
        const needed = (status.requirements || []).filter(x => !x.configured);
        if (!needed.length) return;
        // A concise, nonblocking prompt. No silent writes and no interruption
        // to the current form; the setup wizard opens separately.
        const notice = document.createElement('div');
        notice.className = 'alert alert-warning d-flex align-items-center flex-wrap gap-2 mb-3';
        notice.setAttribute('role', 'status');
        notice.setAttribute('data-smart-setup-hint', module);
        const text = document.createElement('span');
        text.className = 'flex-grow-1 small';
        text.textContent = phrase('Setup needs ' + needed.length + ' item(s): ',
            'সেটআপের আরও ' + needed.length + 'টি বিষয় বাকি: ') +
            needed.slice(0, 3).map(x => bn ? x.labelBangla : x.label).join(', ') +
            (needed.length > 3 ? '…' : '');
        const link = document.createElement('a');
        link.className = 'btn btn-sm btn-outline-dark';
        link.textContent = phrase('Complete setup', 'সেটআপ সম্পন্ন করুন');
        link.href = '/SmartSetup/Index?module=' + encodeURIComponent(module);
        link.target = '_blank';
        link.rel = 'noopener noreferrer';
        link.setAttribute('aria-label', phrase('Open setup in a new tab', 'নতুন ট্যাবে সেটআপ খুলুন'));
        const dismiss = document.createElement('button');
        dismiss.type = 'button';
        dismiss.className = 'btn-close ms-auto';
        dismiss.setAttribute('aria-label', phrase('Dismiss', 'বন্ধ করুন'));
        dismiss.addEventListener('click', () => notice.remove());
        notice.append(text, link, dismiss);
        if (valid()) main.prepend(notice);
    }).catch(() => {
        // Readiness is guidance, never a dependency for business operations.
    });
})();
