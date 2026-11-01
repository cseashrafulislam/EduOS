/* One shared page manual renderer; reviewed route content only, never HTML from a URL.
 * Language follows the server-selected document culture. Related links must be present
 * in the currently rendered, server-authorized navigation. No remote guide API/cache. */
(() => {
    'use strict';
    // The examination module has distinct setup, marks, review, publishing
    // and historical-report workflows. Every guide below follows an actual
    // existing authorized page; no new result or grade engine is implied.
    const examManuals = {
        '/assessmentadministration/marks/index': {
            version: 1, reviewed: true,
            title: ['Marks Entry', 'নম্বর এন্ট্রি'],
            purpose: ['Find an authorized assessment subject and enter or correct permitted student marks before the assessment is locked.', 'অনুমোদিত মূল্যায়নের বিষয় খুঁজে লক হওয়ার আগে শিক্ষার্থীর নম্বর লিখুন বা সংশোধন করুন।'],
            before: ['EXAM module and TenantAdmin, Principal, VicePrincipal, Teacher or ExamController access; the assessment must permit marks entry.', 'EXAM মডিউল এবং TenantAdmin, Principal, VicePrincipal, Teacher বা ExamController অনুমতি; মূল্যায়নে নম্বর এন্ট্রি খোলা থাকতে হবে।'],
            steps: [
                ['Search for the assessment, batch or subject, then select Assessment, Batch/Section and Subject.', 'মূল্যায়ন, ব্যাচ বা বিষয় খুঁজে Assessment, Batch/Section ও Subject বেছে নিন।'],
                ['Select Load marks register, search or page through enrolled students and enter allowed marks or attendance states.', 'Load marks register চাপুন, শিক্ষার্থীদের খুঁজুন বা পৃষ্ঠা বদলান এবং অনুমোদিত নম্বর বা অনুপস্থিতির অবস্থা দিন।'],
                ['Select Save changed marks, check the confirmation and use Marks Submission & Review for the separate review process.', 'Save changed marks দিয়ে নিশ্চিতকরণ দেখুন; আলাদা যাচাইয়ের জন্য Marks Submission & Review খুলুন।']
            ],
            example: ['Fictional example: choose the Grade 9 A Mathematics assessment, record verified marks for the shown students and save changes while entry is open.', 'কাল্পনিক উদাহরণ: নবম শ্রেণি A-এর গণিত মূল্যায়ন বেছে দেখানো শিক্ষার্থীদের যাচাইকৃত নম্বর লিখে এন্ট্রি খোলা থাকতে সংরক্ষণ করুন।'],
            result: ['Updated eligible marks appear in the subject roster; review and publication remain separate authorized steps.', 'অনুমোদিত পরিবর্তিত নম্বর বিষয় তালিকায় দেখা যায়; যাচাই ও ফলাফল প্রকাশ আলাদা ধাপ।'],
            rules: ['Locked or approved marks cannot be silently edited; roster and page CSV are bounded to the authorized scope. Saving marks does not publish results.', 'লক বা অনুমোদিত নম্বর নীরবে বদলানো যায় না; তালিকা ও CSV অনুমোদিত সীমায় থাকে। নম্বর সংরক্ষণে ফলাফল প্রকাশ হয় না।'],
            recovery: ['If the roster is missing, verify the assessment state and subject. On a stale or failed save, reload before editing; do not bypass the review lock.', 'তালিকা না পেলে মূল্যায়নের অবস্থা ও বিষয় দেখুন। পুরোনো সংস্করণ বা ব্যর্থ সংরক্ষণে আবার সম্পাদনার আগে রিলোড করুন; যাচাইয়ের লক এড়াবেন না।'],
            related: [['/AssessmentAdministration/MarkReviews', 'Marks submission and review', 'নম্বর জমা ও যাচাই'], ['/AssessmentAdministration/ComponentMarks', 'Component marks', 'উপাদানভিত্তিক নম্বর']]
        },
        '/assessmentadministration/results/index': {
            version: 1, reviewed: true,
            title: ['Result Review & Publication', 'ফলাফল যাচাই ও প্রকাশ'],
            purpose: ['Verify batch readiness, lock an eligible assessment, preview calculated results and publish an immutable snapshot.', 'ব্যাচের প্রস্তুতি যাচাই, উপযুক্ত মূল্যায়ন লক, হিসাব করা ফলাফলের প্রিভিউ ও অপরিবর্তনীয় সংস্করণ প্রকাশ করুন।'],
            before: ['EXAM module and TenantAdmin, Principal, VicePrincipal or ExamController role; complete and reviewed marks for the intended batch.', 'EXAM মডিউল ও TenantAdmin, Principal, VicePrincipal বা ExamController ভূমিকা; নির্দিষ্ট ব্যাচের নম্বর সম্পূর্ণ ও যাচাইকৃত হতে হবে।'],
            steps: [
                ['Search and select assessment, batch and subject, then Load assessment.', 'মূল্যায়ন, ব্যাচ ও বিষয় নির্বাচন করে Load assessment চাপুন।'],
                ['Check the assessment state, attempt/credit policy and batch readiness issues; resolve missing evidence before locking.', 'মূল্যায়নের অবস্থা, প্রচেষ্টার/ক্রেডিট নীতি ও ব্যাচ প্রস্তুতির ত্রুটি দেখুন; লক করার আগে ঘাটতি ঠিক করুন।'],
                ['Use the authorized Next stage control to verify and lock as allowed; select Preview draft and inspect calculated results and visibility settings.', 'অনুমতি অনুযায়ী Next stage দিয়ে যাচাই ও লক করুন; Preview draft দিয়ে ফলাফল ও দৃশ্যমানতার সেটিংস দেখুন।'],
                ['When the locked preview is correct, choose Publish results and type PUBLISH to confirm. Inspect the saved publication separately.', 'লক করা প্রিভিউ সঠিক হলে Publish results চাপুন এবং PUBLISH লিখে নিশ্চিত করুন। সংরক্ষিত সংস্করণ আলাদা পাতায় দেখুন।']
            ],
            example: ['Fictional example: check the Grade 9 A final assessment for missing marks, lock after review, preview calculated grades and publish the verified batch snapshot.', 'কাল্পনিক উদাহরণ: নবম শ্রেণি A-এর চূড়ান্ত মূল্যায়নে অসম্পূর্ণ নম্বর দেখুন, যাচাই করে লক করুন, গ্রেড প্রিভিউ করে ব্যাচের ফলাফল প্রকাশ করুন।'],
            result: ['A successful publication creates a saved immutable result snapshot; published history and withdrawal are managed separately.', 'সফল প্রকাশে অপরিবর্তনীয় ফলাফল সংস্করণ সংরক্ষিত হয়; ইতিহাস ও প্রত্যাহার আলাদা কাজ।'],
            rules: ['The readiness list is only page-level evidence, not global approval. Published results are immutable, require a locked eligible assessment and an explicit confirmation; this is not permission to rewrite historical results.', 'প্রস্তুতির তালিকা শুধু দৃশ্যমান পৃষ্ঠার প্রমাণ, সামগ্রিক অনুমোদন নয়। প্রকাশিত ফল অপরিবর্তনীয়; উপযুক্ত লক ও স্পষ্ট নিশ্চিতকরণ দরকার। পুরোনো ফল বদলানোর অনুমতি নয়।'],
            recovery: ['If publication times out, inspect the saved snapshot/history before retrying the same request. Resolve readiness, state and permission errors at their source.', 'প্রকাশে সময় শেষ হলে একই অনুরোধ আবার দেওয়ার আগে সংরক্ষিত ফলাফল/ইতিহাস দেখুন। প্রস্তুতি, অবস্থা ও অনুমতির ত্রুটি মূল জায়গায় ঠিক করুন।'],
            related: [['/AssessmentAdministration/Marks', 'Marks entry', 'নম্বর এন্ট্রি'], ['/AssessmentAdministration/Publications', 'Publication history', 'প্রকাশনার ইতিহাস']]
        },
        '/assessmentadministration/publications/index': {
            version: 1, reviewed: true,
            title: ['Published Results & History', 'প্রকাশিত ফলাফল ও ইতিহাস'],
            purpose: ['Inspect saved publication versions, verify snapshot integrity, compare history and export bounded tabulations.', 'সংরক্ষিত ফলাফলের সংস্করণ, স্ন্যাপশট যাচাই, ঐতিহাসিক তুলনা ও সীমিত ট্যাবুলেশন রপ্তানি করুন।'],
            before: ['EXAM module and TenantAdmin, Principal, VicePrincipal or ExamController permission; a previously published assessment and batch.', 'EXAM মডিউল এবং TenantAdmin, Principal, VicePrincipal বা ExamController অনুমতি; আগে প্রকাশিত মূল্যায়ন ও ব্যাচ প্রয়োজন।'],
            steps: [
                ['Find the assessment/batch and select Load assessment, then Load publication versions.', 'মূল্যায়ন/ব্যাচ খুঁজে Load assessment, তারপর Load publication versions চাপুন।'],
                ['Select a saved or withdrawn version and inspect its integrity, historical comparison and publication details.', 'সংরক্ষিত বা প্রত্যাহার করা সংস্করণ বেছে সততা যাচাই, আগের ফলের তুলনা ও বিবরণ দেখুন।'],
                ['For a reconciled version, load saved tabulation; page, search, export the current page CSV or print.', 'সমন্বিত সংস্করণে ট্যাবুলেশন দেখুন; পৃষ্ঠা বদলান, খুঁজুন, বর্তমান পৃষ্ঠার CSV নিন বা প্রিন্ট করুন।'],
                ['Only when authorized and necessary, provide a withdrawal reason and type WITHDRAW; verify the refreshed history.', 'শুধু অনুমোদিত প্রয়োজন হলে প্রত্যাহারের কারণ দিয়ে WITHDRAW লিখুন এবং রিফ্রেশ হওয়া ইতিহাস যাচাই করুন।']
            ],
            example: ['Fictional example: compare Version 2 of a published 2026 final assessment with its earlier version, check reconciled tabulation and export the visible page.', 'কাল্পনিক উদাহরণ: ২০২৬-এর প্রকাশিত চূড়ান্ত মূল্যায়নের Version 2 আগেরটির সঙ্গে মিলিয়ে ট্যাবুলেশন যাচাই করে দৃশ্যমান পৃষ্ঠা CSV নিন।'],
            result: ['Authorized historical versions, comparisons and tabulations appear; a withdrawal hides a published version but preserves its audited record.', 'অনুমোদিত পুরোনো সংস্করণ, তুলনা ও ট্যাবুলেশন দেখা যায়; প্রত্যাহারে প্রকাশ আড়াল হয়, নিরীক্ষার রেকর্ড থাকে।'],
            rules: ['Unreconciled versions cannot be used for tabulation/export. Exports are page-bounded; historical marks remain snapshots and current directory names may differ.', 'অসমন্বিত সংস্করণ থেকে ট্যাবুলেশন/এক্সপোর্ট হয় না। CSV পৃষ্ঠাভিত্তিক; পুরোনো নম্বর স্ন্যাপশট, বর্তমান নামের সঙ্গে পার্থক্য থাকতে পারে।'],
            recovery: ['Reload versions after a conflict or uncertain withdrawal; verify state before retrying. If tabulation is blocked, resolve the recorded integrity issue.', 'সংঘাত বা অনিশ্চিত প্রত্যাহারে সংস্করণ রিফ্রেশ করে অবস্থা দেখে আবার চেষ্টা করুন। ট্যাবুলেশন আটকে গেলে সততার ত্রুটি সমাধান করুন।'],
            related: [['/AssessmentAdministration/Results', 'Result review and publication', 'ফলাফল যাচাই ও প্রকাশ'], ['/AssessmentAdministration/GradeDistribution', 'Grade distribution', 'গ্রেড বণ্টন']]
        },
        '/assessmentadministration/setup/index': {
            version: 1, reviewed: true,
            title: ['Assessment Setup', 'মূল্যায়ন সেটআপ'],
            purpose: ['Create and edit assessment definitions and assign subjects before marks entry.', 'নম্বর এন্ট্রির আগে মূল্যায়নের সংজ্ঞা তৈরি/সংশোধন ও বিষয় নির্ধারণ করুন।'],
            before: ['EXAM module and examination-administrator access; an existing campus, academic year and grade scheme.', 'EXAM মডিউল ও পরীক্ষা প্রশাসনের অনুমতি; আগে ক্যাম্পাস, শিক্ষাবর্ষ ও গ্রেডিং নিয়ম থাকতে হবে।'],
            steps: [
                ['Search existing assessments and inspect the register before choosing New.', 'নতুন করার আগে বিদ্যমান মূল্যায়ন খুঁজে তালিকা দেখুন।'],
                ['Enter assessment name, code, type, campus, year, optional term, grade scheme and dates.', 'নাম, কোড, ধরন, ক্যাম্পাস, শিক্ষাবর্ষ, ঐচ্ছিক টার্ম, গ্রেডিং নিয়ম ও তারিখ দিন।'],
                ['Save the assessment definition, then assign eligible subjects in the page’s subject section.', 'মূল্যায়নের সংজ্ঞা সংরক্ষণ করে সংশ্লিষ্ট বিষয় অংশে উপযুক্ত বিষয় যুক্ত করুন।']
            ],
            example: ['Fictional example: define Final 2026 for Campus 1 and its actual academic year, then attach the applicable Grade 9 subjects.', 'কাল্পনিক উদাহরণ: Campus 1 ও প্রকৃত শিক্ষাবর্ষের জন্য Final 2026 তৈরি করে নবম শ্রেণির উপযুক্ত বিষয় যুক্ত করুন।'],
            result: ['Saved eligible assessments become available for scheduling and marks workflows.', 'সংরক্ষিত মূল্যায়ন সময়সূচি ও নম্বর এন্ট্রির কাজে পাওয়া যায়।'],
            rules: ['Setup is not marks approval or result publication. Existing scheduled/used assessments may restrict edits; do not recreate to avoid a conflict.', 'সেটআপ মানে নম্বর অনুমোদন বা ফলাফল প্রকাশ নয়। নির্ধারিত/ব্যবহৃত মূল্যায়নের পরিবর্তন সীমিত হতে পারে; সংঘাত এড়াতে আবার তৈরি করবেন না।'],
            recovery: ['Check campus, year, code uniqueness and dates, then reload the saved definition before retrying an update.', 'ক্যাম্পাস, শিক্ষাবর্ষ, কোডের স্বতন্ত্রতা ও তারিখ যাচাই করে সম্পাদনার আগে সংরক্ষিত তথ্য রিফ্রেশ করুন।'],
            related: [['/AssessmentAdministration/AssessmentSchedules', 'Exam schedules', 'পরীক্ষার সময়সূচি'], ['/AssessmentAdministration/Marks', 'Marks entry', 'নম্বর এন্ট্রি']]
        },
        '/assessmentadministration/markreviews/index': {
            version: 1, reviewed: true,
            title: ['Marks Submission & Review', 'নম্বর জমা ও যাচাই'],
            purpose: ['Submit completed subject marks and independently approve or reject them with recorded decision history.', 'সম্পন্ন বিষয়ের নম্বর জমা দিন এবং ভিন্ন অনুমোদিত ব্যক্তি দিয়ে ইতিহাসসহ যাচাই বা প্রত্যাখ্যান করুন।'],
            before: ['EXAM module; submitters require an authorized subject, and decision controls require examination-administrator permission.', 'EXAM মডিউল; জমা দিতে অনুমোদিত বিষয়, সিদ্ধান্ত দিতে পরীক্ষা প্রশাসকের অনুমতি প্রয়োজন।'],
            steps: [
                ['Search assessment/batch/subject and select the authorized subject.', 'মূল্যায়ন/ব্যাচ/বিষয় খুঁজে অনুমোদিত বিষয় নির্বাচন করুন।'],
                ['After completing the subject marks, select Submit marks and inspect the submission status.', 'বিষয়ের নম্বর সম্পূর্ণ করে Submit marks চাপুন ও অবস্থা দেখুন।'],
                ['An independent authorized reviewer filters the decision queue, selects a submission and Approves or Rejects with its required reason.', 'স্বাধীন অনুমোদিত যাচাইকারী সিদ্ধান্তের তালিকা ফিল্টার করে আবেদন বেছে প্রয়োজনীয় কারণসহ Approve বা Reject করেন।'],
                ['Inspect paged decision history; if rejected, correct eligible marks and resubmit.', 'পৃষ্ঠাভিত্তিক সিদ্ধান্তের ইতিহাস দেখুন; প্রত্যাখ্যান হলে অনুমোদিত নম্বর ঠিক করে আবার জমা দিন।']
            ],
            example: ['Fictional example: a teacher submits Grade 9 Mathematics marks; a different ExamController reviews the queue and approves the complete subject.', 'কাল্পনিক উদাহরণ: শিক্ষক নবম শ্রেণির গণিতের নম্বর জমা দেন; ভিন্ন ExamController তালিকা যাচাই করে সম্পূর্ণ বিষয় অনুমোদন করেন।'],
            result: ['The submission status and audit history change; approved subject marks are locked from ordinary changes.', 'জমার অবস্থা ও নিরীক্ষার ইতিহাস বদলায়; অনুমোদিত বিষয়ের নম্বর সাধারণ সম্পাদনা থেকে লক হয়।'],
            rules: ['Submitter and reviewer must differ; a rejected submission may return to correction. Approval is not result publication.', 'জমাদাতা ও যাচাইকারী আলাদা হতে হবে; প্রত্যাখ্যানে সংশোধন করা যায়। অনুমোদন মানে ফলাফল প্রকাশ নয়।'],
            recovery: ['If the queue or status looks stale, reload its current decision/version before retrying; do not bypass review by saving new marks elsewhere.', 'তালিকা বা অবস্থা পুরোনো মনে হলে পুনরায় চেষ্টার আগে বর্তমান সিদ্ধান্ত/সংস্করণ রিফ্রেশ করুন; অন্যত্র নতুন নম্বর দিয়ে যাচাই এড়াবেন না।'],
            related: [['/AssessmentAdministration/Marks', 'Marks entry', 'নম্বর এন্ট্রি'], ['/AssessmentAdministration/Results', 'Result publication', 'ফলাফল প্রকাশ']]
        },
        '/assessmentadministration/attempts/index': {
            version: 1, reviewed: true,
            title: ['Retake & Improvement', 'পুনঃপরীক্ষা ও উন্নয়ন'],
            purpose: ['Review authorized student-specific retake or grade-improvement requests against earlier published course results.', 'আগে প্রকাশিত কোর্স ফলাফলের ভিত্তিতে শিক্ষার্থীভিত্তিক পুনঃপরীক্ষা বা গ্রেড উন্নয়নের আবেদন যাচাই করুন।'],
            before: ['EXAM module and examination-administrator role; a historical published result and eligible assessment subject.', 'EXAM মডিউল ও পরীক্ষা প্রশাসকের অনুমতি; পূর্বপ্রকাশিত ফলাফল ও উপযুক্ত বিষয় প্রয়োজন।'],
            steps: [
                ['Search and select assessment, batch and subject in Assessment subject.', 'Assessment subject-এ মূল্যায়ন, ব্যাচ ও বিষয় বাছুন।'],
                ['Find eligible students, review their earlier published evidence and enter an approval/request reason.', 'যোগ্য শিক্ষার্থী খুঁজে আগের প্রকাশিত ফলাফলের প্রমাণ ও আবেদনের কারণ যাচাই করুন।'],
                ['Use the displayed request and approval queue to record the permitted decision; inspect history.', 'দেখানো আবেদন ও অনুমোদনের তালিকা থেকে অনুমোদিত সিদ্ধান্ত নথিভুক্ত করে ইতিহাস দেখুন।']
            ],
            example: ['Fictional example: review a failed Mathematics course result for an eligible student before authorizing a retake attempt.', 'কাল্পনিক উদাহরণ: একজন উপযুক্ত শিক্ষার্থীর গণিতে আগের অনুত্তীর্ণ ফল যাচাই করে পুনঃপরীক্ষার অনুমতি দিন।'],
            result: ['An authorized attempt request and decision are recorded; the original published result remains unchanged.', 'অনুমোদিত প্রচেষ্টার আবেদন ও সিদ্ধান্ত থাকে; মূল প্রকাশিত ফল অপরিবর্তিত থাকে।'],
            rules: ['Retake and improvement have different eligibility. Approval does not rewrite old marks, publish a result or override later assessment validation.', 'পুনঃপরীক্ষা ও উন্নয়নের যোগ্যতা আলাদা। অনুমোদনে পুরোনো নম্বর বদলায় না, নতুন ফল প্রকাশ হয় না।'],
            recovery: ['If a student is not eligible or the request conflicts, inspect the previous published record and current request history before retrying.', 'শিক্ষার্থী অযোগ্য বা আবেদনে সংঘাত হলে আবার চেষ্টা করার আগে পুরোনো ফল ও বর্তমান আবেদন ইতিহাস দেখুন।'],
            related: [['/AssessmentAdministration/Publications', 'Published results', 'প্রকাশিত ফলাফল'], ['/AssessmentAdministration/Results', 'Result publication', 'ফলাফল প্রকাশ']]
        },
        '/assessmentadministration/resultrescrutiny/index': {
            version: 1, reviewed: true,
            title: ['Result Re-scrutiny', 'ফলাফল পুনর্নিরীক্ষণ'],
            purpose: ['Record and independently decide applications to recheck a saved published result without changing its original marks.', 'প্রকাশিত ফলাফল পুনরায় যাচাইয়ের আবেদন ও স্বাধীন সিদ্ধান্ত নথিভুক্ত করুন, মূল নম্বর পরিবর্তন ছাড়াই।'],
            before: ['EXAM module and examination-administrator role; an authorized assessment, published version and affected student.', 'EXAM মডিউল ও পরীক্ষা প্রশাসকের অনুমতি; অনুমোদিত মূল্যায়ন, প্রকাশিত সংস্করণ ও সংশ্লিষ্ট শিক্ষার্থী প্রয়োজন।'],
            steps: [
                ['Find the assessment/batch and choose the relevant Published version.', 'মূল্যায়ন/ব্যাচ খুঁজে সংশ্লিষ্ট Published version বাছুন।'],
                ['Search and select the correct student, enter Reason for re-scrutiny and Submit application.', 'সঠিক শিক্ষার্থী খুঁজে Reason for re-scrutiny লিখে Submit application চাপুন।'],
                ['Use Applications and decisions to review the request through the permitted independent reviewer controls.', 'Applications and decisions থেকে অনুমোদিত স্বাধীন যাচাইকারীর মাধ্যমে আবেদন যাচাই করুন।'],
                ['Review the refreshed decision history before any separate audited marks-correction action.', 'আলাদা নিরীক্ষাযোগ্য নম্বর সংশোধনের আগে রিফ্রেশ হওয়া সিদ্ধান্তের ইতিহাস যাচাই করুন।']
            ],
            example: ['Fictional example: submit a request to recheck a published 2026 Mathematics result for ST-1042 and have a different reviewer decide it.', 'কাল্পনিক উদাহরণ: ST-1042-এর ২০২৬ সালের প্রকাশিত গণিতের ফল পুনর্নিরীক্ষণের আবেদন দিয়ে ভিন্ন যাচাইকারী দিয়ে সিদ্ধান্ত নিন।'],
            result: ['The request and decision are recorded; acceptance itself does not rewrite the original saved grade or publication.', 'আবেদন ও সিদ্ধান্ত নথিভুক্ত হয়; গ্রহণ করলেই মূল গ্রেড বা প্রকাশিত ফল বদলায় না।'],
            rules: ['The requester cannot decide their own case. The existing historical snapshot stays immutable; an approved separate correction workflow is required for marks changes.', 'আবেদনকারী নিজের আবেদন সিদ্ধান্ত দিতে পারেন না। আগের স্ন্যাপশট অপরিবর্তনীয়; নম্বর পরিবর্তনে আলাদা অনুমোদিত সংশোধন দরকার।'],
            recovery: ['If selection or review conflicts, refresh the published version and request status, then follow the displayed validation; do not submit duplicates.', 'নির্বাচন বা সিদ্ধান্তে সংঘাত হলে প্রকাশিত সংস্করণ ও আবেদনের অবস্থা রিফ্রেশ করুন; একাধিক আবেদন তৈরি করবেন না।'],
            related: [['/AssessmentAdministration/Publications', 'Publication history', 'প্রকাশনার ইতিহাস'], ['/AssessmentAdministration/ResultFollowUp', 'Result follow-up', 'ফলাফল পরবর্তী পর্যালোচনা']]
        },
        '/assessmentadministration/resultfollowup/index': {
            version: 1, reviewed: true,
            title: ['Result Follow-up', 'ফলাফল পরবর্তী পর্যালোচনা'],
            purpose: ['Review failed and withheld outcomes from a finalized result version to plan authorized follow-up.', 'চূড়ান্ত ফলাফলের সংস্করণ থেকে অনুত্তীর্ণ ও স্থগিত ফল দেখে অনুমোদিত পরবর্তী ব্যবস্থা পরিকল্পনা করুন।'],
            before: ['EXAM module and examination-administrator access; a finalized, authorized assessment/batch publication.', 'EXAM মডিউল ও পরীক্ষা প্রশাসকের অনুমতি; চূড়ান্ত অনুমোদিত মূল্যায়ন/ব্যাচ প্রকাশনা প্রয়োজন।'],
            steps: [
                ['Search and select assessment/batch, then the Finalized version.', 'মূল্যায়ন/ব্যাচ খুঁজে Finalized version বেছে নিন।'],
                ['Filter Outcome and optional roll, student code or name, then Load register.', 'Outcome ও প্রয়োজনীয় রোল, কোড বা নাম দিয়ে Load register চাপুন।'],
                ['Inspect the bounded result rows; Export CSV (up to 500) or Print page if authorized.', 'সীমিত ফলাফলের তালিকা দেখুন; অনুমতি থাকলে সর্বোচ্চ ৫০০ CSV রপ্তানি বা প্রিন্ট করুন।']
            ],
            example: ['Fictional example: filter the saved Grade 9 final publication for failed students and export an authorized follow-up list.', 'কাল্পনিক উদাহরণ: নবম শ্রেণির প্রকাশিত চূড়ান্ত ফলের অনুত্তীর্ণ শিক্ষার্থী খুঁজে অনুমোদিত পরবর্তী ব্যবস্থা তালিকা CSV নিন।'],
            result: ['Matching failed/withheld results appear for administrative review; no eligibility or retake approval is created.', 'মিল পাওয়া অনুত্তীর্ণ/স্থগিত ফল দেখা যায়; এতে পুনঃপরীক্ষার যোগ্যতা বা অনুমোদন তৈরি হয় না।'],
            rules: ['A read-only follow-up register is not an automatic retake/improvement authorization. Exports respect the page’s scope and limits.', 'এটি শুধু পড়ার রিপোর্ট; স্বয়ংক্রিয় পুনঃপরীক্ষা/উন্নয়নের অনুমতি নয়। CSV-এর পরিসর ও সীমা মানতে হবে।'],
            recovery: ['If the report is empty, verify selected version and filters. Reload after a withdrawn or changed publication and avoid reusing stale exports.', 'রিপোর্ট ফাঁকা হলে সংস্করণ ও ফিল্টার দেখুন। প্রকাশনা বদলালে রিফ্রেশ করুন; পুরোনো CSV-কে বর্তমান ভাববেন না।'],
            related: [['/AssessmentAdministration/Attempts', 'Retake approvals', 'পুনঃপরীক্ষার অনুমোদন'], ['/AssessmentAdministration/Publications', 'Published results', 'প্রকাশিত ফলাফল']]
        },
        '/assessmentadministration/gradedistribution/index': {
            version: 1, reviewed: true,
            title: ['Historical Grade Distribution', 'ঐতিহাসিক গ্রেড বণ্টন'],
            purpose: ['Report saved grade counts by finalized publication version and optionally compare an earlier version.', 'চূড়ান্ত প্রকাশিত সংস্করণ অনুযায়ী গ্রেডের সংখ্যা ও প্রয়োজনে পুরোনো সংস্করণের তুলনা দেখুন।'],
            before: ['EXAM module and examination-administrator permission; at least one finalized publication for the chosen assessment/batch.', 'EXAM মডিউল ও পরীক্ষা প্রশাসকের অনুমতি; নির্বাচিত মূল্যায়ন/ব্যাচের অন্তত একটি চূড়ান্ত প্রকাশিত সংস্করণ থাকতে হবে।'],
            steps: [
                ['Search an authorized assessment/batch and select the Finalized publication version.', 'অনুমোদিত মূল্যায়ন/ব্যাচ খুঁজে Finalized publication version বাছুন।'],
                ['Optionally choose Compare with earlier version and select Load distribution.', 'প্রয়োজনে আগের সংস্করণ তুলনায় বেছে Load distribution চাপুন।'],
                ['Read current grade distribution and historical differences; use scoped CSV or Print.', 'বর্তমান গ্রেড বণ্টন ও পুরোনো পার্থক্য দেখুন; পরিসর অনুযায়ী CSV নিন বা প্রিন্ট করুন।']
            ],
            example: ['Fictional example: compare final exam Grade A/B/C counts from Version 2 against Version 1 for an authorized batch.', 'কাল্পনিক উদাহরণ: অনুমোদিত ব্যাচের চূড়ান্ত পরীক্ষায় Version 2-এর A/B/C গ্রেডের সংখ্যা Version 1-এর সঙ্গে মিলিয়ে দেখুন।'],
            result: ['Grade category totals and selected historical differences appear without recalculating students’ old marks.', 'শিক্ষার্থীর পুরোনো নম্বর আবার হিসাব না করেই গ্রেডভিত্তিক মোট ও ঐতিহাসিক পার্থক্য দেখা যায়।'],
            rules: ['Withdrawn versions remain historical evidence, not a current result; this report cannot edit the published snapshot.', 'প্রত্যাহার করা সংস্করণ ইতিহাস হিসেবে থাকে, বর্তমান ফল নয়; এই রিপোর্টে প্রকাশিত স্ন্যাপশট সম্পাদনা হয় না।'],
            recovery: ['If no distribution is available, check finalized version choice and authorization; reload after a publication change.', 'রিপোর্ট না পেলে চূড়ান্ত সংস্করণ ও অনুমতি দেখুন; প্রকাশনা বদলালে রিফ্রেশ করুন।'],
            related: [['/AssessmentAdministration/Publications', 'Publication history', 'প্রকাশনার ইতিহাস']]
        },
        '/assessmentadministration/admitcards/index': {
            version: 1, reviewed: true,
            title: ['Exam Admit Cards', 'পরীক্ষার প্রবেশপত্র'],
            purpose: ['Prepare bounded printable admit-card lists for eligible enrolled examination candidates.', 'যোগ্য ভর্তি পরীক্ষার্থীদের জন্য সীমিত পরিসরে প্রিন্টযোগ্য প্রবেশপত্রের তালিকা তৈরি করুন।'],
            before: ['EXAM module and examination-administrator access; an authorized scheduled assessment, approved subjects and complete candidate schedule.', 'EXAM মডিউল ও পরীক্ষা প্রশাসকের অনুমতি; নির্ধারিত মূল্যায়ন, অনুমোদিত বিষয় ও সম্পূর্ণ পরীক্ষার সময়সূচি প্রয়োজন।'],
            steps: [
                ['Find assessment/batch, select the authorized scope and optional candidate search.', 'মূল্যায়ন/ব্যাচ খুঁজে অনুমোদিত পরিসর ও প্রয়োজনীয় পরীক্ষার্থী খোঁজ বাছুন।'],
                ['Select Load candidates; review eligibility and any missing schedule or subject information.', 'Load candidates চাপুন; যোগ্যতা এবং সময়সূচি/বিষয়ের ঘাটতি দেখুন।'],
                ['Page through candidates, export Page CSV or Print page / Save PDF for eligible results.', 'পৃষ্ঠাভিত্তিক তালিকা দেখুন; যোগ্য ফলাফলের Page CSV নিন বা Print page / Save PDF করুন।']
            ],
            example: ['Fictional example: select the published schedule for Grade 9 A final assessment and print eligible student admit cards from its authorized page.', 'কাল্পনিক উদাহরণ: নবম শ্রেণি A-এর চূড়ান্ত পরীক্ষার নির্ধারিত সময়সূচি বেছে অনুমোদিত পৃষ্ঠার যোগ্য শিক্ষার্থীর প্রবেশপত্র প্রিন্ট করুন।'],
            result: ['Eligible candidates appear in the page-bounded printable view; no enrollment, room booking or examination result is changed.', 'যোগ্য পরীক্ষার্থী সীমিত প্রিন্টযোগ্য তালিকায় দেখা যায়; ভর্তি, কক্ষ বরাদ্দ বা ফলাফল বদলায় না।'],
            rules: ['Only properly enrolled candidates with approved subjects and complete exam schedules are printable; verify privacy and distribution before sharing PDFs.', 'শুধু সঠিকভাবে ভর্তি, অনুমোদিত বিষয় ও সম্পূর্ণ সময়সূচিযুক্ত পরীক্ষার্থীর প্রবেশপত্র প্রিন্টযোগ্য; PDF শেয়ার করার আগে গোপনীয়তা যাচাই করুন।'],
            recovery: ['If candidates do not appear, verify assessment status, subject approvals and schedule; refresh filters rather than fabricating manual cards.', 'পরীক্ষার্থী না পেলে মূল্যায়নের অবস্থা, বিষয় অনুমোদন ও সময়সূচি দেখুন; হাতে কৃত্রিম প্রবেশপত্র তৈরি না করে ফিল্টার রিফ্রেশ করুন।'],
            related: [['/AssessmentAdministration/AssessmentSchedules', 'Assessment schedules', 'পরীক্ষার সময়সূচি'], ['/AssessmentAdministration/Setup', 'Assessment setup', 'মূল্যায়ন সেটআপ']]
        },
    };
    // Canonical academic workspaces share the existing secured academic APIs,
    // but each has its own focused route and user manual.
    const academicManuals = {
        '/academicmanagement/years/index': {
            version: 1, reviewed: true,
            title: ['Academic Years', 'শিক্ষাবর্ষ'],
            purpose: ['Create and maintain the institution\'s academic-year calendar and current-year selection.', 'প্রতিষ্ঠানের শিক্ষাবর্ষের সময়সীমা ও বর্তমান শিক্ষাবর্ষ নির্ধারণ ও পরিচালনা করুন।'],
            before: ['TenantAdmin access; confirm the institution and campus and know the actual academic dates.', 'TenantAdmin অনুমতি থাকতে হবে; প্রতিষ্ঠান, ক্যাম্পাস ও শিক্ষাবর্ষের প্রকৃত তারিখ যাচাই করুন।'],
            steps: [
                ['Enter year name, unique code, campus scope and start/end dates.', 'শিক্ষাবর্ষের নাম, স্বতন্ত্র কোড, ক্যাম্পাস এবং শুরু ও শেষের তারিখ লিখুন।'],
                ['Select cycle and Current year if applicable, then Save Year.', 'প্রযোজ্য চক্র ও Current year বেছে Save Year চাপুন।'],
                ['Review the year register; choose Edit to correct permitted fields or Archive only when safe.', 'শিক্ষাবর্ষের তালিকা দেখুন; অনুমোদিত তথ্য বদলাতে Edit অথবা নিরাপদ হলে Archive বাছুন।']
            ],
            example: ['Example: create Academic 2026 (code 2026), with the verified school dates, and mark it current.', 'উদাহরণ: যাচাইকৃত তারিখ দিয়ে Academic 2026 (কোড 2026) তৈরি করে বর্তমান শিক্ষাবর্ষ করুন।'],
            result: ['The year appears in the register and can be selected by terms and sections.', 'শিক্ষাবর্ষ তালিকায় আসে এবং টার্ম ও শাখার ফর্মে নির্বাচন করা যায়।'],
            rules: ['Existing linked academic records may prevent archival. Campus and date scope must agree with the institution\'s setup; this page does not enroll students.', 'সংযুক্ত একাডেমিক রেকর্ড থাকলে আর্কাইভ আটকাতে পারে। ক্যাম্পাস ও তারিখ প্রতিষ্ঠানের সেটআপের সঙ্গে মিলতে হবে; এখানে শিক্ষার্থী ভর্তি হয় না।'],
            recovery: ['If saving fails, review required dates/code and the displayed error; reload before retrying a changed record.', 'সংরক্ষণ ব্যর্থ হলে তারিখ, কোড ও দেখানো ত্রুটি যাচাই করুন; পরিবর্তিত রেকর্ডে আবার চেষ্টা করার আগে রিফ্রেশ করুন।'],
            related: [['/AcademicManagement/Terms', 'Terms / Semesters', 'টার্ম / সেমিস্টার'], ['/AcademicManagement/Sections', 'Sections', 'শাখা']]
        },
        '/academicmanagement/terms/index': {
            version: 1, reviewed: true,
            title: ['Terms / Semesters', 'টার্ম / সেমিস্টার'],
            purpose: ['Define term or semester dates within an existing academic year.', 'বিদ্যমান শিক্ষাবর্ষের ভেতরে টার্ম বা সেমিস্টারের সময় নির্ধারণ করুন।'],
            before: ['TenantAdmin access and at least one saved academic year.', 'TenantAdmin অনুমতি ও অন্তত একটি সংরক্ষিত শিক্ষাবর্ষ প্রয়োজন।'],
            steps: [
                ['Select the parent academic year.', 'সংশ্লিষ্ট শিক্ষাবর্ষ নির্বাচন করুন।'],
                ['Enter term name, optional code and date range; mark Current term if appropriate.', 'টার্মের নাম, ঐচ্ছিক কোড ও তারিখ দিন; প্রযোজ্য হলে Current term নির্বাচন করুন।'],
                ['Save Term and review the register; use Edit or Archive only after checking dependencies.', 'Save Term চাপুন ও তালিকা দেখুন; নির্ভরশীল রেকর্ড যাচাই করে Edit বা Archive করুন।']
            ],
            example: ['Example: assign Term 1 to Academic 2026 with its actual teaching dates.', 'উদাহরণ: Academic 2026-এর অধীনে প্রকৃত পাঠদানের তারিখ দিয়ে Term 1 তৈরি করুন।'],
            result: ['The term is available to related academic-section workflows.', 'টার্মটি সংশ্লিষ্ট একাডেমিক শাখার কাজে নির্বাচনযোগ্য হয়।'],
            rules: ['Terms belong to a specific year; linked records can block archival. Do not recreate a term to bypass an edit conflict.', 'প্রতিটি টার্ম নির্দিষ্ট শিক্ষাবর্ষের অধীনে; সংযুক্ত রেকর্ড আর্কাইভ আটকাতে পারে। সম্পাদনার সংঘাত এড়াতে একই টার্ম পুনরায় তৈরি করবেন না।'],
            recovery: ['Check parent year, date range and current version; reload after a conflict or failed update.', 'শিক্ষাবর্ষ, তারিখ ও বর্তমান সংস্করণ দেখুন; সংঘাত বা ব্যর্থ আপডেটের পরে রিফ্রেশ করুন।'],
            related: [['/AcademicManagement/Years', 'Academic Years', 'শিক্ষাবর্ষ']]
        },
        '/academicmanagement/departments/index': {
            version: 1, reviewed: true,
            title: ['Academic Departments', 'একাডেমিক বিভাগ'],
            purpose: ['Organize academic programmes into named departments.', 'একাডেমিক প্রোগ্রামগুলোকে বিভাগ অনুযায়ী সাজান।'],
            before: ['TenantAdmin access; decide a stable department code and name.', 'TenantAdmin অনুমতি থাকতে হবে; বিভাগের স্থায়ী কোড ও নাম ঠিক করুন।'],
            steps: [
                ['Enter department name, code and optional description; set Active as appropriate.', 'বিভাগের নাম, কোড ও ঐচ্ছিক বিবরণ লিখুন; প্রয়োজনমতো Active দিন।'],
                ['Select Create Department; use the name/code prefix search to find records.', 'Create Department চাপুন; নাম বা কোডের শুরু দিয়ে তালিকায় খুঁজুন।'],
                ['Review the paged register; edit available metadata or export the visible page.', 'পৃষ্ঠাভিত্তিক তালিকা দেখুন; অনুমোদিত তথ্য সম্পাদনা বা দৃশ্যমান পৃষ্ঠা রপ্তানি করুন।']
            ],
            example: ['Example: create Science (SCI) and assign programmes later from Programmes.', 'উদাহরণ: Science (SCI) বিভাগ তৈরি করে পরে Programmes থেকে প্রোগ্রাম যুক্ত করুন।'],
            result: ['The department becomes selectable for compatible programmes.', 'সামঞ্জস্যপূর্ণ প্রোগ্রামের জন্য বিভাগটি নির্বাচনযোগ্য হয়।'],
            rules: ['Department codes do not change; programmes that still depend on a department may prevent deactivation.', 'বিভাগের কোড অপরিবর্তনীয়; নির্ভরশীল সক্রিয় প্রোগ্রাম থাকলে বিভাগ নিষ্ক্রিয় করা যাবে না।'],
            recovery: ['Search by a shorter prefix, correct duplicate/invalid codes and refresh after a version conflict.', 'কম অক্ষরের শুরু দিয়ে খুঁজুন, ভুল বা পুনরাবৃত্ত কোড ঠিক করুন এবং সংস্করণ-সংঘাতে রিফ্রেশ করুন।'],
            related: [['/AcademicManagement/Classes', 'Programmes and Classes', 'প্রোগ্রাম ও শ্রেণি']]
        },
        '/academicmanagement/classes/index': {
            version: 1, reviewed: true,
            title: ['Programmes, Classes & Grades', 'প্রোগ্রাম, শ্রেণি ও গ্রেড'],
            purpose: ['Manage academic programmes, their class levels, streams and campus availability.', 'একাডেমিক প্রোগ্রাম, শ্রেণির স্তর, শাখা ও ক্যাম্পাসের প্রাপ্যতা পরিচালনা করুন।'],
            before: ['TenantAdmin access; prepare programme, department and permitted campus details.', 'TenantAdmin অনুমতি নিয়ে প্রোগ্রাম, বিভাগ ও অনুমোদিত ক্যাম্পাসের তথ্য প্রস্তুত রাখুন।'],
            steps: [
                ['Create a programme with name/code and allowed campuses, then review its register.', 'নাম, কোড ও অনুমোদিত ক্যাম্পাস দিয়ে প্রোগ্রাম তৈরি করে তালিকায় দেখুন।'],
                ['Use Manage campuses for allocation and Edit metadata for permitted programme changes.', 'ক্যাম্পাস বরাদ্দে Manage campuses এবং অনুমোদিত প্রোগ্রাম পরিবর্তনে Edit metadata ব্যবহার করুন।'],
                ['Add class/grade levels and tracks under the correct programme; inspect their registers.', 'সঠিক প্রোগ্রামের অধীনে শ্রেণি/গ্রেড ও ট্র্যাক যোগ করে তালিকা যাচাই করুন।']
            ],
            example: ['Example: create Secondary (SEC), then Class 9 and a Science track.', 'উদাহরণ: Secondary (SEC) প্রোগ্রাম, এরপর Class 9 এবং Science ট্র্যাক তৈরি করুন।'],
            result: ['The programme and its levels become available for sections and curriculum setup.', 'প্রোগ্রাম ও শ্রেণির স্তর শাখা ও পাঠ্যক্রম তৈরিতে ব্যবহারযোগ্য হয়।'],
            rules: ['Campus assignment and programme metadata are separate operations. Active dependencies may block deactivation and certain metadata changes.', 'ক্যাম্পাস বরাদ্দ ও প্রোগ্রামের তথ্য সম্পাদনা আলাদা কাজ। সক্রিয় নির্ভরশীল রেকর্ড থাকলে কিছু পরিবর্তন বা নিষ্ক্রিয়করণ আটকে যায়।'],
            recovery: ['Check existing programme codes, parent selection and campus scope; reload the current version after a conflict.', 'আগের প্রোগ্রাম কোড, মূল প্রোগ্রাম নির্বাচন ও ক্যাম্পাস যাচাই করুন; সংঘাতে বর্তমান সংস্করণ রিফ্রেশ করুন।'],
            related: [['/AcademicManagement/Departments', 'Departments', 'বিভাগ'], ['/AcademicManagement/Sections', 'Sections', 'শাখা']]
        },
        '/academicmanagement/sections/index': {
            version: 1, reviewed: true,
            title: ['Sections / Cohorts', 'শাখা / ব্যাচ'],
            purpose: ['Create academic batches tied to a campus, year, programme and grade.', 'ক্যাম্পাস, শিক্ষাবর্ষ, প্রোগ্রাম ও গ্রেডের সঙ্গে যুক্ত শাখা/ব্যাচ তৈরি করুন।'],
            before: ['TenantAdmin access and completed campus, year and programme/level setup.', 'TenantAdmin অনুমতি এবং ক্যাম্পাস, শিক্ষাবর্ষ, প্রোগ্রাম ও শ্রেণির সেটআপ সম্পন্ন থাকতে হবে।'],
            steps: [
                ['Select campus, year, programme, class and optional term.', 'ক্যাম্পাস, শিক্ষাবর্ষ, প্রোগ্রাম, শ্রেণি এবং প্রযোজ্য টার্ম বাছুন।'],
                ['Enter section name/code, capacity and dates; select Add Section.', 'শাখার নাম, কোড, আসনসংখ্যা ও তারিখ লিখে Add Section চাপুন।'],
                ['Search the section register and edit only the permitted name, capacity increase or display order.', 'শাখার তালিকায় খুঁজুন; শুধু অনুমোদিত নাম, আসন বৃদ্ধি বা প্রদর্শনক্রম বদলান।']
            ],
            example: ['Example: add Grade 9 A at Campus 1 for Academic 2026 with capacity 40.', 'উদাহরণ: Campus 1-এ Academic 2026-এর Grade 9 A শাখা ৪০ আসনসহ তৈরি করুন।'],
            result: ['The section becomes an available academic batch for later student/subject workflows.', 'শাখাটি পরবর্তী শিক্ষার্থী ও বিষয় ব্যবস্থাপনায় একাডেমিক ব্যাচ হিসেবে পাওয়া যায়।'],
            rules: ['Campus, year, programme, class, dates and code cannot be edited here; reducing capacity or closing a used section needs separate enrollment-safe reconciliation.', 'এখানে ক্যাম্পাস, শিক্ষাবর্ষ, প্রোগ্রাম, শ্রেণি, তারিখ ও কোড বদলানো যায় না; আসন কমানো বা ব্যবহৃত শাখা বন্ধে আলাদা ভর্তি-নিরাপদ সমন্বয় প্রয়োজন।'],
            recovery: ['Check required parent selections and dates; review the register before retrying a save after network uncertainty.', 'প্রয়োজনীয় মূল নির্বাচন ও তারিখ দেখুন; নেটওয়ার্কে ফল অনিশ্চিত হলে আবার সংরক্ষণের আগে তালিকা যাচাই করুন।'],
            related: [['/AcademicManagement/Years', 'Academic Years', 'শিক্ষাবর্ষ'], ['/AcademicManagement/Offerings', 'Subject Offerings', 'বিষয় অফার']]
        },
        '/academicmanagement/rooms/index': {
            version: 1, reviewed: true,
            title: ['Academic Rooms', 'একাডেমিক কক্ষ'],
            purpose: ['Maintain campus-specific classrooms and timetable room capacity.', 'ক্যাম্পাসভিত্তিক শ্রেণিকক্ষ ও রুটিনে ব্যবহৃত কক্ষের ধারণক্ষমতা পরিচালনা করুন।'],
            before: ['TenantAdmin access and a configured campus.', 'TenantAdmin অনুমতি ও সেটআপ করা ক্যাম্পাস প্রয়োজন।'],
            steps: [
                ['Select campus and enter room name, stable code, capacity and optional type.', 'ক্যাম্পাস নির্বাচন করে কক্ষের নাম, স্থায়ী কোড, ধারণক্ষমতা ও ঐচ্ছিক ধরন দিন।'],
                ['Select Add and search by room name/code or campus to inspect the register.', 'Add চাপুন; নাম, কোড বা ক্যাম্পাস দিয়ে তালিকায় খুঁজুন।'],
                ['Use Edit to update supported metadata and export the visible room rows if needed.', 'Edit দিয়ে অনুমোদিত তথ্য বদলান; প্রয়োজনে দৃশ্যমান কক্ষের তালিকা রপ্তানি করুন।']
            ],
            example: ['Example: add Room 204, code C1-204, capacity 40 to Campus 1.', 'উদাহরণ: Campus 1-এ Room 204, কোড C1-204, ধারণক্ষমতা ৪০ তৈরি করুন।'],
            result: ['The room is listed for timetable-related use.', 'রুটিন-সংশ্লিষ্ট কাজে ব্যবহারের জন্য কক্ষটি তালিকায় আসে।'],
            rules: ['Room campus and code are immutable; capacity decreases and deactivation are not available in this editor because timetable reconciliation is required.', 'কক্ষের ক্যাম্পাস ও কোড অপরিবর্তনীয়; রুটিন সমন্বয়ের প্রয়োজন হওয়ায় এখানে ধারণক্ষমতা কমানো বা নিষ্ক্রিয় করা যায় না।'],
            recovery: ['Check campus, code and capacity; on a rejected update reload the current room before editing again.', 'ক্যাম্পাস, কোড ও ধারণক্ষমতা দেখুন; আপডেট বাতিল হলে আবার সম্পাদনার আগে বর্তমান কক্ষ রিফ্রেশ করুন।'],
            related: [['/AcademicRoutines/Index', 'Class Routines', 'ক্লাস রুটিন']]
        },
        '/academicmanagement/subjects/index': {
            version: 1, reviewed: true,
            title: ['Subjects', 'বিষয়'],
            purpose: ['Maintain the institution-wide subject catalogue and default credits.', 'প্রতিষ্ঠানের বিষয় তালিকা ও ডিফল্ট ক্রেডিট পরিচালনা করুন।'],
            before: ['TenantAdmin access and a unique subject code.', 'TenantAdmin অনুমতি ও স্বতন্ত্র বিষয় কোড প্রয়োজন।'],
            steps: [
                ['Enter subject name, code and default credits; select Add Subject.', 'বিষয়ের নাম, কোড ও ডিফল্ট ক্রেডিট লিখে Add Subject চাপুন।'],
                ['Use prefix search and paging to find active or inactive subjects.', 'নাম বা কোডের শুরু দিয়ে খুঁজে পৃষ্ঠাভিত্তিক সক্রিয়/নিষ্ক্রিয় বিষয় দেখুন।'],
                ['Select Edit for permitted metadata changes and review the saved status.', 'অনুমোদিত তথ্য বদলাতে Edit বেছে সংরক্ষিত অবস্থা দেখুন।']
            ],
            example: ['Example: add Mathematics (MATH) with the verified default credits.', 'উদাহরণ: যাচাইকৃত ডিফল্ট ক্রেডিট দিয়ে Mathematics (MATH) বিষয় যোগ করুন।'],
            result: ['The subject is available for curriculum mapping.', 'পাঠ্যক্রমে বিষয় ম্যাপ করার জন্য বিষয়টি পাওয়া যায়।'],
            rules: ['Codes remain fixed; linked curriculum history may block deactivation. Changing defaults does not rewrite past curriculum credits or marks.', 'কোড অপরিবর্তনীয়; আগের পাঠ্যক্রমে সংযোগ থাকলে নিষ্ক্রিয় করা যায় না। ডিফল্ট বদলালে আগের পাঠ্যক্রমের ক্রেডিট বা নম্বর বদলায় না।'],
            recovery: ['Check code uniqueness and numeric credits; reload and inspect the current row after an edit conflict.', 'কোডের স্বতন্ত্রতা ও সংখ্যাগত ক্রেডিট যাচাই করুন; সম্পাদনা সংঘাতে বর্তমান রেকর্ড রিফ্রেশ করুন।'],
            related: [['/AcademicManagement/Curricula', 'Curriculum', 'পাঠ্যক্রম']]
        },
        '/academicmanagement/curricula/index': {
            version: 1, reviewed: true,
            title: ['Curriculum Versions and Subject Mapping', 'পাঠ্যক্রমের সংস্করণ ও বিষয় ম্যাপিং'],
            purpose: ['Create versioned curricula and map approved subjects to class levels.', 'সংস্করণভিত্তিক পাঠ্যক্রম তৈরি করে শ্রেণির সঙ্গে অনুমোদিত বিষয় যুক্ত করুন।'],
            before: ['TenantAdmin access; the programme, class levels and subjects must already exist.', 'TenantAdmin অনুমতি থাকতে হবে; প্রোগ্রাম, শ্রেণি ও বিষয় আগে থেকে তৈরি থাকতে হবে।'],
            steps: [
                ['Select programme, name, code, version and effective date to create a curriculum.', 'প্রোগ্রাম, নাম, কোড, সংস্করণ ও কার্যকর তারিখ বেছে পাঠ্যক্রম তৈরি করুন।'],
                ['Select an existing eligible curriculum, class and subject; set marks/pass marks and Add Mapping.', 'যোগ্য পাঠ্যক্রম, শ্রেণি ও বিষয় বেছে মোট/পাস নম্বর দিয়ে Add Mapping চাপুন।'],
                ['Search versions and inspect the register; open the prerequisites workspace if needed.', 'সংস্করণ খুঁজে তালিকা যাচাই করুন; প্রয়োজন হলে বিষয়ের পূর্বশর্ত পৃষ্ঠা খুলুন।']
            ],
            example: ['Example: create Secondary 2026 v1 and map Mathematics to Grade 9 with verified marks.', 'উদাহরণ: Secondary 2026 v1 তৈরি করে যাচাইকৃত নম্বরসহ Grade 9-এ Mathematics যুক্ত করুন।'],
            result: ['The curriculum and eligible subject mappings are available for subsequent batch offerings.', 'পাঠ্যক্রম ও উপযুক্ত বিষয় ম্যাপিং পরবর্তী ব্যাচের বিষয় অফারে ব্যবহারযোগ্য হয়।'],
            rules: ['History is versioned; do not overwrite existing assigned curricula. The server checks mapping eligibility and effective dates.', 'ইতিহাস সংস্করণভিত্তিক; আগের বরাদ্দকৃত পাঠ্যক্রম বদলে লিখবেন না। সার্ভার ম্যাপিংয়ের যোগ্যতা ও কার্যকর তারিখ যাচাই করে।'],
            recovery: ['If mapping is disabled, choose an eligible curriculum and level; check existing versions, dates and the displayed error.', 'ম্যাপিং বন্ধ থাকলে যোগ্য পাঠ্যক্রম ও শ্রেণি বাছুন; সংস্করণ, তারিখ ও দেখানো ত্রুটি যাচাই করুন।'],
            related: [['/AcademicManagement/Subjects', 'Subjects', 'বিষয়'], ['/AcademicManagement/Offerings', 'Subject Offerings', 'বিষয় অফার']]
        },
        '/academicmanagement/prerequisites/index': {
            version: 1, reviewed: true,
            title: ['Subject Prerequisites', 'বিষয়ের পূর্বশর্ত'],
            purpose: ['Define required prior subjects for an eligible curriculum version.', 'উপযুক্ত পাঠ্যক্রমের জন্য আগে পাস/সম্পন্ন করতে হবে এমন বিষয় নির্ধারণ করুন।'],
            before: ['TenantAdmin access; select a curriculum with the relevant subjects already mapped.', 'TenantAdmin অনুমতি থাকতে হবে; সংশ্লিষ্ট বিষয়গুলো আগে থেকে ম্যাপ করা পাঠ্যক্রম নির্বাচন করুন।'],
            steps: [
                ['Search and select a curriculum version; load its prerequisite catalogue.', 'পাঠ্যক্রমের সংস্করণ খুঁজে নির্বাচন করে পূর্বশর্তের তালিকা লোড করুন।'],
                ['Choose the subject and its required prior subject; set Mandatory if needed and select Add link.', 'বিষয় ও তার পূর্বশর্তের বিষয় বেছে প্রয়োজনমতো Mandatory দিয়ে Add link চাপুন।'],
                ['Review the paged prerequisite register; search, export the visible page or print if necessary.', 'পৃষ্ঠাভিত্তিক পূর্বশর্তের তালিকা দেখুন; খুঁজুন, দৃশ্যমান পৃষ্ঠা রপ্তানি বা প্রিন্ট করুন।']
            ],
            example: ['Example: for an editable Secondary curriculum, require Mathematics I before Mathematics II.', 'উদাহরণ: সম্পাদনাযোগ্য Secondary পাঠ্যক্রমে Mathematics II-এর আগে Mathematics I বাধ্যতামূলক করুন।'],
            result: ['An eligible prerequisite link is recorded for that curriculum version.', 'সেই পাঠ্যক্রমের সংস্করণে যোগ্য পূর্বশর্ত সংযোগ সংরক্ষিত হয়।'],
            rules: ['Both subjects must belong to the selected curriculum. Circular dependencies are rejected and curricula already in use are immutable.', 'দুই বিষয়ই নির্বাচিত পাঠ্যক্রমে থাকতে হবে। চক্রাকার নির্ভরতা বাতিল হয়; ব্যবহৃত পাঠ্যক্রম অপরিবর্তনীয়।'],
            recovery: ['If linking is denied, check curriculum eligibility, mapped subjects and dependency cycles; refresh before retrying.', 'সংযোগ বাতিল হলে পাঠ্যক্রমের যোগ্যতা, ম্যাপ করা বিষয় ও চক্রাকার নির্ভরতা দেখুন; পুনরায় চেষ্টা করার আগে রিফ্রেশ করুন।'],
            related: [['/AcademicManagement/Curricula', 'Curriculum', 'পাঠ্যক্রম'], ['/AcademicManagement/Subjects', 'Subjects', 'বিষয়']]
        },
        '/academicmanagement/offerings/index': {
            version: 1, reviewed: true,
            title: ['Batch Subject Offerings', 'ব্যাচের বিষয় অফার'],
            purpose: ['Publish curriculum subjects to academic batches before students register.', 'শিক্ষার্থী নিবন্ধনের আগে একাডেমিক ব্যাচে পাঠ্যক্রমের বিষয় অফার করুন।'],
            before: ['TenantAdmin access, an academic batch and a compatible published curriculum subject.', 'TenantAdmin অনুমতি, একাডেমিক ব্যাচ ও সামঞ্জস্যপূর্ণ প্রকাশিত পাঠ্যক্রমের বিষয় প্রয়োজন।'],
            steps: [
                ['Select an academic batch and search existing offerings to avoid duplicates.', 'একাডেমিক ব্যাচ বেছে আগে থেকে থাকা অফার খুঁজে পুনরাবৃত্তি এড়ান।'],
                ['Select curriculum subject, offering code, capacity and delivery mode; set Active as needed.', 'পাঠ্যক্রমের বিষয়, অফার কোড, আসনসংখ্যা ও পাঠদান পদ্ধতি বেছে প্রয়োজনমতো Active দিন।'],
                ['Select Create Offering, then check pending, allocated, reserved and available seats in the register.', 'Create Offering চাপুন; পরে তালিকায় অপেক্ষমাণ, বরাদ্দ, সংরক্ষিত ও খালি আসন দেখুন।']
            ],
            example: ['Example: offer Grade 9 Mathematics at Campus 1 with capacity 40 and On campus delivery.', 'উদাহরণ: Campus 1-এর Grade 9 Mathematics বিষয়ে ৪০ আসন ও On campus পদ্ধতিতে অফার তৈরি করুন।'],
            result: ['A compatible batch offering becomes visible with seat utilization counters.', 'সামঞ্জস্যপূর্ণ ব্যাচ অফার ও আসন ব্যবহারের হিসাব তালিকায় আসে।'],
            rules: ['The server checks campus, curriculum version and academic dates. Pending reservations and approved/completed registrations use seats; rejected/dropped registrations release them.', 'সার্ভার ক্যাম্পাস, পাঠ্যক্রমের সংস্করণ ও একাডেমিক তারিখ যাচাই করে। অপেক্ষমাণ সংরক্ষণ এবং অনুমোদিত/সম্পন্ন নিবন্ধনে আসন ব্যবহৃত হয়; বাতিল/বাদ দেওয়া নিবন্ধনে আসন খালি হয়।'],
            recovery: ['If no eligible subjects appear, verify batch and curriculum prerequisites; search existing offerings before retrying after a network error.', 'যোগ্য বিষয় না এলে ব্যাচ ও পাঠ্যক্রমের পূর্বশর্ত দেখুন; নেটওয়ার্কে ত্রুটির পরে আবার চেষ্টা করার আগে বিদ্যমান অফার খুঁজুন।'],
            related: [['/AcademicManagement/Sections', 'Sections', 'শাখা'], ['/AcademicManagement/Curricula', 'Curriculum', 'পাঠ্যক্রম']]
        }
    };
    // Source-reviewed guides for focused admission, attendance and institution
    // workspaces. Keep them route-specific: a route without verified guidance
    // must continue to show the missing-guide notice, never a guessed manual.
    const taskWorkspaceManuals = {
        '/admissions/forms/index': {
            version: 1, reviewed: true,
            title: ['Public Admission Form Setup', 'পাবলিক ভর্তি ফরম সেটআপ'],
            purpose: ['Configure and publish an intake form with permitted academic scope, questions, document requirements and application dates.', 'অনুমোদিত শিক্ষাবর্ষ, প্রশ্ন, কাগজপত্র ও সময়সীমাসহ ভর্তি ফরম তৈরি ও প্রকাশ করুন।'],
            before: ['TenantAdmin and ADMISSION module access; academic year, campus, class/level and intake window must be available.', 'TenantAdmin ও ADMISSION মডিউল অনুমতি; শিক্ষাবর্ষ, ক্যাম্পাস, শ্রেণি/স্তর ও আবেদন সময়সীমা প্রস্তুত থাকতে হবে।'],
            steps: [
                ['Select New Form and enter code, title, academic year, campus, class/level and opening/closing times.', 'New Form চেপে কোড, শিরোনাম, শিক্ষাবর্ষ, ক্যাম্পাস, শ্রেণি/স্তর ও আবেদন শুরুর/শেষের সময় দিন।'],
                ['Set the application fee and currency, then add any custom fields and required documents.', 'আবেদন ফি ও মুদ্রা দিন, প্রয়োজনে অতিরিক্ত প্রশ্ন ও আবশ্যিক কাগজপত্র যোগ করুন।'],
                ['Save the draft, review it in the Public Admission Forms register, then Publish when the details are correct.', 'খসড়া সংরক্ষণ করে পাবলিক ফরম তালিকায় যাচাই করুন; সঠিক হলে Publish করুন।'],
                ['Reopen the form register to confirm the published status and application window.', 'ফরম তালিকা আবার খুলে প্রকাশের অবস্থা ও সময়সীমা যাচাই করুন।']
            ],
            example: ['Fictional example: prepare a 2027 Grade 6 intake for the approved main campus, with a guardian phone field and required certificate, then publish it.', 'কাল্পনিক উদাহরণ: অনুমোদিত মূল ক্যাম্পাসে ২০২৭ সালের ষষ্ঠ শ্রেণির ফরমে অভিভাবকের ফোন ও প্রয়োজনীয় সনদ যোগ করে প্রকাশ করুন।'],
            result: ['A published form becomes available within its configured window; a saved draft alone does not accept new applications.', 'নির্ধারিত সময়ে প্রকাশিত ফরমে আবেদন নেওয়া যায়; শুধু খসড়া দিয়ে আবেদন জমা হয় না।'],
            rules: ['Do not collect unnecessary private documents. Changing intake scope after submissions can affect applicants; the server remains authoritative for publication and validation.', 'অপ্রয়োজনীয় ব্যক্তিগত নথি সংগ্রহ করবেন না। আবেদন জমার পর ফরমের পরিসর বদলালে আবেদনকারী প্রভাবিত হতে পারেন; প্রকাশ ও যাচাই সার্ভার নিয়ন্ত্রণ করে।'],
            recovery: ['If Publish or Save fails, check dates, required academic selections and permissions. Reload existing forms before retrying to avoid a duplicate draft.', 'Save বা Publish ব্যর্থ হলে সময়, শিক্ষাবর্ষ নির্বাচন ও অনুমতি যাচাই করুন। দ্বৈত খসড়া এড়াতে আবার দেওয়ার আগে তালিকা রিফ্রেশ করুন।'],
            related: [['/Admissions/NewApplication', 'New application', 'নতুন আবেদন'], ['/Admissions/Applications', 'Application review', 'আবেদন যাচাই']]
        },
        '/admissions/newapplication/index': {
            version: 1, reviewed: true,
            title: ['New Admission Application', 'নতুন ভর্তি আবেদন'],
            purpose: ['Submit an applicant to a published intake with accurate contact, guardian and custom-form answers.', 'প্রকাশিত ভর্তি ফরমে সঠিক যোগাযোগ, অভিভাবক ও অতিরিক্ত তথ্যসহ নতুন আবেদন জমা দিন।'],
            before: ['TenantAdmin or AdmissionOfficer with ADMISSION access; an open published intake and required guardian details for a minor.', 'ADMISSION অনুমতিসহ TenantAdmin বা AdmissionOfficer; খোলা প্রকাশিত ফরম ও অপ্রাপ্তবয়স্কের প্রয়োজনীয় অভিভাবক তথ্য থাকতে হবে।'],
            steps: [
                ['Choose Published intake; confirm its permitted year, term, campus and class/level.', 'Published intake বেছে শিক্ষাবর্ষ, টার্ম, ক্যাম্পাস ও শ্রেণি/স্তর যাচাই করুন।'],
                ['Enter applicant name, date of birth, gender, phone and optional email/address.', 'আবেদনকারীর নাম, জন্মতারিখ, লিঙ্গ, ফোন ও প্রয়োজনমতো ইমেইল/ঠিকানা দিন।'],
                ['Complete the guardian fields for a minor and any additional questions required by the intake.', 'অপ্রাপ্তবয়স্কের অভিভাবক তথ্য এবং ফরমে চাওয়া অতিরিক্ত উত্তর দিন।'],
                ['Submit application once and locate the new record in Application Review.', 'Submit application একবার চাপুন; Application Review-তে নতুন রেকর্ড খুঁজুন।']
            ],
            example: ['Fictional example: enter applicant Rina under the 2027 Grade 6 published intake, add her guardian phone and submit.', 'কাল্পনিক উদাহরণ: ২০২৭ সালের ষষ্ঠ শ্রেণির প্রকাশিত ফরমে রিনার তথ্য ও অভিভাবকের ফোন দিয়ে আবেদন জমা দিন।'],
            result: ['A successful submission produces an applicant record for review; it does not automatically enroll a student.', 'সফল জমায় যাচাইযোগ্য আবেদন তৈরি হয়; শিক্ষার্থী স্বয়ংক্রিয়ভাবে ভর্তি হয় না।'],
            rules: ['The chosen intake controls permitted academic fields. A request identifier protects against duplicate retries; later review and verified offer acceptance are separate actions.', 'নির্বাচিত ফরম শিক্ষাবর্ষের তথ্য নিয়ন্ত্রণ করে। request identifier একই আবেদন পুনরায় তৈরি হওয়া আটকায়; যাচাই ও অফার গ্রহণ আলাদা কাজ।'],
            recovery: ['If the intake is unavailable, confirm publishing dates and campus. Fix required guardian or custom answers before resubmitting; check the application list after a timeout.', 'ফরম না পেলে প্রকাশের সময় ও ক্যাম্পাস দেখুন। অভিভাবক বা অতিরিক্ত তথ্যের ত্রুটি ঠিক করুন; সময় শেষ হলে আবার দেওয়ার আগে তালিকা দেখুন।'],
            related: [['/Admissions/Applications', 'Application review', 'আবেদন যাচাই'], ['/Admissions/Forms', 'Admission forms', 'ভর্তি ফরম']]
        },
        '/admissions/applications/index': {
            version: 1, reviewed: true,
            title: ['Application Review & Enrollment', 'আবেদন যাচাই ও ভর্তি'],
            purpose: ['Find submitted applications, inspect documents, record permitted decisions and complete eligible enrollment.', 'জমা আবেদন খোঁজা, কাগজপত্র যাচাই, অনুমোদিত সিদ্ধান্ত ও যোগ্য ভর্তি সম্পন্ন করুন।'],
            before: ['TenantAdmin or AdmissionOfficer and ADMISSION entitlement; existing application and necessary review documents.', 'TenantAdmin বা AdmissionOfficer ও ADMISSION মডিউল; জমা আবেদন এবং প্রয়োজনীয় যাচাইয়ের নথি থাকতে হবে।'],
            steps: [
                ['Search the Application list by name or reference and filter by the correct status.', 'Application list-এ নাম বা রেফারেন্স দিয়ে খুঁজুন এবং সঠিক অবস্থা বাছুন।'],
                ['Choose Review, inspect the applicant details and document evidence, then select an allowed new status.', 'Review খুলে আবেদন ও নথি যাচাই করে অনুমোদিত নতুন অবস্থা নির্বাচন করুন।'],
                ['Enter a decision note when required, Save decision and confirm the refreshed status.', 'প্রয়োজনীয় সিদ্ধান্তের মন্তব্য দিয়ে Save decision করুন; নতুন অবস্থা যাচাই করুন।'],
                ['After the separate verified offer/acceptance process, use Admit applicant and Complete enrollment only for an eligible batch and roll.', 'আলাদা যাচাইকৃত অফার/গ্রহণের পর যোগ্য ব্যাচ ও রোল দিয়ে Admit applicant এবং Complete enrollment করুন।']
            ],
            example: ['Fictional example: search applicant Rina, review required identity documents, record a permitted decision and continue to enrollment only after verified acceptance.', 'কাল্পনিক উদাহরণ: রিনাকে খুঁজে পরিচয়পত্র যাচাই, অনুমোদিত সিদ্ধান্ত দিন; যাচাইকৃত গ্রহণের পরেই ভর্তি সম্পন্ন করুন।'],
            result: ['A saved review updates the applicant status. Successful separate enrollment returns a student record/code for the Student Directory.', 'যাচাই সংরক্ষণে আবেদন অবস্থা বদলায়। আলাদা ভর্তি সফল হলে Student Directory-তে শিক্ষার্থী কোড/রেকর্ড পাওয়া যায়।'],
            rules: ['Review does not equal admission. Document permission and verified acceptance are independently enforced; rowversion conflicts must be resolved before another decision.', 'যাচাই মানেই ভর্তি নয়। নথির অনুমতি ও যাচাইকৃত গ্রহণ আলাদাভাবে নিয়ন্ত্রিত; নতুন সিদ্ধান্তের আগে rowversion সংঘাত সমাধান করুন।'],
            recovery: ['On a stale review, reload the application and confirm current status. After uncertain enrollment failure, search Student Directory before retrying.', 'পুরোনো যাচাই সংঘাতে আবেদন রিলোড করে বর্তমান অবস্থা দেখুন। ভর্তি অনিশ্চিত হলে পুনরায় দেওয়ার আগে Student Directory দেখুন।'],
            related: [['/Admissions/Offers', 'Admission offers', 'ভর্তির অফার'], ['/Students/Index', 'Student directory', 'শিক্ষার্থী তালিকা']]
        },
        '/admissions/assessments/index': {
            version: 1, reviewed: true,
            title: ['Admission Assessment & Merit', 'ভর্তি পরীক্ষা ও মেধাতালিকা'],
            purpose: ['Create an intake assessment, record applicant marks, preview the merit list and publish it when verified.', 'ভর্তি পরীক্ষা তৈরি, আবেদনকারীর নম্বর এন্ট্রি, মেধাতালিকা যাচাই ও প্রকাশ করুন।'],
            before: ['TenantAdmin or AdmissionOfficer with ADMISSION access; an authorized intake and eligible applicants.', 'ADMISSION অনুমতিসহ TenantAdmin বা AdmissionOfficer; অনুমোদিত ফরম ও যোগ্য আবেদনকারী থাকতে হবে।'],
            steps: [
                ['Choose Admission intake and enter assessment name, date, total/pass marks and duration; select Create assessment.', 'Admission intake বেছে পরীক্ষার নাম, তারিখ, মোট/পাস নম্বর ও সময় দিয়ে Create assessment চাপুন।'],
                ['Select the assessment, search applicants and enter marks on the visible roster page.', 'পরীক্ষা বেছে আবেদনকারী খুঁজুন এবং দৃশ্যমান তালিকায় নম্বর দিন।'],
                ['Save page marks before moving between roster pages, then select View merit to inspect the calculated order.', 'পৃষ্ঠা বদলানোর আগে Save page marks করুন; View merit দিয়ে হিসাব করা মেধাক্রম দেখুন।'],
                ['Publish merit only after verifying the eligible candidate marks and preview.', 'যোগ্য প্রার্থীদের নম্বর ও প্রিভিউ যাচাই করার পরেই Publish merit করুন।']
            ],
            example: ['Fictional example: create a Grade 6 admission assessment with total 100 and pass 40, save all eligible marks and inspect merit before publication.', 'কাল্পনিক উদাহরণ: ষষ্ঠ শ্রেণির ভর্তি পরীক্ষায় মোট ১০০ ও পাস ৪০ দিয়ে পরীক্ষা তৈরি, নম্বর সংরক্ষণ ও প্রকাশের আগে মেধা যাচাই করুন।'],
            result: ['Assessment marks and authorized merit publication become available for the intake; an applicant is not enrolled by publishing merit.', 'ফরমের জন্য পরীক্ষার নম্বর ও অনুমোদিত মেধা প্রকাশ হয়; মেধা প্রকাশে শিক্ষার্থী ভর্তি হয় না।'],
            rules: ['Marks entry and merit publication are distinct actions. Do not publish until all eligible page marks and calculated merit have been reviewed.', 'নম্বর এন্ট্রি ও মেধা প্রকাশ আলাদা কাজ। সব যোগ্য পৃষ্ঠার নম্বর ও মেধা যাচাই ছাড়া প্রকাশ করবেন না।'],
            recovery: ['If a candidate is missing, confirm intake and status. On a save timeout reload the roster before writing again; never assume a failed response meant marks were saved.', 'প্রার্থী না পেলে ফরম ও অবস্থা দেখুন। Save-এর ফল অনিশ্চিত হলে আবার লেখার আগে রোস্টার রিলোড করুন।'],
            related: [['/Admissions/Applications', 'Application review', 'আবেদন যাচাই'], ['/Admissions/Offers', 'Admission offers', 'ভর্তির অফার']]
        },
        '/admissions/assessmentsetup/index': {
            version: 1, reviewed: true,
            title: ['Admission Assessment Setup', 'ভর্তি পরীক্ষা সেটআপ'],
            purpose: ['Configure a dated admission assessment for a permitted published intake.', 'অনুমোদিত প্রকাশিত ভর্তি ফরমের জন্য তারিখসহ পরীক্ষা নির্ধারণ করুন।'],
            before: ['TenantAdmin or AdmissionOfficer with ADMISSION access; the admission intake and its academic scope must exist.', 'ADMISSION অনুমতিসহ TenantAdmin বা AdmissionOfficer; ভর্তি ফরম ও শিক্ষাবর্ষের পরিসর আগে থাকতে হবে।'],
            steps: [
                ['Select the Admission intake for which the assessment will be held.', 'যে Admission intake-এর পরীক্ষা হবে সেটি বেছে নিন।'],
                ['Enter assessment name, date, total marks, pass marks and duration; add venue if relevant.', 'পরীক্ষার নাম, তারিখ, মোট নম্বর, পাস নম্বর ও সময় দিন; প্রয়োজনে কেন্দ্রের নাম দিন।'],
                ['Choose Create assessment, then open Marks & Merit to select the created assessment.', 'Create assessment করে Marks & Merit পেজে তৈরি পরীক্ষা নির্বাচন করুন।']
            ],
            example: ['Fictional example: create a 2027 Grade 6 entry test with 100 total marks, 40 pass marks and a 60-minute duration.', 'কাল্পনিক উদাহরণ: ২০২৭ সালের ষষ্ঠ শ্রেণির ভর্তি পরীক্ষায় মোট ১০০, পাস ৪০ ও সময় ৬০ মিনিট দিন।'],
            result: ['A new eligible assessment is ready for applicant marks; this setup does not publish merit.', 'যোগ্য পরীক্ষা নম্বর এন্ট্রির জন্য প্রস্তুত হয়; এখানে মেধাতালিকা প্রকাশ হয় না।'],
            rules: ['Pass marks must be compatible with total marks. Save does not enroll an applicant or publish results.', 'পাস নম্বর মোট নম্বরের সঙ্গে সামঞ্জস্যপূর্ণ হতে হবে। Save-এ আবেদনকারী ভর্তি বা ফলাফল প্রকাশ হয় না।'],
            recovery: ['If the intake is missing, check its scope and permissions. Resolve date/mark validation and inspect existing assessments before retrying.', 'ফরম না পেলে পরিসর ও অনুমতি দেখুন। তারিখ/নম্বরের ত্রুটি ঠিক করে পুনরায় দেওয়ার আগে বিদ্যমান পরীক্ষা যাচাই করুন।'],
            related: [['/Admissions/Merit', 'Marks and merit', 'নম্বর ও মেধাতালিকা'], ['/Admissions/Forms', 'Admission forms', 'ভর্তি ফরম']]
        },
        '/admissions/merit/index': {
            version: 1, reviewed: true,
            title: ['Admission Marks & Merit', 'ভর্তির নম্বর ও মেধাতালিকা'],
            purpose: ['Record assessment marks for applicants, preview the computed merit list and publish it after verification.', 'আবেদনকারীর পরীক্ষার নম্বর দিয়ে হিসাব করা মেধাতালিকা দেখে যাচাই শেষে প্রকাশ করুন।'],
            before: ['TenantAdmin or AdmissionOfficer with ADMISSION access; a configured admission assessment and eligible applicants.', 'ADMISSION অনুমতিসহ TenantAdmin বা AdmissionOfficer; আগে তৈরি পরীক্ষা ও যোগ্য আবেদনকারী থাকতে হবে।'],
            steps: [
                ['Choose Assessment and optionally Search applicant to locate eligible candidates.', 'Assessment বেছে প্রয়োজনে Search applicant দিয়ে যোগ্য প্রার্থী খুঁজুন।'],
                ['Enter applicant marks and Save page marks before moving between roster pages.', 'আবেদনকারীর নম্বর দিয়ে পৃষ্ঠা বদলানোর আগে Save page marks করুন।'],
                ['Select View merit and verify ranking, grades and any missing or invalid marks.', 'View merit দিয়ে মেধাক্রম, গ্রেড ও অসম্পূর্ণ বা ভুল নম্বর যাচাই করুন।'],
                ['Only after reviewing the draft, select Publish merit and verify the displayed result.', 'খসড়া যাচাই শেষে Publish merit করে প্রকাশিত ফল দেখুন।']
            ],
            example: ['Fictional example: enter 78 and 64 for two eligible Grade 6 candidates, save their pages and verify the draft merit order before publishing.', 'কাল্পনিক উদাহরণ: ষষ্ঠ শ্রেণির দুই যোগ্য প্রার্থীকে ৭৮ ও ৬৪ নম্বর দিয়ে পৃষ্ঠা সংরক্ষণ করে মেধাক্রম যাচাই করুন।'],
            result: ['Saved marks and authorized merit appear for the selected assessment; no automatic student enrollment is performed.', 'নির্বাচিত পরীক্ষার নম্বর ও অনুমোদিত মেধা দেখা যায়; স্বয়ংক্রিয় শিক্ষার্থী ভর্তি হয় না।'],
            rules: ['Publishing merit is distinct from verified offer acceptance or enrollment. Check all applicant pages and the preview before publishing.', 'মেধা প্রকাশ অফার গ্রহণ বা ভর্তি নয়। প্রকাশের আগে সব প্রার্থীর পৃষ্ঠা ও প্রিভিউ যাচাই করুন।'],
            recovery: ['If a candidate or mark is missing, check assessment and filters, then reload the roster after any uncertain save before retrying.', 'প্রার্থী বা নম্বর না পেলে পরীক্ষা/ফিল্টার দেখুন; Save অনিশ্চিত হলে পুনরায় দেওয়ার আগে রোস্টার রিলোড করুন।'],
            related: [['/Admissions/AssessmentSetup', 'Assessment setup', 'পরীক্ষা সেটআপ'], ['/Admissions/Offers', 'Admission offers', 'ভর্তির অফার']]
        },
        '/admissions/offers/index': {
            version: 1, reviewed: true,
            title: ['Offer, Verified Acceptance & Enrollment', 'অফার, যাচাইকৃত গ্রহণ ও ভর্তি'],
            purpose: ['Issue eligible admission offers, verify signed acceptance evidence and convert an authorized applicant into a student.', 'যোগ্য ভর্তির অফার দিন, স্বাক্ষরিত গ্রহণের প্রমাণ যাচাই করুন এবং অনুমোদিত আবেদনকারীকে শিক্ষার্থী হিসেবে ভর্তি করুন।'],
            before: ['TenantAdmin with ADMISSION access; a qualified applicant and verifiable offer-acceptance evidence.', 'ADMISSION অনুমতিসহ TenantAdmin; যোগ্য আবেদনকারী ও যাচাইযোগ্য অফার গ্রহণের প্রমাণ থাকতে হবে।'],
            steps: [
                ['Search applicants or enter an exact applicant reference; select Load.', 'আবেদনকারী খুঁজুন বা সঠিক আবেদন রেফারেন্স দিয়ে Load চাপুন।'],
                ['Choose Academic batch and Offer expiry; select Issue offer for an eligible applicant.', 'যোগ্য আবেদনকারীর Academic batch ও Offer expiry দিয়ে Issue offer চাপুন।'],
                ['Choose a verified OFFER_ACCEPTANCE document and signer, confirm the identity check and Record verified acceptance.', 'যাচাইকৃত OFFER_ACCEPTANCE নথি ও স্বাক্ষরকারী বেছে পরিচয় যাচাই নিশ্চিত করে Record verified acceptance চাপুন।'],
                ['Enter a valid Roll and optional Academic track, then Convert to student & enroll; confirm in Student Directory.', 'সঠিক Roll ও প্রয়োজনে Academic track দিয়ে Convert to student & enroll করুন; Student Directory-তে নিশ্চিত করুন।']
            ],
            example: ['Fictional example: issue a dated offer for qualified applicant Rina, verify the signed acceptance and enroll her with an eligible Grade 6 roll.', 'কাল্পনিক উদাহরণ: যোগ্য রিনাকে মেয়াদসহ অফার দিন, স্বাক্ষরিত গ্রহণ যাচাই করে ষষ্ঠ শ্রেণিতে উপযুক্ত রোল দিয়ে ভর্তি করুন।'],
            result: ['The verified offer and acceptance are retained; successful conversion creates a student enrollment.', 'যাচাইকৃত অফার ও গ্রহণ সংরক্ষিত হয়; সফল রূপান্তরে শিক্ষার্থী ভর্তি তৈরি হয়।'],
            rules: ['An offer alone is not acceptance. Do not bypass identity/document checks or manually create a second student if conversion status is uncertain.', 'শুধু অফার মানে গ্রহণ নয়। পরিচয়/নথি যাচাই এড়াবেন না; রূপান্তর অনিশ্চিত হলে দ্বিতীয় শিক্ষার্থী তৈরি করবেন না।'],
            recovery: ['Refresh status and evidence after a conflict. If conversion times out, search Student Directory and applicant status before retrying.', 'সংঘাতে অবস্থা ও প্রমাণ রিফ্রেশ করুন। রূপান্তরে সময় শেষ হলে পুনরায় দেওয়ার আগে শিক্ষার্থী ও আবেদন অবস্থা দেখুন।'],
            related: [['/Admissions/Applications', 'Application review', 'আবেদন যাচাই'], ['/Students/Index', 'Student directory', 'শিক্ষার্থী তালিকা']]
        },
        '/attendance/register/index': {
            version: 1, reviewed: true,
            title: ['Daily Attendance Register', 'দৈনিক উপস্থিতি রেজিস্টার'],
            purpose: ['Record eligible daily or subject attendance and finalize a complete roster when authorized.', 'অনুমোদিত দৈনিক বা বিষয়ভিত্তিক উপস্থিতি নথিভুক্ত করুন এবং অনুমতি থাকলে সম্পূর্ণ রোস্টার চূড়ান্ত করুন।'],
            before: ['ATTENDANCE access for Teacher, Principal, VicePrincipal or TenantAdmin; permitted academic batch/date and optional subject.', 'ATTENDANCE অনুমতিসহ Teacher, Principal, VicePrincipal বা TenantAdmin; অনুমোদিত ব্যাচ/তারিখ ও প্রয়োজনে বিষয় লাগবে।'],
            steps: [
                ['Select Date, find the authorized academic batch and optionally choose Subject offering.', 'তারিখ ও অনুমোদিত ব্যাচ খুঁজে প্রয়োজনে Subject offering বেছে নিন।'],
                ['Select Load roster, record each student and use Mark all present only when appropriate.', 'Load roster দিয়ে প্রতিটি শিক্ষার্থীর উপস্থিতি দিন; উপযুক্ত হলে Mark all present ব্যবহার করুন।'],
                ['Save attendance for the visible roster page before navigating to another page.', 'পরের পৃষ্ঠায় যাওয়ার আগে দৃশ্যমান রোস্টারের Save attendance করুন।'],
                ['Authorized administrators may Finalize register only after completing all eligible marks.', 'অনুমোদিত প্রশাসক সব যোগ্য উপস্থিতি সম্পন্ন করে Finalize register করতে পারেন।']
            ],
            example: ['Fictional example: load the authorized 3 November Grade 6 A roster, save marks on every page and finalize after review.', 'কাল্পনিক উদাহরণ: ৩ নভেম্বর ষষ্ঠ শ্রেণি A-এর রোস্টার এনে প্রতিটি পৃষ্ঠা সংরক্ষণ করে যাচাই শেষে চূড়ান্ত করুন।'],
            result: ['Saved attendance can be reviewed in the roster; finalized changes require audited correction.', 'সংরক্ষিত উপস্থিতি রোস্টারে দেখা যায়; চূড়ান্ত রেকর্ড বদলাতে নিরীক্ষিত সংশোধন লাগে।'],
            rules: ['Teachers cannot finalize or correct privileged records. Mark all present does not replace approved leave; every correction needs an authorized reason.', 'শিক্ষক প্রশাসক-নির্দিষ্ট চূড়ান্ত/সংশোধন করতে পারবেন না। Mark all present অনুমোদিত ছুটি বদলায় না; সংশোধনে অনুমোদিত কারণ লাগে।'],
            recovery: ['If loading fails, verify batch, date, role and subject. Save current-page changes before navigating; reload after a conflict.', 'লোড ব্যর্থ হলে ব্যাচ, তারিখ, ভূমিকা ও বিষয় দেখুন। পৃষ্ঠা বদলানোর আগে সংরক্ষণ করুন; সংঘাতে রিলোড করুন।'],
            related: [['/Attendance/Reports', 'Attendance reports', 'উপস্থিতির রিপোর্ট'], ['/Attendance/Corrections', 'Correction audit', 'সংশোধনের নিরীক্ষা']]
        },
        '/attendance/reports/index': {
            version: 1, reviewed: true,
            title: ['Finalized Attendance Reports', 'চূড়ান্ত উপস্থিতির রিপোর্ট'],
            purpose: ['Review authorized finalized attendance summaries, risk, subject coverage and session/daily registers.', 'অনুমোদিত চূড়ান্ত উপস্থিতির সারাংশ, ঝুঁকি, বিষয় কভারেজ ও সেশন/দৈনিক রেজিস্টার দেখুন।'],
            before: ['TenantAdmin, Principal or VicePrincipal and ATTENDANCE access; historical batch/date interval and authorized subject if needed.', 'ATTENDANCE অনুমতিসহ TenantAdmin, Principal বা VicePrincipal; ঐতিহাসিক ব্যাচ, তারিখের পরিসর ও প্রয়োজনে বিষয় লাগবে।'],
            steps: [
                ['Find historical batch and optional Subject offering; choose From/To dates (at most 30 days).', 'ঐতিহাসিক ব্যাচ ও প্রয়োজনে বিষয় বেছে From/To তারিখ দিন (সর্বোচ্চ ৩০ দিন)।'],
                ['Choose filters such as Attendance risk, Minimum marks and Order by; select Load report.', 'Attendance risk, Minimum marks ও Order by বেছে Load report চাপুন।'],
                ['Inspect risk summary, subject coverage, session register and daily finalized attendance.', 'ঝুঁকির সারাংশ, বিষয় কভারেজ, সেশন রেজিস্টার ও দৈনিক চূড়ান্ত উপস্থিতি দেখুন।'],
                ['Export only the currently authorized visible page CSV, or use the session print action where available.', 'শুধু অনুমোদিত দৃশ্যমান পৃষ্ঠার CSV নিন বা উপলব্ধ সেশন প্রিন্ট ব্যবহার করুন।']
            ],
            example: ['Fictional example: inspect a 7-day Grade 6 A attendance report, filter learners below 75% and export the visible page.', 'কাল্পনিক উদাহরণ: ষষ্ঠ শ্রেণি A-এর সাত দিনের রিপোর্টে ৭৫%-এর নিচে শিক্ষার্থী দেখে দৃশ্যমান পৃষ্ঠা CSV নিন।'],
            result: ['Scoped finalized report totals and bounded rows are displayed; no attendance marks are edited.', 'অনুমোদিত চূড়ান্ত সারাংশ ও সীমিত সারি দেখা যায়; উপস্থিতি সম্পাদিত হয় না।'],
            rules: ['Export is page-bounded. Historical totals are tied to the selected batch, subject, status and time interval; stale exports are not current evidence.', 'CSV পৃষ্ঠাভিত্তিক। ঐতিহাসিক মোট নির্বাচিত ব্যাচ, বিষয়, অবস্থা ও সময়সীমার; পুরোনো CSV বর্তমান প্রমাণ নয়।'],
            recovery: ['If no rows appear, check batch and date filters. Reduce an interval above 30 days and reload after recent finalization.', 'সারি না এলে ব্যাচ ও তারিখ যাচাই করুন। ৩০ দিনের বেশি সময় কমান; নতুন চূড়ান্তকরণের পর রিফ্রেশ করুন।'],
            related: [['/Attendance/Register', 'Attendance register', 'উপস্থিতি এন্ট্রি'], ['/Attendance/Corrections', 'Correction audit', 'সংশোধনের নিরীক্ষা']]
        },
        '/attendance/corrections/index': {
            version: 1, reviewed: true,
            title: ['Attendance Correction Audit', 'উপস্থিতি সংশোধনের নিরীক্ষা'],
            purpose: ['Inspect reasoned amendments to finalized attendance without changing the historical audit ledger.', 'চূড়ান্ত উপস্থিতির কারণসহ সংশোধন ইতিহাস দেখুন; নিরীক্ষার রেকর্ড পরিবর্তন হয় না।'],
            before: ['TenantAdmin, Principal or VicePrincipal with ATTENDANCE access; existing finalized register or correction records.', 'ATTENDANCE অনুমতিসহ TenantAdmin, Principal বা VicePrincipal; পূর্বের চূড়ান্ত বা সংশোধিত রেজিস্টার থাকতে হবে।'],
            steps: [
                ['Optionally enter Find student and Changed by user ID.', 'প্রয়োজনে Find student এবং Changed by user ID দিন।'],
                ['Choose Previous status and New status if a specific change is needed.', 'নির্দিষ্ট পরিবর্তন দেখতে Previous status ও New status বেছে নিন।'],
                ['Select Load corrections for report filters and inspect the reason, time and changed-by columns.', 'Load corrections for report filters চেপে কারণ, সময় ও পরিবর্তনকারীর তথ্য দেখুন।'],
                ['Use Export current page CSV or Print current page for authorized records.', 'অনুমোদিত রেকর্ডে Export current page CSV বা Print current page ব্যবহার করুন।']
            ],
            example: ['Fictional example: find a student whose finalized status changed from Absent to Present and inspect the recorded reason and actor.', 'কাল্পনিক উদাহরণ: Absent থেকে Present হওয়া শিক্ষার্থী খুঁজে কারণ ও পরিবর্তনকারীর তথ্য যাচাই করুন।'],
            result: ['A paginated audit register displays authorized corrections; this page does not create a new correction.', 'পৃষ্ঠাভিত্তিক নিরীক্ষা তালিকায় অনুমোদিত সংশোধন দেখা যায়; এই পৃষ্ঠায় নতুন সংশোধন তৈরি হয় না।'],
            rules: ['Corrections require a separate authorized roster action and reason. Audit history must not be rewritten; exports are scoped to the displayed page.', 'সংশোধনে আলাদা অনুমোদিত রোস্টার কাজ ও কারণ লাগে। নিরীক্ষার ইতিহাস বদলানো যাবে না; CSV দৃশ্যমান পৃষ্ঠাভিত্তিক।'],
            recovery: ['If no changes appear, clear filters and confirm the attendance date. To correct a roster, return to Attendance Register with permission.', 'সংশোধন না পেলে ফিল্টার সরিয়ে উপস্থিতির তারিখ দেখুন। নতুন সংশোধনে অনুমতিসহ Attendance Register-এ যান।'],
            related: [['/Attendance/Register', 'Attendance register', 'উপস্থিতি এন্ট্রি'], ['/Attendance/Reports', 'Finalized reports', 'চূড়ান্ত রিপোর্ট']]
        },
        '/institutionmanagement/profile/index': {
            version: 1, reviewed: true,
            title: ['Institution Profile', 'প্রতিষ্ঠানের প্রোফাইল'],
            purpose: ['Maintain the institution name, verified contact and address without changing campus or billing records.', 'ক্যাম্পাস বা বিলিং রেকর্ড না বদলে প্রতিষ্ঠানের নাম, যাচাইকৃত যোগাযোগ ও ঠিকানা পরিচালনা করুন।'],
            before: ['TenantAdmin for the current institution; review the existing profile before editing.', 'বর্তমান প্রতিষ্ঠানের TenantAdmin; সম্পাদনার আগে প্রোফাইল যাচাই করুন।'],
            steps: [
                ['Review the current institution name, phone, country and address.', 'বর্তমান প্রতিষ্ঠানের নাম, ফোন, দেশ ও ঠিকানা দেখুন।'],
                ['Edit permitted fields; the verified institution email is read-only.', 'অনুমোদিত তথ্য বদলান; যাচাইকৃত ইমেইল শুধু দেখা যাবে।'],
                ['Select Save profile, then Refresh to confirm the persisted values.', 'Save profile করে Refresh দিয়ে সংরক্ষিত তথ্য যাচাই করুন।']
            ],
            example: ['Fictional example: correct the institution phone number and mailing address, save, then reload the profile.', 'কাল্পনিক উদাহরণ: প্রতিষ্ঠানের ফোন ও চিঠি পাঠানোর ঠিকানা ঠিক করে সংরক্ষণ ও রিফ্রেশ করুন।'],
            result: ['Authorized profile changes persist independently; the verified email cannot be changed here.', 'অনুমোদিত প্রোফাইল পরিবর্তন আলাদাভাবে সংরক্ষিত হয়; এখানে যাচাইকৃত ইমেইল বদলানো যায় না।'],
            rules: ['Email changes require a separate verified workflow. Regional settings, branding, portal subdomain and campuses are distinct pages.', 'ইমেইল পরিবর্তনে আলাদা যাচাই লাগে। আঞ্চলিক সেটিংস, ব্র্যান্ডিং, সাবডোমেইন ও ক্যাম্পাস আলাদা পেজ।'],
            recovery: ['On a version conflict Refresh the current profile before editing again. A failed save must not be assumed successful.', 'সংস্করণ সংঘাতে আবার সম্পাদনার আগে বর্তমান প্রোফাইল রিফ্রেশ করুন। Save ব্যর্থ হলে সফল ধরে নেবেন না।'],
            related: [['/InstitutionManagement/Campuses', 'Campus management', 'ক্যাম্পাস পরিচালনা'], ['/InstitutionManagement/History', 'Change history', 'পরিবর্তনের ইতিহাস']]
        },
        '/institutionmanagement/campuses/index': {
            version: 1, reviewed: true,
            title: ['Campus Directory & Management', 'ক্যাম্পাস তালিকা ও পরিচালনা'],
            purpose: ['Review authorized campuses, create or edit campus details and maintain a valid head-office designation.', 'অনুমোদিত ক্যাম্পাস দেখুন, ক্যাম্পাসের তথ্য তৈরি/সম্পাদনা করুন ও প্রধান ক্যাম্পাস নির্ধারণ করুন।'],
            before: ['TenantAdmin for the current institution; unique campus code and required campus details.', 'বর্তমান প্রতিষ্ঠানের TenantAdmin; স্বতন্ত্র ক্যাম্পাস কোড ও প্রয়োজনীয় তথ্য লাগবে।'],
            steps: [
                ['Review the Campus directory and select Add campus to open the editor.', 'Campus directory দেখে Add campus দিয়ে এডিটর খুলুন।'],
                ['Enter campus name, code, contact and address; choose head-office/online options as appropriate.', 'ক্যাম্পাসের নাম, কোড, যোগাযোগ ও ঠিকানা দিয়ে প্রয়োজনমতো প্রধান/অনলাইন অবস্থা বাছুন।'],
                ['Select Save campus and verify the updated directory record.', 'Save campus করে তালিকায় পরিবর্তিত রেকর্ড যাচাই করুন।'],
                ['When editing or archiving an existing campus, review current dependencies and refresh the directory.', 'আগের ক্যাম্পাস সম্পাদনা বা আর্কাইভের সময় সংযুক্ত নির্ভরতা দেখে তালিকা রিফ্রেশ করুন।']
            ],
            example: ['Fictional example: add a second training campus with a unique code, phone and address; confirm it appears in the directory.', 'কাল্পনিক উদাহরণ: স্বতন্ত্র কোড, ফোন ও ঠিকানাসহ দ্বিতীয় প্রশিক্ষণ ক্যাম্পাস যোগ করে তালিকায় যাচাই করুন।'],
            result: ['The authorized campus directory reflects saved changes; there can be only one head-office designation.', 'অনুমোদিত ক্যাম্পাস তালিকায় পরিবর্তন দেখা যায়; একটিমাত্র প্রধান ক্যাম্পাস থাকতে পারে।'],
            rules: ['Archiving is blocked when a campus has linked users, academic history or active dependencies. Changes must not silently remove campus records used in transactions.', 'ক্যাম্পাসে ব্যবহারকারী, শিক্ষাবর্ষের ইতিহাস বা সক্রিয় নির্ভরতা থাকলে আর্কাইভ বন্ধ থাকে। পূর্বের লেনদেনের ক্যাম্পাস মুছে ফেলা যাবে না।'],
            recovery: ['For duplicate code or archive dependency errors, check the displayed campus and current records; Cancel discards editor changes.', 'একই কোড বা আর্কাইভে বাধা এলে বর্তমান ক্যাম্পাস ও সংযুক্ত রেকর্ড দেখুন; Cancel এডিটরের পরিবর্তন বাতিল করে।'],
            related: [['/InstitutionManagement/Profile', 'Institution profile', 'প্রতিষ্ঠানের প্রোফাইল'], ['/AcademicManagement/Years', 'Academic years', 'শিক্ষাবর্ষ']]
        },
        '/institutionmanagement/regional/index': {
            version: 1, reviewed: true,
            title: ['Institution Regional Settings', 'প্রতিষ্ঠানের আঞ্চলিক সেটিংস'],
            purpose: ['Set currency, time zone, date format and default institution language.', 'প্রতিষ্ঠানের মুদ্রা, সময় অঞ্চল, তারিখের ধরন ও ডিফল্ট ভাষা নির্ধারণ করুন।'],
            before: ['TenantAdmin for the current institution; confirm the institution policy for currency and time zone.', 'বর্তমান প্রতিষ্ঠানের TenantAdmin; মুদ্রা ও সময় অঞ্চল সম্পর্কে প্রতিষ্ঠানের নীতি নিশ্চিত করুন।'],
            steps: [
                ['Check the three-letter Currency code and Time zone identifier.', 'তিন অক্ষরের Currency code ও Time zone শনাক্তকারী দেখুন।'],
                ['Choose Date format and Default language for the institution.', 'প্রতিষ্ঠানের Date format ও Default language বেছে নিন।'],
                ['Select Save regional settings, then Refresh to verify persisted values.', 'Save regional settings করে Refresh দিয়ে সংরক্ষিত মান দেখুন।']
            ],
            example: ['Fictional example: set BDT, Asia/Dhaka, DD-MM-YYYY and Bangla for the institution default.', 'কাল্পনিক উদাহরণ: প্রতিষ্ঠানের জন্য BDT, Asia/Dhaka, DD-MM-YYYY ও বাংলা ডিফল্ট করুন।'],
            result: ['The server saves the selected regional preferences; they do not rewrite historical Finance or attendance records.', 'সার্ভার আঞ্চলিক পছন্দ সংরক্ষণ করে; পুরোনো হিসাব বা উপস্থিতির রেকর্ড বদলায় না।'],
            rules: ['The personal user language selector remains independent of the institution default. Financial currency is not converted by changing its display code.', 'ব্যবহারকারীর ভাষা নির্বাচন প্রতিষ্ঠানের ডিফল্ট থেকে আলাদা। মুদ্রার কোড বদলালেই আগের টাকার রূপান্তর হয় না।'],
            recovery: ['If a code or time zone is rejected, use a valid identifier and Refresh before retrying.', 'কোড বা সময় অঞ্চল প্রত্যাখ্যাত হলে সঠিক শনাক্তকারী দিন, রিফ্রেশ করে আবার চেষ্টা করুন।'],
            related: [['/InstitutionManagement/Profile', 'Institution profile', 'প্রতিষ্ঠানের প্রোফাইল'], ['/InstitutionManagement/History', 'Change history', 'পরিবর্তনের ইতিহাস']]
        },
        '/institutionmanagement/branding/index': {
            version: 1, reviewed: true,
            title: ['Institution Branding', 'প্রতিষ্ঠানের ব্র্যান্ডিং'],
            purpose: ['Upload or remove the institution logo and favicon used in supported branded surfaces.', 'প্রতিষ্ঠানের লোগো ও ফেভিকন সমর্থিত ব্র্যান্ডিং স্থানে ব্যবহার করতে আপলোড বা সরান।'],
            before: ['TenantAdmin, a supported PNG/JPEG logo or PNG/JPEG favicon (WebP allowed for logo), each at most 5 MB.', 'TenantAdmin; সর্বোচ্চ ৫ এমবি PNG/JPEG লোগো বা ফেভিকন (লোগোতে WebP-ও সমর্থিত)।'],
            steps: [
                ['Inspect the currently displayed Logo and Favicon previews.', 'বর্তমান Logo ও Favicon প্রিভিউ দেখুন।'],
                ['Choose a supported file in the relevant field and select Upload logo or Upload favicon.', 'সংশ্লিষ্ট ফাইলে সমর্থিত ছবি দিয়ে Upload logo অথবা Upload favicon চাপুন।'],
                ['Refresh to verify the saved preview; if authorized, use Remove logo/favicon to delete an old asset.', 'রিফ্রেশ করে সংরক্ষিত প্রিভিউ যাচাই করুন; প্রয়োজন হলে Remove logo/favicon দিয়ে পুরোনো ছবি সরান।']
            ],
            example: ['Fictional example: upload a 120 KB PNG institution logo and confirm it appears in the preview.', 'কাল্পনিক উদাহরণ: ১২০ KB PNG লোগো আপলোড করে প্রিভিউতে দেখা যাচ্ছে কিনা নিশ্চিত করুন।'],
            result: ['Uploaded image assets change independently of the institution name and personal Workspace theme colors.', 'ছবি আলাদাভাবে বদলায়; প্রতিষ্ঠানের নাম বা ব্যক্তিগত Workspace রং পরিবর্তিত হয় না।'],
            rules: ['Use only allowed image types and sizes. Institute branding is distinct from the seven personal Appearance Studio semantic colors.', 'অনুমোদিত ছবি ও আকার ব্যবহার করুন। প্রতিষ্ঠানের ব্র্যান্ডিং ও ব্যক্তিগত Appearance Studio-র সাতটি রং আলাদা।'],
            recovery: ['On an unsupported file or upload conflict choose an allowed image, Refresh the current profile and retry once.', 'ছবির ধরন ভুল বা আপলোড সংঘাতে সমর্থিত ছবি বেছে প্রোফাইল রিফ্রেশ করে পুনরায় চেষ্টা করুন।'],
            related: [['/InstitutionManagement/Profile', 'Institution profile', 'প্রতিষ্ঠানের প্রোফাইল'], ['/InstitutionManagement/History', 'Change history', 'পরিবর্তনের ইতিহাস']]
        },
        '/institutionmanagement/subdomain/index': {
            version: 1, reviewed: true,
            title: ['Portal Subdomain', 'পোর্টালের সাবডোমেইন'],
            purpose: ['Check and save the institution portal subdomain prefix.', 'প্রতিষ্ঠানের পোর্টাল সাবডোমেইন প্রিফিক্স যাচাই করে সংরক্ষণ করুন।'],
            before: ['TenantAdmin and an approved subdomain name with lowercase letters, digits or internal hyphens.', 'TenantAdmin এবং ছোট হাতের অক্ষর, সংখ্যা বা মাঝখানে হাইফেনযুক্ত গ্রহণযোগ্য সাবডোমেইন নাম লাগবে।'],
            steps: [
                ['Inspect the current Subdomain prefix.', 'বর্তমান Subdomain prefix দেখুন।'],
                ['Enter an allowed prefix and choose Check & save subdomain.', 'গ্রহণযোগ্য প্রিফিক্স দিয়ে Check & save subdomain চাপুন।'],
                ['Refresh and verify the saved value before sharing the portal URL.', 'রিফ্রেশ করে সংরক্ষিত মান যাচাই করুন, তারপর পোর্টালের লিংক শেয়ার করুন।']
            ],
            example: ['Fictional example: change an available training portal prefix from campus-old to campus-central after confirming its use.', 'কাল্পনিক উদাহরণ: প্রয়োজন নিশ্চিত করে উপলব্ধ campus-old প্রিফিক্স বদলে campus-central করুন।'],
            result: ['The authorized portal prefix is updated only after server validation.', 'সার্ভার যাচাই সফল হলেই পোর্টালের প্রিফিক্স বদলায়।'],
            rules: ['Changing a subdomain can break bookmarks and external integrations. Check ownership and communications before a live rename.', 'সাবডোমেইন পরিবর্তনে বুকমার্ক ও বাইরের ইন্টিগ্রেশন ভাঙতে পারে। চলমান পোর্টাল বদলানোর আগে সংশ্লিষ্টদের জানান।'],
            recovery: ['If unavailable or invalid, choose another prefix and Refresh; do not assume the old portal address automatically redirects.', 'নাম পাওয়া না গেলে অন্য প্রিফিক্স বেছে রিফ্রেশ করুন; পুরোনো লিংক স্বয়ংক্রিয়ভাবে কাজ করবে ধরে নেবেন না।'],
            related: [['/InstitutionManagement/Profile', 'Institution profile', 'প্রতিষ্ঠানের প্রোফাইল'], ['/InstitutionManagement/History', 'Change history', 'পরিবর্তনের ইতিহাস']]
        },
        '/institutionmanagement/history/index': {
            version: 1, reviewed: true,
            title: ['Institution Change History', 'প্রতিষ্ঠানের পরিবর্তনের ইতিহাস'],
            purpose: ['Review bounded audit entries for profile, regional, branding and subdomain changes.', 'প্রোফাইল, আঞ্চলিক, ব্র্যান্ডিং ও সাবডোমেইন পরিবর্তনের সীমিত নিরীক্ষার রেকর্ড দেখুন।'],
            before: ['TenantAdmin for the current institution; changes may not exist for a newly created profile.', 'বর্তমান প্রতিষ্ঠানের TenantAdmin; নতুন প্রতিষ্ঠানে পরিবর্তনের ইতিহাস নাও থাকতে পারে।'],
            steps: [
                ['Open the Institution change history register.', 'Institution change history তালিকা খুলুন।'],
                ['Choose Refresh history to load current records.', 'Refresh history দিয়ে নতুন রেকর্ড আনুন।'],
                ['Use Previous and Next to inspect time, action, changed fields and actor.', 'Previous ও Next দিয়ে সময়, কাজ, পরিবর্তিত ক্ষেত্র ও পরিবর্তনকারী দেখুন।']
            ],
            example: ['Fictional example: verify the latest phone correction and the responsible editor without exposing old private values.', 'কাল্পনিক উদাহরণ: পুরোনো ব্যক্তিগত মান প্রকাশ না করে শেষ ফোন সংশোধন ও পরিবর্তনকারী যাচাই করুন।'],
            result: ['Authorized audit metadata appears without stored private before/after values.', 'ব্যক্তিগত আগের/পরের মান ছাড়া অনুমোদিত নিরীক্ষা তথ্য দেখা যায়।'],
            rules: ['History is read-only; this view does not reverse or approve configuration edits.', 'ইতিহাস শুধু পড়ার জন্য; এখানে পরিবর্তন রিভার্স বা অনুমোদন করা যায় না।'],
            recovery: ['If no rows appear, Refresh and confirm the institution; do not seek another institute’s audit records.', 'রেকর্ড না এলে রিফ্রেশ ও প্রতিষ্ঠান যাচাই করুন; অন্য প্রতিষ্ঠানের ইতিহাস খোঁজবেন না।'],
            related: [['/InstitutionManagement/Profile', 'Institution profile', 'প্রতিষ্ঠানের প্রোফাইল'], ['/InstitutionManagement/Branding', 'Branding', 'ব্র্যান্ডিং']]
        }
    };
    // Source-reviewed task routes. Each short definition expands to the same
    // eight-section, bilingual schema as the existing full Page Manuals.
    function taskGuide(title, purpose, before, steps, example, result, rules, recovery, related) {
        return { version: 1, reviewed: true, title, purpose, before, steps,
            example, result, rules, recovery, related };
    }
    const operationsManuals = {
        '/hosteloperations/applications/index': taskGuide(
            ['Hostel Applications', 'আবাসিক আবেদন'],
            ['Request a hostel stay or review the authorized application and waitlist queue.', 'আবাসিক থাকার আবেদন করুন বা অনুমোদিত আবেদন ও অপেক্ষমাণ তালিকা দেখুন।'],
            ['HOSTEL access; the request is limited to verified eligible student or guardian authority. Queue decisions require a hostel manager.', 'HOSTEL অনুমতি; আবেদনটি যাচাইকৃত যোগ্য শিক্ষার্থী বা অভিভাবক কর্তৃত্বের মধ্যে থাকতে হবে। তালিকার সিদ্ধান্তে হোস্টেল পরিচালকের অনুমতি প্রয়োজন।'],
            [['Open My hostel applications and check the available request fields and current state.', 'আমার আবাসিক আবেদন খুলে প্রয়োজনীয় তথ্য ও বর্তমান অবস্থা দেখুন।'], ['Submit the eligible stay request; an authorized manager checks the application and waitlist queue.', 'যোগ্য থাকার আবেদন জমা দিন; অনুমোদিত পরিচালক আবেদন ও অপেক্ষমাণ তালিকা যাচাই করবেন।'], ['Check the decision or updated queue before attempting any further request.', 'নতুন আবেদন করার আগে সিদ্ধান্ত বা আপডেট হওয়া তালিকা দেখুন।']],
            ['Example: a verified guardian requests a stay for an eligible student; the warden later reviews the queue.', 'উদাহরণ: যাচাইকৃত অভিভাবক যোগ্য শিক্ষার্থীর জন্য আবেদন করেন; ওয়ার্ডেন পরে তালিকা যাচাই করেন।'],
            ['The request state is visible; a request alone never confirms a physical check-in.', 'আবেদনের অবস্থা দেখা যায়; শুধু আবেদন করলে বাস্তব চেক-ইন নিশ্চিত হয় না।'],
            ['Role and student scope are checked by the server; approval and actual bed allocation are separate.', 'ভূমিকা ও শিক্ষার্থীর পরিসর সার্ভারে যাচাই হয়; অনুমোদন ও শয্যা বরাদ্দ আলাদা কাজ।'],
            ['If a student is unavailable, check verified authority and eligibility. After an uncertain submission, reload the queue before trying again.', 'শিক্ষার্থী না পেলে যাচাইকৃত কর্তৃত্ব ও যোগ্যতা দেখুন। ফল অনিশ্চিত হলে আবার আবেদনের আগে তালিকা রিলোড করুন।'],
            [['/HostelOperations/Allocations', 'Bed allocations', 'শয্যা বরাদ্দ']]),
        '/hosteloperations/allocations/index': taskGuide(
            ['Hostel Bed Allocations', 'আবাসিক শয্যা বরাদ্দ'],
            ['Allocate an available bed and manage current or scheduled student stays.', 'খালি শয্যা বরাদ্দ করুন এবং বর্তমান বা নির্ধারিত শিক্ষার্থীর থাকা পরিচালনা করুন।'],
            ['HOSTEL access and TenantAdmin, Principal or HostelWarden role; choose a valid student and an eligible bed.', 'HOSTEL অনুমতি এবং TenantAdmin, Principal বা HostelWarden ভূমিকা; বৈধ শিক্ষার্থী ও যোগ্য শয্যা বাছুন।'],
            [['Search for an enrolled student and choose the correct record.', 'ভর্তিকৃত শিক্ষার্থী খুঁজে সঠিক রেকর্ড বাছুন।'], ['Find an available bed, enter the start and optional checkout date, then Allocate.', 'খালি শয্যা খুঁজে শুরু ও ঐচ্ছিক ছাড়ার তারিখ দিয়ে Allocate চাপুন।'], ['Use the active allocations register to close or correct only allowed stays.', 'সক্রিয় বরাদ্দ তালিকা থেকে শুধু অনুমোদিত থাকা বন্ধ বা সংশোধন করুন।']],
            ['Example: allocate an available room bed to an eligible student for a finite 30-day stay.', 'উদাহরণ: যোগ্য শিক্ষার্থীকে ৩০ দিনের জন্য খালি কক্ষের শয্যা বরাদ্দ করুন।'],
            ['The authorized stay appears in the current/scheduled register.', 'অনুমোদিত থাকা বর্তমান/নির্ধারিত বরাদ্দ তালিকায় দেখা যায়।'],
            ['Finite stays are limited to the server-approved date window; billed changes require independent Finance clearance.', 'নির্দিষ্ট সময়ের থাকা সার্ভারের অনুমোদিত সময়সীমায় সীমিত; বিল করা তথ্য বদলাতে স্বতন্ত্র Finance অনুমতি লাগে।'],
            ['When a student, bed or date fails validation, refresh availability. Do not bypass a reserved bed or a posted charge.', 'শিক্ষার্থী, শয্যা বা তারিখের যাচাই ব্যর্থ হলে খালি শয্যা আবার দেখুন; সংরক্ষিত শয্যা বা পোস্ট করা চার্জ এড়িয়ে যাবেন না।'],
            [['/HostelOperations/Setup', 'Rooms and beds', 'কক্ষ ও শয্যা'], ['/HostelOperations/Reports', 'Hostel reports', 'হোস্টেল প্রতিবেদন']]),
        '/hosteloperations/setup/index': taskGuide(
            ['Hostel Rooms and Beds Setup', 'হোস্টেলের কক্ষ ও শয্যা সেটআপ'],
            ['Manage campus-authorized hostel facilities, rooms, capacities and physical bed inventory.', 'ক্যাম্পাস-অনুমোদিত আবাসিক ভবন, কক্ষ, ধারণক্ষমতা ও শয্যার তালিকা পরিচালনা করুন।'],
            ['HOSTEL module and TenantAdmin, Principal or HostelWarden access; campus facilities must be active.', 'HOSTEL মডিউল এবং TenantAdmin, Principal বা HostelWarden অনুমতি; ক্যাম্পাসের ভবন সক্রিয় থাকতে হবে।'],
            [['Create or find an active hostel facility under the permitted campus.', 'অনুমোদিত ক্যাম্পাসে সক্রিয় আবাসিক ভবন তৈরি বা খুঁজে নিন।'], ['Create a room with capacity, then add numbered beds without exceeding room capacity.', 'ধারণক্ষমতাসহ কক্ষ তৈরি করে সীমা না ছাড়িয়ে নম্বরযুক্ত শয্যা যোগ করুন।'], ['Review room and bed registers before correcting labels or closing unused inventory.', 'কক্ষ ও শয্যা তালিকা দেখে লেবেল সংশোধন বা অব্যবহৃত উপকরণ বন্ধ করুন।']],
            ['Example: create room A-101 with four permitted beds, then review its inventory.', 'উদাহরণ: A-101 কক্ষ চার শয্যার জন্য তৈরি করে তালিকায় যাচাই করুন।'],
            ['The saved facility, room and beds are available for authorized allocation searches.', 'সংরক্ষিত ভবন, কক্ষ ও শয্যা অনুমোদিত বরাদ্দ খোঁজায় পাওয়া যায়।'],
            ['Occupied beds and rooms with active stays cannot be silently deactivated; rent is managed by Finance.', 'দখলকৃত শয্যা বা সক্রিয় থাকা কক্ষ নীরবে বন্ধ করা যায় না; ভাড়া Finance পরিচালনা করে।'],
            ['If creation fails, verify campus, unique labels and capacity; close allocations first where required.', 'তৈরি ব্যর্থ হলে ক্যাম্পাস, অনন্য নম্বর ও ধারণক্ষমতা যাচাই করুন; প্রয়োজন হলে আগে বরাদ্দ বন্ধ করুন।'],
            [['/HostelOperations/Allocations', 'Bed allocations', 'শয্যা বরাদ্দ']]),
        '/hosteloperations/reports/index': taskGuide(
            ['Hostel Reports', 'হোস্টেল প্রতিবেদন'],
            ['Review resident rosters, allocation history, occupancy risk and future capacity forecasts.', 'আবাসিক তালিকা, বরাদ্দের ইতিহাস, দখল-ঝুঁকি ও ভবিষ্যৎ ধারণক্ষমতার পূর্বাভাস দেখুন।'],
            ['HOSTEL module and authorized TenantAdmin, Principal or HostelWarden with permitted campus scope.', 'HOSTEL মডিউল ও অনুমোদিত ক্যাম্পাসের TenantAdmin, Principal বা HostelWarden।'],
            [['Select the resident roster date or the relevant history/occupancy report.', 'আবাসিক তালিকার তারিখ বা প্রয়োজনীয় ইতিহাস/দখল প্রতিবেদন বাছুন।'], ['Apply campus, student, date or status filters and load the bounded result.', 'ক্যাম্পাস, শিক্ষার্থী, তারিখ বা অবস্থা দিয়ে সীমিত ফলাফল লোড করুন।'], ['Review exceptions and use the authorized page CSV or print action if available.', 'ব্যতিক্রম যাচাই করে থাকলে অনুমোদিত CSV বা প্রিন্ট ব্যবহার করুন।']],
            ['Example: review tomorrow’s resident roster and occupancy risk before assigning another bed.', 'উদাহরণ: নতুন শয্যা বরাদ্দের আগে আগামীকালের আবাসিক তালিকা ও দখল-ঝুঁকি দেখুন।'],
            ['The selected report shows a point-in-time authorized snapshot; no bookings change.', 'নির্বাচিত প্রতিবেদন নির্দিষ্ট সময়ের অনুমোদিত তথ্য দেখায়; কোনো বুকিং বদলায় না।'],
            ['Report scopes and server paging apply; occupancy forecasts do not reserve beds or confirm check-in.', 'রিপোর্টের পরিসর ও সার্ভার পেজিং প্রযোজ্য; পূর্বাভাস শয্যা সংরক্ষণ বা চেক-ইন নিশ্চিত করে না।'],
            ['If rows are missing, check filters, campus and date, then refresh; do not treat an old CSV as live capacity.', 'সারি না এলে ফিল্টার, ক্যাম্পাস ও তারিখ দেখে রিফ্রেশ করুন; পুরোনো CSV-কে বর্তমান ধারণক্ষমতা ভাববেন না।'],
            [['/HostelOperations/Allocations', 'Bed allocations', 'শয্যা বরাদ্দ']]),
        '/transportoperations/setup/index': taskGuide(
            ['Transport Setup', 'পরিবহন সেটআপ'],
            ['Set up campus routes and ordered stops, vehicles, route-vehicle links and authorized driver coverage.', 'ক্যাম্পাস রুট ও স্টপ, গাড়ি, রুট-গাড়ি সংযোগ ও অনুমোদিত চালক ব্যবস্থা করুন।'],
            ['TRANSPORT module and TenantAdmin, Principal or TransportManager; driver registry controls require TenantAdmin.', 'TRANSPORT মডিউল ও TenantAdmin, Principal বা TransportManager; চালকের নিবন্ধন পরিচালনায় TenantAdmin প্রয়োজন।'],
            [['Select a permitted campus, name/code and ordered stops, then create the route.', 'অনুমোদিত ক্যাম্পাস, রুটের নাম/কোড ও ক্রমানুসারে স্টপ দিয়ে রুট তৈরি করুন।'], ['Set up eligible vehicles and active route-vehicle links for the service period.', 'সেবার সময়ের জন্য যোগ্য গাড়ি ও সক্রিয় রুট-গাড়ি সংযোগ তৈরি করুন।'], ['Review inventory and available driver compliance before assigning riders.', 'যাত্রী বরাদ্দের আগে গাড়ি তালিকা ও চালকের শর্ত যাচাই করুন।']],
            ['Example: create a campus bus route with three ordered stops and link an eligible vehicle.', 'উদাহরণ: একটি ক্যাম্পাসে তিনটি ক্রমানুসার স্টপের রুট তৈরি করে যোগ্য গাড়ি যুক্ত করুন।'],
            ['The active route and vehicle can be selected for eligible transport assignments.', 'সক্রিয় রুট ও গাড়ি যোগ্য পরিবহন বরাদ্দে বেছে নেওয়া যায়।'],
            ['Routes with active riders or assignments cannot be retired; fares remain under Finance.', 'সক্রিয় যাত্রী বা বরাদ্দযুক্ত রুট বন্ধ করা যায় না; ভাড়া Finance-এর অধীনে।'],
            ['If a route or vehicle is unavailable, verify campus, dates, compliance and active links before retrying.', 'রুট বা গাড়ি না এলে ক্যাম্পাস, তারিখ, শর্ত ও সক্রিয় সংযোগ দেখুন।'],
            [['/TransportOperations/Assignments', 'Rider assignments', 'যাত্রী বরাদ্দ']]),
        '/transportoperations/assignments/index': taskGuide(
            ['Transport Assignments', 'পরিবহন বরাদ্দ'],
            ['Assign eligible students to an active route and vehicle; manage reservations, boarding and fleet cases.', 'যোগ্য শিক্ষার্থীকে সক্রিয় রুট ও গাড়িতে বরাদ্দ দিন; সংরক্ষণ, গাড়িতে ওঠা ও গাড়ির সমস্যা পরিচালনা করুন।'],
            ['TRANSPORT access and TenantAdmin, Principal or TransportManager; the route-vehicle link and seat capacity must be valid.', 'TRANSPORT অনুমতি এবং TenantAdmin, Principal বা TransportManager; রুট-গাড়ি সংযোগ ও আসন ধারণক্ষমতা বৈধ হতে হবে।'],
            [['Find an eligible student and select route, vehicle and optional pickup/drop stops.', 'যোগ্য শিক্ষার্থী খুঁজে রুট, গাড়ি ও ঐচ্ছিক ওঠা/নামার স্টপ বাছুন।'], ['Enter start/end dates and allowed fare, then save the assignment.', 'শুরু/শেষের তারিখ ও অনুমোদিত ভাড়া দিয়ে বরাদ্দ সংরক্ষণ করুন।'], ['Use active reservations for allowed extension, cancellation, stop or vehicle corrections.', 'সক্রিয় বরাদ্দ থেকে অনুমোদিত মেয়াদ পরিবর্তন, বাতিল, স্টপ বা গাড়ি সংশোধন করুন।']],
            ['Example: assign a student to an active campus route with a valid seat for the next term.', 'উদাহরণ: পরের টার্মের জন্য শিক্ষার্থীকে খালি আসনসহ সক্রিয় ক্যাম্পাস রুটে বরাদ্দ দিন।'],
            ['The student appears in the authorized transport register; boarding remains a separate confirmation.', 'শিক্ষার্থী অনুমোদিত বরাদ্দ তালিকায় আসে; গাড়িতে ওঠা আলাদা নিশ্চিতকরণ।'],
            ['Billed or posted changes need Finance clearance; capacity and campus are always rechecked server-side.', 'বিল বা পোস্ট হওয়া পরিবর্তনে Finance অনুমতি লাগে; আসন ও ক্যাম্পাস সার্ভারে আবার যাচাই হয়।'],
            ['If seat or dates conflict, refresh current links and capacity. After uncertain save, inspect the register before resubmitting.', 'আসন বা তারিখে সংঘাত হলে বর্তমান সংযোগ ও ধারণক্ষমতা রিফ্রেশ করুন; ফল অনিশ্চিত হলে নতুন জমার আগে তালিকা দেখুন।'],
            [['/TransportOperations/Setup', 'Routes and fleet', 'রুট ও গাড়ি'], ['/TransportOperations/Reports', 'Transport reports', 'পরিবহন প্রতিবেদন']]),
        '/transportoperations/reports/index': taskGuide(
            ['Transport Reports', 'পরিবহন প্রতিবেদন'],
            ['Review rider history, stop demand, seat forecasts, fleet safety impact, driver readiness and assignment changes.', 'যাত্রীর ইতিহাস, স্টপের চাহিদা, আসন পূর্বাভাস, গাড়ির নিরাপত্তা-প্রভাব, চালকের প্রস্তুতি ও বরাদ্দ পরিবর্তন দেখুন।'],
            ['TRANSPORT module and TenantAdmin, Principal or TransportManager; some driver reports are TenantAdmin-only.', 'TRANSPORT মডিউল ও TenantAdmin, Principal বা TransportManager; কিছু চালক রিপোর্ট শুধু TenantAdmin-এর জন্য।'],
            [['Open the required rider, route, fleet or driver report.', 'প্রয়োজনীয় যাত্রী, রুট, গাড়ি বা চালক প্রতিবেদন খুলুন।'], ['Filter by authorized campus, date or route, then load and review the bounded rows.', 'অনুমোদিত ক্যাম্পাস, তারিখ বা রুট দিয়ে সীমিত সারি লোড ও যাচাই করুন।'], ['Use the report’s enabled export or print option if present.', 'থাকলে প্রতিবেদনটির অনুমোদিত রপ্তানি বা প্রিন্ট ব্যবহার করুন।']],
            ['Example: check peak seat forecasts for a campus route before new rider reservations.', 'উদাহরণ: নতুন যাত্রী বরাদ্দের আগে একটি ক্যাম্পাস রুটের সর্বোচ্চ আসনচাহিদা দেখুন।'],
            ['The report shows current authorized evidence; it does not create an assignment.', 'রিপোর্ট বর্তমান অনুমোদিত তথ্য দেখায়; নতুন বরাদ্দ তৈরি করে না।'],
            ['Forecasts and readiness reports are informational; actual assignment validates seats, driver eligibility and Finance state again.', 'পূর্বাভাস ও প্রস্তুতি রিপোর্ট শুধু তথ্য দেয়; আসল বরাদ্দে আসন, চালকের যোগ্যতা ও Finance অবস্থা আবার যাচাই হয়।'],
            ['If no results load, check campus, filters and selected period, then refresh before using a saved export.', 'ফল না এলে ক্যাম্পাস, ফিল্টার ও সময়কাল দেখে রিফ্রেশ করুন; পুরোনো এক্সপোর্ট ব্যবহার করবেন না।'],
            [['/TransportOperations/Assignments', 'Rider assignments', 'যাত্রী বরাদ্দ']])
    };
    const inventoryManuals = {
        '/inventorymovements/transactions/index': taskGuide(
            ['Stock Issues and Transfers', 'স্টক ইস্যু ও স্থানান্তর'], ['Prepare permitted stock transactions and receive eligible in-transit transfers.', 'অনুমোদিত স্টক লেনদেন প্রস্তুত ও পথে থাকা উপযুক্ত স্থানান্তর গ্রহণ করুন।'],
            ['INVENTORY access and TenantAdmin, Principal, InventoryManager or StoreKeeper; permitted locations and quantities are required.', 'INVENTORY অনুমতি ও TenantAdmin, Principal, InventoryManager বা StoreKeeper; অনুমোদিত স্থান ও পরিমাণ প্রয়োজন।'],
            [['Choose the transaction type, stock source/destination and authorized item quantities.', 'লেনদেনের ধরন, উৎস/গন্তব্য এবং অনুমোদিত পণ্য ও পরিমাণ বাছুন।'], ['Save the draft and follow the existing authorization and posting rules.', 'খসড়া সংরক্ষণ করে বিদ্যমান অনুমোদন ও পোস্টিং নিয়ম অনুসরণ করুন।'], ['For an eligible transfer, enter its dispatch reference and Receive stock.', 'যোগ্য স্থানান্তরে ডিসপ্যাচ রেফারেন্স দিয়ে Receive stock চাপুন।']],
            ['Example: prepare an approved classroom supply issue or receive a verified dispatch reference.', 'উদাহরণ: শ্রেণিকক্ষের উপকরণ ইস্যুর খসড়া বা যাচাইকৃত ডিসপ্যাচ গ্রহণ করুন।'],
            ['Authorized transactions are recorded in the canonical stock workflow.', 'অনুমোদিত লেনদেন মূল স্টক প্রক্রিয়ায় নথিভুক্ত হয়।'],
            ['Posting, reservations and receipt evidence are server-authorized; never create a second issue to fix a failed or uncertain request.', 'পোস্টিং, সংরক্ষণ ও গ্রহণের প্রমাণ সার্ভার অনুমোদন করে; অনিশ্চিত অনুরোধে আরেকটি ইস্যু তৈরি করবেন না।'],
            ['If a balance or dispatch conflicts, inspect current stock and transaction history before retrying.', 'স্টক বা ডিসপ্যাচে সংঘাত হলে আবার চেষ্টার আগে বর্তমান স্টক ও ইতিহাস দেখুন।'],
            [['/InventoryMovements/Reports', 'Stock reports', 'স্টক প্রতিবেদন']]),
        '/inventorymovements/adjustments/index': taskGuide(
            ['Stock Adjustments', 'স্টক সমন্বয়'], ['Create permitted inventory correction requests while preserving immutable posted history.', 'পোস্ট করা ইতিহাস অপরিবর্তিত রেখে অনুমোদিত স্টক সংশোধনের অনুরোধ করুন।'],
            ['INVENTORY access and TenantAdmin, Principal, InventoryManager or StoreKeeper.', 'INVENTORY অনুমতি ও TenantAdmin, Principal, InventoryManager বা StoreKeeper।'],
            [['Review the current item/location record and select the adjustment control.', 'বর্তমান পণ্য/স্থানের তথ্য দেখে সমন্বয়ের নিয়ন্ত্রণ বাছুন।'], ['Enter permitted quantity and required explanation, then submit for the authorized workflow.', 'অনুমোদিত পরিমাণ ও প্রয়োজনীয় কারণ লিখে অনুমোদিত প্রক্রিয়ায় জমা দিন।'], ['Check the resulting status before making further changes.', 'আরও পরিবর্তনের আগে ফলাফলের অবস্থা যাচাই করুন।']],
            ['Example: document a verified physical stock discrepancy for controlled review.', 'উদাহরণ: যাচাইকৃত বাস্তব স্টক অমিল নিয়মমাফিক পর্যালোচনার জন্য নথিভুক্ত করুন।'],
            ['The request is visible under its authorized state.', 'অনুরোধ অনুমোদিত অবস্থায় দেখা যায়।'],
            ['Adjustments do not rewrite already posted movement records; independent approvals apply where required.', 'সমন্বয়ে পোস্ট করা মুভমেন্ট রেকর্ড বদলায় না; প্রয়োজনমতো স্বতন্ত্র অনুমোদন লাগে।'],
            ['If the request is rejected, verify quantity, reason and current version; do not bypass the stock ledger.', 'অনুরোধ বাতিল হলে পরিমাণ, কারণ ও বর্তমান সংস্করণ দেখুন; স্টক লেজার এড়াবেন না।'],
            [['/InventoryMovements/Reports', 'Stock reports', 'স্টক প্রতিবেদন']]),
        '/inventorymovements/stockcounts/index': taskGuide(
            ['Physical Stock Counts', 'বাস্তব স্টক গণনা'], ['Record and review controlled physical counts against protected system book quantities.', 'সুরক্ষিত সিস্টেমের হিসাবের সঙ্গে বাস্তব স্টক গণনা নথিভুক্ত ও পর্যালোচনা করুন।'],
            ['INVENTORY access and a permitted location; any blind count or approval restriction remains enforced by the server.', 'INVENTORY অনুমতি ও অনুমোদিত স্থান; ব্লাইন্ড গণনা ও অনুমোদনের সীমা সার্ভার প্রয়োগ করে।'],
            [['Choose the authorized count location and create or locate the count session.', 'অনুমোদিত গণনার স্থান বেছে গণনা সেশন তৈরি বা খুঁজুন।'], ['Enter the observed physical quantities according to the session rules.', 'সেশনের নিয়ম অনুযায়ী বাস্তব পরিমাণ লিখুন।'], ['Submit the count through the existing review process and inspect its state.', 'বিদ্যমান যাচাই প্রক্রিয়ায় জমা দিয়ে অবস্থা দেখুন।']],
            ['Example: count supplies in a permitted store and submit the observed quantities for review.', 'উদাহরণ: অনুমোদিত স্টোরে উপকরণ গুনে দেখা পরিমাণ যাচাইয়ের জন্য জমা দিন।'],
            ['The count evidence is retained for its authorized review.', 'গণনার প্রমাণ অনুমোদিত যাচাইয়ের জন্য সংরক্ষিত হয়।'],
            ['Blind draft counts must not reveal hidden book balances; count submission alone does not silently change the posted ledger.', 'ব্লাইন্ড গণনার খসড়ায় লুকানো হিসাব প্রকাশ করা যাবে না; জমা দিলেই পোস্ট করা লেজার বদলায় না।'],
            ['If a count is stale or location access fails, reload the authorized session and resolve the mismatch.', 'গণনা পুরোনো বা স্থান অননুমোদিত হলে সেশন রিলোড করে অমিল ঠিক করুন।'],
            [['/InventoryMovements/Reports', 'Stock reports', 'স্টক প্রতিবেদন']]),
        '/inventorymovements/reports/index': taskGuide(
            ['Stock Reports and Registers', 'স্টক প্রতিবেদন ও তালিকা'], ['Review reservations, reorder levels, stock position, lot ageing and item history; the transaction ledger stays with Stock Transactions.', 'সংরক্ষণ, পুনঃঅর্ডার, স্টক অবস্থা, লটের বয়স ও পণ্যের ইতিহাস দেখুন; লেনদেনের লেজার স্টক লেনদেন পৃষ্ঠায় রয়েছে।'],
            ['INVENTORY module and a permitted tenant/campus location; Accountant has read-oriented report access.', 'INVENTORY মডিউল ও অনুমোদিত প্রতিষ্ঠান/ক্যাম্পাস; Accountant-এর প্রতিবেদনের অনুমতি রয়েছে।'],
            [['Select the reservation, reorder, stock-position, lot-ageing or item-history report.', 'সংরক্ষণ, পুনঃঅর্ডার, স্টক অবস্থা, লটের বয়স বা পণ্য ইতিহাসের প্রতিবেদন বাছুন।'], ['Apply location, item and period filters and load the bounded results.', 'স্থান, পণ্য ও সময় ফিল্টার দিয়ে সীমিত ফলাফল লোড করুন।'], ['Use enabled page export or print actions after checking scope and totals.', 'পরিসর ও মোট যাচাই করে অনুমোদিত রপ্তানি বা প্রিন্ট করুন।']],
            ['Example: check one stock location’s available units and outstanding reservations before issuing more stock.', 'উদাহরণ: নতুন ইস্যুর আগে নির্দিষ্ট স্থানের মজুত ও বাকি সংরক্ষণ দেখুন।'],
            ['Authorized live register rows are displayed without posting any new movement.', 'নতুন মুভমেন্ট ছাড়াই অনুমোদিত তালিকা দেখা যায়।'],
            ['Available stock can differ from on-hand stock due to reservations; report data is not a write permission.', 'সংরক্ষণের কারণে ব্যবহারযোগ্য স্টক বাস্তব মজুত থেকে আলাদা হতে পারে; রিপোর্ট দেখা মানে লেখার অনুমতি নয়।'],
            ['If rows are missing, check tenant, location and filters; refresh before relying on exported values.', 'সারি না এলে প্রতিষ্ঠান, স্থান ও ফিল্টার দেখুন; রপ্তানির তথ্য ব্যবহারের আগে রিফ্রেশ করুন।'],
            [['/InventoryMovements/Transactions', 'Stock transactions', 'স্টক লেনদেন']])
    };
    const libraryLearningManuals = {
        '/library/branches/index': taskGuide(
            ['Library Branches', 'গ্রন্থাগার শাখা'], ['Create and update campus-specific library locations and review registered branches.', 'ক্যাম্পাসভিত্তিক গ্রন্থাগার শাখা তৈরি, পরিবর্তন ও তালিকা দেখুন।'],
            ['LIBRARY module and TenantAdmin role; choose a permitted active campus.', 'LIBRARY মডিউল ও TenantAdmin ভূমিকা; অনুমোদিত সক্রিয় ক্যাম্পাস বাছতে হবে।'],
            [['Choose campus, branch name and code and save the branch.', 'ক্যাম্পাস, শাখার নাম ও কোড দিয়ে শাখা সংরক্ষণ করুন।'], ['Search the branch register; select a branch to update only permitted details.', 'শাখা খুঁজে অনুমোদিত তথ্য সংশোধনের জন্য বাছুন।']],
            ['Example: register a science library branch for an authorized campus.', 'উদাহরণ: অনুমোদিত ক্যাম্পাসে বিজ্ঞান গ্রন্থাগার শাখা নিবন্ধন করুন।'],
            ['The branch appears in its campus register.', 'শাখাটি ক্যাম্পাসের তালিকায় দেখা যায়।'],
            ['Branch changes are tenant/campus-scoped; active copy and circulation relationships must be respected.', 'শাখা পরিবর্তন প্রতিষ্ঠান/ক্যাম্পাসসীমায় হয়; সক্রিয় বই ও ইস্যু সম্পর্ক বজায় রাখতে হবে।'],
            ['If a campus or branch fails validation, refresh allowed campuses and check its unique code.', 'ক্যাম্পাস বা শাখার যাচাই ব্যর্থ হলে অনুমোদিত ক্যাম্পাস রিফ্রেশ ও অনন্য কোড যাচাই করুন।'],
            [['/Library/Index', 'Library catalogue', 'বইয়ের তালিকা']]),
        '/library/reservations/index': taskGuide(
            ['Library Reservations', 'বই সংরক্ষণ'], ['Find eligible books and create or review permitted reservation requests.', 'উপলব্ধ বই খুঁজে অনুমোদিত সংরক্ষণ আবেদন তৈরি বা যাচাই করুন।'],
            ['LIBRARY access; borrowers and staff can act only for authorized students and copies.', 'LIBRARY অনুমতি; পাঠক ও কর্মী শুধু অনুমোদিত শিক্ষার্থী ও বইয়ের জন্য কাজ করতে পারবেন।'],
            [['Search by book title, author or ISBN.', 'বইয়ের নাম, লেখক বা ISBN দিয়ে খুঁজুন।'], ['Where staff selection is available, find the active student and select an eligible book.', 'কর্মীর ক্ষেত্রে সক্রিয় শিক্ষার্থী খুঁজে যোগ্য বই বাছুন।'], ['Submit the reservation and check its returned status or queue.', 'সংরক্ষণ জমা দিয়ে অবস্থা বা অপেক্ষমাণ তালিকা দেখুন।']],
            ['Example: request an available Mathematics reference title for an eligible borrower.', 'উদাহরণ: যোগ্য পাঠকের জন্য একটি গণিত রেফারেন্স বই সংরক্ষণের আবেদন করুন।'],
            ['An accepted reservation is visible in the authorized view.', 'গৃহীত সংরক্ষণ অনুমোদিত তালিকায় দেখা যায়।'],
            ['A reservation does not automatically issue a physical copy; availability and borrower scope are checked by the server.', 'সংরক্ষণ মানে বই ইস্যু নয়; বইয়ের প্রাপ্যতা ও পাঠক পরিসর সার্ভার যাচাই করে।'],
            ['If no eligible book appears, refine title/ISBN and check copy availability; inspect existing reservations before retrying.', 'যোগ্য বই না এলে নাম/ISBN ও প্রাপ্যতা দেখুন; আবার জমার আগে বিদ্যমান সংরক্ষণ দেখুন।'],
            [['/Library/Index', 'Library catalogue', 'গ্রন্থাগারের তালিকা'], ['/Library/Circulation', 'Circulation', 'ইস্যু ও ফেরত']]),
        '/library/overdue/index': taskGuide(
            ['Overdue Ageing and Reminders', 'বিলম্বিত বই ও স্মরণবার্তা'], ['Review overdue ageing by branch and permitted borrower reminders.', 'শাখাভিত্তিক বিলম্বিত বইয়ের মেয়াদ এবং অনুমোদিত পাঠক স্মরণবার্তা দেখুন।'],
            ['LIBRARY module and TenantAdmin, Principal or Librarian; report is limited to authorized campuses.', 'LIBRARY মডিউল ও TenantAdmin, Principal বা Librarian; রিপোর্ট অনুমোদিত ক্যাম্পাসে সীমিত।'],
            [['Filter by branch name/code and optional campus ID, then Apply.', 'শাখার নাম/কোড ও ঐচ্ছিক ক্যাম্পাস ID দিয়ে Apply চাপুন।'], ['Review 1–7, 8–30 and 31+ day ageing buckets and the eligible reminder list.', '১–৭, ৮–৩০ ও ৩১ দিনের বেশি বিলম্ব এবং যোগ্য স্মরণবার্তার তালিকা দেখুন।'], ['Export the authorized page CSV if needed.', 'প্রয়োজন হলে অনুমোদিত পৃষ্ঠার CSV নিন।']],
            ['Example: examine a branch with several books over 30 days overdue before notices.', 'উদাহরণ: নোটিশের আগে ৩০ দিনের বেশি বিলম্বিত বইয়ের শাখা দেখুন।'],
            ['The report displays current overdue counts without silently charging borrowers.', 'রিপোর্টে বর্তমান বিলম্বের সংখ্যা দেখা যায়; নীরবে জরিমানা হয় না।'],
            ['Ageing is calculated against UTC today; CSV contains only the authorized page and not private borrower details.', 'UTC আজকের দিন ধরে বিলম্ব গণনা হয়; CSV শুধু অনুমোদিত পৃষ্ঠা দেখায়, ব্যক্তিগত পাঠক তথ্য নয়।'],
            ['If counts differ, check campus, branch and current date, then refresh the report.', 'সংখ্যা না মিললে ক্যাম্পাস, শাখা ও বর্তমান তারিখ দেখে রিফ্রেশ করুন।'],
            [['/Library/Circulation', 'Circulation', 'ইস্যু ও ফেরত']]),
        '/library/conditions/index': taskGuide(
            ['Book Copy Conditions', 'বইয়ের কপির অবস্থা'], ['Review and record physical copy condition evidence for permitted library items.', 'অনুমোদিত গ্রন্থাগারের বইয়ের কপির শারীরিক অবস্থা দেখুন ও নথিভুক্ত করুন।'],
            ['LIBRARY module and TenantAdmin, Principal or Librarian access.', 'LIBRARY মডিউল ও TenantAdmin, Principal বা Librarian অনুমতি।'],
            [['Set the permitted filters and Apply to review copy records.', 'অনুমোদিত ফিল্টার দিয়ে Apply চাপুন।'], ['Select an eligible copy and use Record copy condition with the required details.', 'যোগ্য কপি বেছে Record copy condition দিয়ে প্রয়োজনীয় তথ্য দিন।'], ['Recheck the updated register or export the visible page.', 'পরিবর্তিত তালিকা দেখুন বা বর্তমান পৃষ্ঠা রপ্তানি করুন।']],
            ['Example: document an observed damaged copy during a return inspection.', 'উদাহরণ: ফেরতের সময় ক্ষতিগ্রস্ত বইয়ের কপির অবস্থা নথিভুক্ত করুন।'],
            ['The copy condition record is available for permitted review.', 'কপির অবস্থার রেকর্ড অনুমোদিত পর্যালোচনায় পাওয়া যায়।'],
            ['Condition recording is not automatic Finance recovery; separate liability authorization applies.', 'কপির অবস্থা নথি মানেই অর্থ আদায় নয়; দায় নির্ধারণে আলাদা অনুমতি লাগে।'],
            ['If saving is rejected, verify the copy reference, access and current record before retrying.', 'সংরক্ষণ ব্যর্থ হলে কপির পরিচয়, অনুমতি ও বর্তমান রেকর্ড দেখুন।'],
            [['/Library/Liabilities', 'Liabilities', 'দায় ও অর্থায়ন'], ['/Library/Circulation', 'Circulation', 'ইস্যু ও ফেরত']]),
        '/library/integrity/index': taskGuide(
            ['Library Copy Integrity', 'বইয়ের কপি যাচাই'], ['Inspect copy discrepancies using the authorized accession and campus filters.', 'অনুমোদিত অ্যাকসেশন ও ক্যাম্পাস ফিল্টার দিয়ে কপির অসংগতি দেখুন।'],
            ['LIBRARY module and TenantAdmin, Principal or Librarian access.', 'LIBRARY মডিউল ও TenantAdmin, Principal বা Librarian অনুমতি।'],
            [['Enter an accession filter or an authorized campus, then Apply.', 'অ্যাকসেশন বা অনুমোদিত ক্যাম্পাস দিয়ে Apply চাপুন।'], ['Review discrepancy rows and export the current page CSV if needed.', 'অসংগতির সারি দেখে প্রয়োজন হলে বর্তমান পৃষ্ঠার CSV নিন।']],
            ['Example: check whether an accession has an inconsistent physical-copy record.', 'উদাহরণ: কোনো অ্যাকসেশনের বইয়ের কপি রেকর্ডে অসংগতি আছে কি না দেখুন।'],
            ['Matching discrepancies appear for authorized investigation.', 'মিল পাওয়া অসংগতি অনুমোদিত তদন্তের জন্য দেখা যায়।'],
            ['This report does not invent or delete copies; resolve issues through authorized inventory or circulation steps.', 'এই রিপোর্ট বই তৈরি বা মুছে দেয় না; অনুমোদিত বই ও ইস্যু প্রক্রিয়ায় সমাধান করতে হবে।'],
            ['If no rows appear, verify accession/campus filters; avoid assuming that an empty search covers all campuses.', 'সারি না এলে অ্যাকসেশন/ক্যাম্পাস দেখুন; ফাঁকা ফলকে সব ক্যাম্পাসের প্রমাণ ভাববেন না।'],
            [['/Library/Index', 'Library catalogue', 'বইয়ের তালিকা']]),
        '/library/liabilities/index': taskGuide(
            ['Library Liabilities', 'গ্রন্থাগারের দায়'], ['Review unassessed copy incidents and permitted liability and collection records.', 'বইয়ের অনির্ধারিত ঘটনা ও অনুমোদিত দায় এবং আদায়ের রেকর্ড দেখুন।'],
            ['LIBRARY module and TenantAdmin access; inspect the source incident before any decision.', 'LIBRARY মডিউল ও TenantAdmin অনুমতি; সিদ্ধান্তের আগে মূল ঘটনা যাচাই করুন।'],
            [['Find an unassessed incident in the incident register.', 'ঘটনা তালিকায় অনির্ধারিত ঘটনা খুঁজুন।'], ['Review allowed liability controls and confirm the correct source incident.', 'দায়-সংক্রান্ত অনুমোদিত নিয়ন্ত্রণ ও মূল ঘটনা যাচাই করুন।'], ['Inspect the liability/collection register and export the visible page if required.', 'দায়/আদায় তালিকা দেখে প্রয়োজন হলে বর্তমান পৃষ্ঠা রপ্তানি করুন।']],
            ['Example: inspect a lost-copy incident before recording any permitted liability.', 'উদাহরণ: হারানো বইয়ের ঘটনায় দায় নির্ধারণের আগে তথ্য যাচাই করুন।'],
            ['The selected incident and liability state remain traceable in the register.', 'নির্বাচিত ঘটনা ও দায়ের অবস্থা তালিকায় অনুসরণ করা যায়।'],
            ['Do not bypass source evidence or independent Finance posting; a visible liability is not proof of cash collection.', 'মূল প্রমাণ বা স্বতন্ত্র Finance posting এড়াবেন না; দৃশ্যমান দায় মানে টাকা আদায় নয়।'],
            ['If a source incident is missing or stale, refresh the register and verify access before another action.', 'মূল ঘটনা না পেলে বা পুরোনো হলে তালিকা রিফ্রেশ ও অনুমতি যাচাই করুন।'],
            [['/Library/Conditions', 'Copy conditions', 'বইয়ের অবস্থা']]),
        '/learning/quizzes/index': taskGuide(
            ['LMS Quizzes', 'শেখার কুইজ'], ['Create permitted quizzes, attempt published assessments and inspect eligible attempt history.', 'অনুমোদিত কুইজ তৈরি, প্রকাশিত মূল্যায়নে অংশগ্রহণ ও নিজের অনুমোদিত ইতিহাস দেখুন।'],
            ['LMS module; available authoring and attempt controls depend on the signed-in course role.', 'LMS মডিউল; কুইজ তৈরি ও অংশগ্রহণের নিয়ন্ত্রণ কোর্সের ভূমিকা অনুযায়ী দেখা যায়।'],
            [['Choose the eligible course and refresh the available quizzes.', 'যোগ্য কোর্স বেছে কুইজ তালিকা রিফ্রেশ করুন।'], ['Authorized author: add questions and Save quiz; learner: Start attempt and Submit answers.', 'অনুমোদিত শিক্ষক: প্রশ্ন যোগ করে Save quiz; শিক্ষার্থী: Start attempt দিয়ে উত্তর Submit করুন।'], ['Search attempt history by course; use page CSV/Print if authorized.', 'কোর্স দিয়ে ইতিহাস দেখুন; অনুমতি থাকলে পৃষ্ঠার CSV/Print নিন।']],
            ['Example: a learner completes a published course quiz and reviews the attempt state.', 'উদাহরণ: শিক্ষার্থী প্রকাশিত কোর্স কুইজ দিয়ে অংশগ্রহণের অবস্থা দেখেন।'],
            ['The permitted quiz or attempt appears in the corresponding register.', 'অনুমোদিত কুইজ বা অংশগ্রহণ সংশ্লিষ্ট তালিকায় দেখা যায়।'],
            ['Quiz drafts and submissions follow server authorization; do not infer course completion from an attempt.', 'কুইজ খসড়া ও জমা সার্ভারের অনুমতি মানে; কুইজে অংশ নেওয়া মানেই কোর্স সম্পন্ন নয়।'],
            ['If a quiz is missing, verify course selection and publication; refresh history after uncertain submission.', 'কুইজ না পেলে কোর্স ও প্রকাশনা দেখুন; জমার ফল অনিশ্চিত হলে ইতিহাস রিফ্রেশ করুন।'],
            [['/Learning/Index', 'Learning courses', 'কোর্সসমূহ']]),
        '/learning/liveclasses/index': taskGuide(
            ['Live Classes', 'লাইভ ক্লাস'], ['View course session schedules, meeting access and published recording history.', 'কোর্সের লাইভ ক্লাস সময়সূচি, মিটিং ও প্রকাশিত রেকর্ডিং দেখুন।'],
            ['LMS module and eligible course membership; scheduling and recording publication require authorized teaching staff.', 'LMS মডিউল ও যোগ্য কোর্স সদস্যপদ; সময়সূচি ও রেকর্ডিং প্রকাশে অনুমোদিত শিক্ষক প্রয়োজন।'],
            [['Choose an eligible course and Refresh its scheduled sessions.', 'যোগ্য কোর্স বেছে সময়সূচি Refresh করুন।'], ['Authorized staff may schedule a class and save the meeting details.', 'অনুমোদিত কর্মী ক্লাসের সময় ও মিটিং তথ্য সংরক্ষণ করতে পারেন।'], ['Review the schedule and published recordings; export or print if permitted.', 'সময় ও প্রকাশিত রেকর্ডিং দেখে অনুমতি থাকলে রপ্তানি বা প্রিন্ট করুন।']],
            ['Example: a teacher schedules a session and enrolled students view its meeting time.', 'উদাহরণ: শিক্ষক ক্লাস নির্ধারণ করলে নিবন্ধিত শিক্ষার্থী মিটিংয়ের সময় দেখেন।'],
            ['The session appears in the permitted course timetable.', 'সেশন অনুমোদিত কোর্সের সময়সূচিতে দেখা যায়।'],
            ['Publishing a recording is separate from scheduling a session; access remains course-scoped.', 'রেকর্ডিং প্রকাশ ক্লাস নির্ধারণ থেকে আলাদা; প্রবেশাধিকার কোর্সসীমায় থাকে।'],
            ['If an expected session does not appear, check course membership and selected date, then Refresh.', 'ক্লাস না এলে কোর্স সদস্যপদ ও তারিখ দেখে Refresh চাপুন।'],
            [['/Learning/Index', 'Learning courses', 'কোর্সসমূহ']]),
        '/learning/coursecontent/index': taskGuide(
            ['Course Content and Resources', 'কোর্সের পাঠ ও রিসোর্স'], ['Arrange course sections, assign lessons and attach permitted learning resources.', 'কোর্সের বিভাগ, পাঠ ও অনুমোদিত শিক্ষাসামগ্রী সাজান।'],
            ['LMS module and an authorized course; editing controls depend on the educator role.', 'LMS মডিউল ও অনুমোদিত কোর্স; সম্পাদনা শিক্ষকের ভূমিকার ওপর নির্ভরশীল।'],
            [['Choose the course and Refresh its sections and lessons.', 'কোর্স বেছে বিভাগ ও পাঠ Refresh করুন।'], ['Authorized staff: create a section, assign an existing lesson or save a lesson resource.', 'অনুমোদিত কর্মী: বিভাগ তৈরি, বিদ্যমান পাঠ সংযুক্ত বা রিসোর্স সংরক্ষণ করুন।'], ['Check the resulting course structure and export/print if needed.', 'কোর্সের নতুন কাঠামো দেখে প্রয়োজন হলে রপ্তানি/প্রিন্ট করুন।']],
            ['Example: create a Week 1 section and attach a permitted lesson resource.', 'উদাহরণ: প্রথম সপ্তাহের বিভাগ তৈরি করে অনুমোদিত পাঠসামগ্রী যুক্ত করুন।'],
            ['The section or resource becomes available under its authorized course.', 'বিভাগ বা রিসোর্স অনুমোদিত কোর্সে পাওয়া যায়।'],
            ['Only permitted content is shown to enrolled learners; a saved resource is not a certificate.', 'নিবন্ধিত শিক্ষার্থী শুধু অনুমোদিত বিষয় দেখেন; রিসোর্স সংরক্ষণ মানে সনদ নয়।'],
            ['If the course or lesson cannot be found, check membership and page filters before retrying.', 'কোর্স বা পাঠ না পেলে সদস্যপদ ও ফিল্টার যাচাই করুন।'],
            [['/Learning/Index', 'Learning courses', 'কোর্সসমূহ']]),
        '/learning/assignmentfiles/index': taskGuide(
            ['Assignment Handouts', 'অ্যাসাইনমেন্টের ফাইল'], ['View verified course handouts and, where authorized, attach learning materials.', 'যাচাইকৃত কোর্সের নথি দেখুন ও অনুমতি থাকলে শিক্ষাসামগ্রী যুক্ত করুন।'],
            ['LMS module and eligible course membership; uploading requires authorized course responsibility.', 'LMS মডিউল ও যোগ্য কোর্স সদস্যপদ; ফাইল আপলোডে অনুমোদিত কোর্স দায়িত্ব প্রয়োজন।'],
            [['Select an eligible course and Refresh the verified attachment list.', 'যোগ্য কোর্স বেছে যাচাইকৃত ফাইল তালিকা Refresh করুন।'], ['Authorized teacher: complete Add a handout and Save; other members may view eligible published files.', 'অনুমোদিত শিক্ষক: Add a handout ফরম পূরণ করে Save করুন; অন্য সদস্য অনুমোদিত প্রকাশিত ফাইল দেখবেন।'], ['Use the permitted export or print action for the current view.', 'বর্তমান পৃষ্ঠার অনুমোদিত রপ্তানি বা প্রিন্ট ব্যবহার করুন।']],
            ['Example: attach a worksheet to an authorized course for enrolled learners.', 'উদাহরণ: নিবন্ধিত শিক্ষার্থীদের জন্য অনুমোদিত কোর্সে ওয়ার্কশিট যুক্ত করুন।'],
            ['The verified attachment appears in the eligible course list.', 'যাচাইকৃত ফাইল যোগ্য কোর্সের তালিকায় দেখা যায়।'],
            ['File type, size, ownership and publication controls apply; do not upload private student material to a public handout.', 'ফাইলের ধরন, আকার, মালিকানা ও প্রকাশনার নিয়ম প্রযোজ্য; ব্যক্তিগত শিক্ষার্থী তথ্য প্রকাশ্য নথিতে দেবেন না।'],
            ['If upload fails, check the selected course and file constraints, then verify the list before retrying.', 'আপলোড ব্যর্থ হলে কোর্স ও ফাইলের শর্ত দেখুন; আবার দেওয়ার আগে তালিকা যাচাই করুন।'],
            [['/Learning/Index', 'Learning courses', 'কোর্সসমূহ']]),
        '/learning/certificates/index': taskGuide(
            ['LMS Completion Certificates', 'শেখার সমাপ্তি সনদ'], ['Review eligible course completion certificates and print authorized issued documents.', 'যোগ্য কোর্স সমাপ্তির সনদ দেখুন ও অনুমোদিত প্রকাশিত সনদ প্রিন্ট করুন।'],
            ['LMS module and authorized course/learner access; revoke/reissue controls are role-restricted.', 'LMS মডিউল ও অনুমোদিত কোর্স/শিক্ষার্থী প্রবেশাধিকার; বাতিল/পুনঃইস্যুতে বিশেষ ভূমিকা লাগে।'],
            [['Choose the authorized course and review available certificates.', 'অনুমোদিত কোর্স বেছে উপলব্ধ সনদ দেখুন।'], ['Select the eligible certificate, verify learner and issuance state, then Print/Save PDF.', 'যোগ্য সনদ বেছে শিক্ষার্থী ও ইস্যুর অবস্থা যাচাই করে Print/Save PDF করুন।'], ['Authorized staff can use the displayed revoke/reissue controls when the workflow permits.', 'প্রক্রিয়া অনুমোদন দিলে কর্মী বাতিল/পুনঃইস্যুর নিয়ন্ত্রণ ব্যবহার করতে পারবেন।']],
            ['Example: an eligible graduate prints their issued course completion certificate.', 'উদাহরণ: যোগ্য শিক্ষার্থী ইস্যুকৃত কোর্স সমাপ্তির সনদ প্রিন্ট করেন।'],
            ['The selected authorized certificate is displayed for verification or printing.', 'নির্বাচিত অনুমোদিত সনদ যাচাই বা প্রিন্টের জন্য দেখা যায়।'],
            ['Revoked certificates must not be treated as active; printing never changes completion eligibility.', 'বাতিল সনদকে সক্রিয় ভাবা যাবে না; প্রিন্ট করলে সমাপ্তির যোগ্যতা বদলায় না।'],
            ['If a certificate is unavailable, verify course selection, eligibility and issuance/revocation state.', 'সনদ না পেলে কোর্স, যোগ্যতা ও ইস্যু/বাতিলের অবস্থা যাচাই করুন।'],
            [['/Learning/Index', 'Learning courses', 'কোর্সসমূহ']])
    };
    const manuals = {
        ...operationsManuals,
        ...inventoryManuals,
        ...libraryLearningManuals,
        ...academicManuals,
        ...examManuals,
        ...taskWorkspaceManuals,
        "/studentlifecycle/index": {
            version: 1, reviewed: true,
            title: ["Student Exit and Transfer Certificate", "শিক্ষার্থী প্রস্থান ও স্থানান্তর সনদ"],
            purpose: ["Process an active student's final Transfer, Completed or Dropout status and inspect retained exit/certificate history.", "সক্রিয় শিক্ষার্থীর স্থানান্তর, শিক্ষা সমাপ্তি বা পড়াশোনা ত্যাগ চূড়ান্ত করুন এবং প্রস্থান ও সনদের ইতিহাস দেখুন।"],
            before: ["Sign in as TenantAdmin with STUDENT access. Verify the institution, active student and current enrollment. Transfer or Completed requires all outstanding fees to be settled.", "STUDENT অনুমতিসহ TenantAdmin হিসেবে সাইন ইন করুন। প্রতিষ্ঠান, সক্রিয় শিক্ষার্থী ও বর্তমান ভর্তি যাচাই করুন। স্থানান্তর বা শিক্ষা সমাপ্তির আগে সব বকেয়া ফি পরিশোধ করতে হবে।"],
            steps: [
                ["Search by at least two characters of student name or code and select the correct result.", "শিক্ষার্থীর নাম বা কোডের অন্তত দুই অক্ষর দিয়ে খুঁজে সঠিক ফল নির্বাচন করুন।"],
                ["Review status, enrollment count and any prior exit/certificate history.", "অবস্থা, ভর্তির সংখ্যা ও আগের প্রস্থান বা সনদের ইতিহাস যাচাই করুন।"],
                ["Choose Transfer (issues TC), Completed or Dropout; enter the reason and optional conduct remark.", "স্থানান্তর (TC প্রদান), শিক্ষা সমাপ্তি বা পড়াশোনা ত্যাগ বাছুন; কারণ ও ঐচ্ছিক আচরণ মন্তব্য লিখুন।"],
                ["Select Confirm Final Exit, review the confirmation and submit once.", "চূড়ান্ত প্রস্থান নিশ্চিত করুন চাপুন, নিশ্চিতকরণ দেখে একবার জমা দিন।"],
                ["Review the history row; for Transfer only, open the certificate link to view or print the TC.", "ইতিহাস দেখুন; শুধু স্থানান্তরে সনদের লিংক খুলে TC দেখুন বা প্রিন্ট করুন।"]
            ],
            example: ["Fictional example: active student ST-1042 has no unpaid fees. TenantAdmin chooses Transfer, records the reason, confirms and prints the issued TC from history.", "কাল্পনিক উদাহরণ: সক্রিয় শিক্ষার্থী ST-1042-এর বকেয়া নেই। TenantAdmin স্থানান্তর ও কারণ লিখে নিশ্চিত করেন এবং ইতিহাস থেকে TC প্রিন্ট করেন।"],
            result: ["The current enrollment closes and an immutable exit record remains. Transfer issues a TC; Completed sets Passed; Dropout does not issue a TC.", "বর্তমান ভর্তি বন্ধ হয় ও অপরিবর্তনীয় প্রস্থান রেকর্ড থাকে। স্থানান্তরে TC তৈরি হয়; শিক্ষা সমাপ্তিতে Passed অবস্থা হয়; পড়াশোনা ত্যাগে TC হয় না।"],
            rules: ["TenantAdmin-only server authorization checks tenant, current enrollment, student rowversion and idempotent request ID. Transfer and Completed require settled fees; Dropout can record an outstanding due. Final exits are not edited or deleted and do not post Finance GL or collect payments.", "শুধু TenantAdmin-এর জন্য সার্ভার প্রতিষ্ঠান, বর্তমান ভর্তি, শিক্ষার্থীর সংস্করণ ও পুনরাবৃত্ত অনুরোধ নম্বর যাচাই করে। স্থানান্তর ও শিক্ষা সমাপ্তিতে বকেয়া শূন্য হতে হবে; পড়াশোনা ত্যাগে বকেয়া রেকর্ড হতে পারে। চূড়ান্ত প্রস্থান সম্পাদনা বা মুছে ফেলা যায় না; এখানে Finance GL পোস্ট বা পেমেন্ট সংগ্রহ হয় না।"],
            recovery: ["For failed search, check the name/code. A 403 needs authorized access; 409 or changed student requires reload and review. After an uncertain network response inspect exit history before retrying; do not submit a different exit to bypass a conflict. Confirm the institution and certificate number if a TC link fails.", "খোঁজা ব্যর্থ হলে নাম বা কোড যাচাই করুন। ৪০৩ হলে অনুমতি প্রয়োজন; ৪০৯ বা তথ্য বদলালে রিলোড করুন। নেটওয়ার্কে ফল অনিশ্চিত হলে পুনরায় চেষ্টার আগে প্রস্থানের ইতিহাস দেখুন; সংঘাত এড়াতে অন্য প্রস্থান জমা দেবেন না। TC লিংক ব্যর্থ হলে প্রতিষ্ঠান ও সনদ নম্বর যাচাই করুন।"],
            related: [["/Students/Index", "Student Directory", "শিক্ষার্থী তালিকা"], ["/StudentTransferRequests/Index", "Transfer Requests", "স্থানান্তর আবেদন"]]
        },
        "/account/brandingsetup/index": {
            version: 2, reviewed: true,
            title: ["Institution Branding Setup", "প্রতিষ্ঠানের ব্র্যান্ডিং সেটআপ"],
            purpose: ["Set the authorized institution portal address, logo, favicon and three brand colors during setup or subsequent administration.", "প্রতিষ্ঠানের অনুমোদিত পোর্টাল ঠিকানা, লোগো, ফেভিকন ও তিনটি ব্র্যান্ড রঙ সেট করুন।"],
            before: ["Sign in as TenantAdmin for the current institution. Wait for profile, color settings, onboarding stage and session verification to load; use Retry if a read fails. The portal subdomain must be available.", "বর্তমান প্রতিষ্ঠানের TenantAdmin হিসেবে সাইন ইন করুন। প্রোফাইল, রঙ, সেটআপ ধাপ ও সেশন যাচাই লোড হওয়া পর্যন্ত অপেক্ষা করুন; লোড ব্যর্থ হলে Retry চাপুন। পোর্টাল সাবডোমেইন উপলব্ধ থাকতে হবে।"],
            steps: [
                ["Enter a 3–50 character portal subdomain, wait for availability confirmation and select Save Subdomain.", "৩–৫০ অক্ষরের পোর্টাল সাবডোমেইন লিখুন, উপলব্ধতা যাচাইয়ের পর Save Subdomain চাপুন।"],
                ["Use Upload beside Logo or Favicon to select a supported image; the preview updates after a successful upload. Use Remove to delete an existing image.", "Logo বা Favicon-এর Upload দিয়ে সমর্থিত ছবি দিন; সফল আপলোডে প্রিভিউ বদলাবে। আগের ছবি সরাতে Remove চাপুন।"],
                ["Choose Primary, Secondary and Accent colors using the picker or six-digit HEX fields.", "পিকার অথবা ছয় অঙ্কের HEX দিয়ে Primary, Secondary ও Accent রঙ বাছুন।"],
                ["After the current profile and stage load, select Save and Continue to save the three color settings and proceed according to the verified onboarding stage.", "বর্তমান প্রোফাইল ও ধাপ লোড হওয়ার পর তিনটি রঙ সংরক্ষণ করে যাচাইকৃত সেটআপ ধাপ অনুযায়ী এগোতে Save and Continue চাপুন।"],
                ["Reload or revisit the authorized branding page to confirm the saved values and images.", "সংরক্ষিত রঙ ও ছবি যাচাই করতে অনুমোদিত ব্র্যান্ডিং পৃষ্ঠা আবার খুলুন।"]
            ],
            example: ["Fictional example: a school chooses portal name north-campus-demo, uploads its own PNG logo, and sets Primary #174A68, Secondary #64748B and Accent #F59E0B.", "কাল্পনিক উদাহরণ: একটি স্কুল north-campus-demo পোর্টাল নাম, নিজস্ব PNG লোগো এবং Primary #174A68, Secondary #64748B ও Accent #F59E0B নির্ধারণ করে।"],
            result: ["The permitted institution branding settings are saved separately; the portal address and uploaded assets are confirmed by their own actions. Personal Workspace Appearance remains independent.", "অনুমোদিত প্রতিষ্ঠানের ব্র্যান্ডিং সেটিংস আলাদাভাবে সংরক্ষিত হয়; পোর্টাল ঠিকানা ও ছবি নিজ নিজ কাজের মাধ্যমে নিশ্চিত হয়। ব্যক্তিগত Workspace Appearance আলাদা থাকে।"],
            rules: ["TenantAdmin authorization, antiforgery and same-session/tenant verification guard all edits. Profile or onboarding load failure blocks writes. Color settings remain separate versioned rows, not one atomic publication; a network failure can leave earlier colors saved. Do not infer independent approval or invoice/Finance posting.", "সব পরিবর্তনে TenantAdmin অনুমতি, CSRF এবং একই সেশন/প্রতিষ্ঠান যাচাই কার্যকর। প্রোফাইল বা সেটআপ ধাপ লোড ব্যর্থ হলে সংরক্ষণ বন্ধ থাকে। রঙের সেটিংস এখনও পৃথক সংস্করণযুক্ত রেকর্ড, একবারে প্রকাশ নয়; নেটওয়ার্ক ব্যর্থতায় আগের রঙ সংরক্ষিত থাকতে পারে। এখানে আলাদা অনুমোদন বা Finance পোস্টিং হয় না।"],
            recovery: ["If profile or onboarding load fails, use Retry; saving stays blocked until all reads and the current session are verified. After an institution switch reload and sign in rather than retrying on the old page. For unavailable subdomain or invalid images, correct the input. For 409 refresh current versions. An interrupted color save reloads current persisted colors when possible; inspect all three before retrying because earlier writes may have committed.", "প্রোফাইল বা সেটআপ ধাপ লোড ব্যর্থ হলে Retry চাপুন; সব তথ্য ও বর্তমান সেশন যাচাই না হওয়া পর্যন্ত সংরক্ষণ বন্ধ থাকবে। প্রতিষ্ঠান বদলালে পুরোনো পেজে পুনরায় চেষ্টা না করে রিলোড ও সাইন ইন করুন। সাবডোমেইন না পাওয়া গেলে বা ছবি ভুল হলে তথ্য সংশোধন করুন। ৪০৯ হলে নতুন সংস্করণ লোড করুন। রঙ সংরক্ষণ বাধাগ্রস্ত হলে সম্ভব হলে বর্তমান সংরক্ষিত রঙ আবার লোড হবে; আগের পরিবর্তন সংরক্ষিত থাকতে পারে বলে পুনরায় চেষ্টা করার আগে তিনটি রঙ যাচাই করুন।"],
            related: [["/workspace", "Personal Workspace Appearance", "ব্যক্তিগত ওয়ার্কস্পেস ডিজাইন"]]
        },
        "/workspace/index": {
            version: 4, reviewed: true,
            title: ["Workspace Appearance Studio", "ওয়ার্কস্পেস অ্যাপিয়ারেন্স স্টুডিও"],
            purpose: ["Customize personal institution-scoped appearance, navigation, panels and table layouts without changing business records.", "ব্যবসায়িক রেকর্ড না বদলে নিজের প্রতিষ্ঠানভিত্তিক ডিজাইন, নেভিগেশন, প্যানেল ও তালিকা সাজান।"],
            before: ["An active institution session is required. Table-column controls are available only to TenantAdmin.", "সক্রিয় প্রতিষ্ঠানের সেশন প্রয়োজন। তালিকার কলাম নিয়ন্ত্রণ শুধু TenantAdmin-এর জন্য।"],
            steps: [
                ["Choose System, Light or Dark, a preset palette, navigation position, spacing and text size.", "System, Light বা Dark, প্রস্তুত প্যালেট, নেভিগেশন অবস্থান, স্পেসিং ও লেখার আকার বাছুন।"],
                ["Expand Advanced colors; enter valid HEX/RGB or use the picker for any of seven supported surfaces. Blank keeps the preset.", "Advanced colors খুলে সাতটি অংশের জন্য বৈধ HEX/RGB লিখুন বা রঙ বাছুন। ফাঁকা রাখলে প্রস্তুত রঙ থাকবে।"],
                ["Review the live preview, arrange optional dashboard panels and, if TenantAdmin, allowed table columns.", "লাইভ প্রিভিউ দেখে ঐচ্ছিক ড্যাশবোর্ড প্যানেল এবং TenantAdmin হলে অনুমোদিত তালিকার কলাম সাজান।"],
                ["Preview is not saved: choose Save appearance to keep it or Discard preview to restore the last saved design. Reset to defaults saves the default design; reload to verify.", "প্রিভিউ সংরক্ষিত নয়: রাখতে Save appearance চাপুন, অথবা শেষ সংরক্ষিত ডিজাইনে ফিরতে Discard preview চাপুন। Reset to defaults ডিফল্ট ডিজাইন সংরক্ষণ করে; রিলোড করে যাচাই করুন।"],
                ["TenantAdmin can review the read-only institution name, logo and brand colors below this editor, then use Edit Brand Colors to open the existing authorized branding setup. Personal Save appearance never changes institution defaults.", "TenantAdmin এই এডিটরের নিচে প্রতিষ্ঠানের নাম, লোগো ও ব্র্যান্ডের রঙের শুধু-পড়ার সারাংশ দেখতে পারেন। Edit Brand Colors দিয়ে বিদ্যমান অনুমোদিত ব্র্যান্ডিং সেটআপ খুলুন। ব্যক্তিগত Save appearance প্রতিষ্ঠানের ডিফল্ট বদলায় না।"],
                ["Open Previously saved appearances to restore one of up to five prior saved revisions. Discard unsaved edits before restoring, confirm the action and verify after reload.", "আগের সংরক্ষিত ডিজাইন খুলে সর্বশেষ পাঁচটি আগের সংস্করণ থেকে একটি ফিরিয়ে আনুন। আগে অসংরক্ষিত পরিবর্তন বাতিল করুন, নিশ্চিত করুন এবং রিলোড করে যাচাই করুন।"]
            ],
            example: ["Fictional example: Institute A uses Dark, Compact and #174A68 sidebar; Institute B retains a separate appearance.", "কাল্পনিক উদাহরণ: প্রতিষ্ঠান A-তে Dark, Compact ও #174A68 সাইডবার; প্রতিষ্ঠান B-তে আলাদা ডিজাইন থাকবে।"],
            result: ["Saved versioned appearance applies to this user and institution only, with no Finance, stock or invoice change.", "সংরক্ষিত সংস্করণযুক্ত ডিজাইন শুধু এই ব্যবহারকারী ও প্রতিষ্ঠানে প্রযোজ্য; Finance, স্টক বা ইনভয়েস বদলায় না।"],
            rules: ["Only allowlisted HEX/RGB colors are accepted; readable foreground is computed. Institute branding preview is TenantAdmin-only and read-only, sourced from existing tenant settings, not personal appearance. Unsaved previews warn before leaving. Save is required to persist; another-device edits require a version refresh. Restoring a prior revision is a versioned save in the same private institution scope; only five previous snapshots are retained.", "শুধু অনুমোদিত HEX/RGB রঙ গ্রহণযোগ্য; পাঠযোগ্য লেখার রঙ স্বয়ংক্রিয়ভাবে নির্ধারিত হয়। প্রতিষ্ঠানের ব্র্যান্ডিং সারাংশ শুধু TenantAdmin দেখতে পারেন; এটি বিদ্যমান প্রতিষ্ঠান সেটিংস থেকে আসে, ব্যক্তিগত ডিজাইন থেকে নয়। অসংরক্ষিত প্রিভিউ নিয়ে পেজ ছাড়লে সতর্কতা আসে। স্থায়ী করতে Save প্রয়োজন; অন্য ডিভাইসের পরিবর্তনে নতুন সংস্করণ লোড করুন। আগের সংস্করণ ফিরিয়ে আনলেও একই প্রতিষ্ঠানে নতুন সংস্করণযুক্ত সংরক্ষণ হয়; শুধু পাঁচটি আগের স্ন্যাপশট রাখা হয়।"],
            recovery: ["Invalid color: use #123ABC or rgb(18,52,86), then Discard preview if needed. A failed save does not persist the preview. For 409 reload current preferences; after institution switch reload and sign in. Failed loads disable saving until refreshed. If institute branding fails to load, use Retry; after a session switch reload the page.", "ভুল রঙে #123ABC বা rgb(18,52,86) দিন; প্রয়োজনে Discard preview চাপুন। Save ব্যর্থ হলে প্রিভিউ সংরক্ষিত হয় না। ৪০৯ হলে নতুন সেটিং লোড করুন; প্রতিষ্ঠান বদলালে রিলোড করে সাইন ইন করুন। লোড ব্যর্থ হলে রিফ্রেশ না করা পর্যন্ত সংরক্ষণ বন্ধ থাকে। প্রতিষ্ঠানের ব্র্যান্ডিং লোড না হলে Retry চাপুন; সেশন বদলালে পৃষ্ঠা আবার লোড করুন।"],
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
                            "If no graduate appears, ask an authorized administrator to verify the completed exit and account link. For missing campus/events check entitlement, campus access, consent and timing. If Attendance Check in/Undo times out, retry the same action; its request identifier is retained until confirmed success. For 403/409 reload; never guess another record ID or reuse an obsolete request.",
                            "শিক্ষার্থী না এলে অনুমোদিত প্রশাসকের কাছে সম্পন্ন ছাড়পত্র ও অ্যাকাউন্ট সংযোগ যাচাই করুন। ক্যাম্পাস/অনুষ্ঠান না এলে মডিউল, ক্যাম্পাস অনুমতি, সম্মতি ও সময় দেখুন। Attendance Check in/Undo-এর পরে নেটওয়ার্ক টাইমআউট হলে একই কাজ আবার চেষ্টা করুন; নিশ্চিত সফলতার আগে একই request ID রাখা হয়। 403/409 হলে রিলোড করুন; অন্য ID অনুমান বা পুরোনো অনুরোধ ব্যবহার নয়।"
                  ],
                  "related": [
                            [
                                      "/Students/Index",
                                      "Student directory (authorized staff only)",
                                      "শিক্ষার্থী তালিকা (শুধু অনুমোদিত কর্মী)"
                            ]
                  ]
        },
        '/financeadjustments/policies/index': {
            version: 1, reviewed: true,
            title: ['Discount & Fine Rules', 'ছাড় ও জরিমানার নিয়ম'],
            purpose: ['Maintain the Finance adjustment policy registers for student discount assignments and assessed fines.', 'শিক্ষার্থীর ছাড় বরাদ্দ ও জরিমানা নির্ধারণে ব্যবহৃত Finance সমন্বয়ের নিয়ম পরিচালনা করুন।'],
            before: ['TenantAdmin and FINANCE access; know the approved fee head, dates and required policy amounts. This is separate from the general Fee Discount Rules screen.', 'TenantAdmin ও FINANCE অনুমতি এবং অনুমোদিত ফি খাত, তারিখ ও টাকার নিয়ম জানতে হবে। এটি সাধারণ Fee Discount Rules পৃষ্ঠা থেকে আলাদা।'],
            steps: [
                ['Under Discount rules enter name, value, percent/fixed setting, fee head, dates and Active, then Save discount rule.', 'Discount rules-এ নাম, মান, শতাংশ/নির্দিষ্ট টাকা, ফি খাত, তারিখ ও Active দিয়ে Save discount rule চাপুন।'],
                ['Under Fine rules enter name, amount, grace days, per-overdue-day option and fee head, then Save fine rule.', 'Fine rules-এ নাম, টাকা, ছাড়ের দিন, প্রতিদিনের জরিমানা ও ফি খাত দিয়ে Save fine rule চাপুন।'],
                ['Review each rule register; choose Edit to load the saved version, or New to clear an editor.', 'প্রতিটি নিয়মের তালিকা যাচাই করুন; সংরক্ষিত সংস্করণ খুলতে Edit, নতুন নিয়মে যেতে New চাপুন।']
            ],
            example: ['Fictional example: configure a fixed BDT 100 student adjustment discount and a separate BDT 20 fine rule; verify both registers before assigning either to a student.', 'কাল্পনিক উদাহরণ: BDT ১০০ নির্দিষ্ট ছাড় ও আলাদা BDT ২০ জরিমানার নিয়ম তৈরি করে শিক্ষার্থীকে দেওয়ার আগে দুই তালিকা যাচাই করুন।'],
            result: ['Active rules become selectable in the separate Student Finance Adjustments workflow; merely saving a rule does not create an invoice or cash payment.', 'সক্রিয় নিয়ম Student Finance Adjustments পৃষ্ঠায় নির্বাচনযোগ্য হয়; শুধু নিয়ম সংরক্ষণে ইনভয়েস বা নগদ লেনদেন হয় না।'],
            rules: ['Rules and individual assignments are different records. An issued invoice and its posted journal cannot be rewritten by editing a rule.', 'নিয়ম ও শিক্ষার্থীর বরাদ্দ আলাদা রেকর্ড। নিয়ম সম্পাদনায় ইস্যু করা ইনভয়েস বা পোস্ট করা জার্নাল বদলানো যায় না।'],
            recovery: ['If saving fails, inspect the amount, fee head and effective range; refresh the register and current version before editing again.', 'সংরক্ষণ ব্যর্থ হলে টাকা, ফি খাত ও কার্যকর তারিখ দেখুন; আবার সম্পাদনার আগে তালিকা ও বর্তমান সংস্করণ রিফ্রেশ করুন।'],
            related: [['/FinanceAdjustments/StudentAdjustments', 'Student adjustments', 'শিক্ষার্থীর সমন্বয়'], ['/FeeHeadAccounting/Index', 'Fee head accounting', 'ফি খাতের হিসাব']]
        },
        '/financeadjustments/studentadjustments/index': {
            version: 1, reviewed: true,
            title: ['Student Finance Adjustments', 'শিক্ষার্থীর আর্থিক সমন্বয়'],
            purpose: ['Assign a permitted discount, assess a fine and inspect an enrollment’s adjustment history.', 'অনুমোদিত ছাড় বরাদ্দ, জরিমানা নির্ধারণ ও শিক্ষার্থী ভর্তির সমন্বয়ের ইতিহাস দেখুন।'],
            before: ['TenantAdmin and FINANCE access; verify the student enrollment, applicable active rule, period and approval reason.', 'TenantAdmin ও FINANCE অনুমতি; শিক্ষার্থীর ভর্তি, সক্রিয় নিয়ম, সময়কাল ও অনুমোদনের কারণ যাচাই করুন।'],
            steps: [
                ['Search student name/code (at least two characters), select the correct Enrollment and inspect its existing adjustments.', 'অন্তত দুই অক্ষরে শিক্ষার্থী খুঁজে সঠিক Enrollment বেছে আগের সমন্বয় দেখুন।'],
                ['For a discount choose Active discount rule, effective dates and Approval reason, then Assign.', 'ছাড় দিতে Active discount rule, কার্যকর তারিখ ও Approval reason দিয়ে Assign চাপুন।'],
                ['For a fine choose Active fine rule, date, reason and an original unpaid invoice when per-day policy requires it; select Record fine.', 'জরিমানায় Active fine rule, তারিখ, কারণ এবং প্রতিদিনের নিয়মে প্রয়োজন হলে মূল বকেয়া ইনভয়েস বেছে Record fine চাপুন।'],
                ['Review the enrollment register; where permitted, use a valid open accounting period and separate approved posting or reversal controls.', 'ভর্তির সমন্বয় তালিকা দেখুন; অনুমতি অনুযায়ী খোলা হিসাবকাল বেছে আলাদা অনুমোদিত পোস্টিং বা রিভার্সাল করুন।']
            ],
            example: ['Fictional example: find ST-1042, assign an approved BDT 100 adjustment for the verified period, then inspect its saved assignment before any later billing.', 'কাল্পনিক উদাহরণ: ST-1042 খুঁজে যাচাইকৃত সময়ের জন্য অনুমোদিত BDT ১০০ ছাড় দিন এবং পরবর্তী বিলিংয়ের আগে বরাদ্দটি দেখুন।'],
            result: ['The enrollment adjustment register updates with the recorded rule or fine; posted invoices and GL journals remain separate authorized actions.', 'ভর্তির সমন্বয় তালিকায় বরাদ্দ বা জরিমানা দেখা যায়; ইনভয়েস ও GL পোস্টিং আলাদা অনুমোদিত কাজ।'],
            rules: ['Assessors cannot approve their own fine posting. Posted invoice changes require the existing audited reversal process; never recreate an adjustment to bypass a concurrency error.', 'জরিমানা নির্ধারণকারী নিজের পোস্টিং অনুমোদন করতে পারেন না। পোস্ট করা ইনভয়েসে নিরীক্ষাযোগ্য রিভার্সাল প্রয়োজন; সংস্করণ-সংঘাত এড়াতে নতুন সমন্বয় তৈরি করবেন না।'],
            recovery: ['If a student/rule does not appear, verify the enrollment and active policy. After an uncertain request, reload its register and posted status before retrying.', 'শিক্ষার্থী বা নিয়ম না পেলে ভর্তি ও সক্রিয় নীতি দেখুন। অনিশ্চিত অনুরোধের পরে পুনরায় চেষ্টা করার আগে তালিকা ও পোস্টিং অবস্থা রিফ্রেশ করুন।'],
            related: [['/FinanceAdjustments/Policies', 'Discount and fine rules', 'ছাড় ও জরিমানার নিয়ম'], ['/FinanceAdjustments/FineRegister', 'Fine register', 'জরিমানার রেজিস্টার']]
        },
        '/financeadjustments/fineregister/index': {
            version: 1, reviewed: true,
            title: ['Fine Register', 'জরিমানার রেজিস্টার'],
            purpose: ['Review institution fine assessments by campus, date and waiver state, with separate assessed, waived, posted and unposted totals.', 'ক্যাম্পাস, তারিখ ও মওকুফের অবস্থা দিয়ে জরিমানা এবং নির্ধারিত, মওকুফ, পোস্ট ও অপোস্ট মোট দেখুন।'],
            before: ['TenantAdmin and FINANCE access; select an authorized campus ID if filtering and a meaningful fine date range.', 'TenantAdmin ও FINANCE অনুমতি; ফিল্টার করলে অনুমোদিত ক্যাম্পাস ID ও উপযুক্ত জরিমানার তারিখ দিন।'],
            steps: [
                ['Choose optional Campus ID, Fine from/to dates and Status, then Load fines.', 'ঐচ্ছিক Campus ID, Fine from/to তারিখ ও Status দিয়ে Load fines চাপুন।'],
                ['Review the four BDT totals and the 25-row paged list; use Previous and Next for additional results.', 'চারটি BDT মোট ও ২৫টি করে তালিকা দেখুন; আরও ফল পেতে Previous ও Next চাপুন।'],
                ['Select Inspect on a row to open that enrollment’s adjustment history below; this expands a context panel, not an independent posting.', 'কোনো সারিতে Inspect চাপলে নিচে সেই শিক্ষার্থীর সমন্বয়ের ইতিহাস খুলবে; এতে নিজে কোনো পোস্টিং হয় না।']
            ],
            example: ['Fictional example: filter the October 2026 fine register for an authorized campus, compare Posted and Unposted totals, then Inspect ST-1042.', 'কাল্পনিক উদাহরণ: অনুমোদিত ক্যাম্পাসে অক্টোবর ২০২৬-এর জরিমানা বেছে Posted ও Unposted মোট মিলিয়ে ST-1042 Inspect করুন।'],
            result: ['Current matching totals and fine rows are shown; Inspect exposes the related enrollment context for permitted follow-up.', 'মিল পাওয়া বর্তমান জরিমানা ও মোট দেখা যায়; Inspect-এ পরবর্তী অনুমোদিত কাজের জন্য শিক্ষার্থীর তথ্য খুলবে।'],
            rules: ['The register is a report, not proof of GL settlement. Posting and reversal require separate period and independent-approval checks.', 'রেজিস্টার একটি প্রতিবেদন, GL নিষ্পত্তির প্রমাণ নয়। পোস্টিং ও রিভার্সালে আলাদা হিসাবকাল ও স্বাধীন অনুমোদন লাগে।'],
            recovery: ['Correct invalid campus/date filters; on a load failure refresh and rerun the report. Check the student adjustment register for a disputed fine.', 'ভুল ক্যাম্পাস/তারিখ ফিল্টার ঠিক করুন; লোড ব্যর্থ হলে রিফ্রেশ করে আবার চালান। প্রশ্ন থাকা জরিমানায় শিক্ষার্থীর সমন্বয় তালিকা দেখুন।'],
            related: [['/FinanceAdjustments/StudentAdjustments', 'Student adjustments', 'শিক্ষার্থীর সমন্বয়']]
        },
        '/financeadjustments/refunds/index': {
            version: 1, reviewed: true,
            title: ['Cash Refunds & Settlement', 'নগদ ফেরত ও নিষ্পত্তি'],
            purpose: ['Request, independently review and record verified payout of a student cash refund with the original receipt lineage.', 'শিক্ষার্থীর মূল নগদ রসিদের সঙ্গে যুক্ত ফেরতের আবেদন, স্বাধীন যাচাই ও প্রকৃত পরিশোধ নথিভুক্ত করুন।'],
            before: ['TenantAdmin and FINANCE access; verify the original posted receipt, refundable amount, disbursement evidence and open accounting period.', 'TenantAdmin ও FINANCE অনুমতি; মূল পোস্ট করা রসিদ, ফেরতযোগ্য টাকা, পরিশোধের প্রমাণ ও খোলা হিসাবকাল যাচাই করুন।'],
            steps: [
                ['Filter authorized campus and Load cash receipts; select the original posted cash receipt.', 'অনুমোদিত ক্যাম্পাস দিয়ে Load cash receipts চাপুন; মূল পোস্ট করা নগদ রসিদ বাছুন।'],
                ['Enter amount and reason, then Request refund once and inspect the requests list.', 'টাকা ও কারণ দিয়ে Request refund একবার চাপুন এবং আবেদন তালিকা দেখুন।'],
                ['A different authorized administrator reviews the request and uses Approve or Reject, recording a rejection reason when needed.', 'ভিন্ন অনুমোদিত প্রশাসক আবেদন যাচাই করে Approve বা Reject করবেন; প্রত্যাখ্যানে কারণ লিখবেন।'],
                ['Only after real independently verified cash payment, select the open period, actual payout date and voucher reference; use Record verified payout.', 'প্রকৃত নগদ পরিশোধ স্বাধীনভাবে যাচাইয়ের পরে খোলা হিসাবকাল, আসল তারিখ ও ভাউচার নম্বর দিয়ে Record verified payout চাপুন।']
            ],
            example: ['Fictional example: request BDT 500 against a verified posted receipt, obtain independent approval, pay cash separately, then record its actual voucher and date.', 'কাল্পনিক উদাহরণ: যাচাইকৃত পোস্ট করা রসিদে BDT ৫০০ ফেরত চেয়ে স্বাধীন অনুমোদন নিন, আলাদাভাবে নগদ দিন, তারপর প্রকৃত ভাউচার ও তারিখ লিখুন।'],
            result: ['The refund advances through requested, reviewed and recorded payout states; the appropriate Finance accounting lineages are preserved.', 'ফেরত আবেদন, যাচাই ও নথিভুক্ত পরিশোধের ধাপে এগোয়; সংশ্লিষ্ট Finance হিসাবের সূত্র অক্ষত থাকে।'],
            rules: ['This UI never sends funds. Requester and approver must differ, and payout cannot be marked without independently checked evidence. Paid-invoice refunds and partly paid invoices have different canonical GL treatment; do not edit posted receipts.', 'এই UI টাকা পাঠায় না। আবেদনকারী ও অনুমোদনকারী ভিন্ন হতে হবে; স্বাধীন প্রমাণ ছাড়া পরিশোধ নথিভুক্ত নয়। সম্পূর্ণ ও আংশিক পরিশোধের GL প্রক্রিয়া আলাদা; পোস্ট করা রসিদ বদলাবেন না।'],
            recovery: ['For a timeout inspect the existing request before retrying with its original idempotency key. For a rejected payout recheck period, paid cash evidence and current rowversion.', 'সময় শেষ হলে একই অনুরোধ পুনরায় দেওয়ার আগে আগের আবেদন ও মূল idempotency key যাচাই করুন। পরিশোধ আটকে গেলে হিসাবকাল, নগদ প্রমাণ ও বর্তমান সংস্করণ দেখুন।'],
            related: [['/FeeOperations/Ledger', 'Student ledger', 'শিক্ষার্থী লেজার'], ['/FinanceAdjustments/StudentAdjustments', 'Student adjustments', 'শিক্ষার্থীর সমন্বয়']]
        },
        '/hr/directory/index': {
            version: 1, reviewed: true,
            title: ['Employee Directory', 'কর্মী তালিকা'],
            purpose: ['Find institution employees by name/code, teaching type and employment state without changing their records.', 'প্রতিষ্ঠানের কর্মীদের নাম/কোড, শিক্ষাদান ধরন ও চাকরির অবস্থা দিয়ে খুঁজুন; এই পাতায় রেকর্ড পরিবর্তন হয় না।'],
            before: ['HR module access as TenantAdmin, Principal or HR; confirm the institution and the employee identity.', 'HR মডিউল ও TenantAdmin, Principal অথবা HR ভূমিকা প্রয়োজন; প্রতিষ্ঠান ও কর্মীর পরিচয় নিশ্চিত করুন।'],
            steps: [
                ['Enter an employee name or code, optionally choose Type and State, then select Search.', 'কর্মীর নাম বা কোড লিখুন, প্রয়োজনে Type ও State বেছে Search চাপুন।'],
                ['Review the Code, Employee, Designation, Department, Joined and Status columns.', 'Code, Employee, Designation, Department, Joined ও Status কলাম যাচাই করুন।'],
                ['Use Previous and Next to view more results; open Employee Registration & Status separately to perform authorized edits.', 'আরও ফলাফল দেখতে Previous ও Next চাপুন; অনুমোদিত পরিবর্তনের জন্য আলাদা Employee Registration & Status খুলুন।']
            ],
            example: ['Fictional example: search employee code EMP-102, filter Active, and confirm department and join date before starting a separate authorized update.', 'কাল্পনিক উদাহরণ: EMP-102 কোডে খুঁজে Active বাছুন, বিভাগ ও যোগদানের তারিখ যাচাই করে আলাদা অনুমোদিত সংশোধনে যান।'],
            result: ['The matching employee list appears in pages of up to 25 records; this view does not edit or approve records.', 'সর্বোচ্চ ২৫টি করে মিল পাওয়া কর্মীর তালিকা আসে; এখানে তথ্য পরিবর্তন বা অনুমোদন হয় না।'],
            rules: ['Names may be similar; use the employee code and institution scope to confirm identity. Employee registration is a separate workflow.', 'একই নামের কর্মী থাকতে পারে; পরিচয় নিশ্চিত করতে কর্মী কোড ও প্রতিষ্ঠান যাচাই করুন। কর্মী নিবন্ধন আলাদা কাজ।'],
            recovery: ['If no results appear, clear filters or check the correct employee code. If the list fails to load, refresh after verifying your session and permissions.', 'ফল না পেলে ফিল্টার পরিষ্কার করুন বা কোড যাচাই করুন। তালিকা না এলে সেশন ও অনুমতি যাচাই করে রিফ্রেশ করুন।'],
            related: [['/HrEmployeeRecords/Index', 'Employee registration', 'কর্মী নিবন্ধন'], ['/HR/Leaves', 'Leave review', 'ছুটির আবেদন যাচাই']]
        },
        '/hr/leaves/index': {
            version: 1, reviewed: true,
            title: ['Employee Leave Review', 'কর্মীর ছুটির আবেদন'],
            purpose: ['Review submitted employee leave requests and approve or reject with an auditable decision.', 'জমা দেওয়া কর্মীর ছুটির আবেদন যাচাই করে কারণসহ অনুমোদন বা প্রত্যাখ্যান করুন।'],
            before: ['HR module access as TenantAdmin, Principal or HR; confirm the employee, dates, leave type and available entitlement before deciding.', 'HR মডিউল ও TenantAdmin, Principal অথবা HR অনুমতি; সিদ্ধান্তের আগে কর্মী, তারিখ, ছুটির ধরন ও প্রাপ্যতা যাচাই করুন।'],
            steps: [
                ['Search by employee name/code, select Status and optional Starting from date, then Filter.', 'কর্মীর নাম/কোড, Status ও ঐচ্ছিক Starting from তারিখ দিয়ে Filter চাপুন।'],
                ['Read employee, leave type, date range, days and status in the paged register.', 'পৃষ্ঠাভিত্তিক তালিকায় কর্মী, ছুটির ধরন, তারিখ, দিন ও অবস্থা দেখুন।'],
                ['For a Submitted request, enter a review note (mandatory when rejecting), select Approve or Reject, and confirm the employee in the dialog.', 'Submitted আবেদনে প্রয়োজনীয় মন্তব্য দিন (প্রত্যাখ্যানে বাধ্যতামূলক), Approve অথবা Reject চাপুন এবং ডায়ালগে কর্মী নিশ্চিত করুন।'],
                ['Check the refreshed request and its new status before proceeding to another decision.', 'পরবর্তী সিদ্ধান্তের আগে রিফ্রেশ হওয়া আবেদন ও নতুন অবস্থা যাচাই করুন।']
            ],
            example: ['Fictional example: filter Submitted leave for EMP-102, verify a two-day request against entitlement, enter a decision note and approve it.', 'কাল্পনিক উদাহরণ: EMP-102-এর Submitted ছুটি খুঁজে দুই দিনের আবেদন ও প্রাপ্যতা যাচাই করে মন্তব্য দিয়ে অনুমোদন করুন।'],
            result: ['The request shows Approved or Rejected after a successful server-validated review; the history remains linked to the employee.', 'সার্ভারের যাচাই সফল হলে আবেদন Approved বা Rejected দেখায়; ইতিহাস কর্মীর সঙ্গে যুক্ত থাকে।'],
            rules: ['Only Submitted requests show decision controls. Rejections require a reason. A concurrent version change blocks stale decisions; do not retry blindly.', 'শুধু Submitted আবেদনে সিদ্ধান্তের বোতাম থাকে। প্রত্যাখ্যানে কারণ প্রয়োজন। একই সময়ে পরিবর্তিত আবেদনে পুরোনো সিদ্ধান্ত আটকায়; না দেখে পুনরায় চেষ্টা করবেন না।'],
            recovery: ['On an error, read the message and refresh the list; inspect the new status/version before trying again. Check leave entitlements separately.', 'ত্রুটি হলে বার্তা পড়ে তালিকা রিফ্রেশ করুন; আবার চেষ্টার আগে নতুন অবস্থা/সংস্করণ দেখুন। প্রাপ্যতা আলাদা পাতায় যাচাই করুন।'],
            related: [['/HR/Directory', 'Employee directory', 'কর্মী তালিকা'], ['/LeaveEntitlements/Index', 'Leave entitlements', 'ছুটির প্রাপ্যতা']]
        },
        '/feeoperations/structures/index': {
            version: 1, reviewed: true,
            title: ['Fee Structures', 'ফি কাঠামো'],
            purpose: ['Set reusable, date-effective fee heads for a campus, academic year and level, then review or amend eligible structures.', 'ক্যাম্পাস, শিক্ষাবর্ষ ও শ্রেণির জন্য কার্যকর তারিখসহ ফি খাত নির্ধারণ ও অনুমোদিত কাঠামো সংশোধন করুন।'],
            before: ['FINANCE access as TenantAdmin, Principal or Accountant; check campus, academic year, level, fee heads and effective dates. Structure amendments are TenantAdmin-controlled.', 'FINANCE অনুমতিসহ TenantAdmin, Principal বা Accountant ভূমিকা প্রয়োজন; ক্যাম্পাস, শিক্ষাবর্ষ, শ্রেণি, ফি খাত ও কার্যকর তারিখ যাচাই করুন। কাঠামো সংশোধন TenantAdmin নিয়ন্ত্রিত।'],
            steps: [
                ['Choose Campus, Academic year, Level, optional Batch scope and effective dates.', 'Campus, Academic year, Level, প্রয়োজনীয় Batch scope ও কার্যকর তারিখ বেছে নিন।'],
                ['Enter a Structure name, add fee heads with frequency, amount, due day and mandatory status, then Save fee structure.', 'Structure name লিখে frequency, amount, due day ও বাধ্যতামূলক অবস্থা অনুযায়ী ফি খাত যোগ করে Save fee structure চাপুন।'],
                ['TenantAdmin can filter the structure register, review historical entries, amend an unbilled version or close future monthly billing under the displayed safeguards.', 'TenantAdmin কাঠামোর তালিকা ফিল্টার করে আগের তথ্য দেখতে, বিল না-হওয়া সংস্করণ সংশোধন বা দেখানো নিয়ম মেনে ভবিষ্যৎ বিলিং বন্ধ করতে পারেন।']
            ],
            example: ['Fictional example: create a 2026 Class 9 tuition structure with a monthly BDT fee, review its effective date, then inspect it in the register.', 'কাল্পনিক উদাহরণ: ২০২৬ সালের নবম শ্রেণির জন্য মাসিক BDT টিউশন ফি কাঠামো তৈরি করে কার্যকর তারিখ ও তালিকা যাচাই করুন।'],
            result: ['The eligible structure can be selected for invoice generation; linked billed financial terms remain protected.', 'যোগ্য কাঠামো থেকে ইনভয়েস তৈরি করা যাবে; ইতোমধ্যে বিল হওয়া আর্থিক শর্ত সুরক্ষিত থাকবে।'],
            rules: ['Billed terms cannot be silently rewritten. Closing future billing preserves existing invoices, receipts and fee lines; reopening or extending closure is not supported here.', 'ইতোমধ্যে বিল হওয়া শর্ত নীরবে বদলানো যায় না। ভবিষ্যৎ বিলিং বন্ধ করলেও আগের ইনভয়েস, রসিদ ও ফি খাত থাকবে; এখানে বন্ধ অবস্থা পুনরায় খোলা বা বাড়ানো যায় না।'],
            recovery: ['If saving fails, check the selected academic scope, amounts and dates. For a stale amendment reload the register and review its current version before retrying.', 'সংরক্ষণ ব্যর্থ হলে শিক্ষাবর্ষের পরিসর, টাকা ও তারিখ দেখুন। পুরোনো সংস্করণ সংশোধনে সংঘাত হলে তালিকা রিফ্রেশ করে বর্তমান সংস্করণ যাচাই করুন।'],
            related: [['/FeeOperations/Billing', 'Generate monthly invoices', 'মাসিক ইনভয়েস তৈরি'], ['/FeeDiscountRules/Index', 'Discount rules', 'ছাড়ের নিয়ম']]
        },
        '/feeoperations/billing/index': {
            version: 1, reviewed: true,
            title: ['Generate Monthly Invoices', 'মাসিক ইনভয়েস তৈরি'],
            purpose: ['Preview and generate monthly student invoices for one eligible batch and fee structure.', 'উপযুক্ত ব্যাচ ও ফি কাঠামো থেকে মাসিক শিক্ষার্থী ইনভয়েসের প্রিভিউ ও তৈরি করুন।'],
            before: ['FINANCE access, an active academic batch and applicable structure. Confirm the month, year and due date before posting a request.', 'FINANCE অনুমতি, সক্রিয় একাডেমিক ব্যাচ ও প্রযোজ্য ফি কাঠামো প্রয়োজন। অনুরোধ দেওয়ার আগে মাস, বছর ও শেষ তারিখ নিশ্চিত করুন।'],
            steps: [
                ['Select Active batch and Applicable fee structure.', 'Active batch ও Applicable fee structure নির্বাচন করুন।'],
                ['Choose Month, Year and Due date, then Preview billing; inspect existing invoices, eligible students and BDT totals.', 'Month, Year ও Due date দিয়ে Preview billing চাপুন; আগের ইনভয়েস, যোগ্য শিক্ষার্থী ও BDT মোট দেখুন।'],
                ['Only after a current valid preview, select Generate invoices. Use Export preview CSV when available to review the displayed candidates.', 'বর্তমান বৈধ প্রিভিউ থাকলেই Generate invoices চাপুন। প্রযোজ্য হলে Export preview CSV দিয়ে দেখানো তালিকা যাচাই করুন।']
            ],
            example: ['Fictional example: preview November 2026 tuition for Grade 9 A; inspect any existing invoices before generating only eligible new ones.', 'কাল্পনিক উদাহরণ: Grade 9 A-এর নভেম্বর ২০২৬ টিউশন প্রিভিউ করুন; আগের ইনভয়েস যাচাই করে শুধু যোগ্য নতুনগুলো তৈরি করুন।'],
            result: ['New eligible invoices appear in the student ledger; existing invoices are not duplicated for the same request.', 'নতুন যোগ্য ইনভয়েস শিক্ষার্থী লেজারে আসে; একই অনুরোধে বিদ্যমান ইনভয়েস পুনরায় তৈরি হয় না।'],
            rules: ['Changing any billing input invalidates its earlier preview. Creating invoices does not itself receive cash or post the separate canonical GL journal.', 'বিলিংয়ের ইনপুট বদলালে আগের প্রিভিউ আর প্রযোজ্য নয়। ইনভয়েস তৈরির মাধ্যমে নগদ টাকা গ্রহণ বা আলাদা মূল GL জার্নাল পোস্ট হয় না।'],
            recovery: ['If Generate remains disabled, resolve missing scope or preview conflicts and preview again. After uncertain network failure inspect invoice coverage before retrying.', 'Generate নিষ্ক্রিয় থাকলে ব্যাচ/কাঠামো বা প্রিভিউ সমস্যা ঠিক করে আবার প্রিভিউ করুন। নেটওয়ার্কে ফল অনিশ্চিত হলে পুনরায় চেষ্টার আগে ইনভয়েস কভারেজ দেখুন।'],
            related: [['/FeeOperations/Structures', 'Fee structures', 'ফি কাঠামো'], ['/FeeInvoiceCoverage/Index', 'Invoice coverage', 'ইনভয়েস কভারেজ']]
        },
        '/feeoperations/ledger/index': {
            version: 1, reviewed: true,
            title: ['Student Ledger & Cash Collection', 'শিক্ষার্থী লেজার ও নগদ সংগ্রহ'],
            purpose: ['Inspect a student\'s invoices and receipt history, then collect an authorized cash receipt against a selected invoice.', 'শিক্ষার্থীর ইনভয়েস ও রসিদের ইতিহাস দেখুন; নির্বাচিত ইনভয়েসের বিপরীতে অনুমোদিত নগদ রসিদ নথিভুক্ত করুন।'],
            before: ['FINANCE access and the correct student; confirm the current invoice balance, receipt date and amount.', 'FINANCE অনুমতি ও সঠিক শিক্ষার্থী নির্বাচন প্রয়োজন; বর্তমান ইনভয়েস বকেয়া, রসিদের তারিখ ও টাকা যাচাই করুন।'],
            steps: [
                ['Search student by name/code, select the correct student and choose View ledger.', 'নাম/কোড দিয়ে শিক্ষার্থী খুঁজে সঠিকজন নির্বাচন করে View ledger চাপুন।'],
                ['Review paged invoices, outstanding amounts and receipt history; select the intended invoice.', 'পৃষ্ঠাভিত্তিক ইনভয়েস, বকেয়া ও রসিদের ইতিহাস দেখে নির্দিষ্ট ইনভয়েস নির্বাচন করুন।'],
                ['Enter payment date and amount, review the available preflight and select Record cash receipt only once; verify the new receipt in history.', 'টাকার তারিখ ও পরিমাণ দিয়ে প্রিফ্লাইট যাচাই করে Record cash receipt একবার চাপুন; ইতিহাসে নতুন রসিদ দেখুন।']
            ],
            example: ['Fictional example: find ST-1042, inspect its November invoice balance and record a verified BDT 1,000 cash receipt against that invoice.', 'কাল্পনিক উদাহরণ: ST-1042 খুঁজে নভেম্বরের ইনভয়েস বকেয়া দেখুন এবং সেই ইনভয়েসে যাচাইকৃত BDT ১,০০০ নগদ রসিদ নথিভুক্ত করুন।'],
            result: ['The accepted receipt appears in history and its allocation affects the outstanding balance. GL posting remains a separate workflow.', 'গৃহীত রসিদ ইতিহাসে আসে ও বরাদ্দ অনুযায়ী বকেয়া বদলায়। GL পোস্টিং আলাদা কাজ।'],
            rules: ['Never record a receipt against the wrong student/invoice or assume a received cash amount has automatically become a posted GL journal. Posted financial corrections require authorized adjustment.', 'ভুল শিক্ষার্থী/ইনভয়েসে রসিদ দেবেন না এবং নগদ সংগ্রহকে স্বয়ংক্রিয় GL পোস্টিং ভাববেন না। পোস্ট করা হিসাব সংশোধনে অনুমোদিত সমন্বয় প্রয়োজন।'],
            recovery: ['If a receipt response is uncertain, reload ledger/history and use the preflight\'s retry identifier; do not create another receipt to bypass a conflict.', 'রসিদের ফল অনিশ্চিত হলে লেজার/ইতিহাস রিফ্রেশ করে প্রিফ্লাইটের retry identifier ব্যবহার করুন; সংঘাত এড়াতে দ্বিতীয় রসিদ তৈরি করবেন না।'],
            related: [['/StudentPaymentPosting/Index', 'Cash receipt GL posting', 'নগদ রসিদ GL পোস্টিং'], ['/FeeReceivablesAging/Index', 'Fee aging report', 'ফি বকেয়া প্রতিবেদন']]
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
            recovery: ['If a campus/space is missing, verify permitted active setup. A 403 means insufficient role or campus access; 409 means reload the latest row or resolve a technician schedule overlap. If a submission outcome is uncertain, retry unchanged details; editing them is blocked until you reload and verify history. After a confirmed input-validation rejection, correct the fields and resubmit. Do not create a second request to bypass an uncertain retry.', 'ক্যাম্পাস বা স্থান না পেলে অনুমোদিত সক্রিয় সেটআপ যাচাই করুন। ৪০৩ মানে ভূমিকা বা ক্যাম্পাস অনুমতি নেই; ৪০৯ হলে নতুন তথ্য লোড করুন বা টেকনিশিয়ানের সময়ের সংঘাত ঠিক করুন। জমার ফল অনিশ্চিত হলে একই তথ্য দিয়ে পুনরায় চেষ্টা করুন; তথ্য বদলাতে হলে আগে পুনরায় লোড করে ইতিহাস যাচাই করতে হবে। ইনপুট যাচাইয়ে নিশ্চিতভাবে বাতিল হলে তথ্য ঠিক করে আবার জমা দিতে পারবেন। অনিশ্চিত পুনরায় চেষ্টায় দ্বিতীয় অনুরোধ তৈরি করবেন না।'],
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
