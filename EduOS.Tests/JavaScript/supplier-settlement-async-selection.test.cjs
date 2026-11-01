const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

function harness(kind) {
 const source = fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.App/wwwroot/js/procurement/supplier-settlement-instructions.js'), 'utf8');
 class Element {
  constructor(id) {
   this.id=id;this.dataset={};this.children=[];this.listeners={};this.value='';
   this.open=false;this.disabled=false;this.textContent='';
   this.classList={add(){},remove(){}};
  }
  addEventListener(name,callback){this.listeners[name]=callback;}
  append(...children){this.children.push(...children);}
  replaceChildren(...children){this.children=children;}
  showModal(){this.open=true;}
  close(){this.open=false;}
  querySelectorAll(){return [];}
  click(){this.listeners.click?.();}
 }
 const elements={},handlers={},posts=[],gets=[];
 const get=id=>elements[id]??(elements[id]=new Element(id));
 get('supplierSettlement').dataset={maker:kind==='review'?'false':'true',
  reviewer:kind==='review'?'true':'false'};
 get('supplierInvoiceMatch').dataset={userId:'100'};
 const document={getElementById:get,documentElement:{lang:'en-BD'},
  querySelector:()=>({content:'csrf'}),createElement:tag=>new Element(tag)};
 const window={addEventListener:(name,callback)=>handlers[name]=callback};
 const fetch=async(url,options)=>{
  if(options?.method==='POST')
   return await new Promise((resolve,reject)=>posts.push({url,resolve,reject}));
  gets.push(url);
  const invoice=url.match(/supplier-invoices\/([^/]+)\/settlements/)?.[1];
  const data=url.endsWith('/options')?{availableAmount:50,banks:[],periods:[]}:
   url.includes('?page=')?{totalPages:1,items:[{
    reference:invoice+'-instruction',state:'Submitted',amount:25,
    plannedDate:'2026-11-01',makerId:kind==='review'?200:100,rowVersion:'abc'
   }]}:{};
  return {ok:true,json:async()=>({success:true,data})};
 };
 vm.runInNewContext(source,{document,window,fetch,URLSearchParams,
  crypto:{randomUUID:()=> 'test-request-key'},Date,
  Option:class {constructor(name,value){this.name=name;this.value=value;}}});
 const flush=async()=>{for(let i=0;i<30;i++)await Promise.resolve();};
 const select=async reference=>{
  handlers['procurement-settlement-select']({detail:{
   reference,invoiceNumber:reference,rowVersion:'rv'}});
  await flush();
 };
 const action=()=>get('ssRows').children[0].children[5].children[0].click();
 const submit=id=>get(id).listeners.submit({preventDefault(){}});
 const success=()=>({ok:true,json:async()=>({success:true,data:{}})});
 return {get,posts,gets,flush,select,action,submit,success};
}

test('late maker settlement success cannot refresh or relabel a different invoice',async()=>{
 const h=harness('maker');
 await h.select('A');
 const pending=h.submit('ssForm');
 await h.flush();
 await h.select('B');
 const before=h.get('ssStatus').textContent;
 const bOptions=h.gets.filter(x=>x.includes('/B/settlements/options')).length;
 h.posts[0].resolve(h.success());
 await pending;
 await h.flush();
 assert.equal(h.gets.filter(x=>x.includes('/B/settlements/options')).length,bOptions);
 assert.equal(h.get('ssStatus').textContent,before);
 assert.match(h.get('ssSelected').textContent,/^B ·/);
});

test('late withdrawal error cannot overwrite a newly selected invoice',async()=>{
 const h=harness('withdraw');
 await h.select('A');
 h.action();
 const pending=h.submit('ssWithdrawForm');
 await h.flush();
 await h.select('B');
 h.action();
 const before=h.get('ssStatus').textContent;
 h.posts[0].reject(new Error('obsolete withdrawal failure'));
 await pending;
 await h.flush();
 assert.equal(h.get('ssWithdrawDialog').open,true);
 assert.equal(h.get('ssStatus').textContent,before);
});

test('old reviewer completion cannot close a new dialog after A to B to A',async()=>{
 const h=harness('review');
 await h.select('A');
 h.action();
 const pending=h.submit('ssReviewForm');
 await h.flush();
 await h.select('B');
 await h.select('A');
 h.action();
 const before=h.get('ssStatus').textContent;
 h.posts[0].resolve(h.success());
 await pending;
 await h.flush();
 assert.equal(h.get('ssReviewDialog').open,true);
 assert.equal(h.get('ssStatus').textContent,before);
});
