'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../../EduOS.App/wwwroot/js/finance/fees.js'),'utf8');
test('student ledger and receipt audit GETs are abortable and response scoped',()=>{
 assert.match(source,/function resetLedgerView\(/);
 for(const kind of ['search','ledger','invoice','receipt','audit'])assert(source.includes("beginRead('"+kind+"')"));
 assert.match(source,/state\.student!==student\|\|e\('feeStudent'\)\.value!==student/);
 assert.match(source,/state\.auditPage!==page/);
 assert.match(source,/state\.invoicePage!==page/);
 assert.match(source,/state\.receiptPage!==page/);
 assert.match(source,/e\('feeStudent'\)\.addEventListener\('change',resetLedgerView\)/);
 assert.match(source,/state\.invoice!==invoice\)return/);
 assert.match(source,/if\(state\.student!==student\|\|e\('feeStudent'\)\.value!==student\)return;clear\('payment'\)/);
 assert.match(source,/RequestVerificationToken=token/);
});
