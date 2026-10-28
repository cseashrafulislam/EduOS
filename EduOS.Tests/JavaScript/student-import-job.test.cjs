const test = require('node:test');
const assert = require('node:assert/strict');
const { webcrypto } = require('node:crypto');
const { fingerprint, requestId } = require('../../EduOS.App/wwwroot/js/students/import-job.js');

const file = value => ({ arrayBuffer: async () => new TextEncoder().encode(value).buffer });
test('same file/mapping/window yields stable UUID across reloads', async () => {
    const a = await fingerprint(file('file-A'), '{"SourceKey":"key"}', webcrypto);
    const b = await fingerprint(file('file-A'), '{"SourceKey":"key"}', webcrypto);
    assert.deepEqual(a, b);
    const id = await requestId(a.fileHash, a.mappingHash, 100, webcrypto);
    assert.match(id, /^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/);
    assert.equal(id, await requestId(b.fileHash, b.mappingHash, 100, webcrypto));
    assert.notEqual(id, await requestId(a.fileHash, a.mappingHash, 0, webcrypto));
    assert.notEqual(id, await requestId(a.fileHash, a.mappingHash, 200, webcrypto));
});
test('resume recovers from server-confirmed preview without local storage', () => {
    const { resumeRowCount } = require('../../EduOS.App/wwwroot/js/students/import-job.js');
    assert.equal(resumeRowCount(null, { totalRows: 250, invalidRows: 100 }), 250);
    assert.equal(resumeRowCount({ totalRows: 250 }, null), 250);
    assert.throws(() => resumeRowCount(null, null), /preview/);
    assert.throws(() => resumeRowCount({ totalRows: 250 }, { totalRows: 249 }), /changed/);
    assert.throws(() => resumeRowCount(null, { totalRows: 5001 }), /preview/);
});
test('changed file or mapping cannot silently reuse a chunk request', async () => {
    const a = await fingerprint(file('file-A'), '{"SourceKey":"key"}', webcrypto);
    const changed = await fingerprint(file('file-B'), '{"SourceKey":"key"}', webcrypto);
    const remapped = await fingerprint(file('file-A'), '{"SourceKey":"other"}', webcrypto);
    assert.notEqual(await requestId(a.fileHash, a.mappingHash, 0, webcrypto),
        await requestId(changed.fileHash, changed.mappingHash, 0, webcrypto));
    assert.notEqual(await requestId(a.fileHash, a.mappingHash, 0, webcrypto),
        await requestId(remapped.fileHash, remapped.mappingHash, 0, webcrypto));
    await assert.rejects(() => requestId(a.fileHash, a.mappingHash, 1, webcrypto));
});

test('cohort validates eight bounded fingerprints and rejects duplicate or oversized manifests', () => {
    const { validateCohort } = require('../../EduOS.App/wwwroot/js/students/import-job.js');
    const one = { fileHash: 'a'.repeat(64), mappingHash: 'b'.repeat(64), totalRows: 5000 };
    assert.deepEqual(validateCohort([one]), [one]);
    assert.throws(() => validateCohort([one, one]), /already selected/);
    assert.throws(() => validateCohort(Array(9).fill(one)), /between one and eight/);
    assert.throws(() => validateCohort([{ ...one, fileHash: 'invalid' }]), /Invalid/);
    assert.equal(validateCohort(Array.from({ length: 8 }, (_, i) =>
        ({ ...one, fileHash: i.toString(16).repeat(64) }))).length, 8);
});
test('cohort summary export includes only bounded hashes and counts', () => {
    const { cohortSummaryCsv } = require('../../EduOS.App/wwwroot/js/students/import-job.js');
    const csv = cohortSummaryCsv({ files: [
        { fileHash: 'a'.repeat(64), mappingHash: 'b'.repeat(64),
            totalRows: 100, committedRows: 80, missingWindows: 1, isComplete: false }
    ] });
    assert.match(csv, /100,80,1,Pending/);
    assert.doesNotMatch(csv, /student name/i);
    assert.throws(() => cohortSummaryCsv({ files: [
        { fileHash: '=cmd', mappingHash: 'b'.repeat(64),
            totalRows: 1, committedRows: 1, missingWindows: 0 }
    ] }), /Invalid/);
});

test('integrity export is bounded, PII-free and formula-neutralized', () => {
    const { integrityCsv } = require('../../EduOS.App/wwwroot/js/students/import-job.js');
    const csv = integrityCsv({ expectedRows: 100, checkedRows: 100, issues: [
        { rowNumber: 2, code: 'MissingEnrollment', message: '=private formula' }
    ] });
    assert.match(csv, /MissingEnrollment/);
    assert.match(csv, /'=private formula/);
    assert.doesNotMatch(csv, /student name/i);
    assert.throws(() => integrityCsv({ expectedRows: 101, checkedRows: 101, issues: [] }), /Invalid/);
    assert.throws(() => integrityCsv({ expectedRows: 1, checkedRows: 1,
        issues: [{ rowNumber: -1, code: 'bad', message: 'x' }] }), /Invalid/);
    const bounded = Array.from({ length: 500 }, () => ({
        rowNumber: 2, code: 'MissingGuardian', message: 'Guardian link unavailable'
    }));
    assert.equal((integrityCsv({ expectedRows: 100, checkedRows: 100, issues: bounded })
        .match(/MissingGuardian/g) || []).length, 500);
    assert.throws(() => integrityCsv({ expectedRows: 100, checkedRows: 100,
        issues: Array(2002).fill(bounded[0]) }), /Invalid/);
});
