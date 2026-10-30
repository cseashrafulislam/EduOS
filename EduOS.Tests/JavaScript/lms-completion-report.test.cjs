const test = require('node:test');
const assert = require('node:assert/strict');
const { collect, validateSnapshot, toCsv, PAGE_SIZE, MAX_ROWS } = require('../../EduOS.App/wwwroot/js/learning/completion-report.js');
const rows = n => Array.from({ length: n }, (_, i) => ({
    enrollmentId: i + 1, studentCode: `S${i + 1}`, studentName: `Learner ${i + 1}`,
    state: 1, eligibility: { ready: true, completedLessons: 1, requiredLessons: 1 }
}));

test('exports every filtered page without silently truncating at the visible page', async () => {
    const data = rows(53), calls = [];
    const result = await collect(async (page, size) => {
        calls.push([page, size]);
        return { totalCount: data.length, items: data.slice((page - 1) * size, page * size) };
    });
    assert.equal(result.length, 53);
    assert.deepEqual(calls, [[1, PAGE_SIZE], [2, PAGE_SIZE], [3, PAGE_SIZE]]);
    assert.equal(toCsv(result).split('\r\n').length, 54);
});

test('rejects an oversized export before downloading any partial CSV', async () => {
    let calls = 0;
    await assert.rejects(collect(async () => { calls++; return { totalCount: MAX_ROWS + 1, items: rows(25) }; }), /Narrow the student search/);
    assert.equal(calls, 1);
});

test('rejects changed account or filter while a request is in flight', async () => {
    let current = true;
    await assert.rejects(collect(async () => { current = false; return { totalCount: 1, items: rows(1) }; }, () => current), /account changed/);
});

test('rejects duplicate IDs and inconsistent totals instead of silently corrupting a report', async () => {
    const full = rows(25);
    await assert.rejects(collect(async page => page === 1
        ? { totalCount: 26, items: full }
        : { totalCount: 26, items: [full[0]] }), /changed during export/);
    await assert.rejects(collect(async page => page === 1
        ? { totalCount: 26, items: full }
        : { totalCount: 27, items: rows(2) }), /changed during export/);
});

test('neutralizes formula cells including leading tab and newline while preserving Bangla', () => {
    const row = rows(1)[0];
    row.studentCode = '=1+1';
    row.studentName = '\t@SUM(A1:A2)';
    row.eligibility = { blockers: ['\n-1+2', 'বাংলা'], ready: false };
    const csv = toCsv([row]);
    assert.ok(csv.startsWith('\uFEFF'));
    assert.ok(csv.includes('"\'=1+1"'));
    assert.ok(csv.includes('"\'\t@SUM(A1:A2)"'));
    assert.ok(csv.includes('"\'\n-1+2 বাংলা"'));
});

test('empty register yields only the header', async () => {
    const result = await collect(async () => ({ totalCount: 0, items: [] }));
    assert.deepEqual(result, []);
    assert.equal(toCsv(result).split('\r\n').length, 1);
});

test('single-request export accepts an entire bounded register and preserves CSV protection', () => {
    const data = rows(53);
    const accepted = validateSnapshot({ page: 1, pageSize: MAX_ROWS, totalCount: 53, items: data });
    assert.equal(accepted.length, 53);
    assert.equal(toCsv(accepted).split('\r\n').length, 54);
});

test('single-request export rejects truncated, oversized, duplicate and stale snapshots', () => {
    const good = { page: 1, pageSize: MAX_ROWS, totalCount: 2, items: rows(2) };
    assert.throws(() => validateSnapshot({ ...good, totalCount: 3 }), /changed or exceeded/);
    assert.throws(() => validateSnapshot({ ...good, totalCount: 251 }), /changed or exceeded/);
    assert.throws(() => validateSnapshot({ ...good, items: [rows(1)[0], rows(1)[0]] }), /duplicate/);
    assert.throws(() => validateSnapshot(good, () => false), /account changed/);
});

test('lesson progress export rejects guessed enrollment, duplicate evidence and stale tenant', () => {
    const { validateLessonSnapshot, toLessonCsv } =
        require('../../EduOS.App/wwwroot/js/learning/completion-report.js');
    const lesson = { courseEnrollmentId: 42, lessonReference: '0a1b2c3d-0000-4000-8000-000000000001',
        lessonTitle: '\t=SUM(1,2) বাংলা', progressPercent: 100, isCompleted: true,
        completedAt: '2026-10-30T04:00:00Z' };
    const data = { page: 1, pageSize: 250, totalCount: 1, items: [lesson] };
    assert.equal(validateLessonSnapshot(data, 42).length, 1);
    assert.ok(toLessonCsv([lesson]).includes('"\'\t=SUM(1,2) বাংলা"'));
    assert.throws(() => validateLessonSnapshot(data, 43), /invalid or duplicate/);
    assert.throws(() => validateLessonSnapshot({ ...data, totalCount: 2 }), /changed or exceeded/);
    assert.throws(() => validateLessonSnapshot({ ...data, items: [lesson, lesson], totalCount: 2 }),
        /invalid or duplicate/);
    assert.throws(() => validateLessonSnapshot(data, 42, () => false), /account changed/);
});
