'use strict';
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const source=fs.readFileSync(path.resolve(__dirname,
 '../../EduOS.App/wwwroot/js/hr/shift-assignments.js'),'utf8');

function harness(){
 class Element{
  constructor(tag='div'){this.tagName=tag.toUpperCase();this.value='';this.textContent='';
   this.children=[];this.listeners={};this.disabled=false;this.inert=false;}
  append(...c){this.children.push(...c);}
  replaceChildren(...c){this.children=c;}
  addEventListener(type,fn){this.listeners[type]=fn;}
  focus(){}
  reportValidity(){return true;}
  reset(){this.value='';}
 }
 const fields={},gets=[],posts=[],pending=[];
 const el=id=>fields[id]??(fields[id]=new Element());
 let hold='',failHistory=false,deny=false,dirty=false,allowDiscard=true,uid=0;
 el('hrShiftForm').reset=()=>{for(const id of ['hrShiftCode','hrShiftFrom','hrShiftTo'])el(id).value='';};
 const ok=data=>({ok:true,status:200,json:async()=>({success:true,data})});
 const fail=(msg,status=503)=>({ok:false,status,json:async()=>({success:false,message:msg})});
 const fetch=async(url,options={})=>{
  if(options.method==='POST')return new Promise(resolve=>posts.push({body:JSON.parse(options.body),resolve}));
  gets.push(url);
  if(url==='/api/hr/setup/shifts')return ok([{id:3,name:'Morning',startTime:'08:00:00',endTime:'16:00:00',isActive:true}]);
  if(url.startsWith('/api/hr/employees?'))return ok({totalCount:2,items:[
   {reference:'A',employeeCode:'E1',fullName:'Alice'},
   {reference:'B',employeeCode:'E2',fullName:'Bob'}]});
  if(url.includes('shift-assignments')){
   if(deny)return fail('Access revoked',403);
   if(failHistory)return fail('History unavailable');
   const ref=url.match(/employees\/(A|B)/)?.[1];
   if(ref===hold)return new Promise(resolve=>pending.push({ref,resolve}));
   return ok({page:1,totalCount:1,items:[{workShiftName:'Shift '+ref,
    effectiveFrom:'2026-11-10',effectiveTo:null,isCurrent:true}]});
  }
  throw Error('Unexpected request: '+url);
 };
 const window={confirm:()=>true,EduOSFormGuard:{create:()=>({
  isDirty:()=>dirty,confirmDiscard:()=>allowDiscard,markClean:()=>{dirty=false;}
 })}};
 const document={getElementById:el,querySelector:()=>({content:'csrf'}),
  createElement:tag=>new Element(tag)};
 const crypto={randomUUID:()=>`key-${++uid}`};
 vm.runInNewContext(source,{window,document,fetch,crypto,Date,encodeURIComponent});
 const flush=async()=>{for(let i=0;i<35;i++)await Promise.resolve();};
 const click=id=>el(id).listeners.click?.();
 const change=id=>el(id).listeners.change?.();
 const submit=()=>el('hrShiftForm').listeners.submit({preventDefault(){},currentTarget:el('hrShiftForm')});
 const select=async ref=>{
  el('hrShiftSearch').value='Al';click('hrShiftFind');await flush();
  el('hrShiftEmployee').value=ref;change('hrShiftEmployee');await flush();
 };
 const draft=()=>{el('hrShiftCode').value='3';el('hrShiftFrom').value='2026-11-11';
  el('hrShiftTo').value='';};
 return {el,gets,posts,pending,ok,fail,flush,click,change,submit,select,draft,
  set hold(v){hold=v;},set failHistory(v){failHistory=v;},set deny(v){deny=v;},
  set dirty(v){dirty=v;},set allowDiscard(v){allowDiscard=v;}};
}

test('shift assignment is single-flight and retries identical command with same receipt',async()=>{
 const h=harness();await h.flush();await h.select('A');h.draft();
 h.submit();h.submit();await h.flush();assert.equal(h.posts.length,1);
 assert.equal(h.el('hrShiftWorkspace').inert,true);
 h.posts[0].resolve(h.fail('Gateway timeout'));await h.flush();
 h.submit();await h.flush();assert.equal(h.posts.length,2);
 assert.equal(h.posts[0].body.clientRequestId,h.posts[1].body.clientRequestId);
 h.posts[1].resolve(h.fail('Gateway timeout'));await h.flush();
 h.el('hrShiftTo').value='2026-12-31';h.submit();await h.flush();
 assert.notEqual(h.posts[2].body.clientRequestId,h.posts[1].body.clientRequestId);
 h.posts[2].resolve(h.fail('Gateway timeout'));await h.flush();
});

test('committed shift assignment stays success when history GET is unavailable',async()=>{
 const h=harness();await h.flush();await h.select('A');h.draft();h.submit();await h.flush();
 h.failHistory=true;h.posts[0].resolve(h.ok({id:11}));await h.flush();
 assert.match(h.el('hrShiftAlert').className,/alert-success/);
 assert.match(h.el('hrShiftAlert').textContent,/Shift assignment recorded.*refresh unavailable/);
 assert.equal(h.el('hrShiftWorkspace').inert,false);
 h.submit();await h.flush();assert.equal(h.posts.length,1,'saved exact command cannot be reposted');
});

test('late employee A history cannot replace employee B shift history',async()=>{
 const h=harness();await h.flush();h.hold='A';await h.select('A');
 assert.equal(h.pending.length,1);h.hold='';
 h.el('hrShiftEmployee').value='B';h.change('hrShiftEmployee');await h.flush();
 assert.equal(h.el('hrShiftRows').children[0].children[0].textContent,'Shift B');
 h.pending[0].resolve(h.ok({page:1,totalCount:1,items:[{workShiftName:'Shift A',
  effectiveFrom:'2026-11-10',isCurrent:true}]}));await h.flush();
 assert.equal(h.el('hrShiftRows').children[0].children[0].textContent,'Shift B');
});

test('dirty shift assignment protects employee switch until explicit discard',async()=>{
 const h=harness();await h.flush();await h.select('A');h.draft();
 h.dirty=true;h.allowDiscard=false;h.el('hrShiftEmployee').value='B';
 h.change('hrShiftEmployee');await h.flush();
 assert.equal(h.el('hrShiftEmployee').value,'A');
 assert.equal(h.el('hrShiftFrom').value,'2026-11-11');
});

test('denied shift history clears selection and prevents subsequent writes',async()=>{
 const h=harness();await h.flush();await h.select('A');h.deny=true;
 h.change('hrShiftEmployee');await h.flush();
 assert.equal(h.el('hrShiftWorkspace').inert,true);
 assert.equal(h.el('hrShiftRows').children.length,0);
 h.draft();h.submit();await h.flush();assert.equal(h.posts.length,0);
});

test('shift editor retains existing server-side effective date and tenant policy',()=>{
 const view=fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.App/Views/HR/ShiftAssignments.cshtml'),'utf8');
 assert.match(view,/form-guard\.js"[\s\S]*shift-assignments\.js"/);
 assert.match(source,/state\.lastPostedFingerprint/);
 assert.match(source,/seq!==state\.loadSeq/);
 assert.match(source,/signature !== state\.signature/);
 assert.match(source,/employeeReference: state\.employee/);
});
