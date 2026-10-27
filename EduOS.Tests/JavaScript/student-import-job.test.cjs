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
