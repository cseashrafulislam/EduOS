'use strict';
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const source=fs.readFileSync(path.resolve(__dirname,
 '../../EduOS.App/wwwroot/js/hr/employee-attendance.js'),'utf8');

function harness(){
 class Element{
  constructor(tag='div'){this.tagName=tag.toUpperCase();this.value='';this.children=[];
   this.listeners={};this.textContent='';this.disabled=false;this.inert=false;
   this.className='';this.classList={add:()=>{this.hidden=true;},remove:()=>{this.hidden=false;}};}
  addEventListener(type,fn){this.listeners[type]=fn;}
  append(...elements){this.children.push(...elements);}
  add(option){this.children.push(option);}
  replaceChildren(...children){this.children=children;}
  focus(){}
  scrollIntoView(){}
  reportValidity(){return true;}
 }
 const fields={},gets=[],writes=[],pendingSearch=[],pendingHistory=[],pendingList=[];
 const el=id=>fields[id]??(fields[id]=new Element());
 const dirty={},allowDiscard={};
 const resetIds={Form:['Employee','Date','State','In','Out','Ot','Remarks'],
  CorrectionForm:['CorrectionId','CorrectionVersion','CorrectionState','CorrectionReason']};
 for(const [name,ids] of Object.entries(resetIds))el(name).reset=()=>{
  for(const id of ids)el(id).value=id==='State'?'1':id==='Ot'?'0':'';
 };
 el('State').value='1';el('Ot').value='0';
 el('Employee').value='';el('FilterEmployee').value='';
 const ok=data=>({ok:true,status:200,json:async()=>({success:true,data})});
 const fail=(message,status=503)=>({ok:false,status,json:async()=>({success:false,message})});
 const attendance=()=>({totalPages:1,totalCount:2,items:[
  {id:10,rowVersion:'va',attendanceDate:'2026-11-10',employeeCode:'E1',employeeName:'Alice',state:1},
  {id:20,rowVersion:'vb',attendanceDate:'2026-11-10',employeeCode:'E2',employeeName:'Bob',state:2}]});
 let holdSearch=false,holdHistory=false,holdList=false,failList=false,deny=false,uid=0;
 const fetch=async(url,options={})=>{
  if(options.method==='POST')return new Promise(resolve=>writes.push({url,body:JSON.parse(options.body),resolve}));
  gets.push(url);
  if(url.startsWith('/api/hr/employees?')){
   if(holdSearch)return new Promise(resolve=>pendingSearch.push(resolve));
   return ok({totalCount:1,items:[{employeeCode:'E1',fullName:'Alice',reference:'employee-a'}]});
  }
  if(url.startsWith('/api/hr/employee-attendance?')){
   if(deny)return fail('Attendance access revoked',403);
   if(failList)return fail('Register unavailable');
   if(holdList)return new Promise(resolve=>pendingList.push(resolve));
   return ok(attendance());
  }
  if(url.endsWith('/corrections')){
   if(holdHistory)return new Promise(resolve=>pendingHistory.push({url,resolve}));
   return ok([{previousState:1,newState:2,reason:url,changedAt:'2026-11-10',changedByUserId:'admin'}]);
  }
  throw Error('Unexpected GET '+url);
 };
 const guard=form=>({isDirty:()=>!!dirty[form.id],confirmDiscard:()=>allowDiscard[form.id]!==false,
  markClean:()=>{dirty[form.id]=false;}});
 for(const name of ['Form','CorrectionForm'])el(name).id=name;
 const Option=class {constructor(text,value){this.text=text;this.value=value;}};
 const window={EduOSFormGuard:{create:guard}};
 const document={getElementById:id=>el(id.replace(/^employeeAttendance/,'')),
  createElement:tag=>new Element(tag),querySelector:()=>({content:'csrf'})};
 const crypto={randomUUID:()=>`request-${++uid}`};
 vm.runInNewContext(source,{window,document,fetch,crypto,Option,URLSearchParams,Date,
  confirm:()=>true});
 const flush=async()=>{for(let i=0;i<45;i++)await Promise.resolve();};
 const submit=id=>el(id).listeners.submit({preventDefault(){}});
 const click=id=>el(id).listeners.click();
 const action=(row,which)=>el('Rows').children[row].children[7].children[which].listeners.click();
 const draft=()=>{el('Employee').value='employee-a';el('Date').value='2026-11-10';
  el('State').value='1';el('In').value='08:00';el('Out').value='16:00';
  el('Ot').value='1';el('Remarks').value='Office';};
 return {el,gets,writes,pendingSearch,pendingHistory,pendingList,dirty,allowDiscard,
  ok,fail,flush,submit,click,action,draft,
  set holdSearch(v){holdSearch=v;},set holdHistory(v){holdHistory=v;},
  set holdList(v){holdList=v;},set failList(v){failList=v;},set deny(v){deny=v;}};
}

test('attendance search is bounded to one 50-row server query',async()=>{
 const h=harness();await h.flush();
 assert.equal(h.gets.some(url=>url.startsWith('/api/hr/employees?')),false);
 h.el('EmployeeSearch').value='Alice';h.submit('EmployeeSearchForm');await h.flush();
 const searches=h.gets.filter(url=>url.startsWith('/api/hr/employees?'));
 assert.equal(searches.length,1);
 assert.match(searches[0],/pageSize=50/);
 assert.match(searches[0],/search=Alice/);
 assert.equal(h.el('Employee').children[1].value,'employee-a');
 assert.equal(h.el('FilterEmployee').children[1].value,'employee-a');
});

test('attendance exact retry reuses idempotency key, edited command rotates it',async()=>{
 const h=harness();await h.flush();h.draft();h.submit('Form');h.submit('Form');await h.flush();
 assert.equal(h.writes.length,1);assert.equal(h.el('Root').inert,true);
 h.writes[0].resolve(h.fail('Network uncertainty'));await h.flush();
 h.submit('Form');await h.flush();
 assert.equal(h.writes.length,2);
 assert.equal(h.writes[0].body.clientRequestId,h.writes[1].body.clientRequestId);
 h.writes[1].resolve(h.fail('Network uncertainty'));await h.flush();
 h.el('Remarks').value='New evidence';h.submit('Form');await h.flush();
 assert.notEqual(h.writes[1].body.clientRequestId,h.writes[2].body.clientRequestId);
 h.writes[2].resolve(h.fail('Network uncertainty'));await h.flush();
});

test('attendance acknowledged save survives a failed register refresh',async()=>{
 const h=harness();await h.flush();h.draft();h.submit('Form');await h.flush();
 h.failList=true;h.writes[0].resolve(h.ok({id:10}));await h.flush();
 assert.match(h.el('Alert').className,/alert-success/);
 assert.match(h.el('Alert').textContent,/Attendance recorded.*refresh unavailable/);
 assert.equal(h.el('Root').inert,false);
 h.draft();h.submit('Form');await h.flush();assert.equal(h.writes.length,1);
});

test('attendance correction acknowledges committed POST despite failed readback',async()=>{
 const h=harness();await h.flush();h.action(0,0);
 h.el('CorrectionState').value='2';h.el('CorrectionReason').value='Verified original register';
 h.submit('CorrectionForm');h.submit('CorrectionForm');await h.flush();
 assert.equal(h.writes.length,1);assert.equal(h.writes[0].body.rowVersion,'va');
 h.failList=true;h.writes[0].resolve(h.ok({id:10}));await h.flush();
 assert.equal(h.el('CorrectionCard').hidden,true);
 assert.match(h.el('Alert').textContent,/Correction saved.*refresh unavailable/);
 assert.match(h.el('Alert').className,/alert-success/);
});

test('stale A history and stale register response cannot replace B selection',async()=>{
 const h=harness();await h.flush();h.holdHistory=true;h.action(0,1);await h.flush();
 assert.equal(h.pendingHistory.length,1);
 h.holdHistory=false;h.action(1,1);await h.flush();
 assert.match(h.el('HistoryRows').children[0].children[3].textContent,/\/20\/corrections/);
 h.pendingHistory[0].resolve(h.ok([{previousState:1,newState:2,reason:'Old A'}]));await h.flush();
 assert.match(h.el('HistoryRows').children[0].children[3].textContent,/\/20\/corrections/);
 h.holdList=true;h.el('From').value='2026-11-01';h.submit('Filter');await h.flush();
 h.holdList=false;h.el('From').value='2026-11-02';h.submit('Filter');await h.flush();
 const current=h.el('Page').textContent;
 h.pendingList[0](h.fail('Outdated request failed'));await h.flush();
 assert.equal(h.el('Page').textContent,current);
 assert.doesNotMatch(h.el('Alert').textContent,/Outdated request failed/);
});

test('unsaved audited correction blocks close and switching records',async()=>{
 const h=harness();await h.flush();h.action(0,0);
 h.dirty.CorrectionForm=true;h.allowDiscard.CorrectionForm=false;
 h.el('CorrectionReason').value='Unsubmitted proof';h.click('CorrectionClose');
 assert.equal(h.el('CorrectionCard').hidden,false);
 h.action(1,0);assert.equal(h.el('CorrectionId').value,10);
 assert.equal(h.el('CorrectionReason').value,'Unsubmitted proof');
});

test('role revocation clears sensitive attendance and blocks further writes',async()=>{
 const h=harness();await h.flush();h.deny=true;h.submit('Filter');await h.flush();
 assert.equal(h.el('Root').inert,true);
 assert.equal(h.el('Rows').children.length,0);
 h.draft();h.submit('Form');await h.flush();assert.equal(h.writes.length,0);
});

test('attendance view wires shared guard, employee search and audited correction close',()=>{
 const view=fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.App/Views/EmployeeAttendance/Index.cshtml'),'utf8');
 assert.match(view,/form-guard\.js"[\s\S]*employee-attendance\.js"/);
 assert.match(view,/employeeAttendanceEmployeeSearchForm/);
 assert.match(view,/employeeAttendanceCorrectionClose/);
 assert.match(source,/requestIdFor\(command\)/);
 assert.match(source,/encodeURIComponent\(id\).*correct/);
});
