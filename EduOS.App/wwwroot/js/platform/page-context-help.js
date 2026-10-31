/* One shared page manual renderer; reviewed route content only, never HTML from a URL.
 * Language follows the server-selected document culture. Related links must be present
 * in the currently rendered, server-authorized navigation. No remote guide API/cache. */
(() => {
    'use strict';
    const manuals = {
        "/workspace/index": {
            version: 2, reviewed: true,
            title: ["Workspace Appearance Studio", "ওয়ার্কস্পেস অ্যাপিয়ারেন্স স্টুডিও"],
            purpose: ["Customize personal institution-scoped appearance, navigation, panels and table layouts without changing business records.", "ব্যবসায়িক রেকর্ড না বদলে নিজের প্রতিষ্ঠানভিত্তিক ডিজাইন, নেভিগেশন, প্যানেল ও তালিকা সাজান।"],
            before: ["An active institution session is required. Table-column controls are available only to TenantAdmin.", "সক্রিয় প্রতিষ্ঠানের সেশন প্রয়োজন। তালিকার কলাম নিয়ন্ত্রণ শুধু TenantAdmin-এর জন্য।"],
            steps: [
                ["Choose System, Light or Dark, a preset palette, navigation position, spacing and text size.", "System, Light বা Dark, প্রস্তুত প্যালেট, নেভিগেশন অবস্থান, স্পেসিং ও লেখার আকার বাছুন।"],
                ["Expand Advanced colors; enter valid HEX/RGB or use the picker for any of seven supported surfaces. Blank keeps the preset.", "Advanced colors খুলে সাতটি অংশের জন্য বৈধ HEX/RGB লিখুন বা রঙ বাছুন। ফাঁকা রাখলে প্রস্তুত রঙ থাকবে।"],
                ["Review the live preview, arrange optional dashboard panels and, if TenantAdmin, allowed table columns.", "লাইভ প্রিভিউ দেখে ঐচ্ছিক ড্যাশবোর্ড প্যানেল এবং TenantAdmin হলে অনুমোদিত তালিকার কলাম সাজান।"],
                ["Preview is not saved: choose Save appearance to keep it or Discard preview to restore the last saved design. Reset to defaults saves the default design; reload to verify.", "প্রিভিউ সংরক্ষিত নয়: রাখতে Save appearance চাপুন, অথবা শেষ সংরক্ষিত ডিজাইনে ফিরতে Discard preview চাপুন। Reset to defaults ডিফল্ট ডিজাইন সংরক্ষণ করে; রিলোড করে যাচাই করুন।"]
            ],
            example: ["Fictional example: Institute A uses Dark, Compact and #174A68 sidebar; Institute B retains a separate appearance.", "কাল্পনিক উদাহরণ: প্রতিষ্ঠান A-তে Dark, Compact ও #174A68 সাইডবার; প্রতিষ্ঠান B-তে আলাদা ডিজাইন থাকবে।"],
            result: ["Saved versioned appearance applies to this user and institution only, with no Finance, stock or invoice change.", "সংরক্ষিত সংস্করণযুক্ত ডিজাইন শুধু এই ব্যবহারকারী ও প্রতিষ্ঠানে প্রযোজ্য; Finance, স্টক বা ইনভয়েস বদলায় না।"],
            rules: ["Only allowlisted HEX/RGB colors are accepted; readable foreground is computed. Unsaved previews warn before leaving. Save is required to persist; another-device edits require a version refresh.", "শুধু অনুমোদিত HEX/RGB রঙ গ্রহণযোগ্য; পাঠযোগ্য লেখার রঙ স্বয়ংক্রিয়ভাবে নির্ধারিত হয়। অসংরক্ষিত প্রিভিউ নিয়ে পেজ ছাড়লে সতর্কতা আসে। স্থায়ী করতে Save প্রয়োজন; অন্য ডিভাইসের পরিবর্তনে নতুন সংস্করণ লোড করুন।"],
            recovery: ["Invalid color: use #123ABC or rgb(18,52,86), then Discard preview if needed. A failed save does not persist the preview. For 409 reload current preferences; after institution switch reload and sign in. Failed loads disable saving until refreshed.", "ভুল রঙে #123ABC বা rgb(18,52,86) দিন; প্রয়োজনে Discard preview চাপুন। Save ব্যর্থ হলে প্রিভিউ সংরক্ষিত হয় না। ৪০৯ হলে নতুন সেটিং লোড করুন; প্রতিষ্ঠান বদলালে রিলোড করে সাইন ইন করুন। লোড ব্যর্থ হলে রিফ্রেশ না করা পর্যন্ত সংরক্ষণ বন্ধ থাকে।"],
            related: [["/Account/Profile", "My profile", "আমার প্রোফাইল"]]
        },
        "/feereceivablesaging/index": {
          "version": 1,
          "reviewed": true,
          "title": [
            "Student Fee Aging and Reconciliation",
            "শিক্ষার্থী ফি বকেয়া ও সমন্বয়"
          ],
          "purpose": [
            "Inspect current unpaid institutional invoices, aging buckets, receipt reconciliation and daily cash collections without posting Finance records.",
            "প্রতিষ্ঠানের বর্তমান অপরিশোধিত ইনভয়েস, বকেয়ার সময়ভাগ, রসিদ সমন্বয় ও দৈনিক নগদ সংগ্রহ দেখুন; Finance রেকর্ড পোস্ট হয় না।"
          ],
          "before": [
            "FINANCE module; TenantAdmin, Principal or Accountant role; valid reference date and authorized campus. This is a current-balance report, not a historical as-of snapshot.",
            "FINANCE মডিউল; TenantAdmin, Principal বা Accountant ভূমিকা; বৈধ রেফারেন্স তারিখ ও অনুমোদিত ক্যাম্পাস। এটি বর্তমান বকেয়ার প্রতিবেদন, অতীতের নির্দিষ্ট দিনের স্ন্যাপশট নয়।"
          ],
          "steps": [
            [
              "Choose Reference date, accessible Campus, optional invoice/student search and Rows (25, 50 or 100).",
              "Reference date, অনুমোদিত Campus, প্রয়োজনীয় ইনভয়েস/শিক্ষার্থী খোঁজ ও Rows (২৫, ৫০ বা ১০০) বাছুন।"
            ],
            [
              "Select Run report and review currency totals, overdue buckets and outstanding invoice rows.",
              "Run report চাপুন; মুদ্রাভিত্তিক মোট, মেয়াদোত্তীর্ণ ভাগ ও বকেয়া ইনভয়েস দেখুন।"
            ],
            [
              "Use Previous/Next and Export current page CSV; only the visible invoice page is exported.",
              "Previous/Next এবং Export current page CSV ব্যবহার করুন; শুধু দৃশ্যমান ইনভয়েস পৃষ্ঠা রপ্তানি হয়।"
            ],
            [
              "Choose Check current filters in invoice/receipt and receipt/allocation reconciliation to inspect separate exception lists.",
              "ইনভয়েস/রসিদ ও রসিদ/বরাদ্দ সমন্বয়ে Check current filters দিয়ে আলাদা অসংগতি তালিকা দেখুন।"
            ],
            [
              "For Daily cash collection register select From, To and Campus, Run report and optionally Export visible page CSV.",
              "দৈনিক নগদ সংগ্রহ (Daily cash collection register)-এ From, To ও Campus দিয়ে Run report চাপুন; প্রয়োজনে Export visible page CSV নিন।"
            ]
          ],
          "example": [
            "Fictional example: on 8 November 2026 search ST-1042 at an authorized campus, review its unpaid aging bucket, check receipt exceptions, then export the current page; no payment is created.",
            "কাল্পনিক উদাহরণ: ৮ নভেম্বর ২০২৬ অনুমোদিত ক্যাম্পাসে ST-1042 খুঁজে বকেয়ার সময়ভাগ ও রসিদের অসংগতি দেখে বর্তমান পৃষ্ঠা CSV নিন; কোনো পেমেন্ট তৈরি হয় না।"
          ],
          "result": [
            "Current matching unpaid totals and separate reconciliation/cash pages appear read-only; exports are page-bounded.",
            "বর্তমান মিল পাওয়া বকেয়া মোট ও আলাদা সমন্বয়/নগদ তালিকা শুধু দেখার জন্য আসে; CSV পৃষ্ঠাভিত্তিক সীমিত।"
          ],
          "rules": [
            "Reference date groups current balances; it does not reconstruct historical balances or certify GL. Refunds, allocations and reservation credits need separate reconciliation. Cash shows gross eligible receipts; a GL link is not verification. No posting or reversal occurs here.",
            "রেফারেন্স তারিখ বর্তমান বকেয়া ভাগ করে; অতীতের বকেয়া বা GL প্রত্যয়ন নয়। রিফান্ড, বরাদ্দ ও রিজার্ভেশন ক্রেডিট আলাদা যাচাই প্রয়োজন। নগদ তালিকায় যোগ্য মোট রসিদ থাকে; GL সংযোগ মানেই যাচাই নয়। এখানে পোস্ট বা রিভার্স হয় না।"
          ],
          "recovery": [
            "For empty results check date, campus, search and tenant. Invalid precision/date or 403 requires corrected filters or role. Investigate reconciliation exceptions in the authorized Finance workflow, not by editing this report.",
            "তালিকা ফাঁকা হলে তারিখ, ক্যাম্পাস, খোঁজ ও প্রতিষ্ঠান দেখুন। ভুল দশমিক/তারিখ বা ৪০৩ হলে ফিল্টার বা অনুমতি ঠিক করুন। অসংগতি থাকলে প্রতিবেদন না বদলে অনুমোদিত Finance কাজে মূল তথ্য যাচাই করুন।"
          ],
          "related": [
            [
              "/FeeOperations/Index",
              "Fees and billing",
              "ফি ও বিলিং"
            ],
            [
              "/StudentInvoicePosting/Index",
              "Invoice GL posting",
              "ইনভয়েস GL পোস্টিং"
            ]
          ]
        },
        "/payrolljournalposting/index": {
          "version": 1,
          "reviewed": true,
          "title": [
            "Institutional Payroll GL Posting",
            "প্রতিষ্ঠানের বেতন GL পোস্টিং"
          ],
          "purpose": [
            "Review payroll runs, independently approve calculated payroll, map components to canonical accounts, post balanced accruals and request audited contra-journal reversals.",
            "বেতন রান যাচাই, স্বতন্ত্র অনুমোদন, মূল হিসাবের সঙ্গে উপাদান সংযোগ, ভারসাম্যপূর্ণ জার্নাল পোস্ট ও নিরীক্ষিত বিপরীত জার্নাল রিভার্সাল অনুরোধ করুন।"
          ],
          "before": [
            "FINANCE and PAYROLL modules; TenantAdmin, Principal or Accountant may view. Only TenantAdmin can approve/post/reverse here. Posting needs independent payroll approval, active BDT accounts, open period and current row version.",
            "FINANCE ও PAYROLL মডিউল; TenantAdmin, Principal বা Accountant দেখতে পারেন। এখানে অনুমোদন/পোস্ট/রিভার্স শুধু TenantAdmin। পোস্টিংয়ে স্বতন্ত্র বেতন অনুমোদন, সক্রিয় BDT হিসাব, খোলা কাল ও বর্তমান row version প্রয়োজন।"
          ],
          "steps": [
            [
              "Choose Open accounting period and Net salary payable liability, set Unposted only as needed, then Load payroll.",
              "Open accounting period ও Net salary payable liability বেছে প্রয়োজনমতো Unposted only দিয়ে Load payroll চাপুন।"
            ],
            [
              "TenantAdmin: for a Calculated run select Approve payroll only when the run creator is a different authorized administrator; approval is not payment.",
              "TenantAdmin: Calculated রানে Approve payroll চাপুন, তবে রান তৈরিকারী আলাদা অনুমোদিত প্রশাসক হতে হবে; অনুমোদন মানে পরিশোধ নয়।"
            ],
            [
              "For Approved unposted runs choose Map & post. Map earnings to expense, withholding to liability, LOAN_RECOVERY to original loan receivable asset, and employer contributions to expense plus liability.",
              "Approved কিন্তু পোস্ট হয়নি এমন রানে Map & post চাপুন। আয়-উপাদান expense, কর্তন liability, LOAN_RECOVERY মূল ঋণপ্রাপ্য asset এবং নিয়োগকর্তার অবদান expense ও liability-তে যুক্ত করুন।"
            ],
            [
              "Verify period covers payroll end, complete every mapping, then Post balanced payroll GL and inspect the debit/credit and journal reference.",
              "বেতন শেষের তারিখ হিসাবকালে আছে ও সব হিসাব যুক্ত হয়েছে দেখে Post balanced payroll GL চাপুন; debit/credit ও জার্নাল রেফারেন্স যাচাই করুন।"
            ],
            [
              "For posted payroll select Request reversal, open period, reversal date and audit reason. A different administrator reviews the request before Post reversal creates a separate contra journal.",
              "পোস্ট করা বেতনে Request reversal দিয়ে খোলা কাল, রিভার্সালের তারিখ ও কারণ দিন। আলাদা প্রশাসক যাচাইয়ের পর Post reversal আলাদা বিপরীত জার্নাল তৈরি করে।"
            ],
            [
              "Review posted and reversal states in the register or authorized Journal register; original posted history remains intact.",
              "তালিকা বা অনুমোদিত Journal register-এ পোস্ট ও রিভার্সাল অবস্থা দেখুন; মূল পোস্টিং ইতিহাস অক্ষত থাকে।"
            ]
          ],
          "example": [
            "Fictional example: a second administrator approves November 2026 payroll, TenantAdmin maps salary expense and withholding, posts a balanced journal, then verifies its number. Later correction needs a separately approved contra journal.",
            "কাল্পনিক উদাহরণ: অন্য প্রশাসক নভেম্বর ২০২৬-এর বেতন অনুমোদন করেন। TenantAdmin বেতন ব্যয় ও কর্তনের হিসাব যুক্ত করে ভারসাম্যপূর্ণ জার্নাল পোস্ট ও নম্বর যাচাই করেন। পরের সংশোধনে আলাদা অনুমোদিত বিপরীত জার্নাল লাগে।"
          ],
          "result": [
            "Approved payroll links to one canonical institutional Finance journal; reversal creates a linked contra journal without deleting the original. No bank salary payment occurs on this page.",
            "অনুমোদিত বেতন প্রতিষ্ঠানের একটি মূল Finance জার্নালের সঙ্গে যুক্ত হয়; রিভার্সালে মূলটি না মুছে সংযুক্ত বিপরীত জার্নাল হয়। এখানে ব্যাংকে বেতন দেওয়া হয় না।"
          ],
          "rules": [
            "Principal/Accountant may inspect, not post. Maker and approver must differ, as must reversal requester and reviewer. Period lock, campus, loan disbursement/recovery, rowversion, mapping and retry are server-checked. Never double-post or edit immutable journals.",
            "Principal/Accountant দেখতে পারেন, পোস্ট নয়। প্রস্তুতকারী ও অনুমোদনকারী এবং রিভার্সাল আবেদনকারী ও যাচাইকারী আলাদা। হিসাবকাল লক, ক্যাম্পাস, ঋণ বিতরণ/কিস্তি, rowversion, হিসাব-সংযোগ ও পুনঃচেষ্টা সার্ভারে যাচাই হয়। দ্বৈত পোস্ট বা অপরিবর্তনীয় জার্নাল সম্পাদনা নয়।"
          ],
          "recovery": [
            "If Map & post is unavailable check Approved state and existing journal. Missing accounts or closed period need authorized Finance setup. On 403/409, stale version, campus or loan conflict reload and inspect the original journal before retrying.",
            "Map & post না এলে Approved অবস্থা ও আগের জার্নাল দেখুন। হিসাব বা খোলা কাল না থাকলে Finance সেটআপ ঠিক করুন। ৪০৩/৪০৯, পুরোনো সংস্করণ, ক্যাম্পাস বা ঋণ সংঘাতে রিলোড করে পুনঃচেষ্টার আগে মূল জার্নাল যাচাই করুন।"
          ],
          "related": [
            [
              "/PayrollOperations/Index",
              "Payroll operations",
              "বেতন ব্যবস্থাপনা"
            ],
            [
              "/AccountingJournals/Index",
              "Journal register",
              "জার্নাল তালিকা"
            ]
          ]
        },
        "/payrollsalarystatement/index": {
          "version": 1,
          "reviewed": true,
          "title": [
            "Payroll Salary Statement",
            "বেতন বিবরণী"
          ],
          "purpose": [
            "Review stored employee payroll snapshots by month and campus, then print or export a bounded report without recalculating payroll.",
            "মাস ও ক্যাম্পাস অনুযায়ী সংরক্ষিত কর্মী-বেতনের স্ন্যাপশট দেখে পুনর্গণনা ছাড়াই সীমিত প্রতিবেদন প্রিন্ট বা এক্সপোর্ট করুন।"
          ],
          "before": [
            "PAYROLL module and TenantAdmin role; valid year 2000–2200, month and optional permitted campus. Draft and Cancelled runs are excluded.",
            "PAYROLL মডিউল ও TenantAdmin ভূমিকা; বৈধ বছর ২০০০–২২০০, মাস ও প্রয়োজনে অনুমোদিত ক্যাম্পাস। Draft ও Cancelled রান বাদ থাকে।"
          ],
          "steps": [
            [
              "Choose Year, Month and optionally an active Campus (or All campuses within the permitted institution).",
              "Year, Month ও প্রয়োজনে সক্রিয় Campus (অথবা অনুমোদিত প্রতিষ্ঠানের All campuses) বাছুন।"
            ],
            [
              "Select Load statement to view run, employee, period, status, gross, deductions and net.",
              "Load statement চাপলে রান, কর্মী, সময়কাল, অবস্থা, মোট বেতন, কর্তন ও নিট দেখা যায়।"
            ],
            [
              "Use Previous/Next; Current page totals cover only the visible 25-row page, not the full month.",
              "Previous/Next ব্যবহার করুন; Current page totals শুধু দৃশ্যমান ২৫ সারির, পুরো মাসের নয়।"
            ],
            [
              "Choose CSV export for the selected month, maximum 1,000 matching rows, or Print page for the current browser view.",
              "নির্বাচিত মাসের সর্বোচ্চ ১,০০০ মিল পাওয়া সারির CSV export নিন অথবা বর্তমান ব্রাউজার পৃষ্ঠা Print page করুন।"
            ]
          ],
          "example": [
            "Fictional example: select November 2026 and an authorized campus, review the current page net total and export if the month has at most 1,000 rows. No salary or journal changes.",
            "কাল্পনিক উদাহরণ: নভেম্বর ২০২৬ ও অনুমোদিত ক্যাম্পাস বেছে বর্তমান পৃষ্ঠার নিট মোট দেখুন; মাসে সর্বোচ্চ ১,০০০ সারি থাকলে CSV নিন। বেতন বা জার্নাল বদলায় না।"
          ],
          "result": [
            "Read-only payroll employee snapshots show Calculated/Approved/Posted status; CSV covers the bounded selected month and print uses the current browser page.",
            "শুধু দেখার স্ন্যাপশটে Calculated/Approved/Posted অবস্থা থাকে; CSV নির্বাচিত মাসের সীমিত সারি এবং প্রিন্টে বর্তমান ব্রাউজার পৃষ্ঠা আসে।"
          ],
          "rules": [
            "Amounts are historical payroll snapshots, not current salary structures. A statement does not approve, post, pay or reverse payroll. Interactive pages show 25 rows; CSV requires a single month and refuses more than 1,000 rows.",
            "পরিমাণ অতীতের বেতন স্ন্যাপশট, বর্তমান বেতন কাঠামো নয়। বিবরণী অনুমোদন, পোস্ট, পরিশোধ বা রিভার্স করে না। সাধারণ পৃষ্ঠায় ২৫ সারি; CSV-তে এক মাস লাগে এবং ১,০০০-এর বেশি সারি গ্রহণ করে না।"
          ],
          "recovery": [
            "If no rows appear check year, month, campus and run state. For invalid period or 403 correct filters or role. If CSV exceeds 1,000 rows narrow by campus; do not bypass tenant access.",
            "সারি না এলে বছর, মাস, ক্যাম্পাস ও রানের অবস্থা দেখুন। ভুল সময়কাল বা ৪০৩ হলে ফিল্টার বা ভূমিকা ঠিক করুন। CSV-তে ১,০০০-এর বেশি হলে ক্যাম্পাস দিয়ে সীমিত করুন; প্রতিষ্ঠান অনুমতি এড়াবেন না।"
          ],
          "related": [
            [
              "/PayrollOperations/Index",
              "Payroll operations",
              "বেতন ব্যবস্থাপনা"
            ],
            [
              "/PayrollJournalPosting/Index",
              "Payroll GL posting",
              "বেতন GL পোস্টিং"
            ]
          ]
        },
        '/alumni/index': {
                  "version": 1,
                  "reviewed": true,
                  "title": [
                            "Alumni & Graduate Directory",
                            "প্রাক্তন শিক্ষার্থী ও স্নাতক তালিকা"
                  ],
                  "purpose": [
                            "Manage voluntary alumni directory consent, future event RSVPs and authorized attendance without editing the original graduation record.",
                            "মূল উত্তীর্ণ হওয়ার রেকর্ড না বদলে ঐচ্ছিক প্রাক্তন শিক্ষার্থী তালিকার সম্মতি, ভবিষ্যৎ অনুষ্ঠানের উত্তর ও অনুমোদিত উপস্থিতি পরিচালনা করুন।"
                  ],
                  "before": [
                            "An authenticated account in an ALUMNI-enabled institution with a verified completed exit is required for personal consent and RSVP. TenantAdmin manages permitted campuses; a different TenantAdmin must publish drafts.",
                            "ALUMNI চালু থাকা প্রতিষ্ঠানের অনুমোদিত অ্যাকাউন্ট ও যাচাইকৃত সম্পন্ন ছাড়পত্র থাকলে নিজের সম্মতি ও RSVP দেওয়া যায়। TenantAdmin অনুমোদিত ক্যাম্পাস পরিচালনা করেন; খসড়া প্রকাশে অন্য TenantAdmin প্রয়োজন।"
                  ],
                  "steps": [
                            [
                                      "Choose Verified graduation, select directory consent, optionally enter contact email/phone and Save preferences.",
                                      "Verified graduation বেছে তালিকায় তথ্য প্রকাশের সম্মতি দিন, চাইলে ইমেইল/ফোন লিখে Save preferences চাপুন।"
                            ],
                            [
                                      "To revoke sharing, uncheck consent and Save preferences; this clears alumni contact details and active RSVPs but retains graduation evidence.",
                                      "তথ্য প্রকাশ বন্ধ করতে সম্মতির টিক তুলে Save preferences চাপুন; এতে যোগাযোগের তথ্য ও সক্রিয় RSVP মুছে যায়, উত্তীর্ণ হওয়ার প্রমাণ থাকে।"
                            ],
                            [
                                      "For a published future event choose Going or Not going; reload before retrying a conflicting response.",
                                      "প্রকাশিত ভবিষ্যৎ অনুষ্ঠানে Going বা Not going বেছে নিন; সংঘাত হলে নতুন উত্তর দেওয়ার আগে রিলোড করুন।"
                            ],
                            [
                                      "TenantAdmin: choose an authorized Campus, title and local start/end times, then Create draft for approval. A different administrator selects Publish; authorized staff can Cancel.",
                                      "TenantAdmin: অনুমোদিত Campus, শিরোনাম ও স্থানীয় শুরু/শেষের সময় দিয়ে Create draft for approval চাপুন। অন্য প্রশাসক Publish করবেন; অনুমোদিত কর্মী Cancel করতে পারেন।"
                            ],
                            [
                                      "TenantAdmin: during a published event open Attendance, Check in consenting attendees or Undo a check-in, and page through the roster.",
                                      "TenantAdmin: প্রকাশিত অনুষ্ঠান চলাকালে Attendance খুলে সম্মতিপ্রাপ্ত অংশগ্রহণকারীর Check in বা Undo করুন এবং তালিকার পৃষ্ঠা দেখুন।"
                            ],
                            [
                                      "TenantAdmin: Search the opted-in directory and Export current page CSV; Export page engagement CSV contains aggregate event counts, not attendee contacts.",
                                      "TenantAdmin: সম্মতিপ্রাপ্ত তালিকায় Search করে Export current page CSV নিন; Export page engagement CSV-তে অংশগ্রহণকারীর যোগাযোগ নয়, অনুষ্ঠানের মোট সংখ্যা থাকে।"
                            ]
                  ],
                  "example": [
                            "Fictional example: graduate Amina opts in for her verified record and selects Going for an 8 November 2026 reunion. One administrator drafts the event, another publishes it, and authorized staff check her in during the event.",
                            "কাল্পনিক উদাহরণ: উত্তীর্ণ শিক্ষার্থী আমিনা নিজের যাচাইকৃত রেকর্ডে সম্মতি দিয়ে ৮ নভেম্বর ২০২৬-এর পুনর্মিলনীতে Going দিলেন। একজন প্রশাসক খসড়া, অন্যজন প্রকাশ এবং অনুমোদিত কর্মী অনুষ্ঠানে তাঁর উপস্থিতি নিলেন।"
                  ],
                  "result": [
                            "Consent determines restricted directory visibility. Valid RSVP and attendance appear in authorized event views; CSV exports are limited to eligible current-page data. CSV headings and event status labels follow the selected language; timestamps remain ISO-8601 UTC.",
                            "সম্মতি সীমিত তালিকায় তথ্য দেখানো নিয়ন্ত্রণ করে। বৈধ RSVP ও উপস্থিতি অনুমোদিত অনুষ্ঠান পৃষ্ঠায় দেখা যায়; CSV-তে শুধু যোগ্য বর্তমান পৃষ্ঠার তথ্য থাকে। CSV শিরোনাম ও অনুষ্ঠানের অবস্থা নির্বাচিত ভাষায় থাকে; সময় ISO-8601 UTC-তে থাকে।"
                  ],
                  "rules": [
                            "Only verified completed exits qualify. Consent is optional and revocable; revocation clears active RSVPs. Publication needs a different administrator. Idempotency keys and row versions reject stale/duplicate writes; no outbound email/SMS invitation or Finance/stock posting occurs here.",
                            "শুধু যাচাইকৃত সম্পন্ন ছাড়পত্র যোগ্য। সম্মতি ঐচ্ছিক ও প্রত্যাহারযোগ্য; প্রত্যাহারে সক্রিয় RSVP মুছে যায়। প্রকাশে আলাদা প্রশাসক প্রয়োজন। Request ID ও row version পুরোনো/দ্বৈত পরিবর্তন প্রতিরোধ করে; এখান থেকে ইমেইল/SMS আমন্ত্রণ বা Finance/স্টক পোস্টিং হয় না।"
                  ],
                  "recovery": [
                            "If no graduate appears, ask an authorized administrator to verify the completed exit and account link. For missing campus/events check entitlement, campus access, consent and timing. For 403/409 reload; never guess another record ID or reuse an obsolete request.",
                            "শিক্ষার্থী না এলে অনুমোদিত প্রশাসকের কাছে সম্পন্ন ছাড়পত্র ও অ্যাকাউন্ট সংযোগ যাচাই করুন। ক্যাম্পাস/অনুষ্ঠান না এলে মডিউল, ক্যাম্পাস অনুমতি, সম্মতি ও সময় দেখুন। 403/409 হলে রিলোড করুন; অন্য ID অনুমান বা পুরোনো অনুরোধ ব্যবহার নয়।"
                  ],
                  "related": [
                            [
                                      "/Students/Index",
                                      "Student directory (authorized staff only)",
                                      "শিক্ষার্থী তালিকা (শুধু অনুমোদিত কর্মী)"
                            ]
                  ]
        },
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
                ['For an unstarted, unassigned Approved order, a different manager may Request cancellation with a 10-500 character reason. An independent approver then Approves or Rejects cancellation. Release an existing schedule with a reason before requesting cancellation.', 'কাজ শুরু হয়নি ও কাউকে দেওয়া হয়নি এমন Approved অর্ডারে আলাদা ম্যানেজার ১০-৫০০ অক্ষরের কারণ দিয়ে বাতিলের আবেদন করতে পারেন। স্বতন্ত্র অনুমোদনকারী বাতিল অনুমোদন বা প্রত্যাখ্যান করেন। সময়সূচি থাকলে আগে কারণ দিয়ে সেটি বাতিল করতে হবে।'],
                ['An independent approver chooses Verify closure or Return for correction with a reason. After a return, the technician corrects the work and submits fresh evidence for verification.', 'স্বতন্ত্র অনুমোদনকারী সমাপ্তি যাচাই অথবা কারণসহ সংশোধনের জন্য ফেরত দেন। ফেরত এলে টেকনিশিয়ান সংশোধন করে নতুন প্রমাণ জমা দেন।'],
                ['Use Search, overdue filter, dashboard, history and authorized CSV or Print to review progress.', 'খোঁজ, বিলম্বিত কাজের ফিল্টার, ড্যাশবোর্ড, ইতিহাস ও অনুমোদিত CSV বা প্রিন্টে অগ্রগতি যাচাই করুন।']
            ],
            example: ['Fictional example: report a broken hallway light in an authorized campus space. A manager sets tomorrow as SLA, a different approver approves, a technician repairs it, and the verifier returns unsafe wiring with a reason. The technician fixes it and resubmits before final closure.', 'কাল্পনিক উদাহরণ: অনুমোদিত ক্যাম্পাসের করিডরের বাতি নষ্ট হলে অনুরোধ দিন। ম্যানেজার পরদিনের SLA দেন, অন্য অনুমোদনকারী অনুমোদন করেন, টেকনিশিয়ান মেরামত করেন। তারের ত্রুটি থাকলে যাচাইকারী কারণ দিয়ে ফেরত দেন; সংশোধনের পর পুনরায় প্রমাণ জমা দিয়ে কাজ বন্ধ হয়।'],
            result: ['The request and work order show the latest state; the append-only history retains both completion submissions and the return reason. Returned work is not counted as closed; approved cancellation is terminal and excluded from overdue-open totals.', 'অনুরোধ ও ওয়ার্ক অর্ডারে বর্তমান অবস্থা এবং ইতিহাসে উভয় সমাপ্তির প্রমাণ ও ফেরতের কারণ থাকে। ফেরত কাজ সমাপ্ত হিসেবে গণনা হয় না; অনুমোদিত বাতিল চূড়ান্ত এবং বিলম্বিত খোলা কাজের হিসাবে থাকে না।'],
            rules: ['Request maker, triage manager, approver and assigned technician have separate controls. Return and each cancellation decision need a 10-500 character reason. The cancellation maker cannot approve their own request; scheduled work must first be released, and started work cannot be cancelled. Duplicate command IDs cannot repeat the action; stale row versions fail. This page does not issue spare parts or post Finance GL.', 'অনুরোধকারী, ট্রায়াজ ম্যানেজার, অনুমোদনকারী ও নির্ধারিত টেকনিশিয়ানের আলাদা অনুমতি রয়েছে। ফেরত ও বাতিলের প্রতিটি সিদ্ধান্তে ১০-৫০০ অক্ষরের কারণ প্রয়োজন। আবেদনকারী নিজের বাতিল অনুমোদন করতে পারবেন না; নির্ধারিত কাজ আগে সময়সূচি থেকে ছাড়তে হবে এবং শুরু হওয়া কাজ বাতিল করা যাবে না। একই কমান্ড পুনরায় কাজ করে না; পুরোনো সংস্করণ বাতিল হয়। এই পৃষ্ঠায় খুচরা যন্ত্রাংশ ইস্যু বা Finance GL পোস্ট হয় না।'],
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
