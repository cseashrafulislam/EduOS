const test = require('node:test');
const assert = require('node:assert/strict');
const guidance = require('../../EduOS.App/wwwroot/js/learning/quiz-study-guidance.js');
const row = (overrides = {}) => ({ attemptId: 12, quizTitle: 'Fractions', passed: false, obtainedMarks: 3, ...overrides });
test('bilingual, read-only review of graded learner-owned page evidence', () => {
  for (const locale of ['bn-BD', 'en-BD']) {
    const input = [row()];
    const result = guidance.build(input, locale);
    assert.equal(result.status, 'ready');
    assert.equal(result.items[0].attemptId, 12);
    assert.equal(result.items.length, 1);
    assert.match(result.note, locale === 'bn-BD' ? /এই পাতায়/ : /history page/);
    assert.equal(input[0].obtainedMarks, 3);
  }
});
test('pending, empty and passing attempts never become invented failures', () => {
  assert.equal(guidance.build([row({ passed: null, obtainedMarks: null })], 'en-BD').status, 'no-evidence');
  assert.equal(guidance.build([row({ passed: true })], 'en-BD').items.length, 0);
  assert.equal(guidance.build([], 'bn-BD').status, 'no-evidence');
});
test('malformed mixed evidence fails closed without partial learner disclosure', () => {
  for (const invalid of [row({ passed: 'false' }), row({ obtainedMarks: -1 }), row({ attemptId: 0 }), row({ quizTitle: '<'.repeat(201) })]) {
    const result = guidance.build([row(), invalid], 'en-BD');
    assert.equal(result.status, 'invalid');
    assert.equal(result.items.length, 0);
  }
  assert.equal(guidance.build([row()], 'en-US').status, 'invalid');
  assert.equal(guidance.build(Array(21).fill(row()), 'bn-BD').status, 'invalid');
});
