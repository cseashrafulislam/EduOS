'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const root=path.resolve(__dirname,'../..');
const finance=fs.readFileSync(path.join(root,'EduOS.App/wwwroot/js/finance/fees.js'),'utf8');
const labels=fs.readFileSync(path.join(root,'EduOS.App/wwwroot/js/platform/task-workspace-locales.js'),'utf8');
function fixture(culture,feature){
 const nodes=new Map();
 const node=id=>{
  if(!nodes.has(id))nodes.set(id,{
   id,value:'',disabled:false,dataset:id==='feeApp'?{activeFeature:feature}:{},
   textContent:'',className:'',listeners:{},children:[],
   addEventListener(type,handler){this.listeners[type]=handler;},
   focus(){},replaceChildren(){},reset(){},getAttribute(){return null;},
   querySelectorAll(){return [];}
  });
  return nodes.get(id);
 };
 const present=new Set(['feeApp','feeAlert',feature==='billing'?'feeInvoiceForm':'feeStudent']);
 const document={
  documentElement:{lang:culture},
  getElementById(id){return present.has(id)||nodes.has(id)?node(id):null;},
  querySelector(selector){return selector.startsWith('meta')?{content:'synthetic-token'}:node('feeApp');}
 };
 // Initialization of each task binds its own handlers, with no real API writes.
 const context={document,Date,crypto:{randomUUID:()=> 'synthetic-uuid'},
  fetch:()=>new Promise(()=>{}),console};
 vm.runInNewContext(labels,context);
 for(const id of feature==='billing'
  ?['feeBatch','feeInvoiceStructure','feeMonth','feeBillingYear','feeDueDate','feePreview','feePreviewCsv','feeGenerate','feePreviewSummary','feePreviewRows']
  :['feeFindStudent','feeStudentSearch','feeLoadLedger','feePaymentForm','feeInvoicePrev','feeInvoiceNext','feeReceiptPrev','feeReceiptNext','feePaymentDate'])node(id);
 vm.runInNewContext(finance,context);
 return {node,context};
}
test('finance validation and paging use selected locale, never mutate financial inputs',async()=>{
 for(const [culture,expected] of [['bn-BD','ব্যাচ, ফি কাঠামো'],['en-BD','Choose a batch']]){
  const f=fixture(culture,'billing');
  await f.node('feePreview').listeners.click();
  assert.match(f.node('feeAlert').textContent,new RegExp(expected));
  if(culture==='bn-BD')assert.equal(f.context.eduTaskStaticLabel('Allocation BDT'),'বণ্টন (টাকা)');
  const l=fixture(culture,'ledger');
  l.node('feeStudentSearch').value='x';
  await l.node('feeFindStudent').listeners.click();
  assert.match(l.node('feeAlert').textContent,culture==='bn-BD'?/২–১০০/:/2–100/);
  await l.node('feeLoadLedger').listeners.click();
  assert.match(l.node('feeAlert').textContent,culture==='bn-BD'?/শিক্ষার্থী বাছুন/:/Select a student/);
 }
});
test('CSV headers are localized without weakening spreadsheet injection neutralization',()=>{
 assert.match(finance,/\['Student code','Name','Status','Invoice','Subtotal','Discount','Net BDT'\]\.map\(uiLabel\)/);
 assert.match(finance,/\['Invoice','Receipt','Date','Allocation BDT','Payment state','GL journal'\]\.map\(uiLabel\)/);
 assert.equal((finance.match(/\[=\+\\-@\]/g)||[]).length,2);
 assert.match(finance,/clientRequestId=stable\('generate'/);
 assert.match(finance,/clientRequestId=stable\('payment'/);
 assert.match(finance,/RequestVerificationToken=token/);
 assert.match(finance,/invoicePreviewRevision/);
});
