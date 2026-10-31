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
        '/admissions/index': {
            version: 1, reviewed: true,
            title: ['Admission Applications', 'ভর্তির আবেদন'],
            purpose: ['Manage published intake forms, submit applicant details, review applications and complete authorized enrollment after required offer checks.', 'প্রকাশিত ভর্তি ফরম, আবেদন জমা, আবেদন যাচাই এবং প্রয়োজনীয় অফার যাচাইয়ের পর অনুমোদিত ভর্তি সম্পন্ন করুন।'],
            before: ['ADMISSION entitlement and TenantAdmin or AdmissionOfficer role. Submission requires an open published intake, permitted academic setup and guardian details for a minor applicant.', 'ADMISSION মডিউল ও TenantAdmin অথবা AdmissionOfficer ভূমিকা প্রয়োজন। আবেদনের জন্য খোলা প্রকাশিত ভর্তি ফরম, অনুমোদিত শিক্ষাবর্ষের তথ্য এবং অপ্রাপ্তবয়স্কের অভিভাবকের তথ্য লাগবে।'],
            steps: [
                ['Where permitted, choose New Form, configure academic scope, dates and questions, Save Draft, then Publish; a draft alone is not open for applications.', 'অনুমতি থাকলে New Form থেকে শিক্ষাবর্ষ, সময় ও প্রশ্ন নির্ধারণ করে Save Draft, তারপর Publish করুন; শুধু খসড়া থাকলে আবেদন নেওয়া যাবে না।'],
                ['In New application select Published intake, then enter applicant name, birth date, contact, required guardian information and any custom responses.', 'New application-এ Published intake নির্বাচন করে আবেদনকারীর নাম, জন্মতারিখ, যোগাযোগ, প্রয়োজনীয় অভিভাবকের তথ্য ও অতিরিক্ত প্রশ্নের উত্তর দিন।'],
                ['Select Submit application once. Use Application list search, status filter and Review to inspect the saved application and documents.', 'Submit application একবার চাপুন। Application list-এর খোঁজ, অবস্থা ফিল্টার ও Review দিয়ে সংরক্ষিত আবেদন ও কাগজপত্র দেখুন।'],
                ['In Review choose an allowed New status, enter a decision note when required and Save decision. Do not assume that review alone enrolls a student.', 'Review-তে অনুমোদিত New status বেছে প্রয়োজনীয় Decision note দিয়ে Save decision করুন। শুধু যাচাই করলেই শিক্ষার্থী ভর্তি হয়ে যায় না।'],
                ['For a qualified applicant follow the offer and verified-acceptance controls, then use Admit applicant and Complete enrollment with an eligible academic batch and roll when authorized.', 'যোগ্য আবেদনকারীর অফার ও যাচাইকৃত গ্রহণের ধাপ অনুসরণ করুন; অনুমতি থাকলে Admit applicant থেকে উপযুক্ত ব্যাচ ও রোল দিয়ে Complete enrollment করুন।']
            ],
            example: ['Fictional example: select a published November 2026 intake for a permitted campus, submit applicant Rina with the required contact and guardian details, locate the application by search, review documents and proceed through the approved offer process before enrollment.', 'কাল্পনিক উদাহরণ: অনুমোদিত ক্যাম্পাসে নভেম্বর ২০২৬-এর প্রকাশিত ফরম বেছে রিনার যোগাযোগ ও প্রয়োজনীয় অভিভাবকের তথ্য দিয়ে আবেদন জমা দিন; খুঁজে কাগজপত্র যাচাই করুন এবং অনুমোদিত অফার প্রক্রিয়ার পর ভর্তি সম্পন্ন করুন।'],
            result: ['The submitted application appears in the paginated register. A successful separate enrollment returns a student code; use the authorized Student Directory to verify it.', 'জমা দেওয়া আবেদন পৃষ্ঠাভিত্তিক তালিকায় দেখা যায়। আলাদা ভর্তি সফল হলে শিক্ষার্থী কোড পাওয়া যায়; অনুমোদিত শিক্ষার্থী তালিকায় যাচাই করুন।'],
            rules: ['Draft and published intakes have different availability. Applicant creation uses a request identifier to prevent duplicate retries; review and enrollment use row versions. Offer acceptance is independently verified and enrollment is not automatic.', 'খসড়া ও প্রকাশিত ফরমের ব্যবহার আলাদা। একই আবেদন পুনরায় পাঠালে request identifier দ্বৈত রেকর্ড প্রতিরোধ করে; যাচাই ও ভর্তিতে row version লাগে। অফার গ্রহণ আলাদাভাবে যাচাই হয়; স্বয়ংক্রিয় ভর্তি নয়।'],
            recovery: ['If an intake is absent, check its publication window and permitted campus. Missing minor guardian details block submission. On a stale decision or enrollment conflict, reload the application rather than resubmitting a duplicate.', 'ফরম না পেলে প্রকাশনার সময় ও অনুমোদিত ক্যাম্পাস দেখুন। অপ্রাপ্তবয়স্কের অভিভাবকের তথ্য না থাকলে আবেদন জমা হবে না। পুরোনো সিদ্ধান্ত বা ভর্তি সংঘাতে নতুন করে আবেদন না দিয়ে রেকর্ড রিলোড করুন।'],
            related: [['/Students/Index', 'Student directory', 'শিক্ষার্থী তালিকা']]
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
        '/facilityworkorders/index': {
            version: 1, reviewed: true,
            title: ['Campus Service Requests and Work Orders', 'ক্যাম্পাস সার্ভিস অনুরোধ ও ওয়ার্ক অর্ডার'],
            purpose: ['Submit campus maintenance requests, track independent approval, technician work, SLA and verified closure.', 'ক্যাম্পাস রক্ষণাবেক্ষণের অনুরোধ, স্বতন্ত্র অনুমোদন, টেকনিশিয়ানের কাজ, SLA ও যাচাইকৃত সমাপ্তি পরিচালনা করুন।'],
            before: ['The FACILITIES module must be enabled. Use a permitted campus and active Facility Space. Requester, FacilitiesManager, FacilitiesApprover and FacilitiesTechnician actions require their respective roles.', 'FACILITIES মডিউল চালু থাকতে হবে। অনুমোদিত ক্যাম্পাস ও সক্রিয় সুবিধা স্থান প্রয়োজন। অনুরোধকারী, FacilitiesManager, FacilitiesApprover ও FacilitiesTechnician-এর নিজ নিজ ভূমিকার অনুমতি থাকতে হবে।'],
            steps: [
                ['In New service request enter Campus ID, Facility space ID, Category, Priority and Issue summary, then Submit request.', 'নতুন সার্ভিস অনুরোধে ক্যাম্পাস আইডি, স্থানের আইডি, বিভাগ, অগ্রাধিকার ও সমস্যার সারাংশ দিয়ে অনুরোধ জমা দিন।'],
                ['Find and Open the request. A different manager selects Triage request and a future SLA deadline; an independent approver then Approves or Rejects.', 'অনুরোধ খুঁজে খুলুন। আলাদা ম্যানেজার ভবিষ্যৎ SLA সময় দিয়ে প্রাথমিক যাচাই করেন; স্বতন্ত্র অনুমোদনকারী অনুমোদন বা প্রত্যাখ্যান করেন।'],
                ['The manager Assigns a technician user ID and start/end window. The assigned technician Starts work, may Pause with a reason and Resume, then Submits completion evidence.', 'ম্যানেজার টেকনিশিয়ান আইডি ও কাজের সময় নির্ধারণ করেন। নির্ধারিত টেকনিশিয়ান কাজ শুরু, কারণসহ বিরতি ও পুনরায় শুরু করে সমাপ্তির প্রমাণ জমা দেন।'],
                ['An independent approver chooses Verify closure or Return for correction with a reason. After a return, the technician corrects the work and submits fresh evidence for verification.', 'স্বতন্ত্র অনুমোদনকারী সমাপ্তি যাচাই অথবা কারণসহ সংশোধনের জন্য ফেরত দেন। ফেরত এলে টেকনিশিয়ান সংশোধন করে নতুন প্রমাণ জমা দেন।'],
                ['Use Search, overdue filter, dashboard, history and authorized CSV or Print to review progress.', 'খোঁজ, বিলম্বিত কাজের ফিল্টার, ড্যাশবোর্ড, ইতিহাস ও অনুমোদিত CSV বা প্রিন্টে অগ্রগতি যাচাই করুন।']
            ],
            example: ['Fictional example: report a broken hallway light in an authorized campus space. A manager sets tomorrow as SLA, a different approver approves, a technician repairs it, and the verifier returns unsafe wiring with a reason. The technician fixes it and resubmits before final closure.', 'কাল্পনিক উদাহরণ: অনুমোদিত ক্যাম্পাসের করিডরের বাতি নষ্ট হলে অনুরোধ দিন। ম্যানেজার পরদিনের SLA দেন, অন্য অনুমোদনকারী অনুমোদন করেন, টেকনিশিয়ান মেরামত করেন। তারের ত্রুটি থাকলে যাচাইকারী কারণ দিয়ে ফেরত দেন; সংশোধনের পর পুনরায় প্রমাণ জমা দিয়ে কাজ বন্ধ হয়।'],
            result: ['The request and work order show the latest state; the append-only history retains both completion submissions and the return reason. Returned work is not counted as closed.', 'অনুরোধ ও ওয়ার্ক অর্ডারে বর্তমান অবস্থা এবং ইতিহাসে উভয় সমাপ্তির প্রমাণ ও ফেরতের কারণ থাকে। ফেরত কাজ সমাপ্ত হিসেবে গণনা হয় না।'],
            rules: ['Request maker, triage manager, approver and assigned technician have separate controls. Return needs a 10-500 character reason. Duplicate command IDs cannot repeat the action; stale row versions fail. This page does not issue spare parts or post Finance GL.', 'অনুরোধকারী, ট্রায়াজ ম্যানেজার, অনুমোদনকারী ও নির্ধারিত টেকনিশিয়ানের আলাদা অনুমতি রয়েছে। ফেরতে ১০-৫০০ অক্ষরের কারণ প্রয়োজন। একই কমান্ড পুনরায় কাজ করে না; পুরোনো সংস্করণ বাতিল হয়। এই পৃষ্ঠায় খুচরা যন্ত্রাংশ ইস্যু বা Finance GL পোস্ট হয় না।'],
            recovery: ['If a campus/space is missing, verify permitted active setup. A 403 means insufficient role or campus access; 409 means reload the latest row or resolve a technician schedule overlap. Do not create a second request to bypass a failed retry.', 'ক্যাম্পাস বা স্থান না পেলে অনুমোদিত সক্রিয় সেটআপ যাচাই করুন। ৪০৩ মানে ভূমিকা বা ক্যাম্পাস অনুমতি নেই; ৪০৯ হলে নতুন তথ্য লোড করুন বা টেকনিশিয়ানের সময়ের সংঘাত ঠিক করুন। পুনরায় চেষ্টার ভুল এড়াতে দ্বিতীয় অনুরোধ তৈরি করবেন না।'],
            related: [['/FacilitySpaces/Index', 'Facility spaces', 'ক্যাম্পাস সুবিধা স্থান']]
        },
        '/studentinvoiceposting/index': {
            version: 1, reviewed: true,
            title: ['Student Invoice GL Posting', 'শিক্ষার্থী ইনভয়েস GL পোস্টিং'],
            purpose: ['Find institutional student invoices and post eligible invoices to the canonical receivable and income journal; this page does not collect payments.', 'প্রতিষ্ঠানের শিক্ষার্থী ইনভয়েস খুঁজে যোগ্য ইনভয়েসের প্রাপ্য ও আয় হিসাব মূল GL জার্নালে পোস্ট করুন; এখানে টাকা গ্রহণ হয় না।'],
            before: ['FINANCE entitlement and TenantAdmin, Principal or Accountant role to view. Only TenantAdmin or Principal may post. Posting requires a positive BDT invoice, an open accounting period covering its date and a current invoice row version.', 'FINANCE মডিউল ও দেখার জন্য TenantAdmin, Principal বা Accountant ভূমিকা প্রয়োজন। পোস্ট করতে শুধু TenantAdmin বা Principal পারবেন। ধনাত্মক BDT ইনভয়েস, তারিখের জন্য খোলা হিসাবকাল ও বর্তমান row version প্রয়োজন।'],
            steps: [
                ['Optionally select Campus, enter Invoice / student search and choose Unposted only; select Find to load a paginated register.', 'প্রয়োজনে Campus, Invoice / student খোঁজ ও Unposted only বেছে Find চাপুন; পৃষ্ঠাভিত্তিক তালিকা আসবে।'],
                ['Review invoice number, student, date, amount, status and existing journal reference. Choose an Open accounting period that covers the invoice date.', 'ইনভয়েস নম্বর, শিক্ষার্থী, তারিখ, পরিমাণ, অবস্থা ও আগের জার্নাল রেফারেন্স যাচাই করে ইনভয়েসের তারিখ অন্তর্ভুক্ত এমন Open accounting period বাছুন।'],
                ['If your role permits it and Post GL is enabled, select Post GL and confirm the institutional general-ledger posting.', 'ভূমিকার অনুমতি থাকলে এবং Post GL সক্রিয় থাকলে সেটি চাপুন ও প্রতিষ্ঠানের GL পোস্টিং নিশ্চিত করুন।'],
                ['Read the resulting journal number and use Find or page navigation to verify Posted status and the linked journal.', 'ফেরত পাওয়া জার্নাল নম্বর দেখুন; Find বা পৃষ্ঠা পরিবর্তন করে Posted অবস্থা ও সংযুক্ত জার্নাল যাচাই করুন।']
            ],
            example: ['Fictional example: find a BDT 1,500 student fee invoice dated 5 November 2026, choose an open period covering that date, confirm Post GL and verify the returned journal reference. No cash receipt is created.', 'কাল্পনিক উদাহরণ: ৫ নভেম্বর ২০২৬ তারিখের BDT ১,৫০০ টাকার ফি ইনভয়েস খুঁজে ওই তারিখের খোলা হিসাবকাল নির্বাচন করে Post GL নিশ্চিত করুন; ফেরত পাওয়া জার্নাল রেফারেন্স যাচাই করুন। এতে নগদ রসিদ তৈরি হয় না।'],
            result: ['An eligible successful posting links the student invoice to its institutional journal. Existing posted links are shown for review, not posted a second time.', 'সফল পোস্টিংয়ে ইনভয়েস প্রতিষ্ঠানের জার্নালের সঙ্গে যুক্ত হয়। আগে পোস্ট করা রেফারেন্স শুধু দেখানো হয়, আবার পোস্ট করা হয় না।'],
            rules: ['Accountants can inspect but cannot Post GL. A linked, cancelled, non-BDT or non-positive invoice cannot be posted from this page. Posted journals are immutable; corrections need an approved source-linked reversal. Platform subscription invoices and cash settlement are separate.', 'Accountant দেখতে পারেন কিন্তু Post GL করতে পারেন না। সংযুক্ত, বাতিল, BDT ছাড়া বা শূন্য/ঋণাত্মক ইনভয়েস এখানে পোস্ট করা যায় না। পোস্ট করা জার্নাল অপরিবর্তনীয়; সংশোধনে অনুমোদিত উৎস-সংযুক্ত রিভার্সাল লাগে। প্ল্যাটফর্ম সাবস্ক্রিপশন ও নগদ নিষ্পত্তি আলাদা।'],
            recovery: ['If Post GL is disabled, inspect existing journal, currency and amount. If the period does not cover the invoice date, select a valid open period. On a stale row, permission or server error, reload the register and verify journal linkage before retrying.', 'Post GL নিষ্ক্রিয় থাকলে আগের জার্নাল, মুদ্রা ও পরিমাণ দেখুন। হিসাবকালে তারিখ না থাকলে সঠিক খোলা কাল বাছুন। পুরোনো row, অনুমতি বা সার্ভার সমস্যায় আবার পোস্ট করার আগে তালিকা রিলোড করে জার্নাল সংযোগ যাচাই করুন।'],
            related: [['/FeeOperations/Index', 'Fees and billing', 'ফি ও বিলিং']]
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
