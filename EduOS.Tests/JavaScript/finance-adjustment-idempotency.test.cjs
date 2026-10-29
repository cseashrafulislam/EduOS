const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');

const source=fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/wwwroot/js/finance/finance-adjustments.js'),'utf8');

test('finance retries reuse the exact request identifier while input has not changed',()=>{
    const helper=source.match(/const requestFingerprints=new Map\(\);[\s\S]*?function clearRequestId\(operation\)\{requestFingerprints\.delete\(operation\);\}/);
    assert.ok(helper,'The retry helper must remain available.');
    let sequence=0;
    const functions=vm.runInNewContext(helper[0]+';({stableRequestId,clearRequestId})',
        {crypto:{randomUUID:()=>String(++sequence)},Map});
    const first={studentEnrollmentReference:'student-1',fineRuleId:2,reason:'Same'};
    assert.equal(functions.stableRequestId('fine-application',first),'1');
    assert.equal(functions.stableRequestId('fine-application',{...first}),'1');
    assert.equal(functions.stableRequestId('discount-assignment',first),'2');
    assert.equal(functions.stableRequestId('fine-application',{...first,reason:'Changed'}),'3');
    functions.clearRequestId('fine-application');
    assert.equal(functions.stableRequestId('fine-application',first),'4');
});

test('fine, discount, refund request and payout all bind a stable key before sending',()=>{
    for(const operation of ['discount-assignment','fine-application','refund-request','refund-payout-'])
        assert.ok(source.includes(`stableRequestId('${operation}'`) ||
            (operation==='refund-payout-' && source.includes("stableRequestId(operation,body)")),
            `Missing stable idempotency key for ${operation}`);
    assert.match(source,/RequestVerificationToken=token/);
    assert.match(source,/credentials:'same-origin'/);
});
