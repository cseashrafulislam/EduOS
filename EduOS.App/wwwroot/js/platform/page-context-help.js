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
        '/assessmentadministration/examreports/index': {
            version: 1, reviewed: true,
            title: ['Examination & Result Reports', 'পরীক্ষা ও ফলাফলের রিপোর্ট'],
            purpose: ['Search version-pinned examination reports, analyze students and subjects, and export or print an authorized page.', 'সংস্করণ-নির্ভর ফলাফলের রিপোর্টে শিক্ষার্থী ও বিষয় বিশ্লেষণ করুন এবং অনুমোদিত পৃষ্ঠা এক্সপোর্ট বা প্রিন্ট করুন।'],
            before: ['EXAM module and TenantAdmin, Principal, VicePrincipal or ExamController permission. Results must have a finalized publication; subject reports need immutable course evidence.', 'EXAM মডিউল ও TenantAdmin, Principal, VicePrincipal বা ExamController অনুমতি দরকার। ফলাফলের চূড়ান্ত প্রকাশিত সংস্করণ থাকতে হবে; বিষয়ভিত্তিক রিপোর্টে অপরিবর্তনীয় কোর্স তথ্য প্রয়োজন।'],
            steps: [
                ['Find the assessment/batch, select a finalized publication version and choose one of the report types.', 'মূল্যায়ন/ব্যাচ খুঁজে প্রকাশিত সংস্করণ ও রিপোর্টের ধরন বেছে নিন।'],
                ['For a subject-specific list, use Find subject and select a subject. Optionally filter by roll, code or name.', 'বিষয়ভিত্তিক তালিকায় Find subject দিয়ে বিষয় নির্বাচন করুন। চাইলে রোল, কোড বা নাম দিয়ে ফিল্টার করুন।'],
                ['Select View report. Use Previous/Next for database-backed paging; Page CSV and Print page cover only the visible page.', 'View report চাপুন। Previous/Next দিয়ে পৃষ্ঠা বদলান; Page CSV ও Print page শুধু দৃশ্যমান পৃষ্ঠা নেয়।'],
                ['For official Academic Transcript, open the separate Transcript Issue Register instead of treating this preview as an issued document.', 'আনুষ্ঠানিক Academic Transcript-এর জন্য আলাদা Transcript Issue Register খুলুন; এই রিপোর্টকে ইস্যুকৃত দলিল ভাববেন না।']
            ],
            example: ['Fictional example: choose Grade 9 Final, 2026 / Section A, published Version 2; choose Subject Wise Fail List, select Mathematics and export page 1.', 'কাল্পনিক উদাহরণ: নবম শ্রেণির Final 2026 / Section A, প্রকাশিত Version 2 বেছে গণিতের Subject Wise Fail List-এর প্রথম পৃষ্ঠা CSV করুন।'],
            result: ['The selected authorized published snapshot is shown with version, matching-row count and report-specific columns.', 'অনুমোদিত প্রকাশিত সংস্করণের ফলাফল, সংস্করণ নম্বর, মোট মিল পাওয়া সারি ও প্রয়োজনীয় কলাম দেখা যায়।'],
            rules: ['A withdrawn version is historical only. Counselling lists are referral candidates—not counselling attendance. A practical-only failure cannot be claimed until immutable component-level evidence exists. Old course names use the current authorized catalogue; historical marks and grades come from frozen publication snapshots.', 'প্রত্যাহারকৃত সংস্করণ শুধু ইতিহাস। কাউন্সেলিং তালিকা সম্ভাব্য শিক্ষার্থীর, সম্পন্ন কাউন্সেলিং নয়। অপরিবর্তনীয় ব্যবহারিক উপাদানের তথ্য ছাড়া ব্যবহারিক অংশে অনুত্তীর্ণ বলা যায় না। পুরোনো কোর্সের নাম বর্তমান অনুমোদিত ক্যাটালগ থেকে, নম্বর/গ্রেড সংরক্ষিত সংস্করণ থেকে আসে।'],
            recovery: ['If no report appears, confirm the finalized version, chosen subject and campus access. If a snapshot integrity error appears, ask the exam controller to reconcile it; do not rebuild historic marks from editable registers.', 'রিপোর্ট না এলে প্রকাশিত সংস্করণ, বিষয় ও ক্যাম্পাস অনুমতি দেখুন। স্ন্যাপশট ত্রুটিতে পরীক্ষা নিয়ন্ত্রককে যাচাই করতে বলুন; সম্পাদনযোগ্য নম্বর থেকে পুরোনো ফল বানাবেন না।'],
            related: [['/AssessmentAdministration/Publications', 'Publication history', 'প্রকাশনার ইতিহাস'], ['/AssessmentAdministration/Transcripts', 'Transcript issue register', 'ট্রান্সক্রিপ্ট ইস্যুর তালিকা']]
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
    const administrationManuals = {
        '/tenantusers/index': taskGuide(
            ['User and Role Management', 'ব্যবহারকারী ও ভূমিকা ব্যবস্থাপনা'],
            ['Invite institution members and manage their memberships, campus access, roles and permissions.', 'প্রতিষ্ঠানের সদস্য আমন্ত্রণ ও তাঁদের সদস্যপদ, ক্যাম্পাস, ভূমিকা ও অনুমতি পরিচালনা করুন।'],
            ['TenantAdmin access; verify the intended institution, member identity and the least-privilege role before granting access.', 'TenantAdmin অনুমতি প্রয়োজন; প্রবেশাধিকার দেওয়ার আগে সঠিক প্রতিষ্ঠান, সদস্য ও ন্যূনতম প্রয়োজনীয় ভূমিকা যাচাই করুন।'],
            [['In Institution Users, invite by email and optional role/campus, or search for an existing member.', 'Institution Users-এ ইমেইল, ঐচ্ছিক ভূমিকা ও ক্যাম্পাস দিয়ে আমন্ত্রণ জানান অথবা সদস্য খুঁজুন।'], ['Select a member, review roles, campus and status, then save only the intended changes.', 'সদস্য বেছে ভূমিকা, ক্যাম্পাস ও অবস্থা যাচাই করে শুধু প্রয়োজনীয় পরিবর্তন সংরক্ষণ করুন।'], ['Use Roles & Permissions to create or select a role, assign permitted permissions and check access history.', 'Roles & Permissions-এ ভূমিকা তৈরি বা বেছে অনুমতি নির্ধারণ করুন এবং প্রবেশাধিকার পরিবর্তনের ইতিহাস দেখুন।']],
            ['Example: invite a new teacher with the correct campus, then review assigned permissions before their first sign-in.', 'উদাহরণ: নতুন শিক্ষককে সঠিক ক্যাম্পাসে আমন্ত্রণ দিয়ে প্রথম লগইনের আগে তাঁর অনুমতি যাচাই করুন।'],
            ['Authorized membership or role changes appear in the relevant register and audit history.', 'অনুমোদিত সদস্যপদ বা ভূমিকা পরিবর্তন সংশ্লিষ্ট তালিকা ও নিরীক্ষা ইতিহাসে দেখা যায়।'],
            ['A role is not a bypass for server authorization. Removing existing global grants requires the explicit review shown on this page.', 'ভূমিকা সার্ভারের অনুমতি যাচাই এড়িয়ে যায় না। আগের বৈশ্বিক অনুমতি সরাতে পৃষ্ঠার স্পষ্ট পর্যালোচনা প্রয়োজন।'],
            ['If a member is missing, search by a shorter prefix and confirm the institution. On stale changes, reload the member before saving again.', 'সদস্য না পেলে সংক্ষিপ্ত অংশ দিয়ে খুঁজুন ও প্রতিষ্ঠান যাচাই করুন। পুরোনো তথ্যের সংঘাতে আবার সংরক্ষণের আগে সদস্য রিলোড করুন।'],
            [['/TenantPermissionOverrides/Index', 'Permission exceptions', 'বিশেষ অনুমতি']]),
        '/tenantpermissionoverrides/index': taskGuide(
            ['Member Permission Exceptions', 'সদস্যের বিশেষ অনুমতি'],
            ['Inspect and grant or deny a specific institution member permission with an optional expiry.', 'প্রতিষ্ঠানের নির্দিষ্ট সদস্যের অনুমতি যাচাই করে ঐচ্ছিক মেয়াদসহ মঞ্জুর বা প্রত্যাখ্যান করুন।'],
            ['TenantAdmin access and a verified active institution member; use exceptions only when the normal role is insufficient.', 'TenantAdmin অনুমতি ও যাচাইকৃত সক্রিয় সদস্য প্রয়োজন; সাধারণ ভূমিকা যথেষ্ট না হলেই বিশেষ অনুমতি ব্যবহার করুন।'],
            [['Search for the member and select the correct active identity.', 'সদস্য খুঁজে সঠিক সক্রিয় পরিচয় নির্বাচন করুন।'], ['Inspect current permission exceptions, then select a permission, access mode and optional expiry/reason.', 'বর্তমান বিশেষ অনুমতি দেখে অনুমতি, প্রবেশাধিকারের ধরন এবং ঐচ্ছিক মেয়াদ/কারণ বাছুন।'], ['Save exception and reload the selected member to confirm the effective record.', 'Save exception দিয়ে সংরক্ষণ করে সদস্য রিলোড করে কার্যকর রেকর্ড যাচাই করুন।']],
            ['Example: grant an approved time-limited permission to one active employee, not every employee in their role.', 'উদাহরণ: পুরো ভূমিকার বদলে একজন সক্রিয় কর্মীকে অনুমোদিত সীমিত-মেয়াদের অনুমতি দিন।'],
            ['The exception appears against the selected member; server authorization remains the final enforcement.', 'বিশেষ অনুমতি নির্বাচিত সদস্যের অধীনে দেখা যায়; চূড়ান্ত অনুমতি সার্ভারই যাচাই করে।'],
            ['Do not use a blanket grant or indefinite exception when a narrow, expiring permission is sufficient.', 'সীমিত মেয়াদের নির্দিষ্ট অনুমতিই যথেষ্ট হলে সবার জন্য বা অনির্দিষ্ট বিশেষ অনুমতি দেবেন না।'],
            ['If an exception is rejected, verify member status, code and expiry; reload after a version conflict.', 'অনুমতি প্রত্যাখ্যাত হলে সদস্যের অবস্থা, কোড ও মেয়াদ দেখুন; সংস্করণ-সংঘাতে রিলোড করুন।'],
            [['/TenantUsers/Index', 'User and roles', 'ব্যবহারকারী ও ভূমিকা']]),
        '/tenantmodules/index': taskGuide(
            ['Institution Modules', 'প্রতিষ্ঠানের মডিউল'],
            ['Review and select modules available under the current institution subscription.', 'প্রতিষ্ঠানের বর্তমান সাবস্ক্রিপশনে উপলব্ধ মডিউল দেখুন ও নির্বাচন করুন।'],
            ['TenantAdmin access and an active institution context; plan entitlements determine available modules.', 'TenantAdmin অনুমতি ও সক্রিয় প্রতিষ্ঠান প্রয়োজন; প্ল্যান অনুযায়ী উপলব্ধ মডিউল নির্ধারিত হয়।'],
            [['Open the module list and review required, included and optional modules.', 'মডিউলের তালিকা খুলে আবশ্যিক, অন্তর্ভুক্ত ও ঐচ্ছিক মডিউল দেখুন।'], ['Select or deselect only the allowed optional modules.', 'শুধু অনুমোদিত ঐচ্ছিক মডিউল নির্বাচন বা অনির্বাচন করুন।'], ['Check the resulting module status; open Subscription & Billing if a plan change is required.', 'মডিউলের নতুন অবস্থা দেখুন; প্ল্যান বদলানো দরকার হলে Subscription & Billing খুলুন।']],
            ['Example: enable a plan-included optional learning module while keeping required academic modules active.', 'উদাহরণ: আবশ্যিক একাডেমিক মডিউল চালু রেখে প্ল্যানে থাকা ঐচ্ছিক লার্নিং মডিউল চালু করুন।'],
            ['Available modules are updated within the institution scope after server validation.', 'সার্ভার যাচাইয়ের পরে প্রতিষ্ঠানের অনুমোদিত মডিউলগুলো আপডেট হয়।'],
            ['Required modules cannot be disabled and unpurchased modules cannot be unlocked from this page.', 'আবশ্যিক মডিউল বন্ধ বা কেনা হয়নি এমন মডিউল এই পৃষ্ঠা থেকে চালু করা যায় না।'],
            ['If a module is unavailable, check plan inclusion and the displayed server response; retry after refreshing plan status.', 'মডিউল না পেলে প্ল্যানের অন্তর্ভুক্তি ও সার্ভারের বার্তা দেখুন; অবস্থা রিফ্রেশ করে চেষ্টা করুন।'],
            [['/SubscriptionBilling/Index', 'Subscription and billing', 'সাবস্ক্রিপশন ও বিলিং']]),
        '/numberseries/index': taskGuide(
            ['Number Series', 'নম্বর সিরিজ'],
            ['Configure institution-specific prefixes, suffixes and numeric length for server-issued business identifiers.', 'সার্ভারে তৈরি ব্যবসায়িক নম্বরের জন্য প্রতিষ্ঠানভিত্তিক উপসর্গ, প্রত্যয় ও দৈর্ঘ্য নির্ধারণ করুন।'],
            ['TenantAdmin access; know the permitted series key and format required by the business workflow.', 'TenantAdmin অনুমতি; সংশ্লিষ্ট কাজের বৈধ সিরিজ কী ও ফরম্যাট জানা প্রয়োজন।'],
            [['Enter a unique Series key, optional Prefix and Suffix, and Padding from 1 to 30.', 'অনন্য সিরিজ কী, ঐচ্ছিক উপসর্গ ও প্রত্যয় এবং ১–৩০ দৈর্ঘ্য লিখুন।'], ['Choose Active or Inactive and select Save series.', 'সক্রিয় বা নিষ্ক্রিয় বেছে Save series চাপুন।'], ['Use search and the paged Configured series register to find a record; select Edit to change permitted formatting.', 'খোঁজ ও পৃষ্ঠাভিত্তিক তালিকায় সিরিজ দেখে Edit দিয়ে অনুমোদিত ফরম্যাট পরিবর্তন করুন।']],
            ['Example: save an active invoice number format using prefix INV-, six digits and no suffix.', 'উদাহরণ: INV- উপসর্গ, ছয় অঙ্ক ও প্রত্যয় ছাড়া সক্রিয় ইনভয়েস সিরিজ সংরক্ষণ করুন।'],
            ['The saved configuration appears in the register and can be consumed by authorized server workflows.', 'সংরক্ষিত ফরম্যাট তালিকায় আসে এবং অনুমোদিত সার্ভার কার্যক্রমে ব্যবহার হয়।'],
            ['This page never issues or resets live counters. Series key is immutable on edit; the server enforces length and version rules.', 'এই পৃষ্ঠা থেকে লাইভ নম্বর তৈরি বা কাউন্টার রিসেট হয় না। সম্পাদনায় সিরিজ কী বদলায় না; দৈর্ঘ্য ও সংস্করণ সার্ভার যাচাই করে।'],
            ['If the key or length is invalid, correct it before saving; on a stale edit, reload the current series.', 'কী বা দৈর্ঘ্য ভুল হলে ঠিক করুন; পুরোনো তথ্যের সম্পাদনায় বর্তমান সিরিজ রিলোড করুন।'],
            [['/TenantSettings/Index', 'Institution settings', 'প্রতিষ্ঠানের সেটিংস']]),
        '/tenantdomains/index': taskGuide(
            ['Custom Domains', 'নিজস্ব ডোমেইন'],
            ['Register and verify the institution-owned portal hostname and choose the primary domain.', 'প্রতিষ্ঠানের নিজস্ব পোর্টাল হোস্টনেম নিবন্ধন ও যাচাই করে প্রধান ডোমেইন নির্ধারণ করুন।'],
            ['TenantAdmin access, control of the hostname DNS and a configured hosting environment.', 'TenantAdmin অনুমতি, ডোমেইনের DNS নিয়ন্ত্রণ ও প্রস্তুত হোস্টিং ব্যবস্থা প্রয়োজন।'],
            [['Enter the Custom hostname and register it; immediately copy the one-time DNS TXT challenge name and value.', 'নিজস্ব হোস্টনেম দিয়ে নিবন্ধন করুন; একবার দেখানো DNS TXT যাচাইয়ের নাম ও মান সঙ্গে সঙ্গে কপি করুন।'], ['Publish the TXT record in DNS, wait for propagation, then select Verify in the domain row.', 'DNS-এ TXT রেকর্ড প্রকাশ করে কার্যকর হওয়া পর্যন্ত অপেক্ষা করুন, তারপর সারির Verify চাপুন।'], ['After verification, select Make primary if intended; use Deactivate only for a domain no longer in use.', 'যাচাইয়ের পরে প্রয়োজন হলে Make primary চাপুন; ব্যবহার বন্ধ ডোমেইনই Deactivate করুন।']],
            ['Example: register portal.example.edu, publish the shown TXT record and verify it before marking it primary.', 'উদাহরণ: portal.example.edu নিবন্ধন করে দেখানো TXT রেকর্ড প্রকাশ ও যাচাইয়ের পরে প্রধান করুন।'],
            ['The verified hostname and primary state appear in the institution domain register.', 'যাচাইকৃত হোস্টনেম ও প্রধান অবস্থা প্রতিষ্ঠানের ডোমেইন তালিকায় দেখা যায়।'],
            ['TXT ownership verification does not configure TLS, reverse proxy, DNS A or CNAME records. Keep the proof published while active.', 'TXT মালিকানা যাচাইয়ের মাধ্যমে TLS, রিভার্স প্রক্সি বা DNS A/CNAME কনফিগার হয় না। সক্রিয় অবস্থায় রেকর্ড রাখুন।'],
            ['If verification fails, check the exact TXT host/value and DNS propagation. Recheck the current row version before retrying.', 'যাচাই ব্যর্থ হলে সঠিক TXT নাম/মান ও DNS প্রচার দেখুন। আবার চেষ্টা করার আগে সারির বর্তমান সংস্করণ যাচাই করুন।'],
            [['/InstitutionManagement/Subdomain', 'Portal subdomain', 'পোর্টাল সাবডোমেইন']]),
        '/tenantterminology/index': taskGuide(
            ['Institution Terminology', 'প্রতিষ্ঠানের পরিভাষা'],
            ['Override selected institution-specific singular/plural labels in English and Bangla.', 'প্রতিষ্ঠানের নির্দিষ্ট একবচন/বহুবচন নাম ইংরেজি ও বাংলায় নির্ধারণ করুন।'],
            ['TenantAdmin access and a stable terminology key; this does not change underlying database entity types.', 'TenantAdmin অনুমতি ও স্থায়ী পরিভাষার কী প্রয়োজন; এতে ডেটাবেসের সত্তার ধরন বদলায় না।'],
            [['Search the directory by key or label and choose Edit, or prepare a new terminology key.', 'কী বা নাম দিয়ে তালিকায় খুঁজে Edit বাছুন, অথবা নতুন পরিভাষার কী লিখুন।'], ['Fill the required English singular/plural labels and optional Bangla equivalents.', 'আবশ্যিক ইংরেজি একবচন/বহুবচন ও ঐচ্ছিক বাংলা সমতুল্য লিখুন।'], ['Save terminology and inspect the result; use Reset override only when returning to the default is intended.', 'Save terminology দিয়ে সংরক্ষণ করে ফল দেখুন; ডিফল্টে ফিরতে চাইলেই Reset override চাপুন।']],
            ['Example: supply institution-approved English and Bangla labels for a learner type without changing any enrollment.', 'উদাহরণ: ভর্তি তথ্য না বদলে শিক্ষার্থীর ধরনটির অনুমোদিত ইংরেজি ও বাংলা নাম দিন।'],
            ['The override appears in the directory and is available to supported terminology-aware screens.', 'পরিভাষার পরিবর্তন তালিকায় দেখা যায় এবং সমর্থিত পৃষ্ঠাগুলোতে ব্যবহারযোগ্য হয়।'],
            ['An existing terminology key is read-only during editing; a reset removes only the override, not business records.', 'সম্পাদনায় আগের কী অপরিবর্তনীয়; রিসেটে শুধু পরিবর্তিত নাম সরবে, ব্যবসায়িক রেকর্ড নয়।'],
            ['If validation fails, check unique key, required labels and character limits; reopen the latest record after a conflict.', 'যাচাই ব্যর্থ হলে অনন্য কী, আবশ্যিক নাম ও অক্ষরসীমা দেখুন; সংঘাতে নতুন রেকর্ড খুলুন।'],
            [['/TenantSettings/Index', 'Institution settings', 'প্রতিষ্ঠানের সেটিংস']]),
        '/tenantsettings/index': taskGuide(
            ['Institution Settings', 'প্রতিষ্ঠানের সেটিংস'],
            ['Search, create and edit versioned institution settings with protected-value handling.', 'সুরক্ষিত মানের নিয়ম মেনে প্রতিষ্ঠানের সংস্করণযুক্ত সেটিং খুঁজুন, তৈরি ও সম্পাদনা করুন।'],
            ['TenantAdmin access; confirm the institution and the setting key/category before changing a configuration.', 'TenantAdmin অনুমতি; সেটিং পরিবর্তনের আগে প্রতিষ্ঠান এবং কী/বিভাগ যাচাই করুন।'],
            [['Filter the settings directory by exact Category or select Edit beside an existing setting.', 'সঠিক Category দিয়ে তালিকা ছাঁকুন অথবা বিদ্যমান সেটিংয়ের Edit চাপুন।'], ['For a new setting, supply Key, Category, Value and appropriate Sensitive protection; for an existing setting edit only permitted fields.', 'নতুন সেটিংয়ে কী, বিভাগ, মান ও প্রয়োজনীয় সুরক্ষা দিন; পুরোনো সেটিংয়ে শুধু অনুমোদিত তথ্য বদলান।'], ['Select Save setting, inspect the result and use Clear form to start a separate record.', 'Save setting দিয়ে ফল দেখুন; আলাদা রেকর্ডের জন্য Clear form ব্যবহার করুন।']],
            ['Example: add an approved, non-sensitive campus display preference in its designated category.', 'উদাহরণ: নির্ধারিত বিভাগে অনুমোদিত অসংবেদনশীল ক্যাম্পাস প্রদর্শন পছন্দ সংরক্ষণ করুন।'],
            ['The saved setting appears in the institution-scoped directory after server validation.', 'সার্ভার যাচাইয়ের পরে সেটিংটি প্রতিষ্ঠানভিত্তিক তালিকায় দেখা যায়।'],
            ['Protected values may remain masked or locked on edit. Do not copy secrets into public settings or reuse another institution key.', 'সুরক্ষিত মান সম্পাদনায় আড়াল বা লক থাকতে পারে। গোপন তথ্য প্রকাশ্য সেটিংয়ে বা অন্য প্রতিষ্ঠানের কী-তে দেবেন না।'],
            ['If saving fails, inspect category, key and allowed value; reload a record after a version conflict.', 'সংরক্ষণ ব্যর্থ হলে বিভাগ, কী ও বৈধ মান দেখুন; সংস্করণ-সংঘাতে রিলোড করুন।'],
            [['/TenantTerminology/Index', 'Institution terminology', 'প্রতিষ্ঠানের পরিভাষা']]),
        '/subscriptionbilling/index': taskGuide(
            ['Subscription and Billing', 'সাবস্ক্রিপশন ও বিলিং'],
            ['Review the active plan, subscription invoices, payments, expiry and authorized renewal options.', 'সক্রিয় প্ল্যান, ইনভয়েস, পেমেন্ট, মেয়াদ ও অনুমোদিত নবায়নের বিকল্প দেখুন।'],
            ['TenantAdmin access and the correct institution context; payment verification is separate from invoice creation.', 'TenantAdmin অনুমতি ও সঠিক প্রতিষ্ঠান প্রয়োজন; ইনভয়েস তৈরি ও পেমেন্ট যাচাই আলাদা কাজ।'],
            [['Refresh the current subscription, expiry and renewal state; inspect the active plan and auto-renew preference.', 'বর্তমান সাবস্ক্রিপশন ও নবায়ন রিফ্রেশ করে প্ল্যান এবং স্বয়ংক্রিয় নবায়নের পছন্দ দেখুন।'], ['Filter invoices by status; inspect Details, Payments, CSV export or a verified payment receipt as needed.', 'অবস্থা অনুযায়ী ইনভয়েস ছেঁকে Details, Payments, CSV বা যাচাইকৃত পেমেন্টের রসিদ দেখুন।'], ['In the eligible final 30 days, choose a next-period plan and generate its renewal invoice; pay and await separate verification.', 'যোগ্য শেষ ৩০ দিনে পরবর্তী প্ল্যান বেছে নবায়নের ইনভয়েস তৈরি করুন; টাকা পরিশোধ করে আলাদা যাচাইয়ের অপেক্ষা করুন।']],
            ['Example: compare outstanding renewal invoices, select a permitted next-period plan and track its verified payment.', 'উদাহরণ: বকেয়া নবায়ন ইনভয়েস দেখে অনুমোদিত পরবর্তী প্ল্যান বাছুন ও যাচাইকৃত পেমেন্ট অনুসরণ করুন।'],
            ['The page shows the current term, renewal requests, invoices and read-only payment history.', 'পৃষ্ঠায় বর্তমান মেয়াদ, নবায়নের অনুরোধ, ইনভয়েস ও শুধু-পাঠযোগ্য পেমেন্ট ইতিহাস দেখা যায়।'],
            ['Ending a subscription immediately removes access and does not create an automatic refund. Pending/rejected payments have no valid receipt.', 'এখনই সাবস্ক্রিপশন শেষ করলে প্রবেশাধিকার বন্ধ হয়, স্বয়ংক্রিয় রিফান্ড হয় না। অপেক্ষমাণ/প্রত্যাখ্যাত পেমেন্টের বৈধ রসিদ নেই।'],
            ['On an uncertain renewal response, refresh invoice and subscription history before retrying; a stale state requires reloading.', 'নবায়নের ফল অনিশ্চিত হলে আবার করার আগে ইনভয়েস ও সাবস্ক্রিপশন ইতিহাস দেখুন; পুরোনো অবস্থায় রিলোড করুন।'],
            [['/TenantModules/Index', 'Institution modules', 'প্রতিষ্ঠানের মডিউল']]),
        '/integrationadministration/index': taskGuide(
            ['Integration Administration', 'ইন্টিগ্রেশন পরিচালনা'],
            ['Register integration webhooks, inspect delivery history and issue scoped API credentials.', 'ইন্টিগ্রেশন ওয়েবহুক নিবন্ধন, পাঠানোর ইতিহাস পরীক্ষা এবং সীমিত API ক্রেডেনশিয়াল তৈরি করুন।'],
            ['TenantAdmin access; only register trusted endpoints and prepare a safe storage location for any one-time credential.', 'TenantAdmin অনুমতি; বিশ্বস্ত endpoint দিন এবং একবার দেখানো ক্রেডেনশিয়াল নিরাপদে রাখার ব্যবস্থা করুন।'],
            [['Review the paged Webhook endpoints and choose Add / edit webhook to configure permitted delivery settings.', 'ওয়েবহুকের তালিকা দেখে Add / edit webhook থেকে অনুমোদিত ডেলিভারি সেটিং দিন।'], ['Save webhook, inspect its Delivery history and download scoped CSV if required.', 'Save webhook দিয়ে ডেলিভারি ইতিহাস দেখুন; দরকারে অনুমোদিত CSV ডাউনলোড করুন।'], ['Under API credentials, choose permitted scopes, generate the credential and immediately store the displayed key securely.', 'API credentials-এ অনুমোদিত scope বেছে ক্রেডেনশিয়াল তৈরি করে দেখানো কী সঙ্গে সঙ্গে নিরাপদে রাখুন।']],
            ['Example: register an approved HTTPS webhook and inspect delivery records after a permitted test event.', 'উদাহরণ: অনুমোদিত HTTPS ওয়েবহুক নিবন্ধন করে বৈধ পরীক্ষার ঘটনার ডেলিভারি রেকর্ড দেখুন।'],
            ['The saved endpoint or issued credential appears in the institution integration register.', 'সংরক্ষিত endpoint বা তৈরি ক্রেডেনশিয়াল প্রতিষ্ঠানের ইন্টিগ্রেশন তালিকায় দেখা যায়।'],
            ['Credentials are sensitive; never paste a real key into the placeholder example or share delivery payloads outside the authorized scope.', 'ক্রেডেনশিয়াল গোপনীয়; উদাহরণে আসল কী বসাবেন না বা অনুমতির বাইরে ডেলিভারির তথ্য দেবেন না।'],
            ['For failed delivery inspect the endpoint and delivery record; for a lost one-time secret, use the supported revoke/rotate process.', 'ডেলিভারি ব্যর্থ হলে endpoint ও ইতিহাস দেখুন; একবারের গোপন কী হারালে অনুমোদিত বাতিল/পরিবর্তন প্রক্রিয়া ব্যবহার করুন।'],
            [['/TenantSettings/Index', 'Institution settings', 'প্রতিষ্ঠানের সেটিংস']]),
        '/studentdocuments/index': taskGuide(
            ['Student Documents', 'শিক্ষার্থীর নথি'],
            ['Find a student, attach an authorized file and inspect that student’s document history.', 'শিক্ষার্থী খুঁজে অনুমোদিত ফাইল যুক্ত করুন এবং ওই শিক্ষার্থীর নথির ইতিহাস দেখুন।'],
            ['TenantAdmin and STUDENT module access; select the correct institution student and an allowed document file.', 'TenantAdmin ও STUDENT মডিউল অনুমতি; সঠিক প্রতিষ্ঠানের শিক্ষার্থী এবং অনুমোদিত ফাইল প্রয়োজন।'],
            [['Search by student name or code and select the verified student.', 'নাম বা কোড দিয়ে খুঁজে যাচাইকৃত শিক্ষার্থী বাছুন।'], ['Choose the document category and permitted file, then select Upload & attach.', 'নথির বিভাগ ও অনুমোদিত ফাইল বেছে Upload & attach চাপুন।'], ['Refresh Student document history and verify the new attachment is listed.', 'Student document history রিফ্রেশ করে নতুন নথিটি তালিকায় আছে কি না দেখুন।']],
            ['Example: upload an authorized admission document to the selected student and check its history.', 'উদাহরণ: নির্বাচিত শিক্ষার্থীর অনুমোদিত ভর্তির নথি যুক্ত করে ইতিহাসে যাচাই করুন।'],
            ['The attachment is recorded against the selected student and displayed in their document history.', 'নথিটি নির্বাচিত শিক্ষার্থীর নামে সংরক্ষিত হয়ে তাঁর ইতিহাসে দেখা যায়।'],
            ['Document access is student-scoped. Do not attach another student’s private file or bypass upload size/type validation.', 'নথি শিক্ষার্থীসীমায় সুরক্ষিত। অন্য শিক্ষার্থীর ব্যক্তিগত ফাইল দেবেন না বা আকার/ধরনের যাচাই এড়াবেন না।'],
            ['If upload fails, verify student, category, file type/size and the displayed error; inspect history before repeating.', 'আপলোড ব্যর্থ হলে শিক্ষার্থী, বিভাগ, ফাইলের ধরন/আকার ও ত্রুটি দেখুন; আবার দেওয়ার আগে ইতিহাস যাচাই করুন।'],
            [['/Students/Index', 'Student directory', 'শিক্ষার্থী তালিকা']]),
        '/studentguardians/index': taskGuide(
            ['Student Guardians', 'শিক্ষার্থীর অভিভাবক'],
            ['Maintain a student guardian contact, link a new or existing guardian and inspect relationship history.', 'শিক্ষার্থীর অভিভাবকের যোগাযোগ, নতুন বা আগের অভিভাবক সংযুক্তি ও সম্পর্কের ইতিহাস দেখুন।'],
            ['TenantAdmin and STUDENT module access; verify the selected student and guardian identity before linking.', 'TenantAdmin ও STUDENT মডিউল অনুমতি; সংযুক্তির আগে শিক্ষার্থী ও অভিভাবকের পরিচয় যাচাই করুন।'],
            [['Search for and select the student; review the current guardian contact profile.', 'শিক্ষার্থী খুঁজে নির্বাচন করুন; বর্তমান অভিভাবকের যোগাযোগ তথ্য দেখুন।'], ['Use Save guardian contact, Link existing guardian for siblings, or Save guardian for a new relationship as appropriate.', 'প্রয়োজন অনুযায়ী Save guardian contact, ভাইবোনের জন্য Link existing guardian, বা নতুন সম্পর্কের জন্য Save guardian ব্যবহার করুন।'], ['Inspect linked and revoked guardians and open Relationship audit history for the selected student.', 'সংযুক্ত ও প্রত্যাহার করা অভিভাবক এবং নির্বাচিত শিক্ষার্থীর Relationship audit history দেখুন।']],
            ['Example: link a verified shared guardian to two siblings without creating duplicate guardian identities.', 'উদাহরণ: একই যাচাইকৃত অভিভাবককে নতুন পরিচয় না তৈরি করে দুই ভাইবোনের সঙ্গে যুক্ত করুন।'],
            ['The student’s current relationship and past changes appear in their scoped lists.', 'শিক্ষার্থীর বর্তমান সম্পর্ক ও আগের পরিবর্তন তাঁর নিজস্ব তালিকায় দেখা যায়।'],
            ['A guardian relationship is an access-sensitive link; revocation history must remain inspectable and never imply a new login grant.', 'অভিভাবক সম্পর্ক প্রবেশাধিকার-সংবেদনশীল; প্রত্যাহারের ইতিহাস অক্ষত থাকবে এবং সম্পর্ক যোগ মানেই নতুন লগইন অনুমতি নয়।'],
            ['If a guardian is missing, verify the selected student and matching identity. Refresh after a conflict before creating another record.', 'অভিভাবক না পেলে শিক্ষার্থী ও পরিচয় মিলিয়ে দেখুন; সংঘাত হলে আরেকটি রেকর্ড তৈরির আগে রিফ্রেশ করুন।'],
            [['/StudentDocuments/Index', 'Student documents', 'শিক্ষার্থীর নথি']]),
        '/enrollmentmanagement/index': taskGuide(
            ['Student Enrollment Management', 'শিক্ষার্থী ভর্তি ব্যবস্থাপনা'],
            ['Enroll an eligible student in an academic batch, section and curriculum and inspect past enrollments.', 'যোগ্য শিক্ষার্থীকে একাডেমিক ব্যাচ, শাখা ও পাঠ্যক্রমে ভর্তি করে আগের ভর্তির ইতিহাস দেখুন।'],
            ['TenantAdmin and ACADEMIC module access; student, batch and curriculum must be valid for the chosen institution.', 'TenantAdmin ও ACADEMIC মডিউল অনুমতি; নির্বাচিত প্রতিষ্ঠানে শিক্ষার্থী, ব্যাচ ও পাঠ্যক্রম বৈধ হতে হবে।'],
            [['Search by student code/name and select the correct student.', 'শিক্ষার্থীর কোড/নাম দিয়ে খুঁজে সঠিক শিক্ষার্থী বাছুন।'], ['Search sections, select the academic batch, section and compatible curriculum.', 'শাখা খুঁজে ব্যাচ, সেকশন ও সামঞ্জস্যপূর্ণ পাঠ্যক্রম নির্বাচন করুন।'], ['Select Enroll Student and verify the new record under Historical enrollments.', 'Enroll Student চাপুন এবং Historical enrollments-এ নতুন রেকর্ড যাচাই করুন।']],
            ['Example: enroll a verified new student into the approved Grade 9 section and curriculum.', 'উদাহরণ: যাচাইকৃত নতুন শিক্ষার্থীকে অনুমোদিত নবম শ্রেণির শাখা ও পাঠ্যক্রমে ভর্তি করুন।'],
            ['The authorized enrollment is recorded and visible in the student’s enrollment history.', 'অনুমোদিত ভর্তি সংরক্ষিত হয়ে শিক্ষার্থীর ভর্তির ইতিহাসে দেখা যায়।'],
            ['Do not duplicate an active enrollment or select a mismatched academic period/curriculum to bypass eligibility.', 'সক্রিয় ভর্তি নকল করবেন না বা যোগ্যতা এড়াতে ভুল শিক্ষাবর্ষ/পাঠ্যক্রম বাছবেন না।'],
            ['If enrollment is rejected, check student eligibility, batch availability and existing history before resubmitting.', 'ভর্তি প্রত্যাখ্যাত হলে আবার দেওয়ার আগে শিক্ষার্থীর যোগ্যতা, ব্যাচের প্রাপ্যতা ও আগের ইতিহাস দেখুন।'],
            [['/AcademicManagement/Sections', 'Sections', 'শাখা'], ['/Students/Index', 'Students', 'শিক্ষার্থী']]),
    };
    const coreWorkflowManuals = {
        '/feediscountrules/index': taskGuide(
            ['Fee Discount Rules', 'ফি ছাড়ের নিয়ম'],
            ['Create and review institution fee-discount rules applied by authorized Finance workflows.', 'প্রতিষ্ঠানের ফি ছাড়ের নিয়ম তৈরি ও পর্যালোচনা করুন, যা অনুমোদিত Finance কাজে প্রযোজ্য।'],
            ['TenantAdmin and FINANCE access; confirm the intended discount eligibility, value and effective configuration.', 'TenantAdmin ও FINANCE অনুমতি; ছাড়ের যোগ্যতা, মূল্য ও কার্যকারিতা আগে যাচাই করুন।'],
            [['Select New / Reset, then enter the rule name, fee head, type, value and effective dates.', 'New / Reset বেছে নিয়মের নাম, ফি খাত, ধরন, অঙ্ক ও কার্যকর তারিখ দিন।'], ['Confirm Create rule or Update rule; check the result and refresh the register.', 'Create rule বা Update rule নিশ্চিত করে ফলাফল ও তালিকা রিফ্রেশ যাচাই করুন।'], ['Refresh the register, select Edit on a row and verify its version before updating.', 'তালিকা রিফ্রেশ করে সারির Edit বেছে সংস্করণ যাচাই করে হালনাগাদ করুন।']],
            ['Example: configure an approved tuition discount for the eligible batch without editing posted invoices.', 'উদাহরণ: পোস্ট করা ইনভয়েস না বদলে যোগ্য ব্যাচের জন্য অনুমোদিত টিউশন ছাড় নির্ধারণ করুন।'],
            ['The rule appears in the institution discount register for future authorized evaluation.', 'নিয়মটি প্রতিষ্ঠানের ছাড়ের তালিকায় আসে, পরে অনুমোদিত হিসাবে ব্যবহার করা যায়।'],
            ['Saving a rule does not issue a cash refund or silently rewrite posted invoice/GL history; individual awards and approvals remain separate.', 'নিয়ম সংরক্ষণে নগদ ফেরত বা পোস্ট করা ইনভয়েস/GL ইতিহাস বদলায় না; ব্যক্তিগত ছাড় ও অনুমোদন আলাদা।'],
            ['If save conflicts, the fee head is unavailable or the institution session changes, reload the register; never reuse a stale version.', 'সংঘাত, অনুপলব্ধ ফি খাত বা প্রতিষ্ঠানের সেশন বদলালে তালিকা রিলোড করুন; পুরোনো সংস্করণ আবার ব্যবহার করবেন না।'],
            [['/FinanceAdjustments/Policies', 'Discount and fine policies', 'ছাড় ও জরিমানার নীতি']]),
        '/admissioncashreceipts/index': taskGuide(
            ['Admission Cash Receipts', 'ভর্তির নগদ রসিদ'],
            ['Find an admission application, collect its eligible one-time cash application fee and issue the authorized receipt.', 'ভর্তির আবেদন খুঁজে যোগ্য একবারের নগদ আবেদন ফি সংগ্রহ ও অনুমোদিত রসিদ তৈরি করুন।'],
            ['TenantAdmin, ADMISSION and FINANCE access; verify the applicant, amount and existing payment before collecting.', 'TenantAdmin, ADMISSION ও FINANCE অনুমতি; আদায়ের আগে আবেদনকারী, টাকা ও আগের পেমেন্ট যাচাই করুন।'],
            [['Search for the application and select the exact matching applicant.', 'আবেদন খুঁজে সঠিক আবেদনকারী নির্বাচন করুন।'], ['Review the one-time application fee, enter permitted cash details and select Collect cash and post GL.', 'একবারের ফি যাচাই করে নগদের তথ্য দিন এবং Collect cash and post GL চাপুন।'], ['Inspect the confirmed receipt and use Print receipt when the posting succeeds.', 'পোস্টিং সফল হলে নিশ্চিত রসিদ দেখুন ও Print receipt ব্যবহার করুন।']],
            ['Example: find a verified applicant, collect the approved application fee once and print its receipt.', 'উদাহরণ: যাচাইকৃত আবেদনকারী খুঁজে অনুমোদিত আবেদন ফি একবার আদায় করে রসিদ প্রিন্ট করুন।'],
            ['A successful accepted collection records the financial receipt and its permitted GL posting.', 'সফল আদায়ে আর্থিক রসিদ ও অনুমোদিত GL পোস্টিং সংরক্ষিত হয়।'],
            ['Do not collect twice for the same payable application. Confirm prior receipt/posting after a timeout before retrying.', 'একই আবেদনের ফি দুবার নেবেন না। সময় শেষ হলে আবার করার আগে আগের রসিদ/পোস্টিং দেখুন।'],
            ['If an application is absent, verify its reference and eligibility. For uncertain results, reload the current receipt state.', 'আবেদন না পেলে রেফারেন্স ও যোগ্যতা দেখুন। ফল অনিশ্চিত হলে বর্তমান রসিদের অবস্থা রিলোড করুন।'],
            [['/Admissions/Applications', 'Application review', 'আবেদন যাচাই']]),
        '/feeheadaccounting/index': taskGuide(
            ['Fee Heads and GL Mapping', 'ফি খাত ও GL ম্যাপিং'],
            ['Define institution fee heads and map eligible receivable/income accounts for Finance.', 'প্রতিষ্ঠানের ফি খাত নির্ধারণ ও Finance-এর প্রাপ্য/আয়ের হিসাব ম্যাপ করুন।'],
            ['FINANCE access; only TenantAdmin or Principal may change mappings. Confirm the correct institutional account codes.', 'FINANCE অনুমতি; শুধু TenantAdmin বা Principal ম্যাপিং বদলাতে পারেন। প্রতিষ্ঠানের সঠিক হিসাব কোড যাচাই করুন।'],
            [['Review the mapping register and existing fee head codes.', 'ম্যাপিং তালিকা ও বিদ্যমান ফি খাতের কোড দেখুন।'], ['If authorized, use Create / edit fee head to set its code and allowed receivable/income ledger accounts.', 'অনুমতি থাকলে Create / edit fee head-এ কোড ও বৈধ প্রাপ্য/আয় হিসাব দিন।'], ['Select Save fee head and verify the register; use New for a distinct entry.', 'Save fee head দিয়ে তালিকায় যাচাই করুন; আলাদা এন্ট্রির জন্য New চাপুন।']],
            ['Example: map a Tuition fee head to its approved institutional income and receivable ledgers.', 'উদাহরণ: Tuition ফি খাত অনুমোদিত প্রতিষ্ঠানের আয় ও প্রাপ্য লেজারে যুক্ত করুন।'],
            ['The fee-head mapping is saved for use by authorized invoice posting workflows.', 'ফি খাতের ম্যাপিং সংরক্ষিত হয়ে অনুমোদিত ইনভয়েস পোস্টিংয়ে ব্যবহার হয়।'],
            ['Mapping does not itself post invoices/payments or affect platform subscription billing; posted history cannot be silently remapped.', 'ম্যাপিং করলেই ইনভয়েস/পেমেন্ট পোস্ট হয় না বা প্ল্যাটফর্ম বিলিং বদলায় না; পোস্ট করা ইতিহাস নীরবে পুনর্ম্যাপ করা যায় না।'],
            ['If an account cannot be selected, check Chart of Accounts and permissions; reload an amended record before saving.', 'হিসাব নির্বাচন না হলে Chart of Accounts ও অনুমতি দেখুন; পরিবর্তিত রেকর্ড সংরক্ষণের আগে রিলোড করুন।'],
            [['/AccountingSetup/Index', 'Chart of accounts', 'হিসাবের তালিকা']]),
        '/accountingsetup/index': taskGuide(
            ['Institution Accounting Setup', 'প্রতিষ্ঠানের হিসাব সেটআপ'],
            ['Maintain institutional chart of accounts, accounting periods and bank-ledger mappings.', 'প্রতিষ্ঠানের হিসাব তালিকা, হিসাবকাল ও ব্যাংক-লেজার ম্যাপিং পরিচালনা করুন।'],
            ['FINANCE access. TenantAdmin/Principal can edit; Accountant can inspect permitted records.', 'FINANCE অনুমতি; TenantAdmin/Principal সম্পাদনা করতে পারেন, Accountant অনুমোদিত তথ্য দেখেন।'],
            [['Use Accounts to search by code/name, inspect the account tree and edit permitted metadata.', 'Accounts থেকে কোড/নাম দিয়ে খুঁজে হিসাব তালিকা দেখে অনুমোদিত তথ্য সম্পাদনা করুন।'], ['Use Periods to create or review an open accounting period and its dates.', 'Periods থেকে খোলা হিসাবকাল ও তার তারিখ তৈরি বা যাচাই করুন।'], ['If authorized, use Banks to maintain the approved bank-to-asset-ledger mapping and verify the register.', 'অনুমতি থাকলে Banks থেকে ব্যাংক ও সম্পদ লেজারের ম্যাপিং সংরক্ষণ করে তালিকা যাচাই করুন।']],
            ['Example: create an approved financial year and attach an institutional bank to its correct asset ledger.', 'উদাহরণ: অনুমোদিত আর্থিক বছর তৈরি করে প্রতিষ্ঠানের ব্যাংক সঠিক সম্পদ লেজারে যুক্ত করুন।'],
            ['Saved accounts, periods and mappings are available to controlled institutional Finance work.', 'সংরক্ষিত হিসাব, হিসাবকাল ও ম্যাপিং নিয়ন্ত্রিত Finance কাজে ব্যবহারযোগ্য হয়।'],
            ['Institution accounting is separate from SaaS billing; posted journals and closed periods cannot be rewritten by changing setup.', 'প্রতিষ্ঠানের হিসাব SaaS বিলিং থেকে আলাদা; সেটআপ বদলে পোস্ট করা জার্নাল বা বন্ধ হিসাবকাল পুনর্লিখন করা যায় না।'],
            ['On conflicts check code uniqueness, parent account, period and row version; do not recreate a posted account to bypass locks.', 'সংঘাতে অনন্য কোড, মূল হিসাব, হিসাবকাল ও সংস্করণ দেখুন; লক এড়াতে পোস্ট করা হিসাব নতুন করে বানাবেন না।'],
            [['/FeeHeadAccounting/Index', 'Fee account mapping', 'ফি হিসাব ম্যাপিং']]),
        '/accountingjournals/index': taskGuide(
            ['Journal Posting and Reversal', 'জার্নাল পোস্টিং ও বিপরীত এন্ট্রি'],
            ['Create balanced institutional journal drafts, review and post eligible entries, and retain reversal evidence.', 'সমান ডেবিট-ক্রেডিটের জার্নাল খসড়া তৈরি, অনুমোদিত এন্ট্রি পোস্ট ও বিপরীত এন্ট্রির প্রমাণ রাখুন।'],
            ['FINANCE access; TenantAdmin/Principal may use permitted write actions. An open period and valid accounts are required.', 'FINANCE অনুমতি; TenantAdmin/Principal অনুমোদিত লেখার কাজ করতে পারেন। খোলা হিসাবকাল ও বৈধ হিসাব প্রয়োজন।'],
            [['Choose date/open period, enter description and add debit/credit lines with matching totals.', 'তারিখ/খোলা হিসাবকাল দিয়ে বিবরণ ও সমান ডেবিট-ক্রেডিট লাইন লিখুন।'], ['Create draft, then use the Journal register to inspect the saved entry and the allowed Submit/Approve/Post actions.', 'Create draft দিয়ে তালিকায় এন্ট্রি দেখুন ও অনুমোদিত Submit/Approve/Post ব্যবহার করুন।'], ['For an eligible posted entry, use Reverse with a date and substantive reason; inspect retained history.', 'যোগ্য পোস্ট করা এন্ট্রিতে তারিখ ও কারণ দিয়ে Reverse করুন; সংরক্ষিত ইতিহাস দেখুন।']],
            ['Example: create a balanced non-SaaS institutional journal, get the required review and post it once.', 'উদাহরণ: SaaS-বহির্ভূত সমান অঙ্কের জার্নাল তৈরি, প্রয়োজনীয় যাচাই ও একবার পোস্ট করুন।'],
            ['Posted transactions and their compensating reversals remain in institutional accounting history.', 'পোস্ট করা লেনদেন ও তার বিপরীত এন্ট্রি প্রতিষ্ঠানের হিসাব ইতিহাসে থাকে।'],
            ['Never alter posted lines directly, bypass independent approval or use this page for SaaS subscription payments.', 'পোস্ট করা লাইন সরাসরি বদলাবেন না, স্বাধীন অনুমোদন এড়াবেন না বা SaaS পেমেন্ট এখানে দেবেন না।'],
            ['If totals do not balance or the period is closed, correct the draft. After a timeout inspect the register before reposting.', 'অঙ্ক না মিললে বা হিসাবকাল বন্ধ থাকলে খসড়া ঠিক করুন। সময় শেষ হলে আবার পোস্টের আগে তালিকা দেখুন।'],
            [['/AccountingReports/Index', 'General ledger reports', 'সাধারণ লেজার প্রতিবেদন']]),
        '/accountingreports/index': taskGuide(
            ['Institutional Accounting Reports', 'প্রতিষ্ঠানের হিসাব প্রতিবেদন'],
            ['Inspect posted ledger, day book, trial balance, comparative statements, campus/source income breakdown and bank reconciliation without changing journals.', 'জার্নাল না বদলে পোস্ট করা লেজার, ডে-বুক, ট্রায়াল ব্যালেন্স, তুলনামূলক প্রতিবেদন, ক্যাম্পাস/উৎসভিত্তিক আয়-ব্যয় ও ব্যাংক সমন্বয় দেখুন।'],
            ['TenantAdmin with FINANCE access; an institutional accounting period and posted journal evidence are required.', 'FINANCE অনুমতিসহ TenantAdmin; প্রতিষ্ঠানের হিসাবকাল ও পোস্ট করা জার্নালের প্রমাণ প্রয়োজন।'],
            [['Select General Ledger, Day Book, Trial Balance, a comparison or Income/Expense Breakdown and enter the required dates and campus/source grouping.', 'General Ledger, Day Book, Trial Balance, তুলনা বা Income/Expense Breakdown বেছে তারিখ ও ক্যাম্পাস/উৎস গ্রুপ দিন।'], ['Run the report; match debit/credit, comparison columns or the breakdown total against institution-wide P&L. Variance is current minus comparison.', 'ডেবিট/ক্রেডিট, তুলনার কলাম বা প্রতিষ্ঠানের আয়-ব্যয়ের মোটের সঙ্গে গ্রুপভিত্তিক অঙ্ক মিলিয়ে নিন। পার্থক্য = বর্তমান − তুলনাকাল।'], ['Day Book CSV contains only the visible page; totals cover the whole period. Inspect Details for ledger lines and follow fee/payroll/supplier links for source subledgers.', 'Day Book CSV-তে শুধু বর্তমান পেজ থাকে; মোট পুরো সময়ের। Details-এ লেজার লাইন দেখুন এবং ফি/পে-রোল/সরবরাহকারী লিংকে মূল হিসাব দেখুন।']],
            ['Example: select a ledger and date range, run it, compare the running balance with the closing balance, then export CSV.', 'উদাহরণ: লেজার ও তারিখ বেছে চলতি ব্যালেন্স ও শেষ ব্যালেন্স মিলিয়ে CSV রপ্তানি করুন।'],
            ['Read-only report results show the selected posted institutional accounting scope.', 'শুধু-পাঠযোগ্য ফল নির্বাচিত পোস্ট করা প্রতিষ্ঠানের হিসাব দেখায়।'],
            ['SaaS billing is excluded. Bank Liquidity is not a classified cash-flow statement; unconfigured cash-in-hand, budget and fund reports must not be inferred. Report previews never approve/post/reverse.', 'SaaS বিলিং নেই। Bank Liquidity পূর্ণ Cash Flow নয়; অনির্ধারিত হাতে নগদ, বাজেট বা ফান্ডের হিসাব অনুমান করা যাবে না। রিপোর্ট দেখে পোস্ট/রিভার্স হয় না।'],
            ['If no rows appear, verify date, selected account and posted state. For mismatched totals, investigate source entries rather than editing the report.', 'রেকর্ড না থাকলে তারিখ, হিসাব ও পোস্টিং দেখুন; অঙ্ক না মিললে রিপোর্ট বদলানো নয়, মূল এন্ট্রি যাচাই করুন।'],
            [['/AccountingJournals/Index', 'Journal register', 'জার্নাল তালিকা']]),
        '/inventorycatalog/index': taskGuide(
            ['Inventory Item and Location Catalog', 'পণ্য ও অবস্থানের তালিকা'],
            ['Define and search inventory items and stock locations separately from financial stock movements.', 'স্টক লেনদেন থেকে আলাদাভাবে ইনভেন্টরির পণ্য ও সংরক্ষণের স্থান তৈরি ও খুঁজুন।'],
            ['INVENTORY access and a permitted TenantAdmin, Principal, InventoryManager, StoreKeeper or Accountant role; edits require the page-authorized role.', 'INVENTORY অনুমতি ও TenantAdmin, Principal, InventoryManager, StoreKeeper বা Accountant ভূমিকা; সম্পাদনায় আলাদা অনুমতি প্রযোজ্য।'],
            [['Search the Item register by the available filters and inspect the existing item.', 'ফিল্টার দিয়ে পণ্য তালিকা খুঁজে বিদ্যমান পণ্য দেখুন।'], ['When authorized, create/edit an item with its validated code, unit and other required fields, then Save item.', 'অনুমতি থাকলে বৈধ কোড, একক ও তথ্য দিয়ে পণ্য তৈরি/সম্পাদনা করে Save item চাপুন।'], ['Use Locations to search or save a permitted storage location; verify both registers before stock transactions.', 'Locations থেকে অনুমোদিত স্থান খুঁজুন বা সংরক্ষণ করুন; স্টক লেনদেনের আগে দুই তালিকা যাচাই করুন।']],
            ['Example: create a stationery item and an approved storeroom without issuing any stock.', 'উদাহরণ: স্টক ইস্যু না করে স্টেশনারি পণ্য ও অনুমোদিত স্টোররুম তৈরি করুন।'],
            ['The item and location are selectable for authorized inventory transactions.', 'পণ্য ও স্থান অনুমোদিত ইনভেন্টরি লেনদেনে নির্বাচনযোগ্য হয়।'],
            ['Catalog changes alone do not move stock or post Finance GL; do not duplicate item identities to bypass existing movements.', 'ক্যাটালগ বদলালেই স্টক নড়ে বা Finance GL পোস্ট হয় না; আগের লেনদেন এড়াতে একই পণ্য নকল করবেন না।'],
            ['If creation fails, check code uniqueness, required units and permitted location scope; refresh before retrying.', 'তৈরি ব্যর্থ হলে অনন্য কোড, একক ও অনুমোদিত স্থান দেখুন; আবার করার আগে রিফ্রেশ করুন।'],
            [['/InventoryMovements/Transactions', 'Stock transactions', 'স্টক লেনদেন']]),
        '/assetlifecycle/index': taskGuide(
            ['Fixed Assets and Custody', 'স্থায়ী সম্পদ ও জিম্মা'],
            ['Inspect and maintain authorized fixed asset records, custody, returns and maintenance history.', 'অনুমোদিত স্থায়ী সম্পদের রেকর্ড, জিম্মা, ফেরত ও রক্ষণাবেক্ষণের ইতিহাস দেখুন ও পরিচালনা করুন।'],
            ['INVENTORY access; TenantAdmin/Principal/InventoryManager manage eligible assets and category edits are more restricted.', 'INVENTORY অনুমতি; TenantAdmin/Principal/InventoryManager যোগ্য সম্পদ পরিচালনা করেন, বিভাগ সম্পাদনায় আরও সীমা আছে।'],
            [['Open the Asset catalog and find the correct asset/category before making changes.', 'সম্পদের তালিকা খুলে পরিবর্তনের আগে সঠিক সম্পদ/বিভাগ খুঁজুন।'], ['If authorized, update the asset register or custody assignment/return and review the saved record.', 'অনুমতি থাকলে সম্পদের তালিকা বা জিম্মা বরাদ্দ/ফেরত আপডেট করে সংরক্ষিত রেকর্ড দেখুন।'], ['Inspect Maintenance due and Custody history to confirm status and follow-up.', 'Maintenance due ও Custody history দেখে অবস্থা ও পরবর্তী কাজ যাচাই করুন।']],
            ['Example: register a school projector and record its approved handover to a custodian.', 'উদাহরণ: স্কুলের প্রজেক্টর নিবন্ধন করে অনুমোদিত জিম্মাদারের কাছে হস্তান্তর লিখুন।'],
            ['The asset and custody change appear in the institution register with retained history.', 'সম্পদ ও জিম্মার পরিবর্তন প্রতিষ্ঠানের তালিকা ও ইতিহাসে দেখা যায়।'],
            ['Asset custody is distinct from SaaS billing and does not by itself establish a Finance journal posting.', 'সম্পদের জিম্মা SaaS বিলিং থেকে আলাদা এবং এতে নিজে থেকে Finance জার্নাল পোস্ট হয়েছে ধরে নেওয়া যাবে না।'],
            ['If an asset is unavailable, check scope, category and active assignment; inspect history before retrying a return.', 'সম্পদ না পেলে অনুমতি, বিভাগ ও সক্রিয় বরাদ্দ দেখুন; আবার ফেরত দেওয়ার আগে ইতিহাস দেখুন।'],
            [['/InventoryCatalog/Index', 'Inventory catalog', 'ইনভেন্টরি তালিকা']]),
        '/operationsinsights/index': taskGuide(
            ['Transport and Hostel Insights', 'পরিবহন ও হোস্টেলের বিশ্লেষণ'],
            ['Read authorized transport fleet and hostel occupancy risk summaries for operational planning.', 'পরিচালনাগত পরিকল্পনার জন্য অনুমোদিত পরিবহন ও হোস্টেল দখল-ঝুঁকির সারাংশ দেখুন।'],
            ['TenantAdmin or Principal role; relevant optional modules and permitted campus records must be available.', 'TenantAdmin বা Principal ভূমিকা; সংশ্লিষ্ট ঐচ্ছিক মডিউল ও অনুমোদিত ক্যাম্পাসের রেকর্ড প্রয়োজন।'],
            [['Choose the permitted scope in Transport fleet health and select Refresh transport.', 'Transport fleet health-এ অনুমোদিত পরিসর বেছে Refresh transport চাপুন।'], ['Review fleet warnings and available operational indicators without changing vehicle assignments.', 'গাড়িবহরের সতর্কতা ও তথ্য দেখুন; গাড়ি বরাদ্দ বদলাবেন না।'], ['Choose the hostel scope, select Refresh hostel and compare occupancy risks before planning another allocation.', 'হোস্টেলের পরিসর বেছে Refresh hostel চাপুন; নতুন বরাদ্দের আগে দখল-ঝুঁকি তুলনা করুন।']],
            ['Example: inspect tomorrow’s hostel occupancy and vehicle-health warnings before scheduling campus operations.', 'উদাহরণ: ক্যাম্পাস পরিচালনার আগে আগামীকালের হোস্টেল দখল ও গাড়ির সতর্কতা দেখুন।'],
            ['Read-only risk summaries are displayed for the authorized institution/campus.', 'অনুমোদিত প্রতিষ্ঠান/ক্যাম্পাসের শুধু-পাঠযোগ্য ঝুঁকির সারাংশ দেখা যায়।'],
            ['A forecast or risk warning is not an approval, reservation, receipt or Finance posting.', 'পূর্বাভাস বা সতর্কতা কোনো অনুমোদন, রিজার্ভেশন, রসিদ বা Finance পোস্টিং নয়।'],
            ['If no results appear, verify module access and filters, then refresh the underlying report.', 'ফল না এলে মডিউল অনুমতি ও ফিল্টার দেখে মূল রিপোর্ট রিফ্রেশ করুন।'],
            [['/HostelOperations/Reports', 'Hostel reports', 'হোস্টেল প্রতিবেদন'], ['/TransportOperations/Reports', 'Transport reports', 'পরিবহন প্রতিবেদন']]),
        '/academicroutines/index': taskGuide(
            ['Academic Class Routines', 'একাডেমিক ক্লাস রুটিন'],
            ['Manage authorized time slots, teacher assignments and class/teacher timetable views for a batch.', 'ব্যাচের অনুমোদিত সময়ের স্লট, শিক্ষক বরাদ্দ ও ক্লাস/শিক্ষক রুটিন দেখুন ও পরিচালনা করুন।'],
            ['ACADEMIC module and TenantAdmin, Principal, VicePrincipal or Teacher access; batch, subject offering and teacher eligibility must match.', 'ACADEMIC মডিউল ও TenantAdmin, Principal, VicePrincipal বা Teacher অনুমতি; ব্যাচ, বিষয় অফার ও শিক্ষকের যোগ্যতা মিলতে হবে।'],
            [['Find the academic batch and select Load routine to inspect time slots and current assignments.', 'একাডেমিক ব্যাচ খুঁজে Load routine দিয়ে সময়ের স্লট ও বর্তমান বরাদ্দ দেখুন।'], ['Where authorized, create an available slot and assign a permitted teacher to the offered subject.', 'অনুমতি থাকলে খালি স্লট তৈরি করে অফার করা বিষয়ে যোগ্য শিক্ষক বরাদ্দ করুন।'], ['Add a class entry, verify the Class timetable and compare the Teacher timetable; export the visible page if permitted.', 'ক্লাস এন্ট্রি দিয়ে Class timetable ও Teacher timetable মিলিয়ে দেখুন; অনুমতি থাকলে দৃশ্যমান পৃষ্ঠা রপ্তানি করুন।']],
            ['Example: schedule the approved Mathematics offering for Grade 9 A in an available Monday slot.', 'উদাহরণ: নবম শ্রেণি A-এর অনুমোদিত গণিত বিষয় সোমবারের খালি স্লটে নির্ধারণ করুন।'],
            ['Eligible scheduled classes and teacher assignments appear in their respective timetable registers.', 'যোগ্য নির্ধারিত ক্লাস ও শিক্ষক বরাদ্দ সংশ্লিষ্ট রুটিনে দেখা যায়।'],
            ['Overlapping slots, unoffered subjects or ineligible teachers must not be scheduled; CSV is bounded to the displayed scope.', 'একই সময়ে সংঘাত, অফারবিহীন বিষয় বা অযোগ্য শিক্ষক নির্ধারণ করা যাবে না; CSV দৃশ্যমান সীমায় থাকে।'],
            ['If an assignment fails, verify offering, teacher eligibility and slot conflicts; reload the current routine before retrying.', 'বরাদ্দ ব্যর্থ হলে বিষয় অফার, শিক্ষকের যোগ্যতা ও সময়ের সংঘাত দেখুন; আবার করার আগে রুটিন রিলোড করুন।'],
            [['/AcademicManagement/Offerings', 'Subject offerings', 'বিষয় অফার'], ['/AcademicInstruction/Index', 'Lesson plans', 'পাঠ পরিকল্পনা']]),
        '/academicinstruction/index': taskGuide(
            ['Academic Instruction and Lesson Plans', 'পাঠদান ও পাঠ পরিকল্পনা'],
            ['Manage authorized lesson plans and substitute teaching arrangements for selected academic offerings.', 'নির্বাচিত একাডেমিক বিষয়ের অনুমোদিত পাঠ পরিকল্পনা ও বিকল্প শিক্ষক ব্যবস্থা পরিচালনা করুন।'],
            ['ACADEMIC module and TenantAdmin, Principal, VicePrincipal or Teacher access; assigned instructors and active routines control eligible actions.', 'ACADEMIC মডিউল ও TenantAdmin, Principal, VicePrincipal বা Teacher অনুমতি; নির্ধারিত শিক্ষক ও সক্রিয় রুটিন অনুযায়ী কাজের যোগ্যতা নির্ধারিত হয়।'],
            [['Choose batch/date scope and select Load instruction to review existing lesson plans.', 'ব্যাচ/তারিখ বেছে Load instruction দিয়ে আগের পাঠ পরিকল্পনা দেখুন।'], ['Where authorized, select New lesson plan, find its subject offering, enter details and save or advance the allowed stage.', 'অনুমতি থাকলে New lesson plan থেকে বিষয় খুঁজে তথ্য লিখে সংরক্ষণ বা বৈধ ধাপে এগোন।'], ['Review approved/completed lessons, substitute assignments and the dated workload report when permitted.', 'অনুমতি অনুযায়ী অনুমোদিত/সম্পন্ন পাঠ, বিকল্প শিক্ষক ও তারিখভিত্তিক কাজের রিপোর্ট দেখুন।']],
            ['Example: an assigned teacher prepares a lesson for the correct offering and progresses it after approval.', 'উদাহরণ: নির্ধারিত শিক্ষক সঠিক বিষয়ের পাঠ তৈরি করে অনুমোদনের পর অগ্রগতি দেন।'],
            ['The lesson state and permitted substitution records appear in the batch instruction register.', 'পাঠের অবস্থা ও অনুমোদিত বিকল্প শিক্ষক তথ্য ব্যাচের পাঠদান তালিকায় দেখা যায়।'],
            ['Only eligible assigned teachers may progress approved lessons; substitution requires an active routine and a different eligible teacher.', 'শুধু যোগ্য নির্ধারিত শিক্ষক অনুমোদিত পাঠ এগোতে পারেন; বিকল্প শিক্ষক হতে সক্রিয় রুটিন ও অন্য যোগ্য শিক্ষক প্রয়োজন।'],
            ['If a subject or substitute cannot be selected, verify batch/offering and teacher eligibility before retrying.', 'বিষয় বা বিকল্প শিক্ষক না পেলে ব্যাচ/অফার ও শিক্ষকের যোগ্যতা যাচাই করুন।'],
            [['/AcademicRoutines/Index', 'Academic routines', 'একাডেমিক রুটিন']]),
        '/academiccalendar/index': taskGuide(
            ['Academic Calendar', 'একাডেমিক ক্যালেন্ডার'],
            ['Review academic events, authorized weekend policy and working-day reports.', 'একাডেমিক ইভেন্ট, অনুমোদিত সাপ্তাহিক বন্ধের নিয়ম ও কর্মদিবসের প্রতিবেদন দেখুন।'],
            ['ACADEMIC module and TenantAdmin, Principal, VicePrincipal or Teacher access; calendar edits are limited to managing roles.', 'ACADEMIC মডিউল ও TenantAdmin, Principal, VicePrincipal বা Teacher অনুমতি; ক্যালেন্ডার সম্পাদনা পরিচালনাকারী ভূমিকায় সীমিত।'],
            [['Select the academic scope and dates, then Load Calendar events.', 'একাডেমিক পরিসর ও তারিখ বেছে Calendar events লোড করুন।'], ['If authorized, choose New event or Weekend policy, complete valid dates and select Save.', 'অনুমতি থাকলে New event বা Weekend policy বেছে বৈধ তারিখ দিয়ে Save করুন।'], ['Inspect Calendar events and the Working day report; export only the relevant authorized CSV.', 'Calendar events ও Working day report দেখে অনুমোদিত CSV রপ্তানি করুন।']],
            ['Example: add an approved institute holiday and inspect its effect in the working-day report.', 'উদাহরণ: অনুমোদিত প্রতিষ্ঠানের ছুটি যোগ করে কর্মদিবসের প্রতিবেদনে তার প্রভাব দেখুন।'],
            ['The saved event or policy is reflected in the scoped calendar and working-day calculations.', 'সংরক্ষিত ইভেন্ট বা নিয়ম নির্দিষ্ট ক্যালেন্ডার ও কর্মদিবসের হিসাবে দেখা যায়।'],
            ['A calendar event is not attendance approval; past academic records may have independent locking rules.', 'ক্যালেন্ডারের ইভেন্ট উপস্থিতির অনুমোদন নয়; পুরোনো একাডেমিক রেকর্ডে আলাদা লক থাকতে পারে।'],
            ['If a date range is rejected, check overlap, current academic period and role; reload before changing an existing event.', 'তারিখ প্রত্যাখ্যাত হলে ওভারল্যাপ, শিক্ষাবর্ষ ও অনুমতি দেখুন; আগের ইভেন্ট বদলাতে রিলোড করুন।'],
            [['/AcademicRoutines/Index', 'Class routines', 'ক্লাস রুটিন']]),
        '/studentleaves/index': taskGuide(
            ['Student Leave Management', 'শিক্ষার্থীর ছুটি ব্যবস্থাপনা'],
            ['Submit and review student leave requests and inspect a date-scoped leave report.', 'শিক্ষার্থীর ছুটির আবেদন জমা ও যাচাই এবং তারিখভিত্তিক ছুটির প্রতিবেদন দেখুন।'],
            ['ATTENDANCE module and TenantAdmin, Principal or VicePrincipal access; the student must have an eligible enrollment.', 'ATTENDANCE মডিউল ও TenantAdmin, Principal বা VicePrincipal অনুমতি; শিক্ষার্থীকে যোগ্যভাবে ভর্তি থাকতে হবে।'],
            [['Search for the enrolled student and select the correct record.', 'ভর্তিকৃত শিক্ষার্থী খুঁজে সঠিক রেকর্ড বাছুন।'], ['Enter the requested leave period/details and select Submit Leave.', 'ছুটির সময় ও তথ্য দিয়ে Submit Leave চাপুন।'], ['Review Applications & review, then choose the date range and Load report for the bounded leave history.', 'Applications & review যাচাই করে তারিখ বেছে Load report দিয়ে সীমিত ইতিহাস দেখুন।']],
            ['Example: submit a verified two-day absence request and later inspect its review status.', 'উদাহরণ: যাচাইকৃত দুই দিনের অনুপস্থিতির আবেদন দিয়ে পরে যাচাইয়ের অবস্থা দেখুন।'],
            ['The application and allowed decision appear in the current queue and period report.', 'আবেদন ও অনুমোদিত সিদ্ধান্ত বর্তমান তালিকা ও সময়ভিত্তিক রিপোর্টে দেখা যায়।'],
            ['Submitting leave does not itself finalize attendance; approval and attendance effects follow their controlled workflow.', 'ছুটির আবেদন জমা দিলেই উপস্থিতি চূড়ান্ত হয় না; অনুমোদন ও উপস্থিতির প্রভাব আলাদা নিয়মে হয়।'],
            ['If the student is unavailable, verify enrollment; for duplicate or overlapping dates inspect existing applications first.', 'শিক্ষার্থী না পেলে ভর্তি যাচাই করুন; একই বা ওভারল্যাপ তারিখে আগের আবেদন দেখুন।'],
            [['/Attendance/Register', 'Attendance register', 'উপস্থিতি এন্ট্রি']]),
        '/employeeattendance/index': taskGuide(
            ['Employee Attendance', 'কর্মীর উপস্থিতি'],
            ['Record institution employee attendance and inspect the register and audited status corrections.', 'প্রতিষ্ঠানের কর্মীর উপস্থিতি লিখুন, তালিকা ও নিরীক্ষিত অবস্থা সংশোধন দেখুন।'],
            ['TenantAdmin and HR module access; verify employee, date and authorized status.', 'TenantAdmin ও HR মডিউল অনুমতি; কর্মী, তারিখ ও অনুমোদিত অবস্থা যাচাই করুন।'],
            [['Choose the employee and date/status under Record attendance, then select Record.', 'Record attendance-এ কর্মী, তারিখ/অবস্থা বেছে Record চাপুন।'], ['Load the Attendance register using the required filters and review existing records.', 'ফিল্টার দিয়ে Attendance register লোড করে আগের রেকর্ড দেখুন।'], ['For an eligible correction, enter the new status and reason, select Correct and inspect Correction history.', 'যোগ্য সংশোধনে নতুন অবস্থা ও কারণ দিয়ে Correct চাপুন এবং Correction history দেখুন।']],
            ['Example: record a verified employee present on a working day and use audited correction if a clerical mistake is confirmed.', 'উদাহরণ: কর্মদিবসে যাচাইকৃত কর্মীর উপস্থিতি লিখে পরে প্রমাণিত ভুলে নিরীক্ষিত সংশোধন করুন।'],
            ['Attendance and permitted corrections are retained in the employee register and history.', 'উপস্থিতি ও অনুমোদিত সংশোধন কর্মীর তালিকা ও ইতিহাসে সংরক্ষিত থাকে।'],
            ['Corrections must retain the original state and reason; never overwrite prior attendance to hide a change.', 'সংশোধনে আগের অবস্থা ও কারণ রাখতে হবে; পরিবর্তন আড়াল করতে পুরোনো উপস্থিতি মুছবেন না।'],
            ['If recording fails, verify employee scope, date and existing entry; check audit history before submitting again.', 'সংরক্ষণ ব্যর্থ হলে কর্মী, তারিখ ও আগের এন্ট্রি দেখুন; আবার দেওয়ার আগে ইতিহাস যাচাই করুন।'],
            [['/HR/Directory', 'Employee directory', 'কর্মী তালিকা']]),
        '/leaveentitlements/index': taskGuide(
            ['Employee Leave Entitlements', 'কর্মীর ছুটির প্রাপ্যতা'],
            ['Configure allowed leave types, employee annual allowances and audited balance adjustments.', 'ছুটির ধরন, কর্মীর বার্ষিক প্রাপ্যতা ও নিরীক্ষিত ব্যালেন্স সমন্বয় নির্ধারণ করুন।'],
            ['TenantAdmin and HR module access; confirm the employee, leave year and approved policy.', 'TenantAdmin ও HR মডিউল অনুমতি; কর্মী, ছুটির বছর ও অনুমোদিত নীতি যাচাই করুন।'],
            [['Create or edit an allowed Leave type and save its policy.', 'অনুমোদিত Leave type তৈরি/সম্পাদনা করে নীতি সংরক্ষণ করুন।'], ['Load the Employee annual entitlement and save the verified annual allowance.', 'Employee annual entitlement লোড করে যাচাইকৃত বার্ষিক প্রাপ্যতা সংরক্ষণ করুন।'], ['Use Audited adjustment with a reason when needed, then inspect Available leave balances.', 'প্রয়োজনে কারণসহ Audited adjustment করে Available leave balances যাচাই করুন।']],
            ['Example: set the approved annual casual-leave allowance for an employee, then record a justified correction.', 'উদাহরণ: কর্মীর অনুমোদিত নৈমিত্তিক ছুটির প্রাপ্যতা দিয়ে কারণসহ সমন্বয় করুন।'],
            ['Current allowance and adjustment effects appear in leave balances with retained audit evidence.', 'বর্তমান প্রাপ্যতা ও সমন্বয়ের প্রভাব নিরীক্ষা প্রমাণসহ ব্যালেন্সে দেখা যায়।'],
            ['A balance adjustment is not a leave approval; past adjustments and usage history must remain traceable.', 'ব্যালেন্স সমন্বয় ছুটি অনুমোদন নয়; আগের সমন্বয় ও ব্যবহার ইতিহাস অনুসরণযোগ্য থাকতে হবে।'],
            ['If the year or employee is missing, verify HR enrollment and policy; reload the balance before another adjustment.', 'বছর বা কর্মী না পেলে HR রেকর্ড ও নীতি দেখুন; আরেকটি সমন্বয়ের আগে ব্যালেন্স রিলোড করুন।'],
            [['/HR/Leaves', 'Employee leave review', 'কর্মীর ছুটি যাচাই']]),
        '/studentportal/index': taskGuide(
            ['Student and Guardian Portal', 'শিক্ষার্থী ও অভিভাবক পোর্টাল'],
            ['View permitted timetable, registration, attendance, results, fees and learning summaries, with consent/preferences where available.', 'অনুমোদিত রুটিন, নিবন্ধন, উপস্থিতি, ফলাফল, ফি ও শেখার সারাংশ এবং প্রযোজ্য সম্মতি/পছন্দ দেখুন।'],
            ['Sign in as Student, Guardian or Parent and select the verified authorized learner context.', 'Student, Guardian বা Parent হিসেবে লগইন করে যাচাইকৃত অনুমোদিত শিক্ষার্থী নির্বাচন করুন।'],
            [['Use the portal tabs to choose Timetable, Attendance, Results, Fees or the permitted other section.', 'পোর্টালের ট্যাব থেকে Timetable, Attendance, Results, Fees বা অনুমোদিত অন্য অংশ বাছুন।'], ['Select Refresh and page through only the authorized records; verify dates and state.', 'Refresh দিয়ে শুধু অনুমোদিত রেকর্ড দেখুন; তারিখ ও অবস্থা মিলিয়ে পৃষ্ঠা বদলান।'], ['Where shown, review learner data-sharing requests and guardian communication preferences before saving a decision.', 'প্রযোজ্য হলে শিক্ষার্থীর তথ্য ভাগাভাগির আবেদন ও অভিভাবকের যোগাযোগ পছন্দ দেখে সিদ্ধান্ত দিন।']],
            ['Example: a guardian checks an authorized learner’s attendance and fee status without accessing another child’s private data.', 'উদাহরণ: অভিভাবক অনুমোদিত শিক্ষার্থীর উপস্থিতি ও ফি দেখেন, অন্য শিশুর ব্যক্তিগত তথ্যে প্রবেশ করেন না।'],
            ['The selected learner’s eligible records and consent decisions appear within the portal.', 'নির্বাচিত শিক্ষার্থীর যোগ্য রেকর্ড ও সম্মতির সিদ্ধান্ত পোর্টালে দেখা যায়।'],
            ['Portal access is relationship and module scoped; reading a payment or result does not authorize financial or grade changes.', 'পোর্টালের প্রবেশাধিকার সম্পর্ক ও মডিউলসীমায় থাকে; পেমেন্ট বা ফল দেখলে আর্থিক/গ্রেড পরিবর্তনের অনুমতি হয় না।'],
            ['If a tab is empty, confirm learner relationship, module access and publication state; refresh before requesting another grant.', 'ট্যাব খালি থাকলে শিক্ষার্থীর সম্পর্ক, মডিউল ও প্রকাশিত অবস্থা দেখুন; আরেকটি অনুমতির আগে রিফ্রেশ করুন।'],
            [['/StudentSubjects/Index', 'Optional subjects', 'ঐচ্ছিক বিষয়']]),
        '/studentsubjects/index': taskGuide(
            ['Optional Subject Requests', 'ঐচ্ছিক বিষয়ের আবেদন'],
            ['Request an eligible optional subject and review registration decisions or personal history.', 'যোগ্য ঐচ্ছিক বিষয়ের আবেদন ও নিবন্ধনের সিদ্ধান্ত বা ব্যক্তিগত ইতিহাস দেখুন।'],
            ['ACADEMIC module and permitted TenantAdmin, Principal, VicePrincipal, Student, Guardian or Parent access; registration scope is learner-specific.', 'ACADEMIC মডিউল ও অনুমোদিত TenantAdmin, Principal, VicePrincipal, Student, Guardian বা Parent অনুমতি; নিবন্ধন নির্দিষ্ট শিক্ষার্থীর।'],
            [['Choose the student or permitted learner and inspect available optional subjects.', 'শিক্ষার্থী বা অনুমোদিত শিক্ষার্থী বেছে উপলব্ধ ঐচ্ছিক বিষয় দেখুন।'], ['Select the eligible subject and submit the Elective Request.', 'যোগ্য বিষয় বেছে Elective Request জমা দিন।'], ['Review the Registration decisions or My subject registrations, then inspect Historical subject registrations.', 'Registration decisions বা My subject registrations দেখে আগের নিবন্ধনের ইতিহাস যাচাই করুন।']],
            ['Example: an eligible student requests the offered optional ICT subject for the correct batch.', 'উদাহরণ: যোগ্য শিক্ষার্থী সঠিক ব্যাচের অফার করা ঐচ্ছিক ICT বিষয়ের আবেদন করেন।'],
            ['The request and allowed decision appear in the appropriate registration register.', 'আবেদন ও অনুমোদিত সিদ্ধান্ত সংশ্লিষ্ট নিবন্ধন তালিকায় দেখা যায়।'],
            ['A request does not reserve a confirmed subject seat until the authorized registration decision is applied.', 'আবেদন করলেই অনুমোদিত নিবন্ধনের সিদ্ধান্ত ছাড়া বিষয়টির আসন নিশ্চিত হয় না।'],
            ['If no subject appears, check batch offerings, eligibility and prior registrations before making another request.', 'বিষয় না পেলে ব্যাচের অফার, যোগ্যতা ও আগের নিবন্ধন দেখুন।'],
            [['/AcademicManagement/Offerings', 'Subject offerings', 'বিষয় অফার']]),
        '/learneridentities/index': taskGuide(
            ['Learner Identity Registration', 'শিক্ষার্থীর পরিচয় নিবন্ধন'],
            ['Register or request consent for learner identity details and maintain a structured learner address.', 'শিক্ষার্থীর পরিচয় নিবন্ধন বা সম্মতির অনুরোধ এবং গঠিত ঠিকানা পরিচালনা করুন।'],
            ['TenantAdmin or AdmissionOfficer with STUDENT access; verify the right learner before handling identity data.', 'STUDENT অনুমতিসহ TenantAdmin বা AdmissionOfficer; পরিচয় তথ্য ব্যবহারের আগে সঠিক শিক্ষার্থী যাচাই করুন।'],
            [['Search for the learner or existing identity and select the correct record.', 'শিক্ষার্থী বা আগের পরিচয় খুঁজে সঠিক রেকর্ড বাছুন।'], ['Complete Identity details and select Register or Request Consent according to the current authorization state.', 'Identity details পূরণ করে বর্তমান অনুমতি অনুযায়ী Register or Request Consent চাপুন।'], ['Review the result; if enabled, complete Structured learner address and select Save address.', 'ফল দেখুন; অনুমতি থাকলে Structured learner address পূরণ করে Save address চাপুন।']],
            ['Example: register a verified learner identity and request the necessary data-sharing consent before broader use.', 'উদাহরণ: যাচাইকৃত পরিচয় নিবন্ধন করে বিস্তৃত ব্যবহার আগে প্রয়োজনীয় তথ্য ভাগাভাগির সম্মতি চান।'],
            ['The authorized identity/address is recorded or held for the appropriate consent decision.', 'অনুমোদিত পরিচয়/ঠিকানা সংরক্ষিত হয় বা প্রযোজ্য সম্মতির সিদ্ধান্তের অপেক্ষায় থাকে।'],
            ['Learner identity is sensitive; do not reveal private details without relationship/consent and server authorization.', 'শিক্ষার্থীর পরিচয় গোপনীয়; সম্পর্ক, সম্মতি ও সার্ভারের অনুমতি ছাড়া ব্যক্তিগত তথ্য দেখাবেন না।'],
            ['If a record is blocked, verify learner match and consent; refresh history before creating another identity.', 'রেকর্ড আটকে গেলে শিক্ষার্থী ও সম্মতি মিলিয়ে দেখুন; নতুন পরিচয়ের আগে ইতিহাস রিফ্রেশ করুন।'],
            [['/LearnerIdentities/AccessHistory', 'Identity access history', 'পরিচয় ব্যবহারের ইতিহাস']]),
        '/learning/index': taskGuide(
            ['Learning and Courses', 'শিক্ষা ও কোর্স'],
            ['Find eligible LMS courses, manage authorized course/lesson/assignment content and inspect learner progress.', 'যোগ্য LMS কোর্স খুঁজুন, অনুমোদিত কোর্স/পাঠ/অ্যাসাইনমেন্ট পরিচালনা ও শিক্ষার্থীর অগ্রগতি দেখুন।'],
            ['LMS access and TenantAdmin, Principal, Teacher or Student role; course membership and author permissions control each action.', 'LMS অনুমতি ও TenantAdmin, Principal, Teacher বা Student ভূমিকা; প্রতিটি কাজ কোর্স সদস্যপদ ও লেখার অনুমতির ওপর নির্ভর করে।'],
            [['Choose an eligible course from Course and refresh its visible pages or your own learning progress.', 'Course থেকে যোগ্য কোর্স বেছে দৃশ্যমান তালিকা বা নিজের শেখার অগ্রগতি রিফ্রেশ করুন।'], ['Authorized staff may create/edit a course, lesson, assignment or rubric under its current course scope.', 'অনুমোদিত কর্মী বর্তমান কোর্সের অধীনে কোর্স, পাঠ, অ্যাসাইনমেন্ট বা রুব্রিক তৈরি/সম্পাদনা করুন।'], ['Review published lessons and assignment submissions; students use My Submissions and other permitted course links.', 'প্রকাশিত পাঠ ও অ্যাসাইনমেন্ট জমা দেখুন; শিক্ষার্থী My Submissions ও অনুমোদিত কোর্স লিংক ব্যবহার করুন।']],
            ['Example: a teacher publishes a course lesson while an enrolled student reviews the available lesson and submission state.', 'উদাহরণ: শিক্ষক কোর্সের পাঠ প্রকাশ করলে নিবন্ধিত শিক্ষার্থী অনুমোদিত পাঠ ও জমার অবস্থা দেখেন।'],
            ['Saved or published content and eligible progress appear for authorized course members.', 'সংরক্ষিত/প্রকাশিত বিষয় ও যোগ্য অগ্রগতি অনুমোদিত কোর্স সদস্যদের দেখা যায়।'],
            ['Saved drafts are not automatically published, and assignment completion or certificate eligibility has separate validation.', 'খসড়া সংরক্ষণে নিজে থেকে প্রকাশ হয় না; অ্যাসাইনমেন্ট সম্পন্ন বা সনদের যোগ্যতা আলাদা যাচাই হয়।'],
            ['If an expected course/lesson is missing, verify enrollment, publication and course filter before attempting a duplicate.', 'কোর্স/পাঠ না এলে নিবন্ধন, প্রকাশনা ও ফিল্টার দেখুন; নকল তৈরির আগে যাচাই করুন।'],
            [['/Learning/CourseContent', 'Course resources', 'কোর্স রিসোর্স'], ['/Learning/MySubmissions', 'My submissions', 'আমার জমা কাজ']]),
        '/auditlogs/index': taskGuide(
            ['Audit Logs', 'নিরীক্ষা লগ'],
            ['Inspect institution-scoped activity, user/entity summaries and a bounded export of filtered audit entries.', 'প্রতিষ্ঠানের কার্যক্রম, ব্যবহারকারী/সত্তার সারাংশ ও ফিল্টার করা সীমিত নিরীক্ষা তথ্য দেখুন।'],
            ['TenantAdmin or Principal access; use filters only for authorized tenant activity.', 'TenantAdmin বা Principal অনুমতি; শুধু অনুমোদিত প্রতিষ্ঠানের কার্যক্রমে ফিল্টার প্রয়োগ করুন।'],
            [['Choose the required date, entity, user or action filters under Filters.', 'Filters থেকে প্রয়োজনীয় তারিখ, সত্তা, ব্যবহারকারী বা কাজ বাছুন।'], ['Select Apply filters and inspect Activity overview and the paged Activity table.', 'Apply filters দিয়ে Activity overview ও পৃষ্ঠাভিত্তিক Activity তালিকা দেখুন।'], ['If permitted, select Export filtered CSV (max 1,000) for the current filtered scope.', 'অনুমতি থাকলে বর্তমান ফিল্টারে Export filtered CSV (max 1,000) চাপুন।']],
            ['Example: filter a recent configuration change by action and compare its responsible user with the audit detail.', 'উদাহরণ: সাম্প্রতিক সেটিং পরিবর্তন কাজ অনুযায়ী খুঁজে দায়ী ব্যবহারকারী ও নিরীক্ষা বিবরণ মিলিয়ে দেখুন।'],
            ['Authorized audit entries and activity summaries are shown without changing source records.', 'মূল রেকর্ড না বদলে অনুমোদিত নিরীক্ষা এন্ট্রি ও কার্যক্রমের সারাংশ দেখা যায়।'],
            ['Exports are bounded and privacy-sensitive; the audit viewer cannot erase or rewrite historical actions.', 'রপ্তানি সীমিত ও গোপনীয়তাসংবেদনশীল; নিরীক্ষা পৃষ্ঠা ইতিহাস মুছতে বা বদলাতে পারে না।'],
            ['If no rows appear, narrow or correct the filters and check institution scope; avoid exporting another tenant’s data.', 'রেকর্ড না এলে ফিল্টার ও প্রতিষ্ঠান যাচাই করুন; অন্য প্রতিষ্ঠানের তথ্য রপ্তানি করবেন না।'],
            [['/ImportHistory/Index', 'Import history', 'আমদানির ইতিহাস']]),
        '/importhistory/index': taskGuide(
            ['Import History', 'ডেটা আমদানির ইতিহাস'],
            ['Review institution import batches, processing outcomes and permitted failed-row diagnostics.', 'প্রতিষ্ঠানের ডেটা আমদানির ব্যাচ, ফলাফল ও অনুমোদিত ব্যর্থ সারির তথ্য দেখুন।'],
            ['TenantAdmin or Principal access; the source import must belong to the active institution.', 'TenantAdmin বা Principal অনুমতি; আমদানিটি বর্তমান প্রতিষ্ঠানের হতে হবে।'],
            [['Enter the relevant filters and select Search to locate the import batch.', 'প্রয়োজনীয় ফিল্টার দিয়ে Search চাপুন।'], ['Select an import to inspect Import details and page through its processed rows.', 'আমদানিটি বেছে Import details ও প্রক্রিয়াকৃত সারির পৃষ্ঠা দেখুন।'], ['For permitted failures, select Download failed rows CSV and correct the original source before a separate retry.', 'অনুমোদিত ব্যর্থতায় Download failed rows CSV দিয়ে মূল উৎস সংশোধন করুন, তারপর আলাদা পুনঃচেষ্টা করুন।']],
            ['Example: inspect an enrollment import with rejected rows and download only its bounded error CSV.', 'উদাহরণ: প্রত্যাখ্যাত সারিসহ ভর্তি আমদানি দেখে সীমিত ত্রুটির CSV ডাউনলোড করুন।'],
            ['The batch result and diagnostics appear; viewing history performs no new import.', 'ব্যাচের ফল ও ত্রুটির তথ্য দেখা যায়; ইতিহাস দেখলে নতুন আমদানি হয় না।'],
            ['Import history is tenant-scoped and read-only; downloaded failed rows may contain personal data.', 'আমদানির ইতিহাস প্রতিষ্ঠানসীমায় শুধু-পাঠযোগ্য; ব্যর্থ সারির ডাউনলোডে ব্যক্তিগত তথ্য থাকতে পারে।'],
            ['If a batch is absent, verify date, state and institution; do not re-import blindly after a timeout.', 'ব্যাচ না পেলে তারিখ, অবস্থা ও প্রতিষ্ঠান দেখুন; সময় শেষ হলে যাচাই ছাড়া আবার আমদানি করবেন না।'],
            [['/StudentImports/Index', 'Student imports', 'শিক্ষার্থী আমদানি']]),
        '/communicationadministration/index': taskGuide(
            ['Communication Administration', 'যোগাযোগ সেটআপ'],
            ['Manage your institution’s own Email/SMTP and SMS API gateway credentials, notice templates and delivery diagnostics.', 'প্রতিষ্ঠানের নিজস্ব Email/SMTP ও SMS API গেটওয়ে, নোটিশ টেমপ্লেট এবং ডেলিভারির তথ্য পরিচালনা করুন।'],
            ['TenantAdmin access. Each institution supplies its own approved provider, sender identity and encrypted credentials; external sending requires a separate authorized workflow.', 'TenantAdmin অনুমতি প্রয়োজন। প্রতিটি প্রতিষ্ঠান নিজস্ব অনুমোদিত প্রোভাইডার, প্রেরক ও এনক্রিপ্টেড ক্রেডেনশিয়াল দেয়; বাইরে পাঠাতে আলাদা অনুমোদিত কার্যপ্রবাহ প্রয়োজন।'],
            [['Review notice categories and save a unique authorized message template for the selected channel.', 'নোটিশ বিভাগ দেখে নির্বাচিত চ্যানেলের জন্য স্বতন্ত্র অনুমোদিত বার্তার টেমপ্লেট সংরক্ষণ করুন।'], ['Under Provider settings, choose SMS to supply a public HTTPS API endpoint, sender ID and credential; choose Email with provider code SMTP to supply host, TLS port, username, sender email/name and password. Activate and select one default gateway per channel.', 'Provider settings-এ SMS বেছে পাবলিক HTTPS API ঠিকানা, প্রেরক আইডি ও ক্রেডেনশিয়াল দিন; Email-এর SMTP প্রোভাইডারে হোস্ট, TLS পোর্ট, ব্যবহারকারীর নাম, প্রেরকের ইমেইল/নাম ও পাসওয়ার্ড দিন। চালু করে প্রতি চ্যানেলে একটি ডিফল্ট গেটওয়ে বেছে নিন।'], ['Edit the saved gateway without re-entering its secret. For Alumni event invitation counts and provider acceptance, open the published event’s Invitations panel; the generic Delivery register is a separate communication log.', 'আগের গোপন তথ্য আবার না লিখে গেটওয়ে সম্পাদনা করুন। Alumni আমন্ত্রণের সংখ্যা ও গেটওয়ে গ্রহণের ফল দেখতে প্রকাশিত অনুষ্ঠানের Invitations অংশ খুলুন; সাধারণ Delivery তালিকা আলাদা লগ।']],
            ['Example: configure your school’s own SMTP host on TLS port 587 and sender address; separately configure your SMS provider’s HTTPS URL and sender ID. No global EduOS sender is used by this setup.', 'উদাহরণ: প্রতিষ্ঠানের SMTP হোস্টে TLS পোর্ট ৫৮৭ ও প্রেরকের ইমেইল দিন; আলাদাভাবে নিজস্ব SMS প্রোভাইডারের HTTPS ঠিকানা ও প্রেরক আইডি দিন। এখানে EduOS-এর কোনো সাধারণ প্রেরক বাধ্যতামূলক নয়।'],
            ['Validated settings are saved for this institution and one active default can be selected per channel; saved credentials are not returned to the browser.', 'যাচাইকৃত সেটিং শুধু এই প্রতিষ্ঠানের জন্য সংরক্ষিত হয় এবং প্রতি চ্যানেলে একটি সক্রিয় ডিফল্ট রাখা যায়; সংরক্ষিত গোপন তথ্য ব্রাউজারে ফেরত আসে না।'],
            ['Saving a provider does not auto-send or prove delivery. Alumni invitation sending requires explicit confirmation and current channel consent; HTTP API gateways must support Bearer authentication and the documented EduOS JSON request/2xx acceptance contract.', 'প্রোভাইডার সংরক্ষণে নিজে থেকে পাঠানো শুরু হয় না বা ডেলিভারি নিশ্চিত হয় না। Alumni আমন্ত্রণে স্পষ্ট নিশ্চিতকরণ ও বর্তমান মাধ্যমের সম্মতি লাগে; HTTP API গেটওয়েতে Bearer authentication ও নির্ধারিত EduOS JSON/2xx গ্রহণের চুক্তি সমর্থিত হতে হবে।'],
            ['If setup is rejected, check public HTTPS, required SMS sender/key or complete SMTP TLS fields. After an uncertain send, reconcile delivery status before retrying.', 'সেটিং প্রত্যাখ্যাত হলে পাবলিক HTTPS, SMS প্রেরক/কী অথবা সম্পূর্ণ SMTP TLS তথ্য পরীক্ষা করুন। পাঠানোর ফল অনিশ্চিত হলে আবার চেষ্টা করার আগে ডেলিভারি মিলিয়ে নিন।'],
            [['/CommunicationWorkspace/Index', 'Communication workspace', 'যোগাযোগের পৃষ্ঠা']]),
        '/customfields/index': taskGuide(
            ['Institution Custom Fields', 'প্রতিষ্ঠানের নিজস্ব ফিল্ড'],
            ['Define additional permitted institution fields without changing canonical business entity schemas.', 'মূল ব্যবসায়িক সত্তার কাঠামো না বদলে প্রতিষ্ঠানের অনুমোদিত অতিরিক্ত ফিল্ড নির্ধারণ করুন।'],
            ['TenantAdmin access; confirm the correct field key, type, target and validation before creating a definition.', 'TenantAdmin অনুমতি; ফিল্ড তৈরির আগে সঠিক কী, ধরন, লক্ষ্য ও যাচাই নিয়ম নিশ্চিত করুন।'],
            [['Review the paged Definitions register for an existing key.', 'পৃষ্ঠাভিত্তিক Definitions তালিকায় আগের কী খুঁজুন।'], ['Use Add / edit field to enter the approved metadata and constraints, then Save.', 'Add / edit field-এ অনুমোদিত তথ্য ও শর্ত লিখে Save চাপুন।'], ['Refresh the definition list and verify the effective field before use in another workflow.', 'তালিকা রিফ্রেশ করে অন্য কাজে ব্যবহারের আগে কার্যকর ফিল্ড যাচাই করুন।']],
            ['Example: define an approved institution-only informational field with a stable key and constrained input type.', 'উদাহরণ: স্থায়ী কী ও সীমিত ইনপুট ধরনসহ প্রতিষ্ঠানের অনুমোদিত তথ্য ফিল্ড তৈরি করুন।'],
            ['The definition becomes available to supported tenant custom-field consumers.', 'নতুন ফিল্ডের সংজ্ঞা সমর্থিত প্রতিষ্ঠানের পৃষ্ঠায় ব্যবহারযোগ্য হয়।'],
            ['A field definition does not grant access to private entity data or replace existing audited business columns.', 'নিজস্ব ফিল্ডে ব্যক্তিগত তথ্যের অনুমতি পাওয়া যায় না বা নিরীক্ষিত ব্যবসায়িক কলাম বদলায় না।'],
            ['If a key conflicts, find the existing definition; verify type and validation instead of creating a duplicate.', 'কী সংঘাত হলে আগের সংজ্ঞা খুঁজুন; নকল না বানিয়ে ধরন ও যাচাই নিয়ম দেখুন।'],
            [['/TenantSettings/Index', 'Institution settings', 'প্রতিষ্ঠানের সেটিংস']]),
        '/documentadministration/index': taskGuide(
            ['Document Administration', 'ডকুমেন্ট পরিচালনা'],
            ['Manage tenant-scoped document metadata, scanned uploads, document types and reusable templates.', 'প্রতিষ্ঠানের নথির তথ্য, স্ক্যান করা আপলোড, নথির ধরন ও পুনর্ব্যবহারযোগ্য টেমপ্লেট পরিচালনা করুন।'],
            ['TenantAdmin access; only verified permitted files and the owning module’s authorization may be used for entity attachments.', 'TenantAdmin অনুমতি; যাচাইকৃত অনুমোদিত ফাইল ও মূল মডিউলের অনুমতি ছাড়া নির্দিষ্ট সত্তার নথি যুক্ত করা যাবে না।'],
            [['Use Documents to upload a permitted file for security scan and inspect pending uploads.', 'Documents থেকে অনুমোদিত ফাইল স্ক্যানের জন্য আপলোড করে অপেক্ষমাণ তালিকা দেখুন।'], ['Once eligible, save its document metadata and filter the institution register.', 'যোগ্য হলে নথির তথ্য সংরক্ষণ করে প্রতিষ্ঠানের তালিকা ফিল্টার করুন।'], ['Use Document types or Templates to manage approved reusable definitions and verify their status.', 'Document types বা Templates থেকে অনুমোদিত সংজ্ঞা/টেমপ্লেট পরিচালনা করে অবস্থা দেখুন।']],
            ['Example: upload a permitted policy PDF, wait for verification, and classify it under an approved document type.', 'উদাহরণ: অনুমোদিত নীতিমালার PDF আপলোড করে যাচাইয়ের পরে নির্ধারিত নথির ধরনে শ্রেণিবদ্ধ করুন।'],
            ['The eligible document or type/template is retained in the institution register.', 'যোগ্য নথি বা ধরন/টেমপ্লেট প্রতিষ্ঠানের তালিকায় সংরক্ষিত হয়।'],
            ['A pending or unscanned file is not a verified attachment; entity-specific private attachments require their owning module.', 'অপেক্ষমাণ বা স্ক্যানবিহীন ফাইল যাচাইকৃত সংযুক্তি নয়; নির্দিষ্ট সত্তার ব্যক্তিগত নথির জন্য মূল মডিউল ব্যবহার করুন।'],
            ['If upload is rejected, check file type/size and scan status; do not bypass review by attaching raw content elsewhere.', 'আপলোড প্রত্যাখ্যাত হলে ধরন/আকার ও স্ক্যান দেখুন; অন্যত্র কাঁচা ফাইল দিয়ে যাচাই এড়াবেন না।'],
            [['/DocumentVerification/Index', 'Protected document review', 'সুরক্ষিত নথি যাচাই']]),
        '/communicationworkspace/index': taskGuide(
            ['Communication Workspace', 'যোগাযোগের পৃষ্ঠা'],
            ['Read permitted notices and notifications, exchange scoped private messages and manage delivery preferences.', 'অনুমোদিত নোটিশ ও বিজ্ঞপ্তি দেখুন, সীমিত ব্যক্তিগত বার্তা পাঠান ও ডেলিভারির পছন্দ নির্ধারণ করুন।'],
            ['An authenticated institution user; notice publication, messaging and preferences have distinct server permissions.', 'লগইন করা প্রতিষ্ঠানের ব্যবহারকারী; নোটিশ প্রকাশ, বার্তা ও পছন্দে আলাদা সার্ভার অনুমতি প্রযোজ্য।'],
            [['Review Notices and Your notifications; mark read items when appropriate.', 'Notices ও Your notifications দেখে প্রয়োজনমতো পড়া চিহ্ন দিন।'], ['For permitted Messages, find an active institution recipient, select the conversation and send approved content or scanned attachments.', 'Messages-এ অনুমোদিত সক্রিয় প্রাপক খুঁজে কথোপকথন বেছে বৈধ বার্তা বা স্ক্যান করা সংযুক্তি পাঠান।'], ['Inspect Notification preferences or registered push devices and save only permitted changes.', 'Notification preferences বা নিবন্ধিত push devices দেখে শুধু অনুমোদিত পরিবর্তন সংরক্ষণ করুন।']],
            ['Example: read an institutional notice, reply privately to an authorized staff member and review notification preferences.', 'উদাহরণ: প্রতিষ্ঠানের নোটিশ পড়ে অনুমোদিত কর্মীকে ব্যক্তিগত উত্তর দিয়ে বিজ্ঞপ্তির পছন্দ দেখুন।'],
            ['Eligible messages and notification states appear in the relevant authorized lists.', 'যোগ্য বার্তা ও বিজ্ঞপ্তির অবস্থা সংশ্লিষ্ট অনুমোদিত তালিকায় দেখা যায়।'],
            ['A saved notice is not proof of external delivery; private attachments require scanning and institution-scoped recipients.', 'নোটিশ সংরক্ষণ মানেই বাইরে পৌঁছায়নি; ব্যক্তিগত ফাইল স্ক্যান ও প্রতিষ্ঠানসীমার প্রাপক প্রয়োজন।'],
            ['If a recipient or file is unavailable, verify active membership, permission and scan state before retrying.', 'প্রাপক বা ফাইল না পেলে আবার করার আগে সক্রিয় সদস্যপদ, অনুমতি ও স্ক্যানের অবস্থা দেখুন।'],
            [['/CommunicationAdministration/Index', 'Communication settings', 'যোগাযোগের সেটিংস']]),
    };
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
        '/inventorymovements/facilityfinance/index': taskGuide(
            ['Facilities Parts Finance', 'রক্ষণাবেক্ষণ যন্ত্রাংশের হিসাব'],
            ['Reconcile an already posted Facilities parts issue to one approved Finance expense journal, or reverse both ledgers atomically.', 'পোস্ট করা রক্ষণাবেক্ষণ যন্ত্রাংশের ইস্যু অনুমোদিত ব্যয় জার্নালে সমন্বয় করুন অথবা উভয় লেজার একই লেনদেনে প্রত্যাবর্তন করুন।'],
            ['FACILITIES, INVENTORY and FINANCE module access; Accountant/TenantAdmin prepares, independent Principal/TenantAdmin reviews; authorized campus, open period and active BDT leaf accounts.', 'FACILITIES, INVENTORY ও FINANCE মডিউল অনুমতি; Accountant/TenantAdmin প্রস্তুত করেন, আলাদা Principal/TenantAdmin যাচাই করেন; অনুমোদিত ক্যাম্পাস, খোলা হিসাবকাল ও সক্রিয় BDT পোস্টিং খাত লাগে।'],
            [['Search or select an already posted work-order stock issue and review its exact posted cost and Finance state.', 'পোস্ট করা ওয়ার্ক অর্ডারের স্টক ইস্যু খুঁজে সঠিক মূল্য ও হিসাবের অবস্থা দেখুন।'], ['Select an open period, date, repairs expense (debit), inventory asset (credit), and documented reason; submit for a different Finance reviewer to post.', 'খোলা হিসাবকাল, তারিখ, মেরামত ব্যয় (ডেবিট), স্টক সম্পদ (ক্রেডিট) ও কারণ বেছে আলাদা অনুমোদনকারীর কাছে জমা দিন।'], ['For an approved correction, prepare a reversal in a currently open period and let an independent Principal/TenantAdmin post its GL contra and stock reversal together.', 'অনুমোদিত সংশোধনে বর্তমান খোলা হিসাবকালে প্রত্যাবর্তনের অনুরোধ তৈরি করুন; আলাদা Principal/TenantAdmin একই সঙ্গে GL ও স্টক প্রত্যাবর্তন পোস্ট করবেন।']],
            ['Example: an approved filter replacement consumes BDT 250 in stock; post Dr Repairs Expense 250 / Cr Inventory Asset 250 with actual stock cost, then jointly reverse both entries if the work order is cancelled.', 'উদাহরণ: অনুমোদিত ফিল্টার পরিবর্তনে ২৫০ টাকার স্টক লাগে; প্রকৃত মূল্য অনুযায়ী মেরামত ব্যয় ডেবিট ২৫০ / স্টক সম্পদ ক্রেডিট ২৫০ পোস্ট করুন; কাজ বাতিল হলে উভয় এন্ট্রি একসঙ্গে প্রত্যাবর্তন করুন।'],
            ['The original stock issue stays immutable; Finance journal and any compensating entries carry the canonical movement and source references.', 'মূল স্টক ইস্যু অপরিবর্তিত থাকে; হিসাব জার্নাল ও সংশোধনী এন্ট্রিতে মূল মুভমেন্ট ও উৎসের রেফারেন্স থাকে।'],
            ['No automatic account guessing, direct editing of posted values or manual stock reversal after Finance linking. Another person must approve; rejected drafts do not post.', 'স্বয়ংক্রিয় ভুল খাত অনুমান, পোস্ট করা মূল্য সম্পাদনা বা Finance-এর সঙ্গে যুক্ত স্টক আলাদাভাবে প্রত্যাবর্তন করা যাবে না। আলাদা ব্যক্তি অনুমোদন করেন; প্রত্যাখ্যাত খসড়া পোস্ট হয় না।'],
            ['If posting fails, verify current stock reversal, period state, account permissions and journal row version. Retry the same unchanged request ID; do not open a duplicate journal.', 'পোস্টিং ব্যর্থ হলে স্টক প্রত্যাবর্তন, হিসাবকাল, খাতের অনুমতি ও জার্নাল সংস্করণ যাচাই করুন। অপরিবর্তিত অনুরোধ আইডি দিয়ে আবার চেষ্টা করুন; নতুন ডুপ্লিকেট জার্নাল করবেন না।'],
            [['/InventoryMovements/Transactions', 'Stock transactions', 'স্টক লেনদেন'], ['/AccountingSetup/Index', 'Accounting setup', 'হিসাব সেটআপ']]),
        '/inventorymovements/transactions/index': taskGuide(
            ['Stock Issues and Transfers', 'স্টক ইস্যু ও স্থানান্তর'], ['Prepare permitted stock transactions, source-linked Facilities parts issues and eligible in-transit transfer receipts.', 'অনুমোদিত স্টক লেনদেন, Facilities কাজের সঙ্গে সংযুক্ত যন্ত্রাংশ ইস্যু এবং পথে থাকা উপযুক্ত স্থানান্তর গ্রহণ করুন।'],
            ['INVENTORY access and TenantAdmin, Principal, InventoryManager or StoreKeeper; permitted locations and quantities are required.', 'INVENTORY অনুমতি ও TenantAdmin, Principal, InventoryManager বা StoreKeeper; অনুমোদিত স্থান ও পরিমাণ প্রয়োজন।'],
            [['Choose the transaction type, stock source/destination and authorized item quantities.', 'লেনদেনের ধরন, উৎস/গন্তব্য এবং অনুমোদিত পণ্য ও পরিমাণ বাছুন।'], ['For a Facilities parts issue, select Work-order parts issue, enter an approved service request ID, stock origin, items, lots and a reason. A different authorized Inventory user must post the draft.', 'Facilities যন্ত্রাংশ ইস্যুর জন্য Work-order parts issue বেছে অনুমোদিত সার্ভিস অনুরোধ আইডি, স্টক উৎস, পণ্য, লট ও কারণ দিন। আলাদা অনুমোদিত Inventory ব্যবহারকারী খসড়া পোস্ট করবেন।'], ['Save the draft and follow the existing authorization and posting rules.', 'খসড়া সংরক্ষণ করে বিদ্যমান অনুমোদন ও পোস্টিং নিয়ম অনুসরণ করুন।'], ['For an eligible transfer, enter its dispatch reference and Receive stock.', 'যোগ্য স্থানান্তরে ডিসপ্যাচ রেফারেন্স দিয়ে Receive stock চাপুন।']],
            ['Example: prepare a work-order parts issue for an approved service request, or receive a verified dispatch reference.', 'উদাহরণ: অনুমোদিত সার্ভিস অনুরোধের যন্ত্রাংশ ইস্যুর খসড়া বা যাচাইকৃত ডিসপ্যাচ গ্রহণ করুন।'],
            ['Authorized transactions are recorded in the canonical stock workflow.', 'অনুমোদিত লেনদেন মূল স্টক প্রক্রিয়ায় নথিভুক্ত হয়।'],
            ['Posting, reservations and receipt evidence are server-authorized. Facilities parts require both modules and an active work order; stock posting does not automatically create a Finance GL entry. Never create a second issue to fix a failed or uncertain request.', 'পোস্টিং, সংরক্ষণ ও গ্রহণের প্রমাণ সার্ভার অনুমোদন করে। Facilities যন্ত্রাংশের জন্য উভয় মডিউল এবং সক্রিয় ওয়ার্ক অর্ডার প্রয়োজন; স্টক পোস্ট করলেই Finance GL এন্ট্রি হয় না। অনিশ্চিত অনুরোধে আরেকটি ইস্যু তৈরি করবেন না।'],
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
        '/account/profile/index': {
            version: 1, reviewed: true,
            title: ['My Profile and Account Security', 'আমার প্রোফাইল ও অ্যাকাউন্ট নিরাপত্তা'],
            purpose: ['Review your own account information, manage optional MFA and active sessions, and change your password without exposing another institution session.', 'নিজের অ্যাকাউন্টের তথ্য দেখুন, ঐচ্ছিক MFA ও সক্রিয় সেশন পরিচালনা করুন এবং অন্য প্রতিষ্ঠানের সেশন প্রকাশ না করে পাসওয়ার্ড পরিবর্তন করুন।'],
            before: ['Sign in to your own account. Have your current password available for sensitive changes; MFA actions also require a current authenticator or recovery code.', 'নিজের অ্যাকাউন্টে সাইন ইন করুন। সংবেদনশীল পরিবর্তনের জন্য বর্তমান পাসওয়ার্ড রাখুন; MFA পরিবর্তনে অথেনটিকেটর বা রিকভারি কোডও প্রয়োজন।'],
            steps: [
                ['Review the profile summary and current language; edit your name, phone or address, then save only after the account loads.', 'প্রোফাইল সারাংশ ও বর্তমান ভাষা দেখুন; অ্যাকাউন্ট লোড হওয়ার পর নাম, ফোন বা ঠিকানা বদলে সংরক্ষণ করুন।'],
                ['Inspect optional MFA status. If enabled, use your current password and authenticator or recovery code to manage it; save any newly issued recovery codes privately.', 'ঐচ্ছিক MFA অবস্থা দেখুন। চালু থাকলে বর্তমান পাসওয়ার্ড ও অথেনটিকেটর বা রিকভারি কোড দিয়ে পরিচালনা করুন; নতুন রিকভারি কোড গোপনে রাখুন।'],
                ['Review active devices, revoke an authorized session when needed, or enter your current password to sign out other devices.', 'সক্রিয় ডিভাইসগুলো দেখুন, প্রয়োজনমতো অনুমোদিত সেশন বাতিল করুন অথবা বর্তমান পাসওয়ার্ড দিয়ে অন্য ডিভাইস সাইন আউট করুন।'],
                ['To change your password, enter the current password, a new password and its confirmation; expect to sign in again.', 'পাসওয়ার্ড বদলাতে বর্তমান পাসওয়ার্ড, নতুন পাসওয়ার্ড ও নিশ্চিতকরণ দিন; পরে আবার সাইন ইন করতে হবে।']
            ],
            example: ['A staff member updates a phone number, checks that only their current device is active and leaves MFA disabled until they choose to configure it.', 'একজন কর্মী ফোন নম্বর বদলে নিজের সক্রিয় ডিভাইস যাচাই করেন এবং ইচ্ছা না হওয়া পর্যন্ত MFA বন্ধ রাখেন।'],
            result: ['Only your authorized profile and session details are shown. Password and MFA changes follow server verification and may require a fresh login.', 'শুধু আপনার অনুমোদিত প্রোফাইল ও সেশনের তথ্য দেখানো হয়। পাসওয়ার্ড ও MFA পরিবর্তনে সার্ভার যাচাই হয় এবং নতুন লগইন লাগতে পারে।'],
            rules: ['Do not share passwords or recovery codes. A changed tenant/session or unavailable verification locks the old page; the Help Drawer is not an authorization grant.', 'পাসওয়ার্ড বা রিকভারি কোড কাউকে দেবেন না। প্রতিষ্ঠান বা সেশন বদলালে কিংবা যাচাই ব্যর্থ হলে পুরোনো পৃষ্ঠা বন্ধ হয়; Help Drawer কোনো অনুমতি প্রদান করে না।'],
            recovery: ['If loading fails, reconnect and reload. On a session mismatch, sign in again; do not retry a sensitive action from an old tab.', 'লোড ব্যর্থ হলে সংযোগ ঠিক করে রিলোড করুন। সেশন না মিললে আবার সাইন ইন করুন; পুরোনো ট্যাব থেকে সংবেদনশীল কাজ পুনরায় করবেন না।'],
            related: []
        },
        '/feeduecalendar/index': {
            version: 1, reviewed: true,
            title: ['Student Fee Due Calendar', 'শিক্ষার্থী ফি পরিশোধের ক্যালেন্ডার'],
            purpose: ['Review issued unpaid invoices by due month and currency, and export only the current authorized page.', 'ইস্যুকৃত বকেয়া ইনভয়েস শেষ পরিশোধের মাস ও মুদ্রা অনুযায়ী দেখুন এবং শুধু অনুমোদিত বর্তমান পৃষ্ঠা এক্সপোর্ট করুন।'],
            before: ['FINANCE access as TenantAdmin, Principal or Accountant, an active institution session and accessible campus.', 'TenantAdmin, Principal বা Accountant হিসেবে FINANCE অনুমতি, সক্রিয় প্রতিষ্ঠানের সেশন ও অনুমোদিত ক্যাম্পাস প্রয়োজন।'],
            steps: [
                ['Select a due-date range of up to 366 days, campus, optional prefix and page size.', 'সর্বোচ্চ ৩৬৬ দিনের শেষ পরিশোধের তারিখ, ক্যাম্পাস, প্রয়োজনীয় নামের শুরু ও সারির সংখ্যা বাছুন।'],
                ['Run the report and verify month/currency totals and the issued invoice page.', 'প্রতিবেদন চালিয়ে মাস/মুদ্রার মোট ও ইস্যুকৃত ইনভয়েসের পৃষ্ঠা যাচাই করুন।'],
                ['Use Previous/Next or export the verified current page CSV; print after checking the scope.', 'আগের/পরের পৃষ্ঠা দেখুন বা যাচাইকৃত বর্তমান পৃষ্ঠার CSV নিন; পরিসর দেখে প্রিন্ট করুন।']
            ],
            example: ['Fictional example: view demo invoices due in November for one permitted campus.', 'কাল্পনিক উদাহরণ: অনুমোদিত একটি ক্যাম্পাসের নভেম্বরের ডেমো ইনভয়েস দেখুন।'],
            result: ['A read-only current-obligation report; monthly totals cover all matching rows, CSV and details only the current page.', 'শুধু পড়ার বর্তমান বকেয়ার প্রতিবেদন; মাসের মোট সব মিলযুক্ত সারির, CSV ও বিবরণ শুধু বর্তমান পৃষ্ঠার।'],
            rules: ['This is not a historical as-of balance or unissued instalment forecast. Filter edits invalidate old export/print; no GL or payment is posted.', 'এটি অতীতের নির্দিষ্ট দিনের বকেয়া বা অনিষ্পন্ন কিস্তির পূর্বাভাস নয়। ফিল্টার বদলালে পুরোনো এক্সপোর্ট/প্রিন্ট বন্ধ হয়; GL বা পেমেন্ট পোস্ট হয় না।'],
            recovery: ['If access changes or a report fails validation, run it again after checking campus and dates; never export an unverified page.', 'অনুমতি বদলালে বা প্রতিবেদন যাচাই ব্যর্থ হলে ক্যাম্পাস ও তারিখ দেখে আবার চালান; যাচাই না হওয়া পৃষ্ঠা এক্সপোর্ট করবেন না।'],
            related: [['/FeeOperations/Index', 'Fees and Billing', 'ফি ও বিলিং'], ['/FeeReceivablesAging/Index', 'Receivables aging', 'বকেয়ার বয়সভিত্তিক প্রতিবেদন']]
        },
        '/feedocuments/invoice/index': {
            version: 1, reviewed: true,
            title: ['Student fee invoice document', 'শিক্ষার্থীর ফি ইনভয়েস নথি'],
            purpose: ['View or print one authorized invoice without changing its posted Finance record.', 'পোস্ট করা ফাইন্যান্স তথ্য না বদলে একটি অনুমোদিত ইনভয়েস দেখুন বা প্রিন্ট করুন।'],
            before: ['FINANCE access, an authorized student invoice link from Student Ledger and the intended institution session.', 'FINANCE অনুমতি, শিক্ষার্থী লেজারের অনুমোদিত ইনভয়েস লিংক এবং সঠিক প্রতিষ্ঠানের সেশন প্রয়োজন।'],
            steps: [
                ['Open View / Print from a student invoice and verify student, invoice number, state, dates and BDT totals.', 'শিক্ষার্থীর ইনভয়েস থেকে দেখুন / প্রিন্ট খুলে শিক্ষার্থী, নম্বর, অবস্থা, তারিখ ও টাকার মোট যাচাই করুন।'],
                ['Choose Print / Save PDF only after the authorized document loads; use the browser print dialog.', 'অনুমোদিত নথি লোড হওয়ার পর প্রিন্ট / PDF সংরক্ষণ চাপুন এবং ব্রাউজারের প্রিন্ট ডায়ালগ ব্যবহার করুন।']
            ],
            example: ['Fictional example: verify the due amount on a partially paid demo invoice before printing.', 'কাল্পনিক উদাহরণ: আংশিক পরিশোধিত ডেমো ইনভয়েসের বকেয়া যাচাই করে প্রিন্ট করুন।'],
            result: ['A read-only printable invoice; no payment or journal is posted by printing.', 'শুধু পড়া ও প্রিন্টযোগ্য ইনভয়েস; প্রিন্ট করলে পরিশোধ বা জার্নাল পোস্ট হয় না।'],
            rules: ['Cancelled/refunded invoices are not payment demands; keep private PDFs with authorized recipients.', 'বাতিল বা ফেরত দেওয়া ইনভয়েস অর্থ দাবি নয়; ব্যক্তিগত PDF শুধু অনুমোদিত প্রাপককে দিন।'],
            recovery: ['If invalid or denied, return to Student Ledger, verify institution and select an authorized invoice.', 'লিংক ভুল বা অনুমতি না থাকলে শিক্ষার্থী লেজারে ফিরে প্রতিষ্ঠান যাচাই করে অনুমোদিত ইনভয়েস বাছুন।'],
            related: [['/FeeOperations/Ledger', 'Student ledger', 'শিক্ষার্থী লেজার']]
        },
        '/feedocuments/receipt/index': {
            version: 1, reviewed: true,
            title: ['Student payment receipt document', 'শিক্ষার্থীর পরিশোধ রসিদ নথি'],
            purpose: ['Review a payment state and its invoice allocations without creating a receipt.', 'নতুন রসিদ না তৈরি করে পরিশোধের অবস্থা ও ইনভয়েস বণ্টন দেখুন।'],
            before: ['FINANCE access, an authorized student payment link from Student Ledger and the intended institution session.', 'FINANCE অনুমতি, শিক্ষার্থী লেজারের অনুমোদিত পরিশোধ লিংক এবং সঠিক প্রতিষ্ঠানের সেশন প্রয়োজন।'],
            steps: [
                ['Open View / Print from Receipt history and verify number, state, method and allocated invoices.', 'রসিদের ইতিহাস থেকে দেখুন / প্রিন্ট খুলে নম্বর, অবস্থা, মাধ্যম ও বণ্টিত ইনভয়েস যাচাই করুন।'],
                ['Print or save PDF only after load and status verification; failed payments are not proof of collection.', 'নথি লোড ও অবস্থা যাচাইয়ের পর প্রিন্ট বা PDF সংরক্ষণ করুন; ব্যর্থ পরিশোধ সংগ্রহের প্রমাণ নয়।']
            ],
            example: ['Fictional example: compare a successful demo receipt with its allocated invoice before printing.', 'কাল্পনিক উদাহরণ: সফল ডেমো রসিদ প্রিন্টের আগে বণ্টিত ইনভয়েসের সঙ্গে মিলিয়ে নিন।'],
            result: ['A read-only payment record; printing initiates no provider send or Finance posting.', 'শুধু পড়ার পরিশোধ নথি; প্রিন্টে কোনো প্রোভাইডার অনুরোধ বা ফাইন্যান্স পোস্টিং হয় না।'],
            rules: ['Only successful receipts acknowledge collection; keep private PDFs with authorized recipients.', 'শুধু সফল রসিদ সংগ্রহ স্বীকার করে; ব্যক্তিগত PDF অনুমোদিত প্রাপকের মধ্যেই রাখুন।'],
            recovery: ['If unavailable, verify institution and permissions, then reopen from Student Ledger.', 'নথি না পেলে প্রতিষ্ঠান ও অনুমতি যাচাই করে শিক্ষার্থী লেজার থেকে আবার খুলুন।'],
            related: [['/FeeOperations/Ledger', 'Student ledger', 'শিক্ষার্থী লেজার']]
        },
        '/account/login/index': {
            version: 2, reviewed: true,
            title: ['Sign in and institution selection', 'সাইন ইন ও প্রতিষ্ঠান নির্বাচন'],
            purpose: ['Sign in securely to your authorized institution without mixing institution sessions.', 'প্রতিষ্ঠানের সেশন একসঙ্গে না মিশিয়ে নিজের অনুমোদিত প্রতিষ্ঠানে নিরাপদে লগ ইন করুন।'],
            before: ['An active EduOS account, its correct username/email and password; a permitted institution membership. MFA may be required.', 'সক্রিয় EduOS অ্যাকাউন্ট, সঠিক ইউজারনেম/ইমেইল ও পাসওয়ার্ড এবং প্রতিষ্ঠানের অনুমোদিত সদস্যপদ প্রয়োজন। MFA লাগতে পারে।'],
            steps: [
                ['Enter your username/email and password, then select Sign in.', 'ইউজারনেম/ইমেইল ও পাসওয়ার্ড লিখে লগ ইন চাপুন।'],
                ['If you belong to multiple institutions, choose the intended institution from the offered list and sign in again.', 'একাধিক প্রতিষ্ঠানের সদস্য হলে দেখানো তালিকা থেকে সঠিক প্রতিষ্ঠান বেছে আবার লগ ইন করুন।'],
                ['If prompted, complete the authorized MFA challenge; then verify the institution shown in your workspace.', 'প্রয়োজন হলে অনুমোদিত MFA যাচাই শেষ করুন; তারপর ওয়ার্কস্পেসে সঠিক প্রতিষ্ঠান দেখাচ্ছে কি না দেখুন।']
            ],
            example: ['Fictional example: an administrator of Demo School and Demo College selects Demo College when offered both, then completes MFA if required.', 'কাল্পনিক উদাহরণ: Demo School ও Demo College-এর একজন প্রশাসক তালিকা থেকে Demo College বেছে প্রয়োজন হলে MFA সম্পন্ন করেন।'],
            result: ['A successful sign-in opens the authorized workspace for the selected institution; permissions remain server enforced.', 'সফল লগ ইন হলে নির্বাচিত প্রতিষ্ঠানের অনুমোদিত ওয়ার্কস্পেস খোলে; সার্ভার অনুমতি যাচাই করে।'],
            rules: ['Do not share credentials or select another person’s institution. Signing in never approves invoices, payments, grades or other business actions. MFA and role checks cannot be bypassed.', 'পাসওয়ার্ড শেয়ার করবেন না বা অন্যের প্রতিষ্ঠান বাছবেন না। লগ ইন করলেই ইনভয়েস, পেমেন্ট, নম্বর বা অন্য কাজ অনুমোদিত হয় না। MFA ও ভূমিকার অনুমতি এড়ানো যায় না।'],
            recovery: ['If sign-in fails, verify your credentials, active membership and institution choice; use Forgot password when needed. Explicitly choose an institution; changing credentials clears an earlier offer. For lockout or MFA issues, contact an authorized administrator.', 'লগ ইন ব্যর্থ হলে তথ্য, সক্রিয় সদস্যপদ ও প্রতিষ্ঠান নির্বাচন যাচাই করুন; প্রয়োজনে পাসওয়ার্ড পুনরুদ্ধার করুন। প্রতিষ্ঠান স্পষ্টভাবে বেছে নিন; লগইন তথ্য বদলালে আগের নির্বাচন মুছে যায়। লক বা MFA সমস্যায় অনুমোদিত প্রশাসকের সাহায্য নিন।'],
            related: []
        },
        '/facilityspaces/index': {
            version: 1, reviewed: true,
            title: ['Campus Facility Spaces', 'ক্যাম্পাস সুবিধা ও স্থান'],
            purpose: ['Find and maintain operational campus spaces used by Facilities work orders; this is not the academic room, hostel bed or inventory store register.', 'Facilities ওয়ার্ক অর্ডারের জন্য ক্যাম্পাসের কার্যক্রম-সংক্রান্ত স্থান খুঁজুন ও পরিচালনা করুন; এটি একাডেমিক কক্ষ, হোস্টেল শয্যা বা ইনভেন্টরি স্টোরের তালিকা নয়।'],
            before: ['FACILITIES entitlement and a permitted active campus are required. TenantAdmin, Principal and FacilitiesManager may create/edit; FacilitiesTechnician and FacilitiesApprover may search and read only. Have the authorized numeric campus ID and unique space code ready.', 'FACILITIES অনুমতি ও সক্রিয় অনুমোদিত ক্যাম্পাস প্রয়োজন। TenantAdmin, Principal ও FacilitiesManager যোগ/সম্পাদনা করতে পারেন; FacilitiesTechnician ও FacilitiesApprover শুধু খুঁজে দেখতে পারেন। অনুমোদিত ক্যাম্পাসের সংখ্যাসূচক আইডি ও স্বতন্ত্র স্থান কোড প্রস্তুত রাখুন।'],
            steps: [
                ['Enter an optional Campus ID and a code/name search, then select Search. Browse 25 results per page with Previous/Next.', 'ঐচ্ছিক ক্যাম্পাস আইডি ও কোড/নামের শব্দ লিখে খুঁজুন চাপুন। পূর্ববর্তী/পরবর্তী দিয়ে প্রতি পৃষ্ঠায় ২৫টি ফল দেখুন।'],
                ['For a new space, authorized managers enter Campus ID, a letter/number/dot/underscore/hyphen code, name, optional address and Active status.', 'নতুন স্থান যোগ করতে অনুমোদিত ব্যবস্থাপক ক্যাম্পাস আইডি, অক্ষর/সংখ্যা/ডট/আন্ডারস্কোর/হাইফেনের কোড, নাম, ঐচ্ছিক ঠিকানা ও সক্রিয় অবস্থা দিন।'],
                ['Select Save once and confirm the refreshed register; New clears the editor. Readers do not have Save/Edit controls.', 'একবার সংরক্ষণ চাপুন এবং নতুন তালিকায় ফল যাচাই করুন; নতুন চাপলে সম্পাদনা ফর্ম পরিষ্কার হয়। শুধু পাঠকের জন্য সংরক্ষণ/সম্পাদনা নেই।'],
                ['To edit an existing space, select Edit in its row, review the campus/code and update permitted fields; Save checks the original row version.', 'বিদ্যমান স্থান বদলাতে সারির সম্পাদনা চাপুন, ক্যাম্পাস/কোড যাচাই করে অনুমোদিত তথ্য বদলান; সংরক্ষণের সময় আগের সংস্করণ যাচাই হয়।'],
                ['Use Service requests to open the separate authorized work-order workflow; Print prints the current browser page without creating or approving a request.', 'সার্ভিস অনুরোধ দিয়ে আলাদা অনুমোদিত ওয়ার্ক অর্ডার পাতায় যান; প্রিন্টে বর্তমান পৃষ্ঠা ছাপে, নতুন অনুরোধ বা অনুমোদন হয় না।']
            ],
            example: ['Fictional example: a manager authorized for campus 12 creates code LAB-A, name Science Lab A and address North Block. Search LAB-A and confirm the active row; a technician may then find it for a separate service request.', 'কাল্পনিক উদাহরণ: ক্যাম্পাস ১২-এর অনুমোদিত ব্যবস্থাপক LAB-A কোড, Science Lab A নাম ও North Block ঠিকানায় স্থান যোগ করেন। LAB-A খুঁজে সক্রিয় সারি যাচাই করেন; পরে টেকনিশিয়ান আলাদা সার্ভিস অনুরোধে স্থানটি খুঁজতে পারেন।'],
            result: ['The authorized campus list shows the saved space and status. Editing preserves the original space identity; this page does not issue a work order or financial/stock posting.', 'অনুমোদিত ক্যাম্পাসের তালিকায় সংরক্ষিত স্থান ও অবস্থা দেখা যায়। সম্পাদনায় আগের স্থান পরিচয় অক্ষত থাকে; এই পৃষ্ঠা ওয়ার্ক অর্ডার বা অর্থ/স্টক পোস্টিং করে না।'],
            rules: ['A space code must be unique within its campus. Identical create retries may return the existing space, while conflicting duplicates return 409. Updates require a matching row version; only allowed campuses are readable/writable. Saving a space is not a work-order approval, Finance GL entry or stock movement.', 'একই ক্যাম্পাসে স্থান কোড স্বতন্ত্র হতে হবে। একই তথ্য পুনরায় যোগ করলে আগের স্থান ফেরত আসতে পারে; সংঘাতপূর্ণ ডুপ্লিকেটে ৪০৯ আসে। সম্পাদনায় মিল থাকা সংস্করণ প্রয়োজন এবং শুধু অনুমোদিত ক্যাম্পাস পড়া/বদলানো যায়। স্থান সংরক্ষণ ওয়ার্ক অর্ডার অনুমোদন, Finance GL বা স্টক লেনদেন নয়।'],
            recovery: ['On 403, confirm the active campus grant and role; do not try another ID. On 409, reload and inspect the current row before editing or retrying. If a save response is lost, search for the same campus/code before submitting again. Missing security token or changed institution session requires a fresh page load.', '৪০৩ হলে ক্যাম্পাস অনুমতি ও ভূমিকা যাচাই করুন; অন্য আইডি অনুমান করবেন না। ৪০৯ হলে বর্তমান সারি আবার লোড করে সম্পাদনা বা পুনরায় চেষ্টা করুন। সংরক্ষণের উত্তর না পেলে আবার জমার আগে একই ক্যাম্পাস/কোড খুঁজুন। নিরাপত্তা টোকেন না থাকলে বা প্রতিষ্ঠান সেশন বদলালে পৃষ্ঠা নতুন করে লোড করুন।'],
            related: [['/FacilityWorkOrders/Index', 'Service requests and work orders', 'সার্ভিস অনুরোধ ও ওয়ার্ক অর্ডার']]
        },
        ...administrationManuals,
        ...coreWorkflowManuals,
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
        '/researchproposals/index': {
            version: 1, reviewed: true,
            title: ['Research Proposals & Ethics', 'গবেষণা প্রস্তাব ও নৈতিক অনুমোদন'],
            purpose: ['Create campus-owned research proposals, preserve submitted versions, assign independent reviewers and record ethics decisions.', 'ক্যাম্পাসভিত্তিক গবেষণা প্রস্তাব তৈরি, জমা দেওয়া সংস্করণ সংরক্ষণ, স্বতন্ত্র পর্যালোচক নিযুক্তি ও নৈতিক সিদ্ধান্ত রেকর্ড করুন।'],
            before: ['RESEARCH entitlement, active institution membership and explicit campus access are required. Authors need a Researcher/Teacher or authorized leadership role. Coordinators, supervisors, reviewers and ethics decision-makers need their own active roles.', 'RESEARCH মডিউল, সক্রিয় প্রতিষ্ঠান সদস্যপদ ও নির্দিষ্ট ক্যাম্পাসের অনুমতি লাগবে। লেখকের গবেষক/শিক্ষক বা অনুমোদিত নেতৃত্বের ভূমিকা লাগবে। সমন্বয়কারী, তত্ত্বাবধায়ক, পর্যালোচক ও নৈতিক সিদ্ধান্তদাতার নিজস্ব সক্রিয় ভূমিকা প্রয়োজন।'],
            steps: [
                ['Choose a permitted Campus, optional academic program, title, abstract and protocol. Save draft and open it from the queue.', 'অনুমোদিত ক্যাম্পাস, চাইলে একাডেমিক প্রোগ্রাম, শিরোনাম, সারসংক্ষেপ ও পদ্ধতি লিখে খসড়া সংরক্ষণ করুন এবং তালিকা থেকে খুলুন।'],
                ['While the proposal is Draft or ChangesRequested, upload scanned private PDF/images (maximum 5 MB and five files per revision) before submitting. Authorized reviewers can download only frozen submitted versions.', 'প্রস্তাব Draft বা ChangesRequested থাকলে জমা দেওয়ার আগে স্ক্যান করা ব্যক্তিগত PDF/ছবি (প্রতি ফাইল সর্বোচ্চ ৫ এমবি, প্রতি সংস্করণে পাঁচটি) যুক্ত করুন। অনুমোদিত পর্যালোচক শুধু জমা দেওয়া স্থির সংস্করণ ডাউনলোড করতে পারবেন।'],
                ['The author selects Submit; that revision becomes immutable. A different coordinator assigns two distinct eligible campus members.', 'লেখক Submit করলে সেই সংস্করণ অপরিবর্তনীয় হয়। ভিন্ন সমন্বয়কারী ক্যাম্পাসের যোগ্য দুজন আলাদা সদস্য নিযুক্ত করেন।'],
                ['The supervisor approves or requests changes; the reviewer then recommends ethics review or requests a new revision.', 'তত্ত্বাবধায়ক অনুমোদন বা সংশোধন চান; এরপর পর্যালোচক নৈতিক যাচাই সুপারিশ বা নতুন সংস্করণ চান।'],
                ['An independent EthicsReviewer declares no conflict, selects risk, provides a reason and approves or rejects. Use History to inspect recorded transitions.', 'স্বতন্ত্র EthicsReviewer স্বার্থের সংঘাত নেই বলে ঘোষণা, ঝুঁকি ও কারণ দিয়ে অনুমোদন বা প্রত্যাখ্যান করেন। History-তে রেকর্ড দেখুন।']
            ],
            example: ['Fictional: Teacher Mina submits a campus water-quality study. Coordinator Rahim assigns Sara and Nila; after their ordered reviews an independent ethics officer approves the frozen revision.', 'কাল্পনিক: শিক্ষক মিনা ক্যাম্পাসের পানির মান নিয়ে গবেষণা জমা দেন। সমন্বয়কারী রহিম সারা ও নীলাকে নিযুক্ত করেন; তাঁদের পর্যালোচনার পর স্বতন্ত্র নৈতিক কর্মকর্তা সেই সংস্করণ অনুমোদন করেন।'],
            result: ['The proposal has a stable reference, immutable submitted revision, rowversion and append-only command history. No budget, payment or Finance posting occurs.', 'প্রস্তাবের স্থায়ী রেফারেন্স, অপরিবর্তনীয় জমা সংস্করণ, rowversion ও কার্যক্রমের ইতিহাস থাকে। বাজেট, পেমেন্ট বা Finance পোস্ট হয় না।'],
            rules: ['A creator cannot assign or approve their own work. The supervisor, reviewer and ethics approver must be independent. Campus grants and live roles are checked on the server; stale versions return 409. Private files are malware-scanned and SHA-256 verified; file access is reauthorized for every download.', 'লেখক নিজের কাজ নিযুক্ত বা অনুমোদন করতে পারবেন না। তত্ত্বাবধায়ক, পর্যালোচক ও নৈতিক অনুমোদনকারী আলাদা। ক্যাম্পাস ও বর্তমান ভূমিকা সার্ভারে যাচাই হয়; পুরোনো সংস্করণে ৪০৯ আসে। ব্যক্তিগত ফাইল ভাইরাস স্ক্যান ও SHA-256 যাচাই হয়; প্রতিবার ডাউনলোডে অধিকার নতুন করে যাচাই হয়।'],
            recovery: ['On 403/404 verify entitlement, membership, campus grants and role. On 409 reload the proposal before a new action. After an uncertain network result retry the unchanged action; do not create a second approval.', '৪০৩/৪০৪ হলে মডিউল, সদস্যপদ, ক্যাম্পাস অনুমতি ও ভূমিকা দেখুন। ৪০৯ হলে নতুন অবস্থা লোড করুন। নেটওয়ার্ক অনিশ্চিত হলে অপরিবর্তিত একই কাজ আবার দিন; নতুন অনুমোদন তৈরি করবেন না।'],
            related: [['/CareerOpportunities/Index', 'Jobs and internships', 'চাকরি ও ইন্টার্নশিপ']]
        },
        '/researchgrants/index': {
            version: 1, reviewed: true,
            title: ['Research Grants & Projects', 'গবেষণা অনুদান ও প্রকল্প'],
            purpose: ['Request and independently approve grant budgets for ethics-approved research, track milestones and close funded projects without creating Finance journals.', 'নৈতিক অনুমোদিত গবেষণার অনুদান বাজেট আবেদন ও স্বতন্ত্র অনুমোদন, মাইলস্টোন পর্যবেক্ষণ এবং Finance জার্নাল তৈরি ছাড়া প্রকল্প সমাপ্তি।'],
            before: ['RESEARCH entitlement, active membership and current campus access are required. Only the ethics-approved proposal owner may apply; a different authorized coordinator awards budgets and approves closure.', 'RESEARCH মডিউল, সক্রিয় সদস্যপদ ও ক্যাম্পাস অনুমতি প্রয়োজন। নৈতিক অনুমোদিত প্রস্তাবের মালিক আবেদন করবেন; ভিন্ন অনুমোদিত সমন্বয়কারী বাজেট ও সমাপ্তি অনুমোদন করবেন।'],
            steps: [
                ['An administrator creates a grant scheme with its currency and maximum award.', 'প্রশাসক মুদ্রা ও সর্বোচ্চ সীমাসহ অনুদান স্কিম তৈরি করবেন।'],
                ['Use the reference of an ethics-approved proposal, choose a scheme, title and requested amount, then save the draft and submit.', 'নৈতিক অনুমোদিত প্রস্তাবের রেফারেন্স, স্কিম, শিরোনাম ও অর্থ দিয়ে খসড়া সংরক্ষণ করে জমা দিন।'],
                ['An independent coordinator approves the award. The owner adds and completes milestones, then submits publication, dataset, patent, prototype or impact outcomes with an HTTPS reference or completed milestone.', 'স্বতন্ত্র সমন্বয়কারী অনুদান অনুমোদন করবেন। মালিক মাইলস্টোন সম্পন্ন করে HTTPS রেফারেন্স বা সম্পন্ন মাইলস্টোনসহ প্রকাশনা, ডেটাসেট, পেটেন্ট, প্রোটোটাইপ বা প্রভাবের ফলাফল জমা দেবেন।'],
                ['An independent coordinator verifies or rejects each submitted output and can run a campus outcome report. Complete milestones and resolve every pending output before requesting final closure.', 'স্বতন্ত্র সমন্বয়কারী প্রতিটি ফলাফল যাচাই বা প্রত্যাখ্যান করে ক্যাম্পাসভিত্তিক রিপোর্ট দেখবেন। সমাপ্তির অনুরোধের আগে মাইলস্টোন সম্পন্ন ও সব অপেক্ষমাণ ফলাফলের সিদ্ধান্ত নিন।'],
                ['For a budget change, request an amendment and obtain independent approval. Research output verification never creates a Finance posting.', 'বাজেট বদলাতে সংশোধনের অনুরোধ ও স্বতন্ত্র অনুমোদন নিন। গবেষণার ফলাফল যাচাই করলে Finance-এ কোনো লেনদেন তৈরি হয় না।']
            ],
            example: ['Fictional: A Biology proposal receives ethics approval; its owner requests a BDT 100,000 grant. A different coordinator approves BDT 80,000, followed by progress and closure.', 'কাল্পনিক: জীববিজ্ঞানের গবেষণা নৈতিক অনুমোদনের পর মালিক ১ লাখ টাকা অনুদান চান। অন্য সমন্বয়কারী ৮০ হাজার টাকা অনুমোদন করেন; এরপর অগ্রগতি ও সমাপ্তি হয়।'],
            result: ['Grant budgets retain immutable versions; submitted research outcomes preserve their original evidence claims and independent verification decisions. Payments, journals and cash remain under canonical Finance.', 'অনুদান বাজেটের অপরিবর্তনীয় সংস্করণ, জমাকৃত গবেষণা ফলাফলের মূল প্রমাণ এবং স্বতন্ত্র সিদ্ধান্ত সংরক্ষিত থাকে। পেমেন্ট, জার্নাল ও নগদ অর্থ বিদ্যমান Finance-এর অধীনে থাকে।'],
            rules: ['Maker-checker separation, live campus permission, SQL rowversion, idempotent requests and outcome review are enforced by the server. A pending outcome blocks closure; submitting output never posts money.', 'স্বতন্ত্র অনুমোদন, বর্তমান ক্যাম্পাস অনুমতি, SQL rowversion, পুনরায় একই request key ও ফলাফলের সিদ্ধান্ত সার্ভারে যাচাই হয়। অপেক্ষমাণ ফলাফল থাকলে সমাপ্তি বন্ধ থাকে; ফলাফল জমায় অর্থ পোস্ট হয় না।'],
            recovery: ['On 403/404 check active grants, roles, campus and ethics status. On 409 reload the row. On network uncertainty retry exactly the same command.', '৪০৩/৪০৪ এলে মডিউল, ভূমিকা, ক্যাম্পাস ও নৈতিক অনুমোদন যাচাই করুন। ৪০৯ হলে রিফ্রেশ করুন। নেটওয়ার্ক সমস্যায় একই তথ্য দিয়ে আবার চেষ্টা করুন।'],
            related: [['/ResearchProposals/Index', 'Ethics-approved proposals', 'নৈতিকভাবে অনুমোদিত প্রস্তাব']]
        },
        '/accreditation/index': {
            version: 1, reviewed: true,
            title: ['Accreditation & Quality Assurance', 'স্বীকৃতি ও মান নিশ্চিতকরণ'],
            purpose: ['Publish versioned standards, collect campus/program evidence, independently review frozen revisions and export scope-filtered aggregate audit results.', 'সংস্করণভিত্তিক মানদণ্ড প্রকাশ, ক্যাম্পাস/প্রোগ্রামের প্রমাণ সংগ্রহ, অপরিবর্তনীয় সংস্করণ স্বতন্ত্র পর্যালোচনা ও পরিসরভিত্তিক অডিট রিপোর্ট।'],
            before: ['ACCREDITATION entitlement, active institute membership and explicit campus access. Coordinators configure frameworks, assigned reviewers decide evidence, and authors submit their own work.', 'ACCREDITATION মডিউল, সক্রিয় সদস্যপদ ও ক্যাম্পাস অনুমতি প্রয়োজন। সমন্বয়কারী কাঠামো নির্ধারণ, পর্যালোচক প্রমাণ যাচাই, লেখক নিজের প্রমাণ জমা দেন।'],
            steps: [
                ['A coordinator creates a draft framework version, adds rubric, required evidence and validity for each standard.', 'সমন্বয়কারী খসড়া কাঠামো তৈরি করে প্রতিটি মানদণ্ডের মূল্যায়ন ও প্রয়োজনীয় প্রমাণ নির্ধারণ করবেন।'],
                ['Another coordinator independently publishes it. Published frameworks and standards are not edited; publish a new version instead.', 'অন্য সমন্বয়কারী স্বতন্ত্রভাবে প্রকাশ করবেন। প্রকাশিত কাঠামো বদলানো যাবে না; নতুন সংস্করণ তৈরি করতে হবে।'],
                ['Choose campus, optional academic program, published standard and report period. Save and submit the evidence summary.', 'ক্যাম্পাস, প্রোগ্রাম, প্রকাশিত মানদণ্ড ও সময়কাল বেছে প্রমাণের সারসংক্ষেপ জমা দিন।'],
                ['Before submitting, add scanned private PDF/images to the Draft or ChangesRequested evidence version (5 MB per file, five files per revision).', 'জমার আগে Draft বা ChangesRequested প্রমাণ সংস্করণে স্ক্যান করা ব্যক্তিগত PDF/ছবি যুক্ত করুন (প্রতি ফাইল ৫ এমবি, প্রতি সংস্করণে পাঁচটি)।'],
                ['A different coordinator assigns an eligible reviewer; the reviewer declares no conflict, then approves, rejects or asks for revision.', 'ভিন্ন সমন্বয়কারী যোগ্য পর্যালোচক নিযুক্ত করেন; পর্যালোচক স্বার্থের সংঘাত নেই জানিয়ে অনুমোদন, প্রত্যাখ্যান বা সংশোধন চান।'],
                ['Use the campus-scoped report and CSV. Print the report with the browser Save as PDF option.', 'ক্যাম্পাসভিত্তিক রিপোর্ট ও CSV দেখুন; ব্রাউজারের Save as PDF দিয়ে PDF নিন।']
            ],
            example: ['Fictional: Framework QA-2026 v1 includes LAB-SAFETY, then Main Campus Biology submits its period evidence and an independent reviewer approves.', 'কাল্পনিক: QA-2026 v1 কাঠামোর LAB-SAFETY মানদণ্ডে Main Campus Biology প্রমাণ জমা দেয়; স্বতন্ত্র পর্যালোচক অনুমোদন করেন।'],
            result: ['Each approved evidence assessment retains the exact published standard/version, frozen evidence hash, review history and expiry; exported aggregates omit private notes.', 'অনুমোদিত মূল্যায়নে মানদণ্ডের প্রকাশিত সংস্করণ, প্রমাণের hash, পর্যালোচনার ইতিহাস ও মেয়াদ থাকে; রিপোর্টে গোপন নোট থাকে না।'],
            rules: ['Published standards are immutable. Authors cannot approve their own work. Reviewer and campus rights are rechecked for each command. Scanned private PDF/images attach to the next evidence revision before submission, and every download rechecks authorization and the stored file hash.', 'প্রকাশিত মানদণ্ড অপরিবর্তনীয়। লেখক নিজের কাজ অনুমোদন করতে পারবেন না। প্রতিটি অনুরোধে ভূমিকা ও ক্যাম্পাস অধিকার যাচাই হয়। জমার আগে পরের প্রমাণ সংস্করণে স্ক্যান করা ব্যক্তিগত PDF/ছবি যুক্ত হয়; প্রতিবার ডাউনলোডে অধিকার ও ফাইল hash যাচাই হয়।'],
            recovery: ['If no standard appears, confirm independent publication. On 403/404 verify module, role and campus. On 409 reload before a new command. Repeat an uncertain request with its original key.', 'মানদণ্ড না এলে প্রকাশ হয়েছে কি না দেখুন। ৪০৩/৪০৪ হলে মডিউল, ভূমিকা ও ক্যাম্পাস যাচাই করুন। ৪০৯ হলে রিফ্রেশ করুন। অনিশ্চিত অনুরোধে আগের key ব্যবহার করুন।'],
            related: [['/ResearchProposals/Index', 'Research & Ethics', 'গবেষণা ও নৈতিক অনুমোদন']]
        },
        '/careeropportunities/index': {
            version: 1, reviewed: true,
            title: ['Career Opportunities & Applications', 'চাকরি ও ইন্টার্নশিপ আবেদন'],
            purpose: ['Create and independently publish approved employer opportunities; verified students can apply and review their own applications.', 'যাচাইকৃত নিয়োগদাতার সুযোগ তৈরি ও স্বতন্ত্রভাবে প্রকাশ করুন; যাচাইকৃত শিক্ষার্থী আবেদন ও নিজের আবেদন দেখতে পারবেন।'],
            before: ['The ALUMNI module must be enabled. TenantAdmin requires explicit campus permission, a verified employer and a separate publisher. Students require a linked active enrollment or verified completed exit in that campus.', 'ALUMNI মডিউল চালু থাকতে হবে। TenantAdmin-এর ক্যাম্পাস অনুমতি, যাচাইকৃত নিয়োগদাতা ও আলাদা প্রকাশক লাগবে। শিক্ষার্থীর সংশ্লিষ্ট ক্যাম্পাসে সক্রিয় ভর্তি বা যাচাইকৃত সমাপ্তি থাকতে হবে।'],
            steps: [
                ['TenantAdmin: search and select a verified employer, enter the title, type, deadline and optional description, then Save draft.', 'TenantAdmin: যাচাইকৃত নিয়োগদাতা খুঁজে বাছুন, পদের নাম, ধরন, শেষ সময় ও চাইলে বিবরণ দিয়ে Save draft চাপুন।'],
                ['A different TenantAdmin reviews the draft and selects Publish. Authorized staff may later select Close to stop applications.', 'অন্য TenantAdmin খসড়া দেখে Publish করবেন। পরে অনুমোদিত কর্মী Close দিয়ে আবেদন বন্ধ করতে পারবেন।'],
                ['Student: review a published open opportunity and select Apply against your matching verified campus identity.', 'শিক্ষার্থী: প্রকাশিত সক্রিয় সুযোগ দেখে নিজের সংশ্লিষ্ট ক্যাম্পাস পরিচয়ে Apply দিন।'],
                ['Student: inspect My applications; a pending application can be Withdrawn but cannot be applied to twice.', 'শিক্ষার্থী: My applications-এ অবস্থা দেখুন; বিচারাধীন আবেদন Withdraw করা যায় কিন্তু একই পদের জন্য দুইবার আবেদন করা যায় না।']
            ],
            example: ['Fictional: Example Engineering Ltd is verified for Main Campus; Rahim saves a Software Internship draft and Nila publishes it. Active student Mina applies before the deadline and sees Pending in My applications.', 'কাল্পনিক: Main Campus-এর Example Engineering Ltd যাচাইকৃত; রহিম Software Internship খসড়া করেন ও নীলা প্রকাশ করেন। সক্রিয় শিক্ষার্থী মিনা নির্ধারিত সময়ের আগে আবেদন করে My applications-এ বিচারাধীন দেখেন।'],
            result: ['The opportunity and application are recorded within the institution and campus with durable request identities. Publishing and applying do not declare a placement or affect Finance.', 'প্রতিষ্ঠান ও ক্যাম্পাসভিত্তিক সুযোগ ও আবেদন নিরাপদ অনুরোধ পরিচয়সহ সংরক্ষিত হয়। প্রকাশ বা আবেদন প্লেসমেন্ট নিশ্চিত করে না, Finance-এ পরিবর্তন আনে না।'],
            rules: ['The creator cannot publish their own draft. An employer must already be verified. Applications require authenticated linked students and are unique per opportunity/student. Deadlines, campus ownership and statuses are server-enforced.', 'খসড়া তৈরিকারী নিজে প্রকাশ করতে পারবেন না। নিয়োগদাতা আগে যাচাইকৃত হতে হবে। লগইন করা সংশ্লিষ্ট শিক্ষার্থী প্রতি পদে একবারই আবেদন করতে পারবেন। সময়সীমা, ক্যাম্পাস ও অবস্থা সার্ভার যাচাই করে।'],
            recovery: ['If no verified employer appears, check its verification and campus grant. On 403/409 refresh and use an authorized reviewer. On uncertain network response retry the same action without changing its inputs.', 'যাচাইকৃত নিয়োগদাতা না এলে যাচাই ও ক্যাম্পাস অনুমতি দেখুন। ৪০৩/৪০৯ এলে রিফ্রেশ করে অনুমোদিত পর্যালোচক নিন। নেটওয়ার্ক সমস্যা হলে একই তথ্য রেখে আবার চেষ্টা করুন।'],
            related: [['/CareerEmployers/Index', 'Employer verification', 'নিয়োগদাতা যাচাই'], ['/CareerPlacements/Index', 'Placement review', 'প্লেসমেন্ট পর্যালোচনা']]
        },
        '/careerplacements/index': {
            version: 1, reviewed: true,
            title: ['Placement Decisions & Reports', 'প্লেসমেন্ট সিদ্ধান্ত ও রিপোর্ট'],
            purpose: ['Review institution-owned applications, propose outcomes, independently approve decisions and report final placement counts.', 'প্রতিষ্ঠানের আবেদন যাচাই, ফলাফল প্রস্তাব, স্বতন্ত্র অনুমোদন এবং চূড়ান্ত প্লেসমেন্টের সংখ্যা দেখুন।'],
            before: ['Sign in as a TenantAdmin with ALUMNI entitlement and explicit campus grant. A separate TenantAdmin must approve a proposed placement outcome.', 'ALUMNI অনুমতি ও সরাসরি ক্যাম্পাস অধিকারসহ TenantAdmin হিসেবে প্রবেশ করুন। প্রস্তাবিত প্লেসমেন্ট ফলাফল ভিন্ন TenantAdmin অনুমোদন করবেন।'],
            steps: [
                ['Filter Applications by status and review the opportunity and linked student identity.', 'অবস্থা দিয়ে আবেদন খুঁজে পদ ও সংশ্লিষ্ট শিক্ষার্থীর পরিচয় যাচাই করুন।'],
                ['For a pending application choose Propose placed or Propose rejected. This alone does not finalize the outcome.', 'বিচারাধীন আবেদন থেকে Propose placed অথবা Propose rejected দিন। এতে ফলাফল চূড়ান্ত হয় না।'],
                ['A different administrator reviews the proposed outcome and selects Approve outcome.', 'অন্য প্রশাসক প্রস্তাব দেখে Approve outcome চাপবেন।'],
                ['Review Placement outcome report and Export page CSV to reconcile aggregate final counts; navigate pages for more records.', 'Placement outcome report দেখে মোট চূড়ান্ত সংখ্যা মিলিয়ে Export page CSV নিন; বেশি রেকর্ডে পৃষ্ঠা বদলান।']
            ],
            example: ['Fictional: Mina applies for the Software Internship. Rahim proposes Placed; Nila independently approves. The report then increases Placed by one, while an unapproved proposal remains Pending.', 'কাল্পনিক: মিনা Software Internship-এ আবেদন করেন। রহিম Placed প্রস্তাব করেন; নীলা স্বতন্ত্র অনুমোদন দেন। রিপোর্টে Placed এক বাড়ে, কিন্তু অননুমোদিত প্রস্তাব Pending-এই থাকে।'],
            result: ['Approved application outcomes, proposal and approval audit commands persist. Reports count only final statuses and omit student contact information.', 'অনুমোদিত ফলাফল, প্রস্তাব ও অনুমোদনের ইতিহাস সংরক্ষিত হয়। রিপোর্টে শুধু চূড়ান্ত অবস্থা গণনা হয়, শিক্ষার্থীর যোগাযোগ প্রকাশ হয় না।'],
            rules: ['Maker and checker must be different, and row versions prevent stale decisions. A withdrawn application cannot be decided. Reports and CSV are tenant/campus scoped and page-bounded; no payroll or accounting posting occurs.', 'প্রস্তাবক ও অনুমোদনকারী আলাদা হতে হবে; row version পুরোনো সিদ্ধান্ত আটকায়। প্রত্যাহৃত আবেদনে সিদ্ধান্ত হয় না। রিপোর্ট ও CSV ক্যাম্পাস সীমিত এবং পৃষ্ঠাভিত্তিক; Payroll বা Accounts পোস্ট হয় না।'],
            recovery: ['If approval is unavailable, ask a different authorized administrator. On 403 check campus grants; on 409 refresh to inspect latest state before a new command. Missing report rows require authorized scope and existing opportunities.', 'অনুমোদন না পাওয়া গেলে অন্য অনুমোদিত প্রশাসককে বলুন। ৪০৩ হলে ক্যাম্পাস অধিকার দেখুন; ৪০৯ হলে নতুন অবস্থা দেখে আবার কাজ করুন। রিপোর্ট খালি হলে অনুমোদিত পরিসর ও সুযোগ যাচাই করুন।'],
            related: [['/CareerOpportunities/Index', 'Jobs and internships', 'চাকরি ও ইন্টার্নশিপ'], ['/CareerEmployers/Index', 'Employer verification', 'নিয়োগদাতা যাচাই']]
        },
        '/careeremployers/index': {
            version: 1, reviewed: true,
            title: ['Career Employer Verification', 'ক্যারিয়ার নিয়োগদাতা যাচাই'],
            purpose: ['Register campus-owned employers and independently verify their identity for the Alumni career network. This page does not publish jobs or accept applications.', 'প্রাক্তন শিক্ষার্থীদের ক্যারিয়ার নেটওয়ার্কের জন্য ক্যাম্পাসভিত্তিক নিয়োগদাতা নিবন্ধন ও স্বাধীনভাবে যাচাই করুন। এখানে চাকরি প্রকাশ বা আবেদন গ্রহণ হয় না।'],
            before: ['Sign in as TenantAdmin in an ALUMNI-enabled institution. An authorized active campus and a second distinct TenantAdmin are required. Verify employer details outside the system before approving.', 'ALUMNI চালু প্রতিষ্ঠানে TenantAdmin হিসেবে প্রবেশ করুন। সক্রিয় অনুমোদিত ক্যাম্পাস এবং আলাদা দ্বিতীয় TenantAdmin প্রয়োজন। অনুমোদনের আগে প্রতিষ্ঠানের তথ্য স্বাধীনভাবে যাচাই করুন।'],
            steps: [
                ['Select an authorized Campus, enter Employer name (3–160 characters) and an optional HTTPS website.', 'অনুমোদিত Campus বেছে Employer name (৩–১৬০ অক্ষর) এবং চাইলে HTTPS ওয়েবসাইট লিখুন।'],
                ['Select Save draft. The employer appears as Awaiting verification; duplicate names within one campus are rejected.', 'Save draft চাপুন। নিয়োগদাতা Awaiting verification অবস্থায় দেখাবে; একই ক্যাম্পাসে একই নাম আবার দেওয়া যাবে না।'],
                ['A different TenantAdmin selects Refresh, reviews the employer and selects Verify. The creator cannot approve their own draft.', 'অন্য TenantAdmin Refresh করে নিয়োগদাতা পর্যালোচনা করে Verify চাপবেন। খসড়া তৈরিকারী নিজে অনুমোদন করতে পারবেন না।'],
                ['Choose Verified from Status to see the approved list; Export verified page CSV exports only the current verified page.', 'Status থেকে Verified বেছে অনুমোদিত তালিকা দেখুন; Export verified page CSV শুধু বর্তমান যাচাইকৃত পৃষ্ঠা রপ্তানি করে।']
            ],
            example: ['Fictional example: administrator Rahim drafts Example Engineering Ltd for Main Campus with https://example.org. Administrator Nila independently checks the employer and selects Verify. The verified entry appears in the filtered CSV.', 'কাল্পনিক উদাহরণ: প্রশাসক রহিম Main Campus-এ Example Engineering Ltd এবং https://example.org দিয়ে খসড়া করেন। প্রশাসক নীলা আলাদাভাবে যাচাই করে Verify দেন। যাচাইকৃত নিয়োগদাতা CSV তালিকায় আসে।'],
            result: ['Verified employers are recorded with an immutable decision command and can be listed or exported by authorized TenantAdmins. No student contacts, applications, notifications, invoices or stock movements are created.', 'যাচাইকৃত নিয়োগদাতা সিদ্ধান্তের রেকর্ডসহ সংরক্ষিত হয় এবং অনুমোদিত TenantAdmin তালিকা/CSV দেখতে পারেন। শিক্ষার্থীর যোগাযোগ, আবেদন, বিজ্ঞপ্তি, ইনভয়েস বা স্টক পরিবর্তন হয় না।'],
            rules: ['Verification requires a second administrator and the current row version. A repeated identical request is idempotent; a stale or reused request returns 409. This initial workflow has no edit, rejection or job-publication action. Finance and Inventory remain untouched.', 'যাচাইয়ে আলাদা প্রশাসক ও বর্তমান row version লাগে। একই অনুরোধ পুনরায় নিরাপদ; পুরোনো বা অন্য কাজে ব্যবহৃত অনুরোধে ৪০৯ আসে। এই পর্যায়ে Edit, Reject বা চাকরি প্রকাশের ব্যবস্থা নেই। Finance ও Inventory অপরিবর্তিত।'],
            recovery: ['If Campus is empty, check ALUMNI entitlement and campus access. If a duplicate is reported, review the existing draft. After a timeout retry the same action; on 403/409 refresh and request another authorized approver. Incorrect drafts must not be verified; await the correction workflow.', 'Campus না এলে ALUMNI অনুমতি ও ক্যাম্পাস অধিকার দেখুন। ডুপ্লিকেট হলে আগের খসড়া যাচাই করুন। টাইমআউট হলে একই কাজ পুনরায় দিন; ৪০৩/৪০৯ হলে Refresh করে অন্য অনুমোদিত যাচাইকারীর সাহায্য নিন। ভুল খসড়া যাচাই করবেন না; সংশোধনের ব্যবস্থা আসা পর্যন্ত অপেক্ষা করুন।'],
            related: [['/Alumni/Index', 'Alumni directory and engagement', 'প্রাক্তন শিক্ষার্থী তালিকা ও অনুষ্ঠান']]
        },
        '/alumni/index': {
                  "version": 1,
                  "reviewed": true,
                  "title": [
                            "Alumni & Graduate Directory",
                            "প্রাক্তন শিক্ষার্থী ও স্নাতক তালিকা"
                  ],
                  "purpose": [
                            "Manage independent directory-sharing and invitation consents, published-event Email/SMS invitations, event RSVPs and attendance without changing graduation evidence.",
                            "উত্তীর্ণ হওয়ার প্রমাণ না বদলে তালিকায় প্রকাশ ও আমন্ত্রণের পৃথক সম্মতি, প্রকাশিত অনুষ্ঠানের Email/SMS আমন্ত্রণ, RSVP ও উপস্থিতি পরিচালনা করুন।"
                  ],
                  "before": [
                            "An authenticated account in an ALUMNI-enabled institution with a verified completed exit is required. TenantAdmin must have an explicit active grant for each managed campus; another authorized administrator publishes event drafts.",
                            "ALUMNI চালু থাকা প্রতিষ্ঠান ও যাচাইকৃত উত্তীর্ণ রেকর্ড প্রয়োজন। পরিচালিত প্রত্যেক ক্যাম্পাসে TenantAdmin-এর সক্রিয় অনুমতি থাকতে হবে; অনুষ্ঠান খসড়া প্রকাশে আলাদা অনুমোদিত প্রশাসক প্রয়োজন।"
                  ],
                  "steps": [
                            [
                                      "Choose Verified graduation. Independently select directory sharing, email invitation and/or SMS invitation consent; supply the required contact address for any selected invitation channel, then Save preferences.",
                                      "Verified graduation বেছে তালিকায় প্রকাশ, ইমেইল আমন্ত্রণ ও/অথবা SMS আমন্ত্রণের পৃথক সম্মতি দিন; বাছা মাধ্যমে প্রয়োজনীয় যোগাযোগ তথ্য লিখে Save preferences চাপুন।"
                            ],
                            [
                                      "To revoke a purpose, uncheck only its consent and save. Revoking directory sharing clears RSVPs; contact details are erased when no remaining purpose needs them. Verified completion remains.",
                                      "কোনো ব্যবহারের সম্মতি তুলতে শুধু তার টিক তুলে সংরক্ষণ করুন। তালিকার সম্মতি প্রত্যাহারে RSVP মুছে যায়; অন্য সম্মতিতে প্রয়োজন না থাকলে যোগাযোগের তথ্য মুছে যাবে। উত্তীর্ণ হওয়ার প্রমাণ থাকবে।"
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
                                      "TenantAdmin: for a published future event open Invitations. Check the separate Email and SMS eligible, remaining, provider-accepted, review and suppressed counts. If your institution configured an active default gateway, explicitly confirm Send next 5 for one channel; no recipient contacts are shown.",
                                      "TenantAdmin: প্রকাশিত ভবিষ্যৎ অনুষ্ঠানে Invitations খুলুন। ইমেইল ও SMS-এর পৃথক যোগ্য, বাকি, গেটওয়ে-গৃহীত, যাচাইযোগ্য ও বাতিল সংখ্যা দেখুন। প্রতিষ্ঠানের সক্রিয় ডিফল্ট গেটওয়ে থাকলে একটি মাধ্যমে Send next 5 নিশ্চিত করুন; প্রাপকের যোগাযোগ দেখানো হয় না।"
                            ],
                            [
                                      "TenantAdmin: Search the opted-in directory and Export current page CSV; Export page engagement CSV contains aggregate event counts, not attendee contacts.",
                                      "TenantAdmin: সম্মতিপ্রাপ্ত তালিকায় Search করে Export current page CSV নিন; Export page engagement CSV-তে অংশগ্রহণকারীর যোগাযোগ নয়, অনুষ্ঠানের মোট সংখ্যা থাকে।"
                            ]
                  ],
                  "example": [
                            "Fictional example: Amina opts in for Email invitations for a reunion on 8 November 2026. After another admin publishes it, an authorized administrator checks Email invitation eligibility and confirms a batch of up to five. SMTP accepts her message; this does not prove inbox delivery.",
                            "কাল্পনিক উদাহরণ: আমিনা ৮ নভেম্বর ২০২৬-এর পুনর্মিলনীর জন্য Email আমন্ত্রণে সম্মতি দিলেন। অন্য প্রশাসক অনুষ্ঠান প্রকাশের পর অনুমোদিত প্রশাসক ইমেইল আমন্ত্রণের যোগ্যতা দেখে সর্বোচ্চ পাঁচজনের ব্যাচ নিশ্চিত করলেন। SMTP বার্তা গ্রহণ করেছে; ইনবক্সে পৌঁছানো নিশ্চিত নয়।"
                  ],
                  "result": [
                            "Consent controls restricted directory visibility and separate outbound channels. Invitation summary shows aggregate provider-accepted, review and suppressed attempts without recipient identities; RSVP, attendance and current-page CSV remain independently scoped. UI headings follow the selected language; timestamps remain UTC.",
                            "সম্মতি সীমিত তালিকায় প্রকাশ ও পৃথক আমন্ত্রণ মাধ্যম নিয়ন্ত্রণ করে। আমন্ত্রণ সারাংশে পরিচয় ছাড়া গেটওয়ে-গৃহীত, যাচাইযোগ্য ও বাতিল সংখ্যা থাকে; RSVP, উপস্থিতি ও CSV নিজ নিজ সীমায় থাকে। UI ভাষা অনুযায়ী দেখায়; সময় UTC থাকে।"
                  ],
                  "rules": [
                            "Directory consent never authorizes Email/SMS; each channel starts OFF and needs distinct consent and a valid destination. Sending requires an active tenant-owned default gateway, explicit administrator confirmation and campus grant. The database allows at most one attempt per graduate/event/channel; acceptance is not proof of delivery and uncertain attempts must not be resent automatically.",
                            "তালিকার সম্মতি Email/SMS অনুমতি নয়; প্রতিটি মাধ্যম শুরুতে বন্ধ এবং পৃথক সম্মতি ও ঠিকানা প্রয়োজন। পাঠাতে প্রতিষ্ঠানের সক্রিয় ডিফল্ট গেটওয়ে, প্রশাসকের নিশ্চিতকরণ ও ক্যাম্পাস অনুমতি লাগে। এক উত্তীর্ণ/অনুষ্ঠান/মাধ্যমে একবারই চেষ্টা হয়; গ্রহণ মানেই ডেলিভারি নয় এবং অনিশ্চিত চেষ্টা স্বয়ংক্রিয়ভাবে পুনরায় পাঠানো যাবে না।"
                  ],
                  "recovery": [
                            "If Invitations shows no gateway, configure your tenant's default under Communication Administration. Review uncertain/processing attempts with your provider before any manual reconciliation; repeating the same request ID only reads its prior batch. Do not create a fresh request to bypass an unknown outcome. If Attendance Check-in/Undo times out, retry the same action on the unchanged roster row; its request ID is retained until confirmed success. For missing graduates/events check verification, module, campus grant, consent and timing.",
                            "Invitations-এ গেটওয়ে না থাকলে Communication Administration-এ প্রতিষ্ঠানের ডিফল্ট সেট করুন। অনিশ্চিত/চলমান প্রচেষ্টার ক্ষেত্রে পুনর্মিলনের আগে প্রোভাইডারের রেকর্ড দেখুন; একই request ID দিলে শুধু পুরোনো ব্যাচ দেখা যায়। অজানা ফল এড়াতে নতুন অনুরোধ করবেন না। Attendance Check-in/Undo টাইমআউট হলে একই roster row-তে একই কাজ আবার চেষ্টা করুন; সফলতা নিশ্চিত না হওয়া পর্যন্ত request ID অপরিবর্তিত থাকে। শিক্ষার্থী/অনুষ্ঠান না এলে যাচাই, মডিউল, ক্যাম্পাস অনুমতি, সম্মতি ও সময় দেখুন।"
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
        '/feediscountattributionreview/index': {
            version: 1, reviewed: true,
            title: ['Historical Discount Attribution', 'পুরোনো ফি ছাড়ের খাত যাচাই'],
            purpose: ['Review possible historical fee-head discount attribution signals without changing issued invoices, receipts or journals.', 'ইস্যু করা ইনভয়েস, রসিদ বা জার্নাল না বদলে পুরোনো ফি খাতের ছাড়ের সম্ভাব্য অসামঞ্জস্য যাচাই করুন।'],
            before: ['FINANCE entitlement and TenantAdmin, Principal or Accountant role; choose an authorized campus and up to 90 consecutive calendar days.', 'FINANCE মডিউল এবং TenantAdmin, Principal বা Accountant অনুমতি প্রয়োজন; অনুমোদিত ক্যাম্পাস ও সর্বোচ্চ ৯০ দিনের সময়সীমা বাছুন।'],
            steps: [
                ['Select Campus, From, To and page size, then choose Review invoices.', 'ক্যাম্পাস, শুরু, শেষ ও সারির সংখ্যা বেছে ইনভয়েস যাচাই চাপুন।'],
                ['Inspect each invoice, its journal link, candidate amount and evidence status. A candidate is not proof of an incorrect historical discount.', 'প্রতিটি ইনভয়েস, জার্নাল সংযোগ, সম্ভাব্য অঙ্ক ও প্রমাণের অবস্থা দেখুন। সম্ভাব্য অঙ্ক পুরোনো ছাড় ভুল ছিল—এমন প্রমাণ নয়।'],
                ['Use Previous or Next for verified pages; export the current page CSV or print only after a valid report loads.', 'যাচাইকৃত পৃষ্ঠায় আগের বা পরের চাপুন; সঠিক প্রতিবেদন লোড হলে শুধু বর্তমান পৃষ্ঠার CSV বা প্রিন্ট নিন।']
            ],
            example: ['Fictional example: inspect November tuition discounts in one authorized campus and flag a cross-head signal for a separate Finance investigation.', 'কাল্পনিক উদাহরণ: অনুমোদিত ক্যাম্পাসের নভেম্বরের টিউশন ছাড় দেখে ভিন্ন খাতের ইঙ্গিত আলাদা Finance তদন্তে পাঠান।'],
            result: ['A read-only, page-bounded invoice evidence list with candidate amounts and journal-link indicators; no correction is posted.', 'শুধু পড়ার উপযোগী পৃষ্ঠাভিত্তিক ইনভয়েসের প্রমাণ, সম্ভাব্য অঙ্ক ও জার্নাল সংযোগ দেখা যায়; কোনো সংশোধন পোস্ট হয় না।'],
            rules: ['Mutable discount awards are not reliable historical entitlement snapshots. Mixed currencies are not summed. Changing filters or institution sessions invalidates old rows and exports; never use this report to issue refunds or reverse posted Finance records.', 'পরিবর্তনযোগ্য ছাড়ের নিয়ম পুরোনো প্রাপ্যতার নিশ্চিত স্ন্যাপশট নয়। ভিন্ন মুদ্রার অঙ্ক যোগ হয় না। ফিল্টার বা প্রতিষ্ঠানের সেশন বদলালে পুরোনো সারি ও এক্সপোর্ট বাতিল হয়; এই প্রতিবেদন দিয়ে ফেরত বা পোস্ট করা Finance তথ্য রিভার্স করবেন না।'],
            recovery: ['If a campus is unavailable, access is denied or totals fail verification, reselect the authorized scope and rerun. Escalate any suspected historic discrepancy through approved Finance adjustment review.', 'ক্যাম্পাস না পাওয়া গেলে, অনুমতি না থাকলে বা মোট না মিললে অনুমোদিত পরিসর আবার বেছে প্রতিবেদন চালান। পুরোনো অসামঞ্জস্য সন্দেহ হলে অনুমোদিত Finance adjustment review অনুসরণ করুন।'],
            related: [['/FeeOperations/Index', 'Fees and Billing', 'ফি ও বিলিং'], ['/FinanceAdjustments/Policies', 'Finance policies', 'Finance নীতিমালা']]
        },
        '/feeinvoicecoverage/index': {
            version: 1, reviewed: true,
            title: ['Monthly Invoice Coverage', 'মাসিক ইনভয়েস কভারেজ'],
            purpose: ['Compare current active enrolments with non-cancelled invoices in one billing month without posting or creating financial records.', 'একটি বিলিং মাসে সক্রিয় ভর্তি ও বাতিল নয় এমন ইনভয়েস তুলনা করুন; কোনো আর্থিক রেকর্ড তৈরি বা পোস্ট হয় না।'],
            before: ['FINANCE entitlement and TenantAdmin, Principal or Accountant role; select an authorized academic batch and billing month.', 'FINANCE মডিউল ও TenantAdmin, Principal বা Accountant অনুমতি; অনুমোদিত ব্যাচ ও বিলিং মাস নির্বাচন করুন।'],
            steps: [
                ['Choose Academic batch and Billing month, optionally filter Coverage or Student prefix, then select Run.', 'একাডেমিক ব্যাচ ও বিলিং মাস বেছে প্রয়োজনে কভারেজ বা শিক্ষার্থীর খোঁজ দিয়ে চালান চাপুন।'],
                ['Review Eligible, Billed, Unbilled and Review totals for the whole batch, then inspect the current student page.', 'পুরো ব্যাচের যোগ্য, বিল হয়েছে, বিল হয়নি ও যাচাই প্রয়োজন মোট দেখুন; তারপর বর্তমান শিক্ষার্থী পৃষ্ঠা যাচাই করুন।'],
                ['Use Previous or Next for verified pages; Export page CSV or Print page only after the report loads.', 'যাচাইকৃত পৃষ্ঠা দেখতে আগের বা পরের চাপুন; প্রতিবেদন লোড হওয়ার পরই বর্তমান পৃষ্ঠার CSV বা প্রিন্ট নিন।']
            ],
            example: ['Fictional example: select a permitted batch and November 2026, filter Unbilled and review missing monthly invoices before running the separate billing workflow.', 'কাল্পনিক উদাহরণ: অনুমোদিত ব্যাচে নভেম্বর ২০২৬ বেছে বিল হয়নি ফিল্টার দিয়ে মাসিক ইনভয়েসের ঘাটতি যাচাই করে আলাদা বিলিং কাজ করুন।'],
            result: ['A read-only, authorized coverage snapshot with full-batch totals and page-bounded student rows is shown.', 'সম্পূর্ণ ব্যাচের মোট ও বর্তমান পৃষ্ঠার শিক্ষার্থীসহ অনুমোদিত কভারেজ প্রতিবেদন দেখা যায়।'],
            rules: ['Review can mean duplicate invoices, inconsistent headers or missing lines. Coverage does not prove fee-structure compliance, instalment agreement or GL posting; CSV and print include only the verified current page.', 'যাচাই প্রয়োজন মানে দ্বৈত ইনভয়েস, হেডার অসামঞ্জস্য বা লাইন অনুপস্থিত হতে পারে। কভারেজ ফি কাঠামো, কিস্তি চুক্তি বা GL পোস্টিংয়ের প্রমাণ নয়; CSV ও প্রিন্ট শুধু যাচাইকৃত বর্তমান পৃষ্ঠার।'],
            recovery: ['If filters change or a request fails, the old report and export are cleared. Check the batch, campus permission and month, then Run again; do not assume a missing row authorizes new billing.', 'ফিল্টার বদলালে বা অনুরোধ ব্যর্থ হলে পুরোনো প্রতিবেদন ও এক্সপোর্ট বন্ধ হয়। ব্যাচ, ক্যাম্পাস অনুমতি ও মাস যাচাই করে আবার চালান; অনুপস্থিত সারি দেখে সরাসরি নতুন বিল করবেন না।'],
            related: [['/FeeOperations/Billing', 'Monthly billing', 'মাসিক বিলিং'], ['/FeeInstallmentProjection/Index', 'Future fee projection', 'ভবিষ্যৎ ফি প্রক্ষেপণ']]
        },
        '/feeinstallmentprojection/index': {
            version: 1, reviewed: true,
            title: ['Future Fee Projection', 'ভবিষ্যৎ ফি প্রক্ষেপণ'],
            purpose: ['Preview up to twelve months of non-binding gross fee estimates using current enrolments and an applicable monthly fee structure.', 'বর্তমান ভর্তি ও প্রযোজ্য মাসিক ফি কাঠামো দিয়ে সর্বোচ্চ বারো মাসের আনুমানিক মোট ফি দেখুন।'],
            before: ['FINANCE entitlement and TenantAdmin, Principal or Accountant role; an active batch and eligible effective monthly fee structure are required.', 'FINANCE মডিউল ও TenantAdmin, Principal বা Accountant অনুমতি; সক্রিয় ব্যাচ ও কার্যকর মাসিক ফি কাঠামো প্রয়োজন।'],
            steps: [
                ['Select Academic batch, its applicable fee structure, First month and three, six, nine or twelve months.', 'একাডেমিক ব্যাচ, প্রযোজ্য ফি কাঠামো, প্রথম মাস এবং তিন, ছয়, নয় বা বারো মাস বাছুন।'],
                ['Select Run and inspect configured due dates, fee heads, gross per student, current cohort gross and observed invoice totals.', 'চালান চাপুন; শেষ তারিখ, ফি খাত, শিক্ষার্থীপ্রতি মোট, ব্যাচের মোট ও পর্যবেক্ষিত ইনভয়েস যাচাই করুন।'],
                ['After verified results load, Export CSV or Print; changing any filter clears the previous result.', 'যাচাইকৃত ফল লোড হলে CSV বা প্রিন্ট নিন; কোনো ফিল্টার বদলালে আগের ফল বাতিল হয়।']
            ],
            example: ['Fictional example: choose an authorized 2026 batch and monthly tuition structure, start November 2026 for three months and compare projected gross with observed invoices.', 'কাল্পনিক উদাহরণ: অনুমোদিত ২০২৬ ব্যাচ ও মাসিক টিউশন কাঠামো বেছে নভেম্বর থেকে তিন মাসের অনুমান ও পর্যবেক্ষিত ইনভয়েস তুলনা করুন।'],
            result: ['A read-only month-by-month gross estimate appears. No instalment schedule, invoice, receipt or GL entry is created.', 'মাসভিত্তিক আনুমানিক মোট হিসাব দেখা যায়। কিস্তি, ইনভয়েস, রসিদ বা GL এন্ট্রি তৈরি হয় না।'],
            rules: ['Estimates exclude discounts, fines, transfers and future enrolment changes. Observed invoices are separate batch totals, not invoices matched to the selected structure. Unspecified due dates are not invented.', 'অনুমানে ছাড়, জরিমানা, স্থানান্তর ও ভবিষ্যৎ ভর্তি পরিবর্তন নেই। পর্যবেক্ষিত ইনভয়েস নির্বাচিত কাঠামোর সঙ্গে মিলানো নয়, আলাদা ব্যাচের মোট। অনির্ধারিত শেষ তারিখ অনুমান করা হয় না।'],
            recovery: ['If no structure is offered, verify batch scope and effective dates. On a scope mismatch or denied response, the previous projection and export are cleared; reselect and Run.', 'কাঠামো না এলে ব্যাচের পরিসর ও কার্যকর তারিখ দেখুন। পরিসর না মিললে বা অনুমতি না থাকলে আগের প্রক্ষেপণ ও এক্সপোর্ট বন্ধ হয়; নতুন করে নির্বাচন করে চালান।'],
            related: [['/FeeOperations/Structures', 'Fee structures', 'ফি কাঠামো'], ['/FeeInvoiceCoverage/Index', 'Monthly invoice coverage', 'মাসিক ইনভয়েস কভারেজ']]
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
        '/facilityvisitors/index': {
            version: 5, reviewed: true,
            title: ['Campus Visitor Preregistration', 'ক্যাম্পাস ভিজিটর প্রি-রেজিস্ট্রেশন'],
            purpose: ['Register a minimal visitor appointment, obtain independent host approval, record badge access and log bounded incidents.', 'সীমিত ভিজিটর তথ্য নিবন্ধন, স্বতন্ত্র হোস্ট অনুমোদন, ব্যাজসহ প্রবেশ ও সীমিত সময়ের ঘটনা নথিভুক্ত করুন।'],
            before: ['Sign in with FACILITIES access and an active campus membership. The desk needs FacilitiesManager, Principal or TenantAdmin permission. The separate host must have an active campus grant.', 'FACILITIES অনুমতি ও সক্রিয় ক্যাম্পাস সদস্যপদ দিয়ে সাইন ইন করুন। ডেস্কে FacilitiesManager, Principal অথবা TenantAdmin অনুমতি প্রয়োজন। আলাদা হোস্টের সক্রিয় ক্যাম্পাস অনুমতি থাকতে হবে।'],
            steps: [
                ['The desk enters campus, selects an eligible host, provides a visitor name and purpose, then chooses a future UTC visit window.', 'ডেস্ক কর্মকর্তা ক্যাম্পাস, অনুমোদিত হোস্ট, ভিজিটরের নাম ও উদ্দেশ্য এবং ভবিষ্যৎ সময় নির্বাচন করেন।'],
                ['Select Preregister and review the PendingHost row. The separate host approves or rejects; rejection needs a reason.', 'প্রি-রেজিস্টার করে PendingHost সারি দেখুন। আলাদা হোস্ট অনুমোদন বা কারণসহ প্রত্যাখ্যান করবেন।'],
                ['During the approved visit window, enter an unused badge code and select Check in. After the visitor leaves, select Check out.', 'অনুমোদিত সময়ে অব্যবহৃত ব্যাজ কোড দিয়ে প্রবেশ নথিভুক্ত করুন। ভিজিটর চলে গেলে প্রস্থান নথিভুক্ত করুন।'],
                ['Desk staff may enter an authorized campus in Current campus visitor occupancy and select View occupancy. This shows active and overdue checked-in counts and the optional campus limit without names or badges. Investigate overstays; there is no automatic checkout.', 'ডেস্ক কর্মকর্তা অনুমোদিত ক্যাম্পাসের উপস্থিতি দেখতে ক্যাম্পাস আইডি দিয়ে উপস্থিতি দেখুন চাপবেন। এখানে নাম বা ব্যাজ ছাড়া উপস্থিত ও সময় পার হওয়া সংখ্যা এবং কনফিগার করা সীমা দেখায়। সময় পার হলে ডেস্ক যাচাই করবে; স্বয়ংক্রিয় প্রস্থান হয় না।'],
                ['For a checked-in or recently checked-out visitor, enter a factual 10–200 character incident reason and select Report incident; use History to review authorized events. Never enter identity numbers or health details.', 'প্রবেশ করা বা সদ্য প্রস্থান করা ভিজিটরের ঘটনায় ১০–২০০ অক্ষরের তথ্যভিত্তিক কারণ দিয়ে ঘটনা নথিভুক্ত করুন; ইতিহাসে অনুমোদিত ঘটনা দেখুন। পরিচয় নম্বর বা স্বাস্থ্যতথ্য লিখবেন না।'],
                ['Authorized desk staff may choose a campus and a UTC-converted local window up to 31 days, then Export CSV. The 500-row summary omits visitor name, purpose, badge, host and incident narrative. Every successful snapshot is access-audited; export requires an authenticated, CSRF-protected POST.', 'অনুমোদিত ডেস্ক কর্মকর্তা ক্যাম্পাস ও সর্বোচ্চ ৩১ দিনের স্থানীয় সময়সীমা দিয়ে CSV নিন। সর্বোচ্চ ৫০০ সারির সারাংশে ভিজিটরের নাম, উদ্দেশ্য, ব্যাজ, হোস্ট বা ঘটনার বিবরণ নেই। প্রতিটি সফল এক্সপোর্টের অ্যাক্সেস অডিট সংরক্ষিত হয়; CSRF-সুরক্ষিত POST প্রয়োজন।'],
                ['Refresh the authorized list after any conflict or uncertain network result; never guess another campus or visitor ID.', 'সংঘাত বা নেটওয়ার্কে অনিশ্চয়তায় অনুমোদিত তালিকা রিফ্রেশ করুন; অন্য ক্যাম্পাস বা ভিজিটর আইডি অনুমান করবেন না।']
            ],
            example: ['Fictional example: a campus desk preregisters a guest for a meeting with an eligible staff host. The host approves, the desk assigns badge GUEST-01, and the desk records checkout.', 'কাল্পনিক উদাহরণ: ক্যাম্পাস ডেস্ক একজন অতিথির মিটিং নিবন্ধন করে। হোস্ট অনুমোদন দেন, ডেস্ক GUEST-01 ব্যাজ দিয়ে প্রবেশ ও পরে প্রস্থান নথিভুক্ত করে।'],
            result: ['The visit and each command are persisted with rowversion and append-only event history. A checked-in badge cannot be assigned to another active visit at the same campus. A configured campus occupancy ceiling also rejects new admission when full.', 'ভিজিট ও প্রতিটি সিদ্ধান্ত সংস্করণসহ সংরক্ষিত হয় এবং অপরিবর্তনীয় ইতিহাস থাকে। একই ক্যাম্পাসে সক্রিয় ব্যাজ অন্য প্রবেশে ব্যবহার করা যায় না। কনফিগার করা উপস্থিতির সীমা পূর্ণ হলে নতুন প্রবেশ প্রত্যাখ্যাত হয়।'],
            rules: ['Only name and purpose are collected; no ID number, phone, photo or document. Server checks tenant, campus, active host, maker-checker, idempotency and concurrency. Incident reporting is append-only and desk-only within 24 hours of checkout; host history does not expose incident narrative. An optional campus occupancy ceiling (1–5000) can be configured through versioned Tenant Settings and is enforced on check-in; absent configuration preserves existing admission behavior. Optional Facilities.Visitor.MaxVisitMinutes.{CampusId} (15–10080, default 10080) and Facilities.Visitor.EarlyCheckInMinutes.{CampusId} (0–60, default 30) use existing non-sensitive tenant settings; malformed values deny booking or entry. This is not physical anti-tailgating or a complete independently approved site policy. Retention deletion is not enabled; only the PII-minimized, manager-only, bounded CSV summary is available.', 'শুধু নাম ও উদ্দেশ্য নেওয়া হয়; পরিচয় নম্বর, ফোন, ছবি বা নথি নয়। সার্ভার প্রতিষ্ঠান, ক্যাম্পাস, সক্রিয় হোস্ট, স্বতন্ত্র অনুমোদন, পুনরাবৃত্ত অনুরোধ ও সমসাময়িক পরিবর্তন যাচাই করে। ঘটনা নথিভুক্তি অপরিবর্তনীয় এবং প্রস্থানের ২৪ ঘণ্টার মধ্যে ডেস্কে সীমাবদ্ধ; হোস্ট ঘটনার বিবরণ পড়তে পারেন না। ক্যাম্পাসভিত্তিক উপস্থিতির সীমা (১–৫০০০) বিদ্যমান সংস্করণ-নিয়ন্ত্রিত Tenant Settings-এ দেওয়া থাকলে প্রবেশে তা কার্যকর হয়; সেটিং না থাকলে আগের নিয়ম বহাল থাকে। ঐচ্ছিক Facilities.Visitor.MaxVisitMinutes.{CampusId} (১৫–১০০৮০, ডিফল্ট ১০০৮০) ও Facilities.Visitor.EarlyCheckInMinutes.{CampusId} (০–৬০, ডিফল্ট ৩০) বিদ্যমান অসংবেদনশীল সেটিংস ব্যবহার করে; ভুল মানে নিবন্ধন বা প্রবেশ বন্ধ হয়। এটি শারীরিক টেইলগেটিং প্রতিরোধ বা পূর্ণ স্বতন্ত্র-অনুমোদিত সাইট নীতি নয়। মেয়াদোত্তীর্ণ তথ্য অপসারণ এখনো চালু নয়; শুধু অনুমোদিত ডেস্কের সীমিত পরিচয়বিহীন CSV সারাংশ আছে।'],
            recovery: ['403 means the current actor, campus or host lacks authorization. 409 means a stale row, reused command, visit window, active badge conflict, full occupancy or malformed configured limit. Reload before retrying. Do not bypass a denied host approval.', '৪০৩ হলে বর্তমান ব্যবহারকারী, ক্যাম্পাস বা হোস্টের অনুমতি নেই। ৪০৯ হলে পুরোনো সারি, পুনর্ব্যবহৃত কমান্ড, সময়, ব্যাজের সংঘাত, উপস্থিতির সীমা পূর্ণ বা ভুল সীমা কনফিগার করা হয়েছে। পুনরায় চেষ্টার আগে রিলোড করুন। প্রত্যাখ্যাত হোস্ট অনুমোদন এড়িয়ে যাবেন না।'],
            related: [['/FacilityWorkOrders/Index', 'Work orders', 'ওয়ার্ক অর্ডার'], ['/FacilitySpaces/Index', 'Facility spaces', 'ক্যাম্পাস স্থান']]
        },
        '/facilityworkorders/index': {
            version: 9, reviewed: true,
            title: ['Campus Service Requests and Work Orders', 'ক্যাম্পাস সার্ভিস অনুরোধ ও ওয়ার্ক অর্ডার'],
            purpose: ['Submit campus maintenance requests, track independent approval, technician work, SLA and verified closure.', 'ক্যাম্পাস রক্ষণাবেক্ষণের অনুরোধ, স্বতন্ত্র অনুমোদন, টেকনিশিয়ানের কাজ, SLA ও যাচাইকৃত সমাপ্তি পরিচালনা করুন।'],
            before: ['The FACILITIES module must be enabled. Use Find spaces to choose an active campus space from your current campus grants. Requester, FacilitiesManager, FacilitiesApprover and FacilitiesTechnician actions require their respective roles.', 'FACILITIES মডিউল চালু থাকতে হবে। Find spaces দিয়ে বর্তমান ক্যাম্পাস অনুমতির মধ্যে সক্রিয় স্থান নির্বাচন করুন। অনুরোধকারী, FacilitiesManager, FacilitiesApprover ও FacilitiesTechnician-এর নিজ নিজ ভূমিকার অনুমতি থাকতে হবে।'],
            steps: [
                ['In New service request, search campus/space by name or code, choose an authorized active space from the paged results, then enter Category, Priority and Issue summary and Submit request.', 'নতুন সার্ভিস অনুরোধে ক্যাম্পাস/স্থান নাম বা কোড দিয়ে খুঁজুন, পৃষ্ঠাভিত্তিক তালিকা থেকে অনুমোদিত সক্রিয় স্থান বাছুন, তারপর বিভাগ, অগ্রাধিকার ও সমস্যার সারাংশ দিয়ে জমা দিন।'],
                ['Find and Open the request. A different manager selects Triage request and a future SLA deadline; an independent approver then Approves or Rejects.', 'অনুরোধ খুঁজে খুলুন। আলাদা ম্যানেজার ভবিষ্যৎ SLA সময় দিয়ে প্রাথমিক যাচাই করেন; স্বতন্ত্র অনুমোদনকারী অনুমোদন বা প্রত্যাখ্যান করেন।'],
                ['The manager enters start/end times first, finds campus-authorized technicians available in that window, selects one and applies the schedule. Changing either time clears the choice and requires a new check. Changing an existing schedule also requires a 10-500 character reason. The assigned technician Starts work, may Pause with a reason and Resume, then Submits completion evidence.', 'ম্যানেজার আগে কাজের শুরু ও শেষ সময় দেন, সেই সময়ে উপলব্ধ ক্যাম্পাস-অনুমোদিত টেকনিশিয়ান খুঁজে নির্বাচন করে সময়সূচি সংরক্ষণ করেন। সময় বদলালে আগের নির্বাচন মুছে যায় এবং আবার যাচাই করতে হয়। বিদ্যমান সময়সূচি পরিবর্তনে ১০-৫০০ অক্ষরের কারণও দিতে হবে। নির্ধারিত টেকনিশিয়ান কাজ শুরু, কারণসহ বিরতি ও পুনরায় শুরু করে সমাপ্তির প্রমাণ জমা দেন।'],
                ['For an unstarted, unassigned Approved order, a different manager may Request cancellation with a 10-500 character reason. An independent approver then Approves or Rejects cancellation. Release an existing schedule with a reason before requesting cancellation.', 'কাজ শুরু হয়নি ও কাউকে দেওয়া হয়নি এমন Approved অর্ডারে আলাদা ম্যানেজার ১০-৫০০ অক্ষরের কারণ দিয়ে বাতিলের আবেদন করতে পারেন। স্বতন্ত্র অনুমোদনকারী বাতিল অনুমোদন বা প্রত্যাখ্যান করেন। সময়সূচি থাকলে আগে কারণ দিয়ে সেটি বাতিল করতে হবে।'],
                ['An independent approver chooses Verify closure or Return for correction with a reason. After a return, the technician corrects the work and submits fresh evidence for verification.', 'স্বতন্ত্র অনুমোদনকারী সমাপ্তি যাচাই অথবা কারণসহ সংশোধনের জন্য ফেরত দেন। ফেরত এলে টেকনিশিয়ান সংশোধন করে নতুন প্রমাণ জমা দেন।'],
                ['Managers can open an approved, unstarted work order they did not request, enter a future booking window, search available Approved campus vendors, and propose the selected vendor with a 10-500 character reason. A different FacilitiesApprover approves or rejects it after a fresh supplier and overlap check. An independent approver can release an unstarted approved booking with a reason. Rejected/released bookings may be proposed again. These actions never post stock or Finance.', 'ম্যানেজার নিজের অনুরোধ নয় এমন অনুমোদিত ও শুরু না হওয়া অর্ডারে আগে ভবিষ্যৎ বুকিং সময় দিয়ে উপলব্ধ অনুমোদিত ক্যাম্পাস সরবরাহকারী খুঁজে নির্বাচন করেন, তারপর ১০-৫০০ অক্ষরের কারণ দিয়ে প্রস্তাব দেন। আলাদা FacilitiesApprover সরবরাহকারীর অনুমোদন ও সময়ের সংঘাত আবার যাচাই করে অনুমোদন বা প্রত্যাখ্যান করেন। কাজ শুরুর আগে স্বতন্ত্র অনুমোদনকারী কারণ দিয়ে অনুমোদিত বুকিং মুক্ত করতে পারেন। প্রত্যাখ্যাত বা মুক্ত বুকিং পুনরায় প্রস্তাব করা যায়। এতে স্টক বা হিসাব পোস্ট হয় না।'],
                ['Vendor booking and technician scheduling must have an overlapping time window. A disjoint booking or reschedule is rejected before approval/commit, even if the supplier and technician are individually eligible; adjust the window or independently release/rebook the vendor. Preview results are advisory.', 'সরবরাহকারী বুকিং ও টেকনিশিয়ানের সময়সূচির অন্তত কিছু সময় মিলতে হবে। সরবরাহকারী ও টেকনিশিয়ান আলাদাভাবে অনুমোদিত হলেও সম্পূর্ণ আলাদা সময়সীমার বুকিং বা পুনঃসময়সূচি অনুমোদন/সংরক্ষণের আগে প্রত্যাখ্যাত হবে; সময় পরিবর্তন করুন অথবা স্বতন্ত্র অনুমোদনে বুকিং মুক্ত করে পুনরায় দিন। উপলব্ধতার ফল চূড়ান্ত অনুমোদন নয়।'],
                ['The assigned technician starts only after the scheduled start and before its end. If a vendor booking is Approved, its own start/end window must also be active. The server checks both windows again at commit; an early or late Start returns 409 without creating an event. Live account, tenant membership, campus grant and scoped technician role are rechecked before Start, Pause, Resume, Complete and command replay. A revoked technician cannot submit or replay completion; ask a manager to restore access or arrange an authorized reassignment.', 'নির্ধারিত টেকনিশিয়ান সময়সূচির শুরু হওয়ার পরে এবং শেষ হওয়ার আগে কাজ শুরু করবেন। সরবরাহকারীর বুকিং অনুমোদিত থাকলে তার শুরু ও শেষ সময়ের মধ্যেও থাকতে হবে। সংরক্ষণের আগে সার্ভার উভয় সময় আবার যাচাই করে; আগে বা দেরিতে শুরু করলে ৪০৯ আসে এবং কোনো নতুন ঘটনা সংরক্ষিত হয় না। কাজ শুরু, বিরতি, পুনরায় শুরু, সমাপ্তি ও কমান্ড পুনরায় পাঠানোর আগে সক্রিয় অ্যাকাউন্ট, প্রতিষ্ঠানের সদস্যপদ, ক্যাম্পাস অনুমতি ও টেকনিশিয়ান ভূমিকা আবার যাচাই হয়। অনুমতি বাতিল হলে আগের সমাপ্তি জমা বা পুনরায় পাঠানো যাবে না; ম্যানেজারের কাছে অনুমতি পুনর্বহাল বা অনুমোদিত পুনঃনিয়োগ চাইতে হবে।'],
                ['Use Search, overdue filter, dashboard, history and authorized CSV or Print to review progress.', 'খোঁজ, বিলম্বিত কাজের ফিল্টার, ড্যাশবোর্ড, ইতিহাস ও অনুমোদিত CSV বা প্রিন্টে অগ্রগতি যাচাই করুন।']
            ],
            example: ['Fictional example: search for Building A, choose its permitted campus space and report a broken hallway light. A manager sets tomorrow as SLA, a different approver approves, a technician repairs it, and the verifier returns unsafe wiring with a reason. The technician fixes it and resubmits before final closure.', 'কাল্পনিক উদাহরণ: Building A খুঁজে অনুমোদিত ক্যাম্পাসের স্থান বেছে করিডরের বাতি নষ্ট হওয়ার অনুরোধ দিন। ম্যানেজার পরদিনের SLA দেন, অন্য অনুমোদনকারী অনুমোদন করেন, টেকনিশিয়ান মেরামত করেন। তারের ত্রুটি থাকলে যাচাইকারী কারণ দিয়ে ফেরত দেন; সংশোধনের পর পুনরায় প্রমাণ জমা দিয়ে কাজ বন্ধ হয়।'],
            result: ['The request and work order show the latest state; the append-only history retains both completion submissions and the return reason. Returned work is not counted as closed; approved cancellation is terminal and excluded from overdue-open totals. SLA late closure is measured at independent verification, not at the earlier technician completion submission.', 'অনুরোধ ও ওয়ার্ক অর্ডারে বর্তমান অবস্থা এবং ইতিহাসে উভয় সমাপ্তির প্রমাণ ও ফেরতের কারণ থাকে। ফেরত কাজ সমাপ্ত হিসেবে গণনা হয় না; অনুমোদিত বাতিল চূড়ান্ত এবং বিলম্বিত খোলা কাজের হিসাবে থাকে না। SLA সময়সীমা পেরিয়ে স্বতন্ত্র যাচাই হলে কাজ বিলম্বে বন্ধ হিসেবে গণনা হয়, টেকনিশিয়ানের আগের সমাপ্তি জমা দেওয়ার সময় দিয়ে নয়।'],
            rules: ['Request maker, triage manager, approver and assigned technician have separate controls. Return and each cancellation decision need a 10-500 character reason. The cancellation maker cannot approve their own request; scheduled work must first be released, and started work cannot be cancelled. An unchanged schedule cannot be resubmitted as a new decision. Availability is advisory: a competing schedule can still return 409 at save; own-order rescheduling excludes only the authorized current order. Duplicate command IDs cannot repeat the action; stale row versions fail. Vendor availability preview excludes overlapping approved bookings but is advisory; proposals are not booked until a different approver revalidates the supplier and time window; a second independent decision is required to release an approved unstarted booking. An approved vendor booking does not authorize work before its start; a technician schedule does not authorize early entry either. This page does not issue spare parts or post Finance GL.', 'অনুরোধকারী, ট্রায়াজ ম্যানেজার, অনুমোদনকারী ও নির্ধারিত টেকনিশিয়ানের আলাদা অনুমতি রয়েছে। ফেরত ও বাতিলের প্রতিটি সিদ্ধান্তে ১০-৫০০ অক্ষরের কারণ প্রয়োজন। আবেদনকারী নিজের বাতিল অনুমোদন করতে পারবেন না; নির্ধারিত কাজ আগে সময়সূচি থেকে ছাড়তে হবে এবং শুরু হওয়া কাজ বাতিল করা যাবে না। একই সময়সূচি নতুন সিদ্ধান্ত হিসেবে আবার জমা দেওয়া যাবে না। উপলব্ধতার তালিকা সাময়িক; অন্য কাজ আগে নির্ধারিত হলে সংরক্ষণের সময় ৪০৯ আসতে পারে। পুনঃনির্ধারণে শুধু নিজের অনুমোদিত বর্তমান অর্ডার বাদ যায়। একই কমান্ড পুনরায় কাজ করে না; পুরোনো সংস্করণ বাতিল হয়। সরবরাহকারীর উপলব্ধতার তালিকা অন্য অনুমোদিত বুকিংয়ের সময়ের সংঘাত বাদ দেয়, তবে সাময়িক; আলাদা অনুমোদনকারী সরবরাহকারী ও সময় পুনরায় যাচাই করে অনুমোদন না দেওয়া পর্যন্ত বুকিং নয়; অনুমোদিত ও শুরু না হওয়া বুকিং মুক্ত করতেও আলাদা সিদ্ধান্ত প্রয়োজন। অনুমোদিত সরবরাহকারী বুকিংয়ের শুরুর আগে কাজ করা যাবে না; টেকনিশিয়ানের সময়সূচির আগেও শুরু করা যাবে না। এই পৃষ্ঠায় খুচরা যন্ত্রাংশ ইস্যু বা Finance GL পোস্ট হয় না।'],
            recovery: ['If Find spaces shows no matching location, check search text, page and active campus grant; do not guess numeric IDs. Retry a failed lookup before submitting. A 403 means insufficient role or campus access; 409 means reload the latest row or resolve a technician schedule overlap. If a submission outcome is uncertain, retry unchanged details; editing them is blocked until you reload and verify history. After a confirmed input-validation rejection, correct the fields and resubmit. Do not create a second request to bypass an uncertain retry. If the institution session changes or the server revokes role/campus access (403), the page clears its data and disables actions; confirm access and reload. If a refreshed list excludes the selected request or cannot load, its old actions and history are cleared.', 'Find spaces-এ স্থান না পেলে খোঁজার লেখা, পৃষ্ঠা ও সক্রিয় ক্যাম্পাস অনুমতি যাচাই করুন; সংখ্যাসূচক আইডি অনুমান করবেন না। খোঁজ ব্যর্থ হলে জমার আগে আবার চেষ্টা করুন। ৪০৩ মানে ভূমিকা বা ক্যাম্পাস অনুমতি নেই; ৪০৯ হলে নতুন তথ্য লোড করুন বা টেকনিশিয়ানের সময়ের সংঘাত ঠিক করুন। জমার ফল অনিশ্চিত হলে একই তথ্য দিয়ে পুনরায় চেষ্টা করুন; তথ্য বদলাতে হলে আগে পুনরায় লোড করে ইতিহাস যাচাই করতে হবে। ইনপুট যাচাইয়ে নিশ্চিতভাবে বাতিল হলে তথ্য ঠিক করে আবার জমা দিতে পারবেন। অনিশ্চিত পুনরায় চেষ্টায় দ্বিতীয় অনুরোধ তৈরি করবেন না। প্রতিষ্ঠান সেশন বদলালে বা সার্ভার ভূমিকা/ক্যাম্পাস অনুমতি বাতিল করলে (৪০৩) পৃষ্ঠার তথ্য মুছে কাজ বন্ধ হয়; অনুমতি যাচাই করে নতুন করে লোড করুন। নতুন তালিকায় নির্বাচিত অনুরোধ না থাকলে বা তালিকা লোড না হলে পুরোনো কাজের বোতাম ও ইতিহাস মুছে যায়।'],
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
    if (typeof module !== 'undefined' && module.exports) module.exports = { guideFor, coverage, normalizeRoute, validateGuide, helpContextStatus };
    if (typeof document === 'undefined') return;

    const trigger = document.getElementById('pageContextHelp');
    const dialog = document.getElementById('pageHelpDrawer');
    const title = document.getElementById('pageHelpTitle');
    const content = document.getElementById('pageHelpContent');
    const close = document.getElementById('pageHelpClose');
    if (!trigger || !dialog || !title || !content || !close) return;
    // A manual is a view of the *current* authorized document, not a reusable
    // cross-session panel. The existing shared session verifier is authoritative.
    const tenant = document.body.dataset.authTenantId || '';
    const session = document.body.dataset.authSessionReference || '';
    const shield = document.getElementById('eduosSessionShield');
    let invalidated = false;
    let opening = false;
    let fallback = false;
    let bn = false;
    let labels = sectionLabels.en;
    let guide = null;
    function helpContextStatus(tenantId, sessionId, currentTenant, currentSession, transitioning, shieldVisible) {
        if (tenantId !== currentTenant || sessionId !== currentSession || transitioning) return 'changed';
        return shieldVisible ? 'verifying' : 'ready';
    }
    function status() {
        if (invalidated) return 'changed';
        return helpContextStatus(tenant, session,
            document.body.dataset.authTenantId || '',
            document.body.dataset.authSessionReference || '',
            typeof window.EduOSSessionTransitionPending === 'function' &&
                window.EduOSSessionTransitionPending(),
            !!shield && !shield.hidden);
    }
    function clearDrawer() {
        content.replaceChildren();
        if (dialog.open) {
            if (typeof dialog.close === 'function' && !fallback) dialog.close();
            else { dialog.removeAttribute('open'); dialog.hidden = true; }
        }
    }
    function currentContext() {
        const state = status();
        if (state === 'ready') return true;
        clearDrawer();
        if (state === 'changed') {
            invalidated = true;
            trigger.disabled = true;
        }
        return false;
    }
    function syncLocale() {
        const locale = document.documentElement.lang || 'en-BD';
        bn = locale.toLowerCase().startsWith('bn');
        labels = sectionLabels[bn ? 'bn' : 'en'];
        guide = guideFor(window.location.pathname, locale);
        dialog.lang = locale;
        trigger.title = guide?.purpose || labels.missing;
        trigger.setAttribute('aria-label', bn ? 'এই পৃষ্ঠার সম্পূর্ণ নির্দেশিকা' : 'Full user manual for this page');
        close.setAttribute('aria-label', bn ? 'নির্দেশিকা বন্ধ করুন' : 'Close user manual');
    }
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
    function dismiss() {
        if (typeof dialog.close === 'function' && !fallback) dialog.close();
        else { dialog.removeAttribute('open'); dialog.hidden = true; if (status() === 'ready') trigger.focus(); }
    }
    async function open() {
        if (opening || !currentContext()) return;
        opening = true;
        trigger.disabled = true;
        try {
            // Fail closed if the authenticated verifier is unavailable; never
            // render an old guide while a session verification is in flight.
            if (session) {
                if (typeof window.EduOSVerifySessionContext !== 'function') return;
                await window.EduOSVerifySessionContext();
            }
            if (!currentContext()) return;
            syncLocale();
            render();
            fallback = typeof dialog.showModal !== 'function';
            if (!fallback) dialog.showModal();
            else { dialog.hidden = false; dialog.setAttribute('open', ''); }
            close.focus();
        } catch {
            clearDrawer();
        } finally {
            opening = false;
            if (!invalidated) trigger.disabled = false;
        }
    }
    syncLocale();
    trigger.addEventListener('click', () => { void open(); });
    close.addEventListener('click', dismiss);
    dialog.addEventListener('close', () => { if (status() === 'ready') trigger.focus(); });
    dialog.addEventListener('click', event => { if (event.target === dialog) dismiss(); });
    dialog.addEventListener('keydown', event => {
        if (event.key === 'Escape' && fallback) { event.preventDefault(); dismiss(); }
        if (event.key !== 'Tab' || !fallback) return;
        const focusable = [...dialog.querySelectorAll('button, a[href]')].filter(el => !el.hidden);
        if (!focusable.length) return;
        if (event.shiftKey && document.activeElement === focusable[0]) { event.preventDefault(); focusable.at(-1).focus(); }
        else if (!event.shiftKey && document.activeElement === focusable.at(-1)) { event.preventDefault(); focusable[0].focus(); }
    });
    function reconcile() { if (dialog.open || opening) currentContext(); }
    window.addEventListener('pageshow', reconcile);
    window.addEventListener('focus', reconcile);
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') reconcile();
    });
    if (typeof MutationObserver === 'function') {
        const sessionObserver = new MutationObserver(reconcile);
        sessionObserver.observe(document.body, { attributes: true,
            attributeFilter: ['data-auth-tenant-id', 'data-auth-session-reference'] });
        if (shield) sessionObserver.observe(shield, { attributes: true, attributeFilter: ['hidden'] });
        const languageObserver = new MutationObserver(() => {
            if (!currentContext()) return;
            syncLocale();
            if (dialog.open) render();
        });
        languageObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['lang'] });
    }
})();
