/* One shared page manual renderer; reviewed route content only, never HTML from a URL.
 * Language follows the server-selected document culture. Related links must be present
 * in the currently rendered, server-authorized navigation. No remote guide API/cache. */
(() => {
    'use strict';
    const manuals = {
        '/feeoperations/index': {
            version: 1, reviewed: true,
            title: ['Student Fees & Billing', 'শিক্ষার্থী ফি ও বিলিং'],
            purpose: ['Maintain fee structures, preview monthly invoices and collect a receipt against a selected invoice.', 'ফি কাঠামো পরিচালনা, মাসিক ইনভয়েসের প্রিভিউ এবং নির্বাচিত ইনভয়েসে রসিদ গ্রহণের কাজ এখানে হয়।'],
            before: ['TenantAdmin, Principal or Accountant access; an authorized campus, active academic batch and applicable fee structure for billing.', 'TenantAdmin, Principal বা Accountant অনুমতি; বিলিংয়ের জন্য অনুমোদিত ক্যাম্পাস, সক্রিয় ব্যাচ ও প্রযোজ্য ফি কাঠামো প্রয়োজন।'],
            steps: [
                ['For a new structure choose campus, academic year, level, effective date and fee heads, then Save fee structure.', 'নতুন কাঠামোর জন্য ক্যাম্পাস, শিক্ষাবর্ষ, স্তর, কার্যকর তারিখ ও ফি খাত বেছে ফি কাঠামো সংরক্ষণ করুন।'],
                ['Under Generate monthly invoices choose Active batch, Applicable fee structure, Month, Year and Due date.', 'মাসিক ইনভয়েস অংশে সক্রিয় ব্যাচ, প্রযোজ্য কাঠামো, মাস, বছর ও শেষ তারিখ নির্বাচন করুন।'],
                ['Select Preview billing. Review eligible, existing and conflicting invoices and the displayed BDT totals.', 'বিলিং প্রিভিউ চাপুন। যোগ্য, বিদ্যমান ও সাংঘর্ষিক ইনভয়েস এবং BDT মোট যাচাই করুন।'],
                ['When the preview permits generation, select Generate invoices and confirm. For collection, find a student, View ledger, select an invoice and Record cash receipt after its preview.', 'প্রিভিউ অনুমতি দিলে ইনভয়েস তৈরি করে নিশ্চিত করুন। টাকা গ্রহণে শিক্ষার্থী খুঁজে লেজার দেখুন, ইনভয়েস নির্বাচন করুন এবং রসিদের প্রিভিউয়ের পর নগদ রসিদ নথিভুক্ত করুন।']
            ],
            example: ['Fictional example: for an active batch choose November 2026, a valid fee structure and a due date. If the preview shows existing invoices, inspect them before confirming only the eligible new ones.', 'কাল্পনিক উদাহরণ: সক্রিয় ব্যাচে নভেম্বর ২০২৬, বৈধ ফি কাঠামো ও শেষ তারিখ দিন। প্রিভিউতে আগের ইনভয়েস থাকলে শুধু যোগ্য নতুন ইনভয়েস নিশ্চিত করার আগে সেগুলো যাচাই করুন।'],
            result: ['New invoices appear in the student ledger; recorded receipts appear in receipt history. GL posting is a separate authorized workflow.', 'নতুন ইনভয়েস শিক্ষার্থী লেজারে এবং নথিভুক্ত রসিদ রসিদের ইতিহাসে দেখা যায়। GL পোস্টিং আলাদা অনুমোদিত কাজ।'],
            rules: ['Preview becomes stale after input changes; generate only from the current preview. Repeated identical requests must not duplicate invoices. Posted financial records require approved correction/reversal, not direct editing.', 'ইনপুট বদলালে প্রিভিউ বাতিল হয়; নতুন প্রিভিউ ছাড়া তৈরি করবেন না। একই অনুরোধে দ্বৈত ইনভয়েস হবে না। পোস্ট করা হিসাব সরাসরি সম্পাদনা নয়, অনুমোদিত সংশোধন/রিভার্সাল প্রয়োজন।'],
            recovery: ['If Generate stays disabled, resolve preview conflicts, missing scope or fee structure and preview again. If receipt confirmation fails, reload the preflight and use the original request identifier when retrying.', 'Generate নিষ্ক্রিয় থাকলে প্রিভিউয়ের সংঘাত, অসম্পূর্ণ স্কোপ বা ফি কাঠামো ঠিক করে আবার প্রিভিউ করুন। রসিদ ব্যর্থ হলে নতুন প্রিফ্লাইট নিয়ে আগের request identifier দিয়েই পুনরায় চেষ্টা করুন।'],
            related: [['/StudentInvoicePosting/Index', 'Invoice GL posting', 'ইনভয়েস GL পোস্টিং'], ['/FeeReceivablesAging/Index', 'Receivables aging', 'বকেয়া বয়সভিত্তিক প্রতিবেদন']]
        },
        '/students/index': {
            version: 1, reviewed: true,
            title: ['Student Directory', 'শিক্ষার্থী তালিকা'],
            purpose: ['Search permitted student records, inspect profiles and access authorized lifecycle actions.', 'অনুমোদিত শিক্ষার্থী খোঁজা, প্রোফাইল দেখা ও অনুমোদিত জীবনচক্রের কাজ এখানে করা যায়।'],
            before: ['TenantAdmin or AdmissionOfficer access; students must already exist in the institution.', 'TenantAdmin বা AdmissionOfficer অনুমতি; প্রতিষ্ঠানে শিক্ষার্থীর রেকর্ড থাকতে হবে।'],
            steps: [
                ['Enter a name or student code and optionally select Status; select Search.', 'নাম বা শিক্ষার্থী কোড লিখে প্রয়োজনে অবস্থা নির্বাচন করে Search চাপুন।'],
                ['Open the relevant student profile from the results and review guardian, enrollment and lifecycle history.', 'ফলাফল থেকে শিক্ষার্থী প্রোফাইল খুলে অভিভাবক, ভর্তি ও জীবনচক্রের ইতিহাস দেখুন।'],
                ['Where permitted, update contact details with the required reason or operational status; save and review feedback.', 'অনুমতি থাকলে প্রয়োজনীয় কারণ দিয়ে যোগাযোগের তথ্য বা পরিচালনাগত অবস্থা বদলে সংরক্ষণ করে ফল যাচাই করুন।'],
                ['Use Export page CSV only for the currently displayed authorized page.', 'শুধু বর্তমানে প্রদর্শিত অনুমোদিত পৃষ্ঠার জন্য Export page CSV ব্যবহার করুন।']
            ],
            example: ['Fictional example: search student code ST-1042, filter Active, open the matching profile and verify its enrollment before requesting a permitted contact correction.', 'কাল্পনিক উদাহরণ: ST-1042 কোডে Active অবস্থা দিয়ে খুঁজে প্রোফাইল খুলুন; অনুমোদিত যোগাযোগ সংশোধনের আগে ভর্তি তথ্য যাচাই করুন।'],
            result: ['The filtered list and profile show the current authorized record; a successful edit shows its confirmation.', 'ফিল্টার করা তালিকা ও প্রোফাইলে অনুমোদিত বর্তমান রেকর্ড দেখা যায়; সফল সম্পাদনায় নিশ্চিতকরণ দেখায়।'],
            rules: ['Contact/status changes depend on role and validation. Final exit, transfer and readmission are separate controlled workflows, not free-text status edits.', 'যোগাযোগ/অবস্থা পরিবর্তন ভূমিকা ও যাচাইয়ের অধীন। চূড়ান্ত ছাড়পত্র, স্থানান্তর ও পুনঃভর্তি আলাদা নিয়ন্ত্রিত কাজ; সাধারণ অবস্থা সম্পাদনা নয়।'],
            recovery: ['If no record appears, clear filters and confirm the correct institution. Permission denied requires an authorized administrator; do not try another student ID.', 'রেকর্ড না পেলে ফিল্টার সরিয়ে সঠিক প্রতিষ্ঠান যাচাই করুন। অনুমতি না থাকলে প্রশাসকের সঙ্গে যোগাযোগ করুন; অন্য শিক্ষার্থী ID দিয়ে চেষ্টা করবেন না।'],
            related: [['/Admissions/Index', 'Admission applications', 'ভর্তির আবেদন'], ['/StudentLifecycle/Index', 'Student lifecycle', 'শিক্ষার্থী জীবনচক্র']]
        },
        '/attendance/index': {
            version: 1, reviewed: true,
            title: ['Student Attendance', 'শিক্ষার্থী উপস্থিতি'],
            purpose: ['Load an eligible roster, record attendance and finalize a completed register when authorized.', 'যোগ্য শিক্ষার্থীর তালিকা এনে উপস্থিতি নথিভুক্ত এবং অনুমতি থাকলে সম্পূর্ণ রেজিস্টার চূড়ান্ত করা হয়।'],
            before: ['TenantAdmin, Principal, VicePrincipal or Teacher access; an authorized academic batch/section and attendance date.', 'TenantAdmin, Principal, VicePrincipal বা Teacher অনুমতি; অনুমোদিত ব্যাচ/শাখা ও উপস্থিতির তারিখ প্রয়োজন।'],
            steps: [
                ['Select Date, find an Academic batch / section and optionally a Subject offering.', 'তারিখ, ব্যাচ/শাখা এবং প্রয়োজনে বিষয় নির্বাচন করুন।'],
                ['Select Load roster; mark each eligible student. Mark all present does not overwrite approved-leave entries.', 'Load roster চাপুন; প্রতিটি যোগ্য শিক্ষার্থীর অবস্থা দিন। Mark all present অনুমোদিত ছুটির তথ্য বদলায় না।'],
                ['Select Save attendance for each roster page before navigating to the next page.', 'পরবর্তী পৃষ্ঠায় যাওয়ার আগে প্রতিটি রোস্টার পৃষ্ঠার Save attendance চাপুন।'],
                ['TenantAdmin, Principal or VicePrincipal may select Finalize register after all eligible students are marked; later corrections require the audited correction action and reason.', 'সব যোগ্য শিক্ষার্থীর অবস্থা দেওয়া হলে TenantAdmin, Principal বা VicePrincipal Finalize register করতে পারেন; পরবর্তী সংশোধনে নিরীক্ষিত সংশোধন ও কারণ প্রয়োজন।']
            ],
            example: ['Fictional example: select 3 November 2026 and a permitted section, load its roster, mark the displayed students and save each page before finalization.', 'কাল্পনিক উদাহরণ: ৩ নভেম্বর ২০২৬ ও অনুমোদিত শাখা বেছে রোস্টার আনুন, প্রদর্শিত শিক্ষার্থীদের অবস্থা দিন এবং চূড়ান্ত করার আগে প্রতিটি পৃষ্ঠা সংরক্ষণ করুন।'],
            result: ['Saved marks appear in the register; finalized records are protected and later changes are traceable in correction history.', 'সংরক্ষিত উপস্থিতি রেজিস্টারে দেখা যায়; চূড়ান্ত রেকর্ড সুরক্ষিত থাকে এবং পরবর্তী পরিবর্তন সংশোধন ইতিহাসে পাওয়া যায়।'],
            rules: ['Do not finalize an incomplete roster. Teachers cannot use the administrator-only finalization/correction controls. Corrections require a reason and audit.', 'অসম্পূর্ণ রোস্টার চূড়ান্ত করবেন না। শিক্ষক প্রশাসক-নির্দিষ্ট চূড়ান্ত/সংশোধন অপশন ব্যবহার করতে পারেন না। সংশোধনে কারণ ও নিরীক্ষা আবশ্যক।'],
            recovery: ['If a batch is missing, search or page through authorized batches. If Save fails, retain the current page and retry after correcting validation; do not finalize.', 'ব্যাচ না পেলে অনুমোদিত ব্যাচে খুঁজুন বা পৃষ্ঠা বদলান। Save ব্যর্থ হলে যাচাইয়ের ভুল ঠিক করে একই পৃষ্ঠা থেকে পুনরায় চেষ্টা করুন; চূড়ান্ত করবেন না।'],
            related: [['/Students/Index', 'Student directory', 'শিক্ষার্থী তালিকা']]
        },
        '/payrolloperations/index': {
            version: 1, reviewed: true,
            title: ['Payroll Operations', 'বেতন ব্যবস্থাপনা'],
            purpose: ['Calculate monthly payroll runs, review snapshots and view posted payslips; authorized administrators may approve or request payment.', 'মাসিক বেতন হিসাব, হিসাবের স্ন্যাপশট ও পোস্ট করা পে-স্লিপ দেখা যায়; অনুমোদিত প্রশাসক অনুমোদন বা পেমেন্ট অনুরোধ করতে পারেন।'],
            before: ['TenantAdmin, Principal, HR, Accountant, Staff or Teacher may access the page; payroll run management is restricted to TenantAdmin.', 'TenantAdmin, Principal, HR, Accountant, Staff বা Teacher পৃষ্ঠা দেখতে পারেন; বেতন রান পরিচালনা শুধু TenantAdmin-এর জন্য।'],
            steps: [
                ['TenantAdmin selects Month, Year, Payroll scope and Partial-month policy, then Load runs or Calculate payroll.', 'TenantAdmin মাস, বছর, বেতনের পরিসর ও আংশিক-মাস নীতি বেছে Load runs বা Calculate payroll চাপেন।'],
                ['Review the run register and calculated employee payroll snapshots.', 'রান তালিকা ও হিসাব করা কর্মীদের বেতনের স্ন্যাপশট যাচাই করুন।'],
                ['A different authorized administrator approves the calculated run; approval alone does not post a GL journal or pay employees.', 'অন্য অনুমোদিত প্রশাসক হিসাব করা রান অনুমোদন করেন; শুধু অনুমোদনে GL জার্নাল পোস্ট বা কর্মীর বেতন পরিশোধ হয় না।'],
                ['Use the separate institutional GL posting and payment settlement workflows where authorized; staff can refresh My posted payslips.', 'অনুমতি অনুযায়ী আলাদা GL পোস্টিং ও পেমেন্ট নিষ্পত্তির কাজ করুন; কর্মীরা My posted payslips রিফ্রেশ করতে পারেন।']
            ],
            example: ['Fictional example: calculate November 2026 payroll for an authorized scope, review gross/deductions/net, then have another authorized administrator approve before separate posting.', 'কাল্পনিক উদাহরণ: অনুমোদিত পরিসরে নভেম্বর ২০২৬-এর বেতন হিসাব করে মোট বেতন/কর্তন/নিট যাচাই করুন; আলাদা পোস্টিংয়ের আগে অন্য অনুমোদিত প্রশাসক দিয়ে অনুমোদন করান।'],
            result: ['The calculated run appears in the run register. Posted payslips appear only after the corresponding authorized posting workflow.', 'হিসাব করা রান তালিকায় দেখা যায়। সংশ্লিষ্ট অনুমোদিত পোস্টিংয়ের পরেই পোস্ট করা পে-স্লিপ দেখা যায়।'],
            rules: ['The creator cannot approve their own run. Approval, GL journal posting and payment settlement are distinct steps; do not treat approval as payment.', 'রান তৈরিকারী নিজে অনুমোদন করতে পারবেন না। অনুমোদন, GL পোস্টিং ও পেমেন্ট নিষ্পত্তি আলাদা ধাপ; অনুমোদনকে পরিশোধ ভাববেন না।'],
            recovery: ['If a run is unavailable, check the selected month, scope and role. Resolve validation or approval conflicts before retrying; never create a second run to bypass a conflict.', 'রান না পেলে মাস, পরিসর ও ভূমিকা যাচাই করুন। পুনরায় চেষ্টার আগে যাচাই বা অনুমোদনের সংঘাত ঠিক করুন; সংঘাত এড়াতে দ্বিতীয় রান তৈরি করবেন না।'],
            related: [['/PayrollJournalPosting/Index', 'Payroll GL posting', 'বেতন GL পোস্টিং'], ['/PayrollSalaryStatement/Index', 'Salary statement', 'বেতন বিবরণী']]
        }
    };
    const sectionLabels = {
        en: { purpose: 'Page overview', before: 'Before you start', steps: 'How to use', example: 'Worked example (fictional)', result: 'Expected result', rules: 'Important rules', recovery: 'Errors and recovery', related: 'Related authorized workflows', missing: 'A verified step-by-step manual is not yet available for this page. Use the page’s validation messages and contact an authorized administrator for workflow guidance.' },
        bn: { purpose: 'পেজ পরিচিতি', before: 'শুরুর আগে', steps: 'কীভাবে করবেন', example: 'কাল্পনিক উদাহরণ', result: 'প্রত্যাশিত ফল', rules: 'গুরুত্বপূর্ণ নিয়ম', recovery: 'সমস্যা ও সমাধান', related: 'অনুমোদিত সংশ্লিষ্ট কাজ', missing: 'এই পৃষ্ঠার যাচাইকৃত ধাপে-ধাপে নির্দেশিকা এখনো প্রস্তুত হয়নি। পৃষ্ঠার যাচাই বার্তা অনুসরণ করুন এবং প্রয়োজন হলে অনুমোদিত প্রশাসকের সাহায্য নিন।' }
    };
    function normalizeRoute(path) {
        if (typeof path !== 'string' || !/^\/[a-z0-9/_-]*$/i.test(path)) return null;
        const normalized = path.toLowerCase().replace(/\/+$/, '');
        return normalized && !normalized.endsWith('/index') ? normalized + '/index' : normalized;
    }
    function validateGuide(guide) {
        if (!guide || guide.reviewed !== true || !Number.isInteger(guide.version) || guide.version < 1) return false;
        for (const key of ['title','purpose','before','example','result','rules','recovery']) {
            if (!Array.isArray(guide[key]) || guide[key].length !== 2 ||
                guide[key].some(s => typeof s !== 'string' || !s.trim() || /[<>]/.test(s))) return false;
        }
        return Array.isArray(guide.steps) && guide.steps.length >= 2 &&
            guide.steps.every(pair => Array.isArray(pair) && pair.length === 2 &&
                pair.every(s => typeof s === 'string' && s.trim() && !/[<>]/.test(s))) &&
            Array.isArray(guide.related) && guide.related.every(link => Array.isArray(link) &&
                link.length === 3 && normalizeRoute(link[0]) && link.slice(1).every(s => typeof s === 'string' && !!s.trim()));
    }
    function guideFor(path, locale) {
        const guide = manuals[normalizeRoute(path)];
        if (!validateGuide(guide)) return null;
        const n = String(locale || '').toLowerCase().startsWith('bn') ? 1 : 0;
        return {
            version: guide.version, title: guide.title[n],
            purpose: guide.purpose[n], before: guide.before[n],
            steps: guide.steps.map(pair => pair[n]), example: guide.example[n],
            result: guide.result[n], rules: guide.rules[n], recovery: guide.recovery[n],
            related: guide.related.map(link => ({ path: link[0], label: link[n + 1] }))
        };
    }
    function coverage() {
        const routes = Object.keys(manuals);
        return { routes: routes.length, en: routes.filter(route => !!guideFor(route, 'en-BD')).length,
            bn: routes.filter(route => !!guideFor(route, 'bn-BD')).length };
    }
    if (typeof module !== 'undefined' && module.exports) module.exports = { guideFor, coverage, normalizeRoute, validateGuide };
    if (typeof document === 'undefined') return;

    const trigger = document.getElementById('pageContextHelp');
    const dialog = document.getElementById('pageHelpDrawer');
    const title = document.getElementById('pageHelpTitle');
    const content = document.getElementById('pageHelpContent');
    const close = document.getElementById('pageHelpClose');
    if (!trigger || !dialog || !title || !content || !close) return;
    const locale = document.documentElement.lang || 'en-BD';
    const bn = locale.toLowerCase().startsWith('bn');
    const labels = sectionLabels[bn ? 'bn' : 'en'];
    const guide = guideFor(window.location.pathname, locale);
    trigger.title = guide?.purpose || labels.missing;
    function appendSection(key, value, ordered = false) {
        const section = document.createElement('section');
        section.className = 'page-help-section';
        const heading = document.createElement('h3');
        heading.textContent = labels[key];
        section.append(heading);
        if (ordered) {
            const list = document.createElement('ol');
            for (const step of value) {
                const item = document.createElement('li');
                item.textContent = step;
                list.append(item);
            }
            section.append(list);
        } else {
            const paragraph = document.createElement('p');
            paragraph.textContent = value;
            section.append(paragraph);
        }
        content.append(section);
    }
    function authorizedRelated(path) {
        const target = normalizeRoute(path);
        if (!target) return null;
        const links = document.querySelectorAll('nav[data-sidebar-section] a.nav-link, a[data-feature-discovery-link]');
        for (const link of links) {
            const href = link.getAttribute('href');
            if (!href || !href.startsWith('/') || href.startsWith('//') || /[?#\\]/.test(href)) continue;
            if (normalizeRoute(href) === target) return href;
        }
        return null;
    }
    function render() {
        content.replaceChildren();
        title.textContent = guide?.title || (bn ? 'পৃষ্ঠার নির্দেশিকা' : 'Page user manual');
        if (!guide) {
            const note = document.createElement('p');
            note.textContent = labels.missing;
            content.append(note);
            return;
        }
        appendSection('purpose', guide.purpose);
        appendSection('before', guide.before);
        appendSection('steps', guide.steps, true);
        appendSection('example', guide.example);
        appendSection('result', guide.result);
        appendSection('rules', guide.rules);
        appendSection('recovery', guide.recovery);
        const related = guide.related.map(item => ({ ...item, href: authorizedRelated(item.path) })).filter(item => item.href);
        if (related.length) {
            const section = document.createElement('section');
            section.className = 'page-help-section';
            const heading = document.createElement('h3');
            heading.textContent = labels.related;
            section.append(heading);
            const list = document.createElement('ul');
            for (const item of related) {
                const li = document.createElement('li');
                const a = document.createElement('a');
                a.href = item.href;
                a.textContent = item.label;
                li.append(a);
                list.append(li);
            }
            section.append(list);
            content.append(section);
        }
    }
    let fallback = false;
    function dismiss() {
        if (typeof dialog.close === 'function' && !fallback) dialog.close();
        else { dialog.removeAttribute('open'); dialog.hidden = true; trigger.focus(); }
    }
    trigger.addEventListener('click', () => {
        render();
        fallback = typeof dialog.showModal !== 'function';
        if (!fallback) dialog.showModal();
        else { dialog.hidden = false; dialog.setAttribute('open', ''); }
        close.focus();
    });
    close.addEventListener('click', dismiss);
    dialog.addEventListener('close', () => trigger.focus());
    dialog.addEventListener('click', event => { if (event.target === dialog) dismiss(); });
    dialog.addEventListener('keydown', event => {
        if (event.key === 'Escape' && fallback) { event.preventDefault(); dismiss(); }
        if (event.key !== 'Tab' || !fallback) return;
        const focusable = [...dialog.querySelectorAll('button, a[href]')].filter(el => !el.hidden);
        if (!focusable.length) return;
        if (event.shiftKey && document.activeElement === focusable[0]) { event.preventDefault(); focusable.at(-1).focus(); }
        else if (!event.shiftKey && document.activeElement === focusable.at(-1)) { event.preventDefault(); focusable[0].focus(); }
    });
})();
