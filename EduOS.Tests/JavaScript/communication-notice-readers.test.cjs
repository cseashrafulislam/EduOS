const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const view = read('EduOS.App/Views/CommunicationWorkspace/Index.cshtml');
const entry = read('EduOS.App/wwwroot/js/communication/workspace.js');
const listing = read('EduOS.App/wwwroot/js/communication/workspace-notice-list.js');
const readers = read('EduOS.App/wwwroot/js/communication/workspace-notice-readers.js');
test('admin read receipt panel is connected to the existing notice list', () => {
    for (const id of ['cwReaderPanel', 'cwReaderTitle', 'cwReaderRows', 'cwReaderPrev', 'cwReaderNext', 'cwReaderClose', 'cwReaderPage'])
        assert.match(view, new RegExp('id="' + id + '"'));
    assert.match(entry, /initNoticeReaders\(\)/);
    assert.match(listing, /showNoticeReaders\(row\)/);
});
test('read receipts use tenant-authorized API, bounded pagination and safe DOM rendering', () => {
    assert.match(readers, /if \(!admin/);
    assert.match(readers, /read-receipts\?/);
    assert.match(readers, /pageSize: '20'/);
    assert.match(readers, /cell\(row\.userName\)/);
    assert.match(readers, /generation\+\+/);
    assert.doesNotMatch(readers, /innerHTML|localStorage|sessionStorage/);
});
