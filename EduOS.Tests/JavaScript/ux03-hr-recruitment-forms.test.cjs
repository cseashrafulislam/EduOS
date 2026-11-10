'use strict';
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const source=fs.readFileSync(path.resolve(__dirname,
 '../../EduOS.App/wwwroot/js/hr/recruitment.js'),'utf8');

function harness(){
 class Element{
  constructor(tag='div'){this.tagName=tag.toUpperCase();this.value='';this.textContent='';
   this.children=[];this.listeners={};this.checked=false;this.hidden=false;
   this.disabled=false;this.inert=false;}
  addEventListener(type,fn){this.listeners[type]=fn;}
  append(...elements){this.children.push(...elements);}
  add(option){this.children.push(option);}
  replaceChildren(...elements){this.children=elements;}
  reset(){this.value='';}
  scrollIntoView(){}
  focus(){}
  reportValidity(){return true;}
  querySelector(selector){return selector==='input[name="__RequestVerificationToken"]'?{value:'csrf'}:null;}
 }
 const fields={},gets=[],writes=[],pendingHistory=[],pendingList=[];
 const el=id=>fields[id]??(fields[id]=new Element());
 let heldHistory='',holdList=false,failList=false,deny=false,uid=0;
 const dirty={},allowDiscard={};
 el('recruitmentCreate').reset=()=>{for(const id of ['recruitCampus','recruitDesignation',
  'recruitAppliedOn','recruitName','recruitEmail','recruitPhone','recruitNotes'])el(id).value='';
  el('recruitConsent').checked=false;};
 el('recruitTransition').reset=()=>{for(const id of ['recruitNextStage','recruitInterviewOn',
  'recruitReason','recruitEmployeeSearch','recruitEmployee'])el(id).value='';
  el('recruitIdentityVerified').checked=false;};
 const ok=data=>({ok:true,status:200,json:async()=>({success:true,data})});
 const fail=(message,status=503)=>({ok:false,status,json:async()=>({success:false,message})});
 const applicants=()=>({totalCount:2,items:[
  {reference:'A',fullName:'Alice Applicant',campusId:1,designationId:2,
   appliedOn:'2026-10-10',stage:1,rowVersion:'va'},
  {reference:'B',fullName:'Bob Applicant',campusId:1,designationId:2,
   appliedOn:'2026-10-10',stage:2,rowVersion:'vb'}]});
 const fetch=async(url,options={})=>{
  if(options.method==='POST')return new Promise(resolve=>writes.push({url,body:JSON.parse(options.body),resolve}));
  gets.push(url);
  if(url==='/api/institution-onboarding/campus-list')return ok([{id:1,name:'Main',isActive:true}]);
  if(url==='/api/hr/setup/designations')return ok([{id:2,name:'Teacher',isActive:true}]);
  if(url.startsWith('/api/hr/recruitment?')){
   if(deny)return fail('HR access revoked',403);
   if(failList)return fail('Register unavailable');
   if(holdList)return new Promise(resolve=>pendingList.push(resolve));
   return ok(applicants());
  }
  if(url.includes('/history?')){
   if(deny)return fail('HR access revoked',403);
   const reference=url.match(/recruitment\/(A|B)/)?.[1];
   if(heldHistory===reference)return new Promise(resolve=>pendingHistory.push({reference,resolve}));
   return ok({items:[{previousStage:1,newStage:2,transitionAt:'2026-11-10T12:00:00',reason:reference}]});
  }
  if(url.startsWith('/api/hr/employees?'))return ok({totalCount:1,
   items:[{employeeCode:'E1',fullName:'Alice',reference:'E-A'}]});
  throw Error('Unexpected GET: '+url);
 };
 const guard=form=>({isDirty:()=>!!dirty[form.id],confirmDiscard:()=>allowDiscard[form.id]!==false,
  markClean:()=>{dirty[form.id]=false;}});
 el('recruitmentCreate').id='recruitmentCreate';el('recruitTransition').id='recruitTransition';
 const window={EduOSFormGuard:{create:guard}};
 const Option=class {constructor(text,value){this.text=text;this.value=value;}};
 const document={getElementById:el,createElement:tag=>new Element(tag)};
 const crypto={randomUUID:()=>`key-${++uid}`};
 vm.runInNewContext(source,{window,document,crypto,fetch,Option,Date,URLSearchParams,confirm:()=>true});
 const flush=async()=>{for(let i=0;i<45;i++)await Promise.resolve();};
 const click=id=>el(id).listeners.click?.();
 const submit=id=>el(id).listeners.submit({preventDefault(){},currentTarget:el(id)});
 const open=ref=>{const row=ref==='A'?el('recruitRows').children[0]:el('recruitRows').children[1];
  row.children[5].children[0].listeners.click();};
 const draft=()=>{el('recruitCampus').value='1';el('recruitDesignation').value='2';
  el('recruitAppliedOn').value='2026-11-10';el('recruitName').value='Applicant';
  el('recruitConsent').checked=true;el('recruitNotes').value='Consent documented';};
 return {el,gets,writes,pendingHistory,pendingList,dirty,allowDiscard,ok,fail,flush,click,submit,open,draft,
  set heldHistory(v){heldHistory=v;},set holdList(v){holdList=v;},
  set failList(v){failList=v;},set deny(v){deny=v;}};
}

test('recruitment registration exact retry key rotates when applicant terms change',async()=>{
 const h=harness();await h.flush();h.draft();
 h.submit('recruitmentCreate');h.submit('recruitmentCreate');await h.flush();
 assert.equal(h.writes.length,1);assert.equal(h.el('recruitmentApp').inert,true);
 h.writes[0].resolve(h.fail('Gateway failure'));await h.flush();
 h.submit('recruitmentCreate');await h.flush();assert.equal(h.writes.length,2);
 assert.equal(h.writes[0].body.clientRequestId,h.writes[1].body.clientRequestId);
 h.writes[1].resolve(h.fail('Gateway failure'));await h.flush();
 h.el('recruitNotes').value='Different terms';h.submit('recruitmentCreate');await h.flush();
 assert.notEqual(h.writes[2].body.clientRequestId,h.writes[1].body.clientRequestId);
 h.writes[2].resolve(h.fail('Gateway failure'));await h.flush();
});

test('acknowledged applicant creation is not mislabeled failure when list refresh fails',async()=>{
 const h=harness();await h.flush();h.draft();h.submit('recruitmentCreate');await h.flush();
 h.failList=true;h.writes[0].resolve(h.ok({reference:'C'}));await h.flush();
 assert.match(h.el('recruitmentNotice').className,/alert-success/);
 assert.match(h.el('recruitmentNotice').textContent,/Application registered with consent.*refresh unavailable/);
 assert.equal(h.el('recruitmentApp').inert,false);
});

test('approved recruitment stage remains committed when read-only list fails',async()=>{
 const h=harness();await h.flush();h.open('A');h.el('recruitNextStage').value='2';
 h.submit('recruitTransition');h.submit('recruitTransition');await h.flush();
 assert.equal(h.writes.length,1);
 assert.equal(h.writes[0].body.rowVersion,'va');
 h.failList=true;h.writes[0].resolve(h.ok({reference:'A'}));await h.flush();
 assert.match(h.el('recruitmentNotice').className,/alert-success/);
 assert.match(h.el('recruitmentNotice').textContent,/Recruitment stage recorded.*refresh unavailable/);
 assert.equal(h.el('recruitActionPanel').hidden,true);
});

test('late applicant A transition history cannot overwrite B panel',async()=>{
 const h=harness();await h.flush();h.heldHistory='A';h.open('A');await h.flush();
 assert.equal(h.pendingHistory.length,1);h.heldHistory='';h.open('B');await h.flush();
 assert.equal(h.el('recruitHistoryRows').children[0].children[3].textContent,'B');
 h.pendingHistory[0].resolve(h.ok({items:[{previousStage:1,newStage:2,
  transitionAt:'2026-11-10T12:00:00',reason:'A'}]}));await h.flush();
 assert.equal(h.el('recruitHistoryRows').children[0].children[3].textContent,'B');
});

test('unsaved stage decision blocks panel Close and switch to another applicant',async()=>{
 const h=harness();await h.flush();h.open('A');
 h.el('recruitReason').value='Interview evidence';
 h.dirty.recruitTransition=true;h.allowDiscard.recruitTransition=false;
 h.click('recruitTransitionClose');assert.equal(h.el('recruitActionPanel').hidden,false);
 h.open('B');assert.match(h.el('recruitActionTitle').textContent,/Alice Applicant/);
 assert.equal(h.el('recruitReason').value,'Interview evidence');
});

test('role revocation removes applicant list and blocks applicant POST',async()=>{
 const h=harness();await h.flush();h.deny=true;h.click('recruitSearchBtn');await h.flush();
 assert.equal(h.el('recruitmentApp').inert,true);
 assert.equal(h.el('recruitRows').children.length,0);
 h.draft();h.submit('recruitmentCreate');await h.flush();assert.equal(h.writes.length,0);
});

test('recruitment workflow retains existing typed transitions and consent validation',()=>{
 const view=fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.App/Views/Recruitment/Index.cshtml'),'utf8');
 assert.match(view,/form-guard\.js"[\s\S]*recruitment\.js"/);
 assert.match(source,/const allowed=\{1:\[2,5\],2:\[3,5\],3:\[4,5\],4:\[6,5\]\}/);
 assert.match(source,/consentConfirmed:el\('recruitConsent'\)\.checked/);
 assert.match(source,/rowVersion:selected\.rowVersion/);
 assert.match(source,/requestKeyFor\(command\)/);
});
