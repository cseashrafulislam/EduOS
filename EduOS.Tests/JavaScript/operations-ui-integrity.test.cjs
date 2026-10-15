const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('transport and hostel assignment writes send antiforgery header', () => {
    assert.match(read('EduOS.App/Views/Shared/_Layout.cshtml'), /name="request-verification-token"/);
    for (const name of ['transport', 'hostel']) {
        const p = 'EduOS.App/wwwroot/js/' + name + '-operations/index.js';
        const script = read(p);
        assert.match(script, /headers\.RequestVerificationToken = token/);
        assert.match(script, /if \(!token\) throw new Error/);
        assert.match(script, /credentials: 'same-origin'/);
        const syntax = spawnSync(process.execPath, ['--check', path.join(root, p)], { encoding: 'utf8' });
        assert.equal(syntax.status, 0, syntax.stderr);
    }
});

test('hostel report and room capacity editor have unique DOM IDs', () => {
    const html = read('EduOS.App/Views/HostelOperations/Index.cshtml');
    const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map(x => x[1]);
    assert.equal(new Set(ids).size, ids.length);
    assert.match(html, /id="hostelCapacityPanel"/);
    assert.match(html, /id="hostelRoomCapacityPanel"/);
    const room = read('EduOS.App/wwwroot/js/hostel-operations/room-capacity.js');
    assert.match(room, /hostelRoomCapacitySearch/);
    assert.match(room, /hostelRoomCapacityStatus/);
    assert.doesNotMatch(room, /'hostelCapacitySearch'|'hostelCapacityStatus'/);
    const syntax = spawnSync(process.execPath, ['--check',
        path.join(root, 'EduOS.App/wwwroot/js/hostel-operations/room-capacity.js')], { encoding: 'utf8' });
    assert.equal(syntax.status, 0, syntax.stderr);
});
