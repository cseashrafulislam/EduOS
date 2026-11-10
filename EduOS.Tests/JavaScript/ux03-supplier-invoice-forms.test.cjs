'use strict';
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const source=fs.readFileSync(path.resolve(__dirname,
 '../../EduOS.App/wwwroot/js/procurement/supplier-invoice-matching.js'),'utf8');

function harness(){
 class Element{
  constructor(id){this.id=id;this.value='';this.dataset={};this.children=[];this.listeners={};
   this.disabled=false;this.inert=false;this.open=false;this.textContent='';this.className='';
   this.classList={add(){},remove(){},contains(){return false;},toggle(){}};}
  addEventListener(type,fn){this.listeners[type]=fn;}
  setAttribute(){}
  append(...items){this.children.push(...items);}
  replaceChildren(...items){this.children=items;}
  showModal(){this.open=true;}
  close(){this.open=false;}
  reset(){this.value='';}
  click(){return this.listeners.click?.();}
  querySelectorAll(selector){return selector==='input'?this.children.flatMap(x=>x.children.filter(y=>y.tagName==='INPUT')):[];}
  querySelector(selector){return this.children.flatMap(x=>x.children).find(x=>
   selector.startsWith('.')&&x.className.split(' ').includes(selector.slice(1)));}
  get tagName(){return this.id.toUpperCase()==='INPUT'?'INPUT':this.id.toUpperCase();}
 }
 const items={},posts=[],gets=[];let nextKey=0,failList=false;
 const get=id=>items[id]??(items[id]=new Element(id));
 get('supplierInvoiceMatch').dataset={maker:'true',reviewer:'true',userId:'100'};
 const invoice={reference:'inv-1',supplierInvoiceNumber:'SIN-1',orderNumber:'PO-1',campusId:2,
  totalAmount:25,stockValue:25,priceVariance:0,stateCode:'Submitted',createdByUserId:200,
  rowVersion:'rv-1',lines:[]};
 const po={id:'po-1',orderNumber:'PO-1',currencyCode:'BDT',totalAmount:25,campusId:2,stateCode:'Approved'};
 const receipt={reference:'receipt-1',lines:[{lineNo:1,inventoryItemId:21,lotOrBatchNo:'L1',
  quantity:4,approvedPoUnitPrice:10,postedUnitCost:10}]};
 const ok=data=>({ok:true,status:200,json:async()=>({success:true,data})});
 const fail=(message,status=500)=>({ok:false,status,json:async()=>({success:false,message})});
 const fetch=async(url,options)=>{
  if(options?.method==='POST')return new Promise(resolve=>posts.push({url,resolve,
   body:JSON.parse(options.body)}));
  gets.push(url);
  if(url.startsWith('/api/procurement/supplier-invoices?'))
   return failList?fail('Register unavailable',typeof failList==='number'?failList:500):
    ok({totalPages:1,items:[invoice]});
  if(url==='/api/procurement/supplier-invoices/options')
   return ok({periods:[{id:1,name:'Nov'}],accounts:[
    {id:2,name:'Inventory',type:'Asset'},
    {id:3,name:'Payable',type:'Liability'},
    {id:4,name:'Variance',type:'Expense'}]});
  if(url==='/api/procurement/supplier-invoices/inv-1')return ok(invoice);
  if(url.includes('/purchase-orders?'))return ok({items:[po]});
  if(url.endsWith('/purchase-orders/po-1'))return ok(po);
  if(url.includes('/goods-receipts?'))return ok({totalCount:1,totalPages:1,items:[receipt]});
  return fail('Unexpected GET '+url,404);
 };
 const document={getElementById:get,documentElement:{lang:'en-BD'},
  querySelector:()=>({content:'csrf'}),createElement:tag=>new Element(tag)};
 const window={location:{assign(){}},dispatchEvent(){}};
 vm.runInNewContext(source,{document,window,fetch,URLSearchParams,
  crypto:{randomUUID:()=>'invoice-key-'+(++nextKey)},
  Option:class {constructor(text,value){this.text=text;this.value=value;}}});
 const flush=async()=>{for(let i=0;i<25;i++)await Promise.resolve();};
 const submit=id=>get(id).listeners.submit({preventDefault(){}});
 return {get,posts,gets,flush,submit,invoice,receipt,ok,fail,
  set failList(value){failList=value;}};
}

test('Finance review retains a key on uncertain retry and rotates it for an edited decision',async()=>{
 const h=harness();await h.flush();
 h.get('simInvoiceRows').children[0].children[5].children[0].click();await h.flush();
 h.get('simDetailActions').children[0].click();await h.flush();
 assert.equal(h.get('simReviewDialog').open,true,h.get('simStatus').textContent);
 h.get('simReviewReason').value='Independent review evidence';
 let task=h.submit('simReviewForm');await h.flush();
 assert.equal(h.get('simReviewForm').inert,true);
 h.posts[0].resolve(h.fail('Lost connection',503));await task;
 task=h.submit('simReviewForm');await h.flush();
 assert.equal(h.posts[0].body.clientRequestId,h.posts[1].body.clientRequestId);
 h.posts[1].resolve(h.fail('Timeout',503));await task;
 h.get('simReviewReason').value='Different independent evidence';
 task=h.submit('simReviewForm');await h.flush();
 assert.notEqual(h.posts[1].body.clientRequestId,h.posts[2].body.clientRequestId);
 assert.equal(h.posts[2].body.rowVersion,'rv-1');
 h.posts[2].resolve(h.ok({reference:'inv-1',stateCode:'Posted'}));await task;
 assert.equal(h.get('simReviewForm').inert,false);
 assert.match(h.get('simStatus').textContent,/Finance decision recorded/);
});

test('saved supplier draft is never called failed just because the invoice list cannot refresh',async()=>{
 const h=harness();await h.flush();
 let search=h.submit('simSearchForm');await search;
 h.get('simOrderResults').children[0].click();await h.flush();
 const row=h.get('simRows').children[0];
 row.children[6].children[0].value='2';
 h.get('simInvoiceNo').value='SUP-33';
 h.get('simInvoiceDate').value='2026-11-10';h.get('simDueDate').value='2026-11-15';
 h.get('simReason').value='Source signed receipt evidence';
 h.failList=true;
 const task=h.get('simSave').click();await h.flush();
 assert.equal(h.get('simPreviewForm').inert,true);
 h.posts[0].resolve(h.ok({reference:'inv-1'}));await task;
 assert.equal(h.get('simPreviewForm').inert,false);
 assert.match(h.get('simStatus').textContent,/Invoice draft saved.*Register refresh incomplete/);
});

test('supplier invoice retains form-guard, read version and confirmed-write semantics',()=>{
 const view=fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.App/Views/ProcurementSupplierInvoiceMatching/Index.cshtml'),'utf8');
 assert.match(view,/form-guard\.js"[\s\S]*supplier-invoice-matching\.js"/);
 assert.match(source,/canDiscardDraft\(\)/);
 assert.match(source,/signatureKey\(\{reference:source\.reference,\.\.\.body\}/);
 assert.match(source,/if\(seq!==detailSeq\)return false/);
 assert.match(source,/close\(reviewDialog,true\);close\(detailDialog,true\)/);
 assert.match(source,/draftForm\.inert=true/);
});

test('revoked Finance list access clears stale invoice links and disables paging',async()=>{
 const h=harness();await h.flush();
 assert.equal(h.get('simInvoiceRows').children.length,1);
 h.failList=403;
 await h.submit('simInvoiceFilters');await h.flush();
 assert.equal(h.get('simInvoiceRows').children.length,0);
 assert.equal(h.get('simPrev').disabled,true);
 assert.equal(h.get('simNext').disabled,true);
 assert.match(h.get('simStatus').textContent,/Register unavailable/);
});
