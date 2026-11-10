'use strict';
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const source=fs.readFileSync(path.resolve(__dirname,
 '../../EduOS.App/wwwroot/js/payroll/loan-register.js'),'utf8');

function harness(){
 class Element{
  constructor(tag='div'){this.tagName=tag.toUpperCase();this.value='';this.children=[];
   this.listeners={};this.className='';this.textContent='';this.disabled=false;this.inert=false;}
  addEventListener(type,fn){this.listeners[type]=fn;}
  append(...children){this.children.push(...children);}
  add(option){this.children.push(option);}
  replaceChildren(...children){this.children=children;}
  reset(){this.value='';}
  focus(){}
  reportValidity(){return true;}
  querySelector(selector){return selector==='input[name="__RequestVerificationToken"]'?{value:'csrf'}:null;}
 }
 const elements={},gets=[],posts=[],searches=[];
 const el=id=>elements[id]??(elements[id]=new Element());
 let errorHistory=false,denyHistory=false,holdSearch=false,dirty=false,confirmDiscard=true,uuid=0;
 const ok=data=>({ok:true,status:200,json:async()=>({success:true,data})});
 const fail=(message,status=503)=>({ok:false,status,json:async()=>({success:false,message})});
 const fetch=async(url,options={})=>{
  if(options.method==='POST')return new Promise(resolve=>posts.push({body:JSON.parse(options.body),resolve}));
  gets.push(url);
  if(url.startsWith('/api/hr-payroll/employee-options?')){
   if(holdSearch)return new Promise(resolve=>searches.push(resolve));
   return ok({totalCount:1,items:[{employeeCode:'E-1',fullName:'Alice',reference:'A'}]});
  }
  if(url.startsWith('/api/finance/loan-disbursements?')){
   if(denyHistory)return fail('Not authorized',403);
   if(errorHistory)return fail('Register unavailable');
   return ok({pageSize:10,totalCount:0,loans:[]});
  }
  throw Error('Unexpected GET: '+url);
 };
 const document={getElementById:el,createElement:tag=>new Element(tag),querySelector:()=>({content:'csrf'})};
 const window={EduOSFormGuard:{create:()=>({isDirty:()=>dirty,confirmDiscard:()=>confirmDiscard,
  markClean:()=>{dirty=false;}})}};
 el('loanRegForm').reset=()=>{for(const id of ['loanRegSearch','loanRegEmployee','loanRegDate',
  'loanRegAmount','loanRegInstallment','loanRegRemarks'])el(id).value='';};
 const Option=class {constructor(text,value){this.text=text;this.value=value;}};
 const crypto={randomUUID:()=>`id-${++uuid}`};
 vm.runInNewContext(source,{document,window,Option,crypto,fetch,Date,URLSearchParams,
  confirm:()=>confirmDiscard});
 const flush=async()=>{for(let i=0;i<35;i++)await Promise.resolve();};
 const click=id=>el(id).listeners.click?.();
 const input=id=>el(id).listeners.input?.();
 const change=id=>el(id).listeners.change?.();
 const submit=()=>el('loanRegForm').listeners.submit({preventDefault(){},currentTarget:el('loanRegForm')});
 const choose=async()=>{
  el('loanRegSearch').value='Alice';click('loanRegFind');await flush();
  el('loanRegEmployee').value='A';change('loanRegEmployee');
 };
 const draft=()=>{el('loanRegDate').value='2026-11-10';el('loanRegAmount').value='1000';
  el('loanRegInstallment').value='100';el('loanRegRemarks').value='Approved advance';};
 return {el,gets,posts,searches,ok,fail,flush,click,input,change,submit,choose,draft,
  set errorHistory(v){errorHistory=v;},set denyHistory(v){denyHistory=v;},
  set holdSearch(v){holdSearch=v;},set dirty(v){dirty=v;},set confirmDiscard(v){confirmDiscard=v;}};
}

test('loan registration single-flight and exact-command idempotency on uncertain retries',async()=>{
 const h=harness();await h.flush();await h.choose();h.draft();
 h.submit();h.submit();await h.flush();assert.equal(h.posts.length,1);
 assert.equal(h.el('payrollLoanRegister').inert,true);
 h.posts[0].resolve(h.fail('Network interrupted'));await h.flush();
 assert.equal(h.el('loanRegSave').disabled,false);
 h.submit();await h.flush();assert.equal(h.posts.length,2);
 assert.equal(h.posts[1].body.clientRequestId,h.posts[0].body.clientRequestId);
 h.posts[1].resolve(h.fail('Network interrupted'));await h.flush();
 h.el('loanRegAmount').value='1200';h.submit();await h.flush();
 assert.notEqual(h.posts[2].body.clientRequestId,h.posts[1].body.clientRequestId);
 h.posts[2].resolve(h.fail('Network interrupted'));await h.flush();
});

test('successful loan registration remains successful when register readback fails',async()=>{
 const h=harness();await h.flush();await h.choose();h.draft();h.submit();await h.flush();
 h.errorHistory=true;h.posts[0].resolve(h.ok({id:72}));await h.flush();
 assert.match(h.el('loanRegNotice').className,/alert-success/);
 assert.match(h.el('loanRegNotice').textContent,/Loan #72 registered.*refresh unavailable/);
 assert.equal(h.el('loanRegSave').disabled,true);
 assert.equal(h.el('payrollLoanRegister').inert,false);
});

test('stale employee search cannot replace changed search term',async()=>{
 const h=harness();await h.flush();h.holdSearch=true;
 h.el('loanRegSearch').value='Ali';h.click('loanRegFind');await h.flush();
 h.el('loanRegSearch').value='Bob';h.input('loanRegSearch');
 h.searches[0](h.ok({totalCount:1,items:[{employeeCode:'E1',fullName:'Alice',reference:'A'}]}));
 await h.flush();assert.equal(h.el('loanRegEmployee').children.length,0);
});

test('New entry honors unsaved draft guard and does not silently clear loan amounts',async()=>{
 const h=harness();await h.flush();await h.choose();h.draft();h.dirty=true;
 h.confirmDiscard=false;h.click('loanRegNew');
 assert.equal(h.el('loanRegAmount').value,'1000');
 h.confirmDiscard=true;h.click('loanRegNew');assert.equal(h.el('loanRegAmount').value,'');
 assert.match(source,/guard\?\.isDirty\(\)&&!guard\.confirmDiscard\(\)/);
});

test('revoked Finance access clears loan register and prevents new POST',async()=>{
 const h=harness();await h.flush();h.denyHistory=true;h.click('loanRegRefresh');await h.flush();
 assert.equal(h.el('payrollLoanRegister').inert,true);
 assert.equal(h.el('loanRegRows').children.length,0);
 h.submit();await h.flush();assert.equal(h.posts.length,0);
});

test('server enforces replay identity and keeps disbursement separate from registration',()=>{
 const page=fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.App/Views/PayrollLoanRegister/Index.cshtml'),'utf8');
 const service=fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.Service/Services/HR/HrPayrollService.cs'),'utf8');
 assert.match(page,/form-guard\.js"[\s\S]*loan-register\.js"/);
 assert.match(source,/requestIdFor\(command\)/);
 assert.match(source,/submitted=true;guard\?\.markClean\(\)/);
 assert.match(service,/Loan date is outside the employee's employment period/);
 assert.match(service,/Request key already belongs to a different loan/);
 assert.match(service,/Loan registered; disbursement journal must be completed separately/);
});
