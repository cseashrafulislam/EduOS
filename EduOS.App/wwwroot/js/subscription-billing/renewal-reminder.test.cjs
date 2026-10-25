const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'index.js'),'utf8');
test('renewal reminders use same-origin antiforgery-protected POST',()=>{
 assert.match(source,/\/api\/subscription\/invoices\/.*\/remind/);
 assert.match(source,/RequestVerificationToken/);
 assert.match(source,/api\(.*"POST"/);
});
test('renewal reminder is limited to unpaid REN invoices',()=>{
 assert.match(source,/invoiceNumber.*startsWith\("REN-"\)/);
 assert.match(source,/Number\(inv\.dueAmount\)>0/);
});
