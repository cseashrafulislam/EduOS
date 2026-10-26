'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/wwwroot/js/students/guardian-revoked-paging.js'), 'utf8');
const setup = vm.runInNewContext(source.replace('export function', 'function') +
    '\nsetupRevokedGuardianPaging;', {});

function fixture(api) {
    const elements = {};
    for (const id of ['guardianRevokedPrev', 'guardianRevokedNext',
        'guardianRevokedPage', 'guardianRevokedLinks']) {
        elements[id] = {
            disabled: false, textContent: '',
            addEventListener(name, callback) { this[name] = callback; }
        };
    }
    const state = { busy: false, student: { studentReference: 'student-a', revokedGuardians: [] } };
    let renders = 0;
    const pager = setup({
        $: id => elements[id], api, state,
        renderRevokedGuardianLinks: () => { renders++; },
        render: () => {},
        notice: message => { throw Error(message); }
    });
    return { elements, state, pager, get renders() { return renders; } };
}

(async () => {
    const requested = [];
    const f = fixture(async url => {
        requested.push(url);
        const page = Number(new URL(url, 'https://test.local').searchParams.get('page'));
        return { totalCount: 45, items: [{ linkId: page }] };
    });
    await f.pager.refresh('student-a');
    assert.equal(f.state.student.revokedGuardians[0].linkId, 1);
    assert.equal(f.elements.guardianRevokedPrev.disabled, true);
    assert.equal(f.elements.guardianRevokedNext.disabled, false);
    assert.match(f.elements.guardianRevokedPage.textContent, /Page 1 of 3/);
    f.elements.guardianRevokedNext.click();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(f.state.student.revokedGuardians[0].linkId, 2);
    assert.match(requested[1], /page=2&pageSize=20/);
    f.elements.guardianRevokedPrev.click();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(f.state.student.revokedGuardians[0].linkId, 1);

    let releaseOld;
    const stale = fixture(url => url.includes('student-a')
        ? new Promise(resolve => { releaseOld = resolve; })
        : Promise.resolve({ totalCount: 1, items: [{ linkId: 99 }] }));
    const oldRequest = stale.pager.refresh('student-a');
    stale.state.student = { studentReference: 'student-b', revokedGuardians: [] };
    await stale.pager.refresh('student-b');
    releaseOld({ totalCount: 1, items: [{ linkId: 1 }] });
    await oldRequest;
    assert.equal(stale.state.student.revokedGuardians[0].linkId, 99);
    assert.equal(stale.renders, 1);
    console.log('Guardian revoked pagination and stale-student isolation PASS');
})().catch(error => { console.error(error); process.exitCode = 1; });
