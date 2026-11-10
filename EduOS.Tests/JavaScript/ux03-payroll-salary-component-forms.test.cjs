'use strict';
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const source=fs.readFileSync(path.resolve(__dirname,
 '../../EduOS.App/wwwroot/js/payroll/salary-components.js'),'utf8');

function harness(){
 class Element{
  constructor(){this.value='';this.checked=false;this.children=[];this.listeners={};
   this.textContent='';this.disabled=false;this.inert=false;}
  addEventListener(type,fn){this.listeners[type]=fn;}
  append(...nodes){this.children.push(...nodes);}
  replaceChildren(...nodes){this.children=nodes;}
  scrollIntoView(){}
  focus(){}
  reportValidity(){return true;}
  querySelector(){return {value:'csrf'};}
 }
 const fields={},calls=[],pendingGet=[],rows=[{id:10,code:'BASIC',name:'Basic salary',
  type:1,isTaxable:true,isActive:true,rowVersion:'version-a'}];
 const el=id=>fields[id]??(fields[id]=new Element());
 el('componentForm').reset=()=>{for(const id of ['componentCode','componentName','componentType'])el(id).value='';
  el('componentTaxable').checked=false;el('componentActive').checked=false;};
 let dirty=false,allowDiscard=true,deny=false,failList=false,holdGet=false;
 const ok=data=>({ok:true,status:200,json:async()=>({success:true,data})});
 const fail=(message,status=503)=>({ok:false,status,json:async()=>({success:false,message})});
 const fetch=async(url,options={})=>{
  calls.push({url,options});
  if(options.method==='POST'||options.method==='PUT')return new Promise(resolve=>{
   calls[calls.length-1].resolve=resolve;});
  if(deny)return fail('Payroll access revoked',403);
  if(failList)return fail('Register unavailable');
  if(holdGet)return new Promise(resolve=>pendingGet.push(resolve));
  return ok(rows);
 };
 const guard=()=>({isDirty:()=>dirty,confirmDiscard:()=>allowDiscard,
  markClean:()=>{dirty=false;}});
 const window={EduOSFormGuard:{create:guard}};
 const document={getElementById:el,createElement:()=>new Element()};
 vm.runInNewContext(source,{window,document,fetch,confirm:()=>true});
 const flush=async()=>{for(let i=0;i<45;i++)await Promise.resolve();};
 const click=id=>el(id).listeners.click();
 const submit=()=>el('componentForm').listeners.submit({preventDefault(){}});
 const edit=()=>el('componentRows').children[0].children[5].children[0].listeners.click();
 const writes=()=>calls.filter(c=>['POST','PUT'].includes(c.options.method));
 return {el,calls,pendingGet,rows,ok,fail,flush,click,submit,edit,writes,
  set dirty(v){dirty=v;},set allowDiscard(v){allowDiscard=v;},
  set deny(v){deny=v;},set failList(v){failList=v;},set holdGet(v){holdGet=v;}};
}

test('component editor protects unsaved changes on Edit, New and statutory preset',async()=>{
 const h=harness();await h.flush();h.edit();
 h.el('componentName').value='Modified';h.dirty=true;h.allowDiscard=false;
 h.click('componentClear');h.click('componentPresetTax');h.click('componentPresetPf');h.edit();
 assert.equal(h.el('componentName').value,'Modified');
 assert.equal(h.el('componentCode').value,'BASIC');
 assert.match(h.el('componentFormTitle').textContent,/BASIC/);
});

test('component update is single-flight with server rowversion and survived failed readback',async()=>{
 const h=harness();await h.flush();h.edit();h.el('componentName').value='Revised';
 h.submit();h.submit();await h.flush();
 assert.equal(h.writes().length,1);assert.equal(h.el('payrollComponents').inert,true);
 const write=h.writes()[0];assert.equal(write.options.method,'PUT');
 assert.equal(JSON.parse(write.options.body).rowVersion,'version-a');
 h.failList=true;write.resolve(h.ok({id:10}));await h.flush();
 assert.match(h.el('componentNotice').textContent,/Salary component saved; register refresh unavailable/);
 assert.match(h.el('componentNotice').className,/alert-success/);
 assert.equal(h.el('payrollComponents').inert,false);
});

test('stale component list reply cannot overwrite the newer refresh',async()=>{
 const h=harness();await h.flush();h.holdGet=true;h.click('componentRefresh');
 assert.equal(h.pendingGet.length,1);h.holdGet=false;h.rows[0].name='New server name';
 h.click('componentRefresh');await h.flush();
 assert.equal(h.el('componentRows').children[0].children[1].textContent,'New server name');
 h.pendingGet[0](h.ok([{id:10,code:'BASIC',name:'Stale name',type:1,
  isTaxable:false,isActive:true,rowVersion:'old'}]));await h.flush();
 assert.equal(h.el('componentRows').children[0].children[1].textContent,'New server name');
});

test('denied component list removes private rows, draft and write access',async()=>{
 const h=harness();await h.flush();h.edit();h.el('componentName').value='Private draft';
 h.deny=true;h.click('componentRefresh');await h.flush();
 assert.equal(h.el('payrollComponents').inert,true);
 assert.equal(h.el('componentCode').value,'');
 h.submit();await h.flush();assert.equal(h.writes().length,0);
});

test('salary-component editor includes shared guard and preserves active state semantics',()=>{
 const view=fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.App/Views/PayrollSalaryComponents/Index.cshtml'),'utf8');
 assert.match(view,/form-guard\.js"[\s\S]*salary-components\.js"/);
 assert.match(source,/rowVersion:editing\?\.rowVersion\|\|null/);
 assert.match(source,/Every eligible employee will need an explicit salary structure deduction/);
 assert.match(source,/editing\?\.isActive&&!payload\.isActive/);
});
