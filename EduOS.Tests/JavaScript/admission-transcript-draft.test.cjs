const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/admissions/transcript-draft.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/Admissions/Index.cshtml'), 'utf8');
const {parse, parseDetailed, mount} = require('../../EduOS.App/wwwroot/js/admissions/transcript-draft.js');

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
    assert.deepEqual(parse('Name: ' + 'a'.repeat(500)), {});
    assert.deepEqual(parse('ঠিকানা: ' + 'a'.repeat(1200)), {});
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
    assert.match(source,/window\.addEventListener\('offline'/);
    assert.match(source,/Clear draft/);
    assert.match(source,/may send audio to their provider/);
    assert.match(source,/reviewed.some/);
    assert.doesNotMatch(source,/\.submit\s*\(|requestSubmit\s*\(|fetch\s*\(|localStorage|sessionStorage|SpeechRecognition|webkitSpeechRecognition/);
});

test('Bangladesh phone prefixes normalize consistently and never accept invalid numbers', () => {
    const {normalizePhone,validateDraftField} = require('../../EduOS.App/wwwroot/js/admissions/transcript-draft.js');
    assert.equal(normalizePhone('০১৮১২৩৪৫৬৭৮'), '01812345678');
    assert.deepEqual(validateDraftField('primaryMobile','+৮৮০১৭১২৩৪৫৬৭৮'), {valid:true,value:'01712345678'});
    assert.deepEqual(validateDraftField('guardianMobile','8801812345678'), {valid:true,value:'01812345678'});
    for (const bad of ['0171234567','01212345678','017123456789','01712abc678','']) {
        assert.equal(validateDraftField('primaryMobile',bad).valid,false,bad);
    }
});
test('preview has explicit accessible labels and validation focuses an invalid field', () => {
    assert.match(source,/role:'group'/);
    assert.match(source,/for:'admissionDraftPreview_'/);
    assert.match(source,/id:'admissionDraftPreview_'/);
    assert.match(source,/setAttribute\('aria-invalid','true'\)/);
    assert.match(source,/snapshot\[invalid\[0\]\]\.focus\(\)/);
    assert.doesNotMatch(source,/requestSubmit\s*\(|\.submit\s*\(/);
});

test('draft refuses partial application when destination fields disappear or become read-only', () => {
    assert.match(source, /const destinations = reviewed\.map/);
    assert.match(source, /const unavailable = destinations\.find/);
    assert.match(source, /target\.disabled \|\| target\.readOnly \|\| !target\.isConnected/);
    assert.match(source, /if \(unavailable\) \{/);
    assert.match(source, /No fields applied; review the form/);
    assert.ok(source.indexOf('if (unavailable) {') < source.indexOf('destinations.forEach'));
    assert.doesNotMatch(source, /requestSubmit\s*\(|\.submit\s*\(/);
});


test('reviewed admission fields are never silently truncated to destination maxlength', () => {
    assert.ok(source.includes('result.value.length > Number(target.maxLength)'));
    assert.ok(source.includes('target.value = result.value;'));
    assert.ok(!source.includes('target.value = result.value.slice('));
    assert.ok(source.indexOf('const unavailable = destinations.find') < source.indexOf('destinations.forEach'));
});

test('oversized recognized fields fail closed without losing data silently or applying other fields', () => {
    const tooLong = parseDetailed('Mobile: 01712345678\nName: ' + 'a'.repeat(201));
    assert.deepEqual(tooLong.fields, {});
    assert.deepEqual(tooLong.errors, [{key:'applicantName',line:2,limit:200}]);
    assert.deepEqual(parseDetailed('ঠিকানা: ' + 'ক'.repeat(1001)).fields, {});
    assert.equal(parseDetailed('Name: ' + 'a'.repeat(200)).fields.applicantName.length, 200);
    assert.equal(parseDetailed('Address: ' + 'a'.repeat(1000)).fields.presentAddress.length, 1000);
    assert.match(source, /input\.setAttribute\('aria-invalid', 'true'\)/);
    assert.match(source, /input\.focus\(\)/);
});

test('repeated bilingual labels fail closed instead of silently overwriting an applicant field', () => {
    const repeated = parseDetailed('Name: Amina\nApplicant name: Other\nMobile: 01712345678');
    assert.deepEqual(repeated.fields, {});
    assert.deepEqual(repeated.errors, [{key:'applicantName',line:2,duplicate:true}]);
    assert.deepEqual(parse('মোবাইল: 01712345678\nPhone: 01812345678'), {});
    assert.deepEqual(parseDetailed('Name: Amina\nMobile: 01712345678').errors, []);
    assert.match(source, /first\.duplicate/);
    assert.match(source, /repeats an admission field/);
});


test('recognized blank transcript values fail closed without silently dropping fields', () => {
    const blank = parseDetailed('Name: Amina\nMobile:   \nGuardian: Rahim');
    assert.deepEqual(blank.fields, {});
    assert.deepEqual(blank.errors, [{key:'primaryMobile',line:2,empty:true}]);
    assert.deepEqual(parse('নাম:  \nঠিকানা: ঢাকা'), {});
    assert.deepEqual(parseDetailed('Name: Amina\nUnrecognized:   ').fields, {applicantName:'Amina'});
    assert.match(source, /first\.empty/);
    assert.match(source, /empty admission field/);
});


test('oversized pasted transcripts are rejected without browser-side silent truncation', () => {
    const oversized = 'Name: Amina\n' + 'x'.repeat(3000);
    assert.deepEqual(parseDetailed(oversized), {fields:{},errors:[{tooLong:true,limit:3000}]});
    assert.deepEqual(parse(oversized), {});
    assert.deepEqual(parseDetailed('Name: Amina').fields, {applicantName:'Amina'});
    assert.doesNotMatch(source, /rows:'3',maxlength:'3000'/);
    assert.match(source, /first\.tooLong/);
    assert.doesNotMatch(source, /requestSubmit\s*\(|\.submit\s*\(/);
});


test('reviewed transcript has reversible application without persistent applicant storage', () => {
    assert.match(source, /const previous = destinations\.map/);
    assert.match(source, /lastApply = previous/);
    assert.match(source, /undo\.addEventListener\('click'/);
    assert.match(source, /lastApply\.some/);
    assert.match(source, /target\.value !== after/);
    assert.match(source, /target\.value = before/);
    assert.match(source, /resetUndo\(\)/);
    assert.match(source, /reviewSummary\.textContent/);
    assert.doesNotMatch(source, /localStorage|sessionStorage|indexedDB|requestSubmit\s*\(|\.submit\s*\(/);
});

test('admission readiness offers accessible bilingual required-field review without bypassing submit', () => {
    assert.match(source, /Application readiness/);
    assert.match(source, /আবেদন প্রস্তুতি/);
    assert.match(source, /form\.querySelectorAll\('\[required\]'\)/);
    assert.match(source, /field\.checkValidity\(\)/);
    assert.match(source, /field\.scrollIntoView/);
    assert.match(source, /field\.focus\(\)/);
    assert.match(source, /readinessList\.replaceChildren\(\)/);
    assert.match(source, /aria-live:'polite'/);
    assert.doesNotMatch(source, /requestSubmit\s*\(|\.submit\s*\(/);
});
