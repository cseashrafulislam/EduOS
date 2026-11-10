'use strict';
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const source=fs.readFileSync(path.resolve(__dirname,
 '../../EduOS.App/wwwroot/js/hr/bank-accounts.js'),'utf8');

function harness(){
 class Element{
  constructor(tag='div'){this.tagName=tag.toUpperCase();this.value='';this.textContent='';
   this.children=[];this.listeners={};this.className='';this.checked=false;
   this.disabled=false;this.inert=false;}
  addEventListener(type,fn){this.listeners[type]=fn;}
  append(...nodes){this.children.push(...nodes);}
  replaceChildren(...nodes){this.children=nodes;}
  focus(){}
  add(node){this.children.push(node);}
  reset(){this.value='';}
  reportValidity(){return true;}
 }
 const fields={},gets=[],writes=[],heldReads=[],events={};
 const el=id=>fields[id]??(fields[id]=new Element());
 let holdEmployee='',failAccounts=false,deny=false,confirmDiscard=true;
 el('hrBankForm').reset=()=>{for(const id of ['hrBankName','hrBankAccountName','hrBankNumber',
  'hrBankBranch','hrBankRouting','hrBankCurrency'])el(id).value='';
  el('hrBankPrimary').checked=false;el('hrBankActive').checked=false;};
 const ok=data=>({ok:true,status:200,json:async()=>({success:true,data})});
 const fail=(message,status=503)=>({ok:false,status,json:async()=>({success:false,message})});
 const bank=ref=>[{id:12,rowVersion:'rv-'+ref,bankName:'Bank '+ref,accountName:'User '+ref,
  accountNumberLast4:'1234',branchName:'Dhaka',routingNumber:'123',currencyCode:'BDT',
  isPrimary:true,isActive:true}];
 const fetch=async(url,options={})=>{
  if(options.method==='POST'||options.method==='PUT')return new Promise(resolve=>writes.push({
   url,body:JSON.parse(options.body),resolve}));
  gets.push(url);
  if(url.startsWith('/api/hr/employees?'))return ok({totalCount:2,items:[
   {reference:'A',employeeCode:'E1',fullName:'Alice'},
   {reference:'B',employeeCode:'E2',fullName:'Bob'}]});
  if(url.includes('/bank-accounts')){
   if(deny)return fail('Tenant administrator required',403);
   if(failAccounts)return fail('Masked register unavailable');
   const ref=url.match(/employees\/(A|B)/)?.[1];
   if(ref===holdEmployee)return new Promise(resolve=>heldReads.push({ref,resolve}));
   return ok(bank(ref));
  }
  throw Error('Unexpected GET: '+url);
 };
 const window={confirm:()=>confirmDiscard,addEventListener:(name,handler)=>{events[name]=handler;}};
 const document={getElementById:el,querySelector:()=>({content:'csrf'}),
  createElement:tag=>new Element(tag)};
 vm.runInNewContext(source,{document,window,fetch,Option:class {constructor(text,value){this.text=text;this.value=value;}}});
 const flush=async()=>{for(let i=0;i<35;i++)await Promise.resolve();};
 const submit=id=>el(id).listeners.submit({preventDefault(){},currentTarget:el(id)});
 const click=id=>el(id).listeners.click?.();
 const change=id=>el(id).listeners.change?.();
 const select=async ref=>{
  el('hrBankSearch').value='Ali';submit('hrBankSearchForm');await flush();
  el('hrBankEmployee').value=ref;change('hrBankEmployee');await flush();
 };
 const draft=()=>{
  el('hrBankName').value='Bank';el('hrBankAccountName').value='Alice';
  el('hrBankNumber').value='012345678901';el('hrBankCurrency').value='BDT';
  el('hrBankActive').checked=true;
 };
 return {el,gets,writes,heldReads,events,bank,ok,fail,flush,submit,click,change,select,draft,
  set holdEmployee(v){holdEmployee=v;},set failAccounts(v){failAccounts=v;},
  set deny(v){deny=v;},set confirmDiscard(v){confirmDiscard=v;}};
}

test('protected bank-account POST single-flight clears plaintext after uncertain response',async()=>{
 const h=harness();await h.select('A');h.draft();
 h.submit('hrBankForm');h.submit('hrBankForm');await h.flush();
 assert.equal(h.writes.length,1);assert.equal(h.el('hrBankWorkspace').inert,true);
 assert.equal(h.writes[0].body.accountNumber,'012345678901');
 h.writes[0].resolve(h.fail('Network lost'));await h.flush();
 assert.equal(h.el('hrBankNumber').value,'');
 assert.match(h.el('hrBankAlert').textContent,/Check the masked register and re-enter/);
 assert.equal(h.el('hrBankWorkspace').inert,false);
});

test('confirmed bank-account update remains success when masked register refresh fails',async()=>{
 const h=harness();await h.select('A');h.draft();h.submit('hrBankForm');await h.flush();
 h.failAccounts=true;h.writes[0].resolve(h.ok({id:13}));await h.flush();
 assert.match(h.el('hrBankAlert').className,/alert-success/);
 assert.match(h.el('hrBankAlert').textContent,/Protected bank account saved.*refresh unavailable/);
 assert.equal(h.el('hrBankNumber').value,'');
});

test('unsaved bank details cannot silently disappear on search, employee switch or Edit',async()=>{
 const h=harness();await h.select('A');h.draft();h.confirmDiscard=false;
 h.el('hrBankEmployee').value='B';h.change('hrBankEmployee');
 assert.equal(h.el('hrBankEmployee').value,'A');
 assert.equal(h.el('hrBankNumber').value,'012345678901');
 h.el('hrBankSearch').value='Bob';h.submit('hrBankSearchForm');await h.flush();
 assert.equal(h.el('hrBankNumber').value,'012345678901');
 h.el('hrBankRows').children[0].children[6].children[0].listeners.click();
 assert.equal(h.el('hrBankNumber').value,'012345678901');
});

test('late employee A account response cannot replace employee B register',async()=>{
 const h=harness();h.holdEmployee='A';await h.select('A');assert.equal(h.heldReads.length,1);
 h.holdEmployee='';h.el('hrBankEmployee').value='B';h.change('hrBankEmployee');await h.flush();
 assert.equal(h.el('hrBankRows').children[0].children[0].textContent,'Bank B');
 h.heldReads[0].resolve(h.ok(h.bank('A')));await h.flush();
 assert.equal(h.el('hrBankRows').children[0].children[0].textContent,'Bank B');
});

test('revoked payroll bank permission clears protected records and prevents writes',async()=>{
 const h=harness();await h.select('A');h.deny=true;
 h.change('hrBankEmployee');await h.flush();
 assert.equal(h.el('hrBankWorkspace').inert,true);
 assert.equal(h.el('hrBankRows').children.length,0);
 h.draft();h.submit('hrBankForm');await h.flush();assert.equal(h.writes.length,0);
});

test('bank unsaved guard never fingerprints or persists plaintext account number',()=>{
 const snapshot=source.slice(source.indexOf('function nonSecretSnapshot()'),source.indexOf('function markClean()'));
 assert.doesNotMatch(snapshot,/hrBankNumber|accountNumber/);
 assert.match(source,/function dirty\(\) \{ return !!el\('hrBankNumber'\)\.value/);
 assert.match(source,/event\.preventDefault\(\);event\.returnValue=''/);
 const view=fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.App/Views/HR/BankAccounts.cshtml'),'utf8');
 assert.match(view,/type="password"/);
 const service=fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.Service/Services/HR/HrAdminService.cs'),'utf8');
 assert.match(service,/"EduOS\.HR\.EmployeeBankAccount\.v1"/);
 assert.match(service,/protector\.Protect\(number\)/);
});
