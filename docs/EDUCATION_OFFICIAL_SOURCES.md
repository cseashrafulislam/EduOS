# Official educational sources in EduOS (2026-11-12)

## Implemented scope
The reviewed `EduOS.App/wwwroot/data/official-education-sources-2026.json` manifest contains **27 curated official publisher LANDING PAGES**, including NCTB 2026 textbook shelves (pre-primary, primary, secondary, Ebtedayi, Dakhil, technical index and HSC), teacher guides, and NTRCA school/school-2/college syllabus and notice pages.

Two existing module controllers expose the **same shared read-only view**:
- `/Library/OfficialBooks` (only LIBRARY-entitled, authorized users)
- `/Learning/OfficialBooks` (only LMS-entitled, authorized users)

Library Digital/Study and LMS Index/Course Content provide direct links. Search filters in the shared client UI show only reviewed `https://nctb.gov.bd`, `https://nctb.portal.gov.bd`, or `https://ntrca.gov.bd` publisher pages. The external publisher hosts PDFs; users can download on the publisher site. External links open in new tabs without referrer or opener. Source-page availability can change, so page/edition accuracy must be reconfirmed periodically.

## Copyright and security constraints
**Nothing here is a copied PDF**. Government publication and free-to-read status do not establish a right to rehost/redistribute all book PDFs in a commercial multi-tenant service. No material is bulk-downloaded, republished or exposed as an EduOS-owned file. NTRCA syllabus pages are not mislabeled as NTRCA-authored textbooks; paid coaching notes, board questions and third-party guidebooks are NOT copied. All source entries declare `localCopy: false`.

The existing canonical `LibraryDigitalResourceService` and private `FileAsset` workflow remains the only path for institution-authorized file uploads: scanned/size-limited assets, curriculum association, publisher/right-holder identification, licence basis/evidence, explicit administrator review, and existing campus/member access controls. Do **not** let the source manifest skip publication rights review or copy a PDF into Git/history.

Similarly, instructors can use canonical LMS LessonResource `ExternalUrl` for a reviewed official source, subject to current permissions. Do not auto-assign a book to students/classes merely because the title matches a curriculum, or bypass enrollment, consent or content review.

## Future import: requires verified redistribution rights
If permission is obtained, an authorized ingestion operator should separately: record precise license and permitted campuses/duration; download through a vetted pinned-source allowlist with size/content/MIME checks and virus scanning; compute SHA-256 and provenance; store content outside Git in private object/file storage through `FileAsset`; review before publication; associate to the correct tenant/curriculum; implement expiry/revocation and an auditable resync process. No new second Library/LMS file table is warranted.

## QA
Validate the static catalog's 27 unique IDs, official HTTPS origins, 2026 textbook coverage, NTRCA syllabus coverage, no `localCopy:true`, no unapproved direct binary links, role/module-gated routes and both shared navigation entry points. Existing Library/LMS uploads, publishing, privacy, historical access and migrations must remain unchanged.
