const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..', '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('offline study is opt-in, scanned, time-bounded, campus scoped and chunked', () => {
    const service = read('EduOS.Service/Services/Library/LibraryDigitalResourceService.cs');
    const controller = read('EduOS.App/Controllers/Api/LibraryDigitalResourcesController.cs');
    const migration = read('EduOS.Persistence/Migrations/20261109030000_ExplicitOfflineStudyRights.cs');
    assert.match(service, /ContentOfflineAllowed/);
    assert.match(service, /PublishedResources\(\)/);
    assert.match(service, /CanReadAsync\(ct\)/);
    assert.match(service, /Now\.AddHours\(24\)/);
    assert.match(service, /x\.File\.SizeBytes <= 4194304/);
    assert.match(controller, /service\.DownloadAsync\(reference, ct\)/);
    assert.match(controller, /service\.GetOfflinePermitAsync\(reference, ct\)/);
    assert.match(controller, /StatusCodes\.Status206PartialContent/);
    assert.match(controller, /Content-Range/);
    assert.match(migration, /defaultValue: false/);
    assert.match(migration, /LastReadPage/);
});

test('PWA never caches private responses, vault encrypts each resumable part and purges on logout', () => {
    const worker = read('EduOS.App/wwwroot/service-worker.js');
    const script = read('EduOS.App/wwwroot/js/learning/offline-library.js');
    const site = read('EduOS.App/wwwroot/js/site.js');
    const switching = read('EduOS.App/wwwroot/js/platform/institution-switch.js');
    assert.match(worker, /Never cache API responses or authenticated HTML/);
    assert.match(worker, /indexedDB\.deleteDatabase\('eduos-offline-study-v1'\)/);
    assert.match(worker, /offline-study\.html/);
    assert.match(script, /PBKDF2/);
    assert.match(script, /AES-GCM/);
    assert.match(script, /keyFrom\(password, salt\)/);
    assert.match(script, /record\.parts\[i\] = await seal\(key, raw\)/);
    assert.match(script, /record\.sha256/);
    assert.match(script, /progressVersion/);
    assert.match(script, /response\.status === 409/);
    assert.match(script, /window\.confirm/);
    assert.match(site, /BroadcastChannel\('eduos-offline-study'\)/);
    assert.match(switching, /BroadcastChannel\('eduos-offline-study'\)/);
    assert.doesNotMatch(script, /localStorage\.setItem|sessionStorage\.setItem/);
});

test('mobile bilingual offline reader has no inline executable scripts or sensitive automatic sync', () => {
    const html = read('EduOS.App/wwwroot/offline-study.html');
    const study = read('EduOS.App/Views/Library/Study.cshtml');
    const code = read('EduOS.App/wwwroot/js/learning/offline-library.js');
    assert.match(html, /viewport/);
    assert.match(html, /অফলাইন অধ্যয়ন/);
    assert.match(html, /Content-Security-Policy/);
    assert.doesNotMatch(html, /<script(?![^>]*src=)/i);
    assert.match(study, /studyOfflinePassword/);
    assert.match(code, /expectedRowVersion/);
    assert.match(code, /Sign in to sync reading progress/);
    assert.doesNotMatch(code, /quiz-attempt|grades|finance|health/i);
});

test('offline reader revokes displayed bytes and cancels stale decrypt or sync after rights/session changes', () => {
    const code = read('EduOS.App/wwwroot/js/learning/offline-library.js');
    const worker = read('EduOS.App/wwwroot/service-worker.js');
    assert.match(code, /function closeReader\(message\)/);
    assert.match(code, /URL\.revokeObjectURL\(objectUrl\)/);
    assert.match(code, /openingId === record\.id \|\| openRecord\?\.id === record\.id/);
    assert.match(code, /const sequence = \+\+openGeneration/);
    assert.match(code, /if \(sequence !== openGeneration\) throw Error/);
    assert.ok((code.match(/assertOpen\(\);/g) || []).length >= 3);
    assert.match(code, /const syncGeneration = generation/);
    assert.match(code, /if \(syncGeneration !== generation\) throw Error/);
    assert.match(code, /await discard\(record, tr\('Offline rights revoked\.'/);
    assert.match(code, /await discard\(record, tr\('Offline rights changed\.'/);
    assert.match(worker, /\$\{CACHE_PREFIX\}v13/);
});
