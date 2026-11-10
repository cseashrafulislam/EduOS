'use strict';
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const source=fs.readFileSync(path.resolve(__dirname,
 '../../EduOS.App/wwwroot/js/payroll/salary-structures.js'),'utf8');

function harness(){
 class Element{
  constructor(tag='div'){
   this.tagName=tag.toUpperCase();this.value='';this.textContent='';this.className='';
   this.disabled=false;this.inert=false;this.required=false;this.readOnly=false;
   this.children=[];this.dataset={};this.listeners={};this.style={};
   this.classList={toggle:()=>{}};
  }
  get options(){return this.children;}
  append(...elements){this.children.push(...elements);}
  replaceChildren(...elements){this.children=elements;}
  addEventListener(type,fn){this.listeners[type]=fn;}
  setAttribute(){}
  reportValidity(){return true;}
  querySelector(selector){
   if(selector==='input[name="__RequestVerificationToken"]')return {value:'csrf'};
   if(selector==='select')return this.children.find(x=>x.tagName==='SELECT')||
    this.children.flatMap(x=>x.children).find(x=>x.tagName==='SELECT');
   if(selector.startsWith('.'))return this.children.flatMap(x=>x.children)
    .find(x=>x.className.split(' ').includes(selector.slice(1)));
   return null;
  }
  querySelectorAll(selector){return selector==='tr'?
   this.children.filter(x=>x.tagName==='TR'):[];}
 }
 const elements={},gets=[],posts=[],heldHistory=[],heldFind=[];
 const el=id=>elements[id]??(elements[id]=new Element());
 const root=el('salaryWorkspace');root.querySelectorAll=()=>[];
 let holdHistory='',holdFind=false,deny=false,failHistory=false,allowDiscard=true,dirty=false,uid=0,markClean=0;
 const ok=data=>({ok:true,status:200,json:async()=>({success:true,data})});
 const fail=(message,status=503)=>({ok:false,status,json:async()=>({success:false,message})});
 const components=[{id:1,code:'BASE',name:'Basic salary',isActive:true,isTaxable:true,type:1}];
 const history=reference=>({totalCount:1,items:[{effectiveFrom:'2026-11-01',effectiveTo:null,
  isCurrent:true,grossMonthlyAmount:reference==='A'?1000:2000,lines:[]}]});
 const fetch=async(url,options={})=>{
  if(options.method&&options.method!=='GET')
   return new Promise(resolve=>posts.push({url,body:JSON.parse(options.body),resolve}));
  gets.push(url);
  if(url==='/api/hr-payroll/salary-components')return ok(components);
  if(url.startsWith('/api/hr-payroll/employee-options?')){
   if(holdFind)return new Promise(resolve=>heldFind.push(resolve));
   return ok({totalCount:2,items:[{reference:'A',fullName:'Alice',employeeCode:'E1'},
    {reference:'B',fullName:'Bob',employeeCode:'E2'}]});
  }
  if(url.startsWith('/api/hr-payroll/salary-structures/')){
   if(deny)return fail('HR access revoked',403);
   if(failHistory)return fail('History unavailable');
   const reference=url.match(/structures\/(A|B)/)?.[1];
   if(reference===holdHistory)return new Promise(resolve=>heldHistory.push({reference,resolve}));
   return ok(history(reference));
  }
  throw Error('Unexpected URL: '+url);
 };
 const guard={isDirty:()=>dirty,confirmDiscard:()=>allowDiscard,
  markClean:()=>{markClean++;dirty=false;}};
 const document={getElementById:el,createElement:tag=>new Element(tag)};
 const window={EduOSFormGuard:{create:()=>guard}};
 const crypto={randomUUID:()=>`request-${++uid}`};
 vm.runInNewContext(source,{document,window,crypto,fetch,URLSearchParams,
  confirm:()=>true,bootstrap:{},console});
 const flush=async()=>{for(let i=0;i<50;i++)await Promise.resolve();};
 const click=id=>el(id).listeners.click?.();
 const change=id=>el(id).listeners.change?.();
 const submit=()=>el('salaryForm').listeners.submit({preventDefault(){},
  currentTarget:el('salaryForm')});
 const select=async reference=>{
  el('salaryEmployeeSearch').value='Ali';click('salaryFind');await flush();
  el('salaryEmployee').value=reference;change('salaryEmployee');await flush();
 };
 const draft=()=>{
  el('salaryEffective').value='2026-12-01';
  const tr=el('salaryLines').children[0];
  tr.querySelector('select').value='1';tr.querySelector('.salary-amount').value='1000';
 };
 return {el,gets,posts,heldHistory,heldFind,flush,click,change,submit,select,draft,history,ok,fail,
  get markClean(){return markClean;},set holdHistory(v){holdHistory=v;},
  set holdFind(v){holdFind=v;},set deny(v){deny=v;},set failHistory(v){failHistory=v;},
  set allowDiscard(v){allowDiscard=v;},set dirty(v){dirty=v;}};
}

test('salary save is single-flight and retains the exact retry ID for an unchanged uncertain POST',async()=>{
 const h=harness();await h.flush();await h.select('A');h.draft();h.submit();h.submit();await h.flush();
 assert.equal(h.posts.length,1);
 assert.equal(h.el('salaryWorkspace').inert,true);
 h.posts[0].resolve(h.fail('Gateway unavailable'));await h.flush();
 h.submit();await h.flush();assert.equal(h.posts.length,2);
 assert.equal(h.posts[1].body.clientRequestId,h.posts[0].body.clientRequestId);
 h.posts[1].resolve(h.fail('Gateway unavailable'));await h.flush();
 h.el('salaryLines').children[0].querySelector('.salary-amount').value='1200';
 h.submit();await h.flush();assert.equal(h.posts.length,3);
 assert.notEqual(h.posts[2].body.clientRequestId,h.posts[1].body.clientRequestId);
 h.posts[2].resolve(h.fail('Gateway unavailable'));await h.flush();
});

test('confirmed salary write is a success even when the history refresh fails',async()=>{
 const h=harness();await h.flush();await h.select('A');h.draft();
 h.submit();await h.flush();h.failHistory=true;
 h.posts[0].resolve(h.ok({id:91}));await h.flush();
 assert.equal(h.posts.length,1);
 assert.match(h.el('salaryNotice').className,/alert-success/);
 assert.match(h.el('salaryNotice').textContent,/Salary structure saved.*History refresh unavailable/);
 assert.equal(h.el('salaryWorkspace').inert,false);
 assert.ok(h.markClean>1);
});

test('late salary history from employee A cannot overwrite employee B',async()=>{
 const h=harness();await h.flush();h.holdHistory='A';await h.select('A');
 assert.equal(h.heldHistory.length,1);
 h.holdHistory='';h.el('salaryEmployee').value='B';h.change('salaryEmployee');await h.flush();
 assert.match(h.el('salaryHistory').children[0].children[3].textContent,/2000\.00/);
 h.heldHistory[0].resolve(h.ok(h.history('A')));await h.flush();
 assert.match(h.el('salaryHistory').children[0].children[3].textContent,/2000\.00/);
});

test('late employee search does not replace a newer search term or selection',async()=>{
 const h=harness();await h.flush();h.holdFind=true;
 h.el('salaryEmployeeSearch').value='Al';h.click('salaryFind');await h.flush();
 h.el('salaryEmployeeSearch').value='Bo';h.el('salaryEmployeeSearch').listeners.input();
 h.heldFind[0](h.ok({totalCount:1,items:[{reference:'A',fullName:'Alice',employeeCode:'E1'}]}));
 await h.flush();
 assert.equal(h.el('salaryEmployee').children.length,0);
});

test('unsaved salary draft is not discarded by employee switch, new search or historical copy',async()=>{
 const h=harness();await h.flush();await h.select('A');h.draft();
 h.dirty=true;h.allowDiscard=false;
 const original=h.el('salaryLines').children[0].querySelector('.salary-amount').value;
 h.el('salaryEmployee').value='B';h.change('salaryEmployee');await h.flush();
 assert.equal(h.el('salaryEmployee').value,'A');
 assert.equal(h.el('salaryLines').children[0].querySelector('.salary-amount').value,original);
 h.el('salaryEmployeeSearch').value='Bob';const searches=h.gets.length;
 h.click('salaryFind');await h.flush();assert.equal(h.gets.length,searches);
 const copy=h.el('salaryHistory').children[0].children[5].children[0];copy.onclick();
 assert.equal(h.el('salaryEffective').value,'2026-12-01');
});

test('denied salary history removes privileged rows and blocks future submission',async()=>{
 const h=harness();await h.flush();await h.select('A');h.deny=true;
 h.change('salaryEmployee');await h.flush();
 assert.equal(h.el('salaryWorkspace').inert,true);
 assert.equal(h.el('salaryHistory').children.length,0);
 h.draft=()=>{};h.submit();await h.flush();assert.equal(h.posts.length,0);
});

test('salary editor keeps the server as source of truth for tenant and idempotency',()=>{
 const page=fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.App/Views/PayrollSalaryStructures/Index.cshtml'),'utf8');
 const service=fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.Service/Services/HR/HrPayrollService.cs'),'utf8');
 assert.match(page,/form-guard\.js"[\s\S]*salary-structures\.js"/);
 assert.match(source,/requestIdFor\(command\)/);
 assert.match(source,/!confirmDiscard\(\)/);
 assert.match(source,/serial===state\.historySerial/);
 assert.match(service,/SalaryStructureFingerprint\(request\)/);
 assert.match(service,/x\.TenantId == _user\.TenantId/);
});
