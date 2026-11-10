'use strict';
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const source=fs.readFileSync(path.resolve(__dirname,
 '../../EduOS.App/wwwroot/js/hr/leave-entitlements.js'),'utf8');

function harness(){
 class Element{
  constructor(tag='div'){this.tagName=tag.toUpperCase();this.value='';this.textContent='';
   this.children=[];this.listeners={};this.checked=false;this.disabled=false;this.inert=false;
   this.className='';this.button={disabled:false};}
  addEventListener(type,fn){this.listeners[type]=fn;}
  append(...items){this.children.push(...items);}
  add(option){this.children.push(option);}
  replaceChildren(...items){this.children=items;}
  reset(){this.value='';}
  focus(){}
  reportValidity(){return true;}
  querySelector(selector){return selector==='[type=submit]'?this.button:null;}
 }
 const fields={},gets=[],posts=[],pendingReads=[];
 let failRefresh=false,holdEmployee=null;
 const get=id=>fields[id]??(fields[id]=new Element());
 const document={getElementById:id=>get(id),querySelector:()=>({content:'csrf'}),
  createElement:tag=>new Element(tag)};
 const rows=[{id:1,code:'AL',name:'Annual',maxDaysPerYear:20,isPaid:true,isActive:true,rowVersion:'tv'}];
 const ok=data=>({ok:true,status:200,json:async()=>({success:true,data})});
 const fail=(message,status=503)=>({ok:false,status,json:async()=>({success:false,message})});
 const fetch=async(url,options)=>{
  if(options?.method!=='GET')return new Promise(resolve=>posts.push({url,resolve,body:JSON.parse(options.body)}));
  gets.push(url);
  if(url==='/api/hr/leave-entitlements/types')return ok(rows);
  if(url.startsWith('/api/hr/employees?'))return ok({totalCount:2,items:[
   {reference:'A',employeeCode:'E1',fullName:'Alice'},
   {reference:'B',employeeCode:'E2',fullName:'Bob'}]});
  const match=url.match(/employees\/(A|B)\/(entitlements|balances)/);
  if(match){
   if(holdEmployee===match[1])return new Promise(resolve=>pendingReads.push({url,resolve}));
   if(failRefresh)return fail('Balance refresh unavailable',typeof failRefresh==='number'?failRefresh:503);
   if(match[2]==='entitlements')return ok([{
    leaveTypeId:1,entitledDays:match[1]==='A'?20:24,carriedForwardDays:0,rowVersion:'rv-'+match[1]}]);
   return ok([{leaveTypeName:'Annual',allowedDays:20,usedDays:1,
    remainingDays:match[1]==='A'?19:23}]);
  }
  throw Error('Unexpected GET: '+url);
 };
 vm.runInNewContext(source,{document,window:{},fetch,URLSearchParams,Date,
  Option:class {constructor(text,value){this.text=text;this.value=value;}}});
 const flush=async()=>{for(let i=0;i<35;i++)await Promise.resolve();};
 const submit=id=>get('leaveEntitlement'+id).listeners.submit({preventDefault(){},
  currentTarget:get('leaveEntitlement'+id)});
 const change=id=>get('leaveEntitlement'+id).listeners.change?.();
 const select=async reference=>{
  get('leaveEntitlementEmployeeSearch').value='Ali';submit('EmployeeSearchForm');await flush();
  get('leaveEntitlementEmployee').value=reference;change('Employee');
  get('leaveEntitlementYear').value='2026';change('Year');
  submit('EmployeeForm');await flush();
 };
 return {get,gets,posts,pendingReads,flush,submit,change,select,ok,
  set failRefresh(value){failRefresh=value;},set holdEmployee(value){holdEmployee=value;}};
}

test('HR employee selector uses one bounded server-filtered search',async()=>{
 const h=harness();await h.flush();
 h.get('leaveEntitlementEmployeeSearch').value='Al';h.submit('EmployeeSearchForm');await h.flush();
 const hits=h.gets.filter(url=>url.startsWith('/api/hr/employees?'));
 assert.equal(hits.length,1);
 assert.match(hits[0],/search=Al/);
 assert.match(hits[0],/pageSize=50/);
 assert.equal(h.get('leaveEntitlementEmployee').children.length,3);
 assert.doesNotMatch(source,/while\(page<=pages\)/);
});

test('old annual allowance requests cannot replace a different employee or year',async()=>{
 const h=harness();await h.flush();h.holdEmployee='A';
 await h.select('A');assert.equal(h.pendingReads.length,2);
 h.holdEmployee=null;
 h.get('leaveEntitlementEmployee').value='B';h.change('Employee');
 h.submit('EmployeeForm');await h.flush();
 h.get('leaveEntitlementAllowanceType').value='1';h.change('AllowanceType');
 assert.equal(h.get('leaveEntitlementAllowed').value,24);
 for(const read of h.pendingReads)read.resolve(h.ok([]));await h.flush();
 assert.equal(h.get('leaveEntitlementAllowed').value,24);
 assert.equal(h.get('leaveEntitlementAllowanceForm').inert,false);
});

test('adjustment is single-flight; a committed write remains a success on failed reload',async()=>{
 const h=harness();await h.flush();await h.select('A');
 h.get('leaveEntitlementAdjustmentType').value='1';
 h.get('leaveEntitlementAdjustmentDays').value='2';
 h.get('leaveEntitlementAdjustmentReason').value='Approved leave correction';
 h.submit('AdjustmentForm');h.submit('AdjustmentForm');await h.flush();
 assert.equal(h.posts.length,1);
 assert.equal(h.posts[0].body.employeeReference,'A');
 assert.equal(h.get('leaveEntitlementAdjustmentForm').inert,true);
 h.failRefresh=true;h.posts[0].resolve(h.ok({id:55}));await h.flush();
 assert.equal(h.get('leaveEntitlementAdjustmentForm').inert,true,'unloaded balance remains locked');
 assert.match(h.get('leaveEntitlementAlert').textContent,/Audited leave adjustment saved.*Refresh unavailable/);
 assert.match(h.get('leaveEntitlementAlert').className,/alert-success/);
 assert.equal(h.get('leaveEntitlementAdjustmentDays').value,'');
});

test('leave forms use shared guard and server status, without changing adjustment contract',()=>{
 const page=fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.App/Views/LeaveEntitlements/Index.cshtml'),'utf8');
 const service=fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.Service/Services/HR/LeaveEntitlementService.cs'),'utf8');
 assert.match(page,/form-guard\.js"[\s\S]*leave-entitlements\.js"/);
 assert.match(source,/guards\.type\?\.isDirty\(\)/);
 assert.match(source,/!confirmDiscard\(\)/);
 assert.match(source,/forms\.allowance\.inert=true/);
 assert.match(source,/if\(current!==loadSeq\|\|currentContext\(\)!==context\)return/);
 assert.match(service,/Existing DTO has no client-request ID; prevent exact duplicate adjustment replay/);
});

test('revoked HR permission clears old employee balance and blocks further writes',async()=>{
 const h=harness();await h.flush();await h.select('A');
 assert.equal(h.get('leaveEntitlementAllowanceForm').inert,false);
 h.failRefresh=403;h.submit('EmployeeForm');await h.flush();
 assert.equal(h.get('leaveEntitlementAllowanceRows').children.length,0);
 assert.equal(h.get('leaveEntitlementEmployee').children.length,1);
 assert.equal(h.get('leaveEntitlementTypeForm').inert,true);
 h.get('leaveEntitlementAdjustmentType').value='1';h.submit('AdjustmentForm');await h.flush();
 assert.equal(h.posts.length,0);
});
