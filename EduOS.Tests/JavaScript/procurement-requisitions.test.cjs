const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/wwwroot/js/procurement/requisitions.js'), 'utf8');

test('procurement UI is parseable and uses authenticated no-store antiforgery requests', () => {
    assert.doesNotThrow(() => new Function(source));
    assert.match(source, /credentials:'same-origin'/);
    assert.match(source, /cache:'no-store'/);
    assert.match(source, /RequestVerificationToken/);
    assert.match(source, /rowVersion:current.rowVersion/);
});
test('procurement UI supports bilingual workflow, bounded server paging and safe row rendering', () => {
    assert.match(source, /'bn-BD'/);
    assert.match(source, /'en-BD'/);
    assert.match(source, /pageSize:'25'/);
    assert.match(source, /textContent = value/);
    assert.match(source, /requestEpoch !== epoch/);
    assert.match(source, /r.createdBy === currentUser/);
    assert.match(source, /!owner/);
});
test('procurement UI has working draft and approval actions, item search and export', () => {
    for (const name of ['submit', 'approve', 'reject', 'cancel', 'prItemSearch', 'prExport', 'prSave'])
        assert.ok(source.includes(name), name);
    assert.match(source, /crypto.randomUUID\(\)/);
    assert.match(source, /selectedItemLine.value = item.id/);
});

test('submitted maker can withdraw with reason and revise while checker remains independent', () => {
    assert.match(source, /stateCode === 'Submitted' && canMake && owner/);
    assert.match(source, /transition\(r, 'withdraw'\)/);
    assert.match(source, /action === 'reject' \|\| action === 'withdraw'/);
    assert.match(source, /withdraw:'Withdraw for revision'/);
    assert.match(source, /withdraw:'সংশোধনের জন্য ফেরত'/);
});

test('checker-only server approval queue and scoped audit timeline use safe DOM text', () => {
    assert.match(source, /pending-approvals/);
    assert.match(source, /aria-pressed/);
    assert.match(source, /history\?/);
    assert.match(source, /li\.textContent = details\.join/);
    assert.match(source, /historyEpoch/);
    assert.match(source, /historyId = null/);
    assert.match(source, /pending:'অপেক্ষমাণ অনুমোদন'/);
});

test('read-only requisitions keep the authorized history dialog operable', () => {
    for (const id of ['prHistoryButton', 'prHistoryPrev', 'prHistoryNext'])
        assert.ok(source.includes(id), id);
    assert.match(source, /'prHistoryButton', 'prHistoryPrev', 'prHistoryNext'\]\.includes/);
});
