# Bangladesh study collections — Issue #124 / Task B2

## Canonical architecture

No second book catalogue or QuestionBank. `Document` holds a reviewable,
version-specific exam classification (`SSC`, `HSC`, `UniversityAdmission`,
`NTRCAStudy`), board/institute code, syllabus year/version, HTTPS syllabus source,
optional previous-paper year, and a short educational explanation. `Book` owns
bibliographic facts; FileAsset owns scanned private bytes; the existing Library
human review owns publication authority. Existing LMS Quiz/Assessment modules
continue owning exam attempts, marking, results and certificates.

The study page is a server-filtered read model over currently approved, clean,
active, tenant/campus-entitled library publications. No resource is reuploaded,
cloned or auto-authorized by categorization. An exam code alone is never a
government-approval or NTRCA-approved-book claim.

`StudyBookmark` contains only `TenantId`, `UserId`, `DocumentId` and standard
audit/concurrency fields, with a permanent unique composite key. It contains no
copy of the file, note, topic, student identity or entitlement. Bookmark toggles
recheck live publication access; saved references are returned only if the
original is still readable by the current user.

## বাংলা ব্যবহার নির্দেশিকা

1. **অ্যাডমিন:** ডিজিটাল লাইব্রেরিতে বিদ্যমান বই নির্বাচন করে বৈধ ফাইল আপলোড
   করুন। প্রয়োজন হলে SSC/HSC/বিশ্ববিদ্যালয় ভর্তি/শুধু NTRCA অধ্যয়ন নির্বাচন করুন।
2. বোর্ড/প্রতিষ্ঠান, সিলেবাসের বছর ও সংস্করণ, মূল HTTPS সিলেবাস লিংক লিখুন।
   আগের বছরের প্রশ্ন হলে প্রশ্নের বছর দিন। উৎস এবং কপিরাইটের প্রমাণ দিতে হবে।
3. রিসোর্স `Pending` অবস্থায় যাবে। অ্যাডমিনের স্বতন্ত্র অনুমোদন ছাড়া পড়া যাবে না।
   সংশোধিত সংস্করণ অনুমোদিত না হওয়া পর্যন্ত আগের অনুমোদিত সংস্করণ সচল থাকবে।
4. **শিক্ষার্থী/শিক্ষক/সংযুক্ত অভিভাবক:** বাংলাদেশ অধ্যয়ন সংগ্রহে পরীক্ষা,
   বোর্ড, সিলেবাসের বছর, অধ্যায়, টপিক দিয়ে খুঁজুন। `সংরক্ষণ` দিয়ে ব্যক্তিগত
   বুকমার্ক রাখুন। প্রত্যাহার/মেয়াদ শেষ হলে সেটি আর পড়া যাবে না।
5. NTRCA শুধু অধ্যয়নের উপকরণ; নিয়োগ, MPO, কর্মসংস্থান বা অনুমোদনের ভুয়া দাবি নয়।
   বাণিজ্যিক বইয়ের অননুমোদিত PDF আপলোড করা যাবে না।

## English operator guidance

Admins classify an existing scanned Library Document with a verified exam
track, syllabus year/version, board and HTTPS primary syllabus link. Upload is
pending until rights approval. Readers use `/Library/Study` with bounded search,
filters and session-safe bookmarks. This is study content, not NTRCA employment
or recruitment. Do not ingest commercial copyrighted PDFs without permission.

## Deployment, test and rollback

- Migration `20261109010000_BangladeshStudyCollections` is additive and keeps all
  legacy documents with null exam fields. No automated guessed backfill.
- New bookmarks use tenant/document FKs and a unique tenant/user/document index.
  Historical files remain inaccessible when rights, campus, scan or approval
  status changes. No offline persistence or personalized cache is introduced.
- Before production: SQL migration + repeat migration smoke, model-drift check,
  idempotent bookmark and authorization tests, actual browser with tenant switch,
  institutional rights audit, backup/restore and owner UAT.
- Down migration removes exam metadata/bookmarks and **loses user data**; preserve
  a backup/export first and prefer a corrective forward migration.
