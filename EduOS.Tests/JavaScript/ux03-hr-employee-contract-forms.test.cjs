'use strict';
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const source=fs.readFileSync(path.resolve(__dirname,
 '../../EduOS.App/wwwroot/js/hr/employee-contracts.js'),'utf8');

function harness(){
 class Element{
  constructor(tag='div'){
   this.tagName=tag.toUpperCase();this.textContent='';this.className='';this.value='';
   this.children=[];this.listeners={};this.files=[];this.disabled=false;this.inert=false;
   this.classes=new Set();
   this.classList={add:name=>this.classes.add(name),remove:name=>this.classes.delete(name),
    toggle:(name,force)=>force?this.classes.add(name):this.classes.delete(name)};
  }
  append(...nodes){this.children.push(...nodes);}
  add(node){this.children.push(node);}
  replaceChildren(...nodes){this.children=nodes;}
  addEventListener(type,fn){this.listeners[type]=fn;}
  focus(){}
  remove(){this.removed=true;}
  reset(){this.value='';}
  reportValidity(){return true;}
  querySelector(selector){return selector==='input[name="__RequestVerificationToken"]'?{value:'csrf'}:null;}
 }
 const elements={},gets=[],posts=[],uploads=[],pendingReads=[],dirty={},allowDiscard={};
 const el=id=>elements[id]??(elements[id]=new Element());
 let idCounter=0,holdEmployee='',failHistory=false,deny=false;
 const ok=data=>({ok:true,status:200,json:async()=>({success:true,data})});
 const fail=(message,status=503)=>({ok:false,status,json:async()=>({success:false,message})});
 const contracts=reference=>({totalCount:2,items:[
  {contractCode:'DRAFT-'+reference,reference:'R-'+reference,rowVersion:'v-'+reference,
   effectiveFrom:'2026-11-01',effectiveTo:null,state:1,canReview:true,signedPdf:null},
  {contractCode:'SIGNED-'+reference,reference:'S-'+reference,rowVersion:'w-'+reference,
   effectiveFrom:'2026-08-01',effectiveTo:null,state:2,canReview:false,signedPdf:null}
 ]});
 const fetch=async(url,options={})=>{
  if(options.method==='POST'){
   if(url==='/api/document-admin/files'){
    uploads.push(options.body.entries);
    return ok({reference:'staged-file'});
   }
   return new Promise(resolve=>posts.push({url,body:JSON.parse(options.body),resolve}));
  }
  gets.push(url);
  if(url.startsWith('/api/hr/employees?'))return ok({totalCount:2,items:[
   {employeeCode:'E1',fullName:'Alice',reference:'A'},
   {employeeCode:'E2',fullName:'Bob',reference:'B'}]});
  if(url.startsWith('/api/hr/employee-contracts?')){
   if(deny)return fail('HR permission revoked',403);
   if(failHistory)return fail('Register read unavailable');
   const reference=new URLSearchParams(url.split('?')[1]).get('employeeReference');
   if(holdEmployee===reference)return new Promise(resolve=>pendingReads.push({reference,resolve}));
   return ok(contracts(reference));
  }
  throw Error('Unexpected GET '+url);
 };
 const window={EduOSFormGuard:{create:form=>({
  isDirty:()=>!!dirty[form.id],confirmDiscard:()=>allowDiscard[form.id]!==false,
  markClean:()=>{dirty[form.id]=false;}
 })}};
 for(const id of ['contractForm','contractReview','contractEvidenceUpload','contractEvidenceReview'])el(id).id=id;
 const FormData=class {constructor(){this.entries=[];}append(k,v){this.entries.push([k,v]);}};
 const document={getElementById:el,createElement:tag=>new Element(tag)};
 const crypto={randomUUID:()=>`key-${++idCounter}`};
 const Option=class {constructor(text,value){this.text=text;this.value=value;}};
 vm.runInNewContext(source,{document,window,crypto,fetch,Option,FormData,
  URLSearchParams,confirm:()=>true});
 const flush=async()=>{for(let i=0;i<35;i++)await Promise.resolve();};
 const submit=id=>el(id).listeners.submit({preventDefault(){},currentTarget:el(id)});
 const click=id=>el(id).listeners.click?.();
 const select=async reference=>{
  el('contractSearch').value='Al';submit('contractFind');await flush();
  el('contractEmployee').value=reference;el('contractEmployee').listeners.change();await flush();
 };
 const draft=()=>{
  el('contractCode').value='CN-9';el('contractFrom').value='2026-11-10';
  el('contractTo').value='';el('contractRemarks').value='Reviewed terms';
 };
 const review=()=>el('contractRows').children[0].children[4].children[0].listeners.click();
 const evidence=()=>el('contractRows').children[1].children[4].children[0].listeners.click();
 return {el,gets,posts,uploads,pendingReads,flush,submit,click,select,draft,review,evidence,contracts,
  ok,fail,dirty,allowDiscard,set holdEmployee(x){holdEmployee=x;},
  set failHistory(x){failHistory=x;},set deny(x){deny=x;}};
}

test('contract create blocks overlapping POST and reuses key only for unchanged terms',async()=>{
 const h=harness();await h.select('A');h.draft();h.submit('contractForm');h.submit('contractForm');
 await h.flush();assert.equal(h.posts.length,1);assert.equal(h.el('employeeContracts').inert,true);
 h.posts[0].resolve(h.fail('Network interrupted'));await h.flush();
 h.submit('contractForm');await h.flush();
 assert.equal(h.posts[1].body.clientRequestId,h.posts[0].body.clientRequestId);
 h.posts[1].resolve(h.fail('Network interrupted'));await h.flush();
 h.el('contractRemarks').value='Different terms';h.submit('contractForm');await h.flush();
 assert.notEqual(h.posts[2].body.clientRequestId,h.posts[1].body.clientRequestId);
 h.posts[2].resolve(h.fail('Network interrupted'));await h.flush();
});

test('committed contract draft remains success if list readback fails',async()=>{
 const h=harness();await h.select('A');h.draft();h.submit('contractForm');await h.flush();
 h.failHistory=true;h.posts[0].resolve(h.ok({reference:'new'}));await h.flush();
 assert.match(h.el('contractAlert').className,/alert-success/);
 assert.match(h.el('contractAlert').textContent,/Contract draft saved.*refresh unavailable/);
 assert.equal(h.el('employeeContracts').inert,false);
});

test('late contract list from employee A cannot replace employee B history',async()=>{
 const h=harness();h.holdEmployee='A';await h.select('A');
 assert.equal(h.pendingReads.length,1);
 h.holdEmployee='';h.el('contractEmployee').value='B';
 h.el('contractEmployee').listeners.change();await h.flush();
 assert.equal(h.el('contractRows').children[0].children[0].textContent,'DRAFT-B');
 h.pendingReads[0].resolve(h.ok(h.contracts('A')));await h.flush();
 assert.equal(h.el('contractRows').children[0].children[0].textContent,'DRAFT-B');
});

test('dirty review blocks Close and employee switch without losing typed rejection reason',async()=>{
 const h=harness();await h.select('A');h.review();
 h.el('contractReason').value='Incomplete signature';h.dirty.contractReview=true;
 h.allowDiscard.contractReview=false;
 h.click('contractCancel');assert.equal(h.el('contractReview').classes.has('d-none'),false);
 h.el('contractEmployee').value='B';h.el('contractEmployee').listeners.change();await h.flush();
 assert.equal(h.el('contractEmployee').value,'A');
 assert.equal(h.el('contractReason').value,'Incomplete signature');
});

test('staged signed PDF is reused when attachment fails and is retried',async()=>{
 const h=harness();await h.select('A');h.evidence();
 h.el('contractPdf').files=[{name:'signed.pdf',size:1500,type:'application/pdf'}];
 h.submit('contractEvidenceUpload');await h.flush();
 assert.equal(h.uploads.length,1);assert.equal(h.posts.length,1);
 assert.equal(h.posts[0].body.fileReference,'staged-file');
 h.posts[0].resolve(h.fail('Document conflict'));await h.flush();
 h.submit('contractEvidenceUpload');await h.flush();
 assert.equal(h.uploads.length,1,'do not upload a second staged file');
 assert.equal(h.posts[1].body.fileReference,'staged-file');
 h.posts[1].resolve(h.fail('Document conflict'));await h.flush();
});

test('HR permission revocation clears contract details and locks edits',async()=>{
 const h=harness();await h.select('A');h.deny=true;
 h.el('contractEmployee').listeners.change();await h.flush();
 assert.equal(h.el('employeeContracts').inert,true);
 assert.equal(h.el('contractRows').children.length,0);
 h.draft();h.submit('contractForm');await h.flush();assert.equal(h.posts.length,0);
});

test('employee contract UX still uses original server document and tenant rules',()=>{
 const view=fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.App/Views/EmployeeContracts/Index.cshtml'),'utf8');
 const service=fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.Service/Services/HR/EmployeeContractService.SignedPdf.cs'),'utf8');
 assert.match(view,/form-guard\.js"[\s\S]*employee-contracts\.js"/);
 assert.match(view,/<form id="contractEvidenceReview"/);
 assert.match(source,/requestIdFor\(command\)/);
 assert.match(source,/state\.stagedReference/);
 assert.match(source,/serial!==state\.loadSeq/);
 assert.match(service,/x\.TenantId == user\.TenantId/);
 assert.match(service,/current\.VerificationStatusCode == "Pending"/);
});
