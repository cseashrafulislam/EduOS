const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/admissions/transcript-draft.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/Admissions/Index.cshtml'), 'utf8');
const {parse, mount} = require('../../EduOS.App/wwwroot/js/admissions/transcript-draft.js');

test('bilingual transcript labels map only to allowlisted non-financial admission fields', () => {
    assert.deepEqual(parse('Name: Amina\nMobile: 01712345678\nGuardian: Rahim\nঠিকানা: ঢাকা\nAmount: 100000\nStatus: Approved'), {
        applicantName: 'Amina', primaryMobile: '01712345678', guardianName: 'Rahim', presentAddress: 'ঢাকা'
    });
    assert.deepEqual(parse('বাংলা নাম: আমিনা\nঅভিভাবকের মোবাইল: 01812345678'), {
        applicantNameBangla: 'আমিনা', guardianMobile: '01812345678'
    });
    assert.deepEqual(parse('ignore me\nPassword: secret\n: value'), {});
});
test('draft is bounded, ignores duplicate unknown keys and never parses JSON commands', () => {
    assert.equal(parse('Name: ' + 'a'.repeat(500)).applicantName.length, 200);
    assert.equal(parse('ঠিকানা: ' + 'a'.repeat(1200)).presentAddress.length, 1000);
    assert.deepEqual(parse('{"submit":true,"amount":100}'), {});
});
test('admission draft is mounted only on authorized new-application view and never submits itself', () => {
    assert.match(view, /id="admissionDraftHost"/);
    assert.match(view, /js\/admissions\/transcript-draft\.js/);
    assert.match(view, /id="admissionForm"/);
    assert.doesNotMatch(source, /\.submit\s*\(|requestSubmit\s*\(|fetch\s*\(|localStorage|sessionStorage|SpeechRecognition|webkitSpeechRecognition/);
    assert.match(source, /confirm\.addEventListener\('click'/);
    assert.match(source, /target\.dispatchEvent/);
    assert.doesNotThrow(() => mount({getElementById: () => null}));
});

test('Bangladesh mobile entry accepts Bengali digits but rejects malformed phone before applying', () => {
    const {normalizePhone, validateDraftField} = require('../../EduOS.App/wwwroot/js/admissions/transcript-draft.js');
    assert.equal(normalizePhone('০১৭১২৩৪৫৬৭৮'), '01712345678');
    assert.deepEqual(validateDraftField('guardianMobile','০১৭১২৩৪৫৬৭৮'), {valid:true,value:'01712345678'});
    assert.equal(validateDraftField('primaryMobile','123').valid, false);
    assert.equal(validateDraftField('primaryMobile','01912345678').valid, true);
    assert.equal(validateDraftField('applicantName','  আমিনা  ').value,'আমিনা');
});
test('typed-only admission draft offers offline fallback, clear action and provider privacy disclosure', () => {
    assert.match(source,/window.addEventListener\\('offline'/);
    assert.match(source,/Clear draft/);
    assert.match(source,/may send audio to their provider/);
    assert.match(source,/reviewed.some/);
    assert.doesNotMatch(source,/\\.submit\\s*\\(|requestSubmit\\s*\\(|fetch\\s*\\(|localStorage|sessionStorage|SpeechRecognition|webkitSpeechRecognition/);
});
