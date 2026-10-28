'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const script = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/academic/instruction.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/AcademicInstruction/Index.cshtml'), 'utf8');
const dto = fs.readFileSync(path.join(root, 'EduOS.Core/DTOs/Academic/AcademicDtos.cs'), 'utf8');
const service = fs.readFileSync(path.join(root, 'EduOS.Service/Services/Academic/AcademicInstructionService.cs'), 'utf8');

test('lesson offering picker requests one server-paged authorized batch page, with search', async () => {
    const source = script.match(/    async function fetchOfferingPage\([\s\S]*?\n    }\n(?=    function updateOfferingPager)/)?.[0]?.trim();
    assert.ok(source, 'picker function is present');
    const calls = [];
    const picker = vm.runInNewContext('(' + source + ')', {
        api: async url => { calls.push(url); return { items: [{ reference: 'a' }], page: 12, totalPages: 20, totalCount: 1000 }; },
        URLSearchParams, Number, Error
    });
    const result = await picker(77, 12, 'Calculus');
    assert.equal(result.totalCount, 1000);
    assert.equal(calls.length, 1);
    assert.match(calls[0], /^\/api\/academic-instruction\/batches\/77\/lesson-offerings\?/);
    assert.match(calls[0], /page=12/);
    assert.match(calls[0], /pageSize=50/);
    assert.match(calls[0], /search=Calculus/);
    await assert.rejects(() => picker(77, 0, ''), /Invalid offering page/);
    await assert.rejects(() => picker(77, 1, 'x'.repeat(101)), /Offering search is too long/);
    assert.equal(calls.length, 1);
});

test('lesson edit uses scoped saved offering reference, not a limited current selector page', () => {
    assert.match(dto, /public Guid SubjectOfferingReference \{ get; set; \}/);
    assert.match(service, /SubjectOfferingReference = offerings\.TryGetValue\(x\.SubjectOfferingId/);
    assert.match(script, /const offering = row \? row\.subjectOfferingReference : get\('lessonAssignment'\)\.value/);
    assert.match(script, /id: ref, name: editing\.subjectName/);
    assert.doesNotMatch(script, /page <= 5/);
    assert.doesNotMatch(script, /cannot be edited here/);
    assert.match(view, /id="lessonOfferingSearch"/);
    assert.match(view, /id="lessonOfferingsPrevious"/);
    assert.match(view, /id="lessonOfferingsNext"/);
    assert.match(view, /id="instructionHelp"[^>]*aria-expanded="false"/);
});
