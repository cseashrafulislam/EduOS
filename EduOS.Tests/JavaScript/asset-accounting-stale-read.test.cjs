const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Node DOM contract regression only. This is not an authenticated Chromium/SQL test.
function harness() {
 const source = fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.App/wwwroot/js/inventory/asset-accounting.js'), 'utf8')
  .replace(/^import .*;\r?\n/m, '');
 const elements = new Map(), blocked = new Set();
 class Element {
  constructor(id) {
   this.id=id;this.dataset={};this.children=[];this.listeners={};this.value='';
   this.textContent='';this.disabled=false;
   const classes=new Set(['d-none']);
   this.classList={add:x=>classes.add(x),remove:x=>classes.delete(x),
    contains:x=>classes.has(x)};
  }
  addEventListener(type,fn){this.listeners[type]=fn;}
  append(...nodes){this.children.push(...nodes);}
  replaceChildren(...nodes){this.children=nodes;}
  add(node){this.children.push(node);}
  click(){return this.listeners.click?.();}
 }
 const el=id=>elements.get(id)||(elements.set(id,new Element(id)),elements.get(id));
 el('assetAccountingWorkspace').dataset={maker:'false',checker:'false'};
 const api=async path=>{
  if([...blocked].some(x=>path.includes(x)))throw new Error('Campus access denied');
  if(path.includes('/capitalization-readiness'))
   return {assetTag:'ASSET-1',statusCode:'Active',candidateLineCount:0,candidates:[]};
  if(path==='asset-accounting/options')return {accounts:[],periods:[]};
  if(path.endsWith('/statement'))
   return {assetBookId:42,costBasis:10,postedDepreciation:0,
    postedImpairment:0,carryingValue:10,policyVersion:1,policyMode:'Automatic',
    remainingUsefulLifeMonths:36,events:[],policyHistory:[]};
  if(path.endsWith('/capitalization'))return null;
  throw new Error('Unexpected API path '+path);
 };
 vm.runInNewContext(source,{el,inventoryApi:api,
  td:value=>{const node=new Element('td');node.textContent=String(value);return node;},
  document:{createElement:tag=>new Element(tag),createTextNode:text=>({textContent:text})},
  Option:class {constructor(text,value){this.text=text;this.value=value;}},
  location:{search:''},URLSearchParams,crypto:{randomUUID:()=> 'request-key'},
  Date,prompt:()=>null});
 return {el,blocked,load:async asset=>{el('aaAsset').value=asset;await el('aaLoad').click();}};
}

test('denied asset switch erases old book, receipt evidence and approval surfaces',async()=>{
 const h=harness();
 await h.load('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa');
 assert.equal(h.el('aaResults').classList.contains('d-none'),false);
 assert.match(h.el('aaBook').textContent,/Book #42/);
 h.blocked.add('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb/statement');
 await h.load('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb');
 assert.equal(h.el('aaResults').classList.contains('d-none'),true);
 assert.equal(h.el('aaBook').textContent,'');
 assert.equal(h.el('aaEvents').children.length,0);
 assert.equal(h.el('aaCapReview').children.length,0);
 assert.match(h.el('aaStatus').textContent,/Campus access denied/);
});

test('failed same-asset refresh cannot retain old Finance evidence or actions',async()=>{
 const h=harness(),asset='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
 await h.load(asset);
 h.blocked.add(asset+'/statement');
 await h.load(asset);
 assert.equal(h.el('aaResults').classList.contains('d-none'),true);
 assert.equal(h.el('aaBook').textContent,'');
 assert.equal(h.el('aaCandidate').children.length,0);
 assert.equal(h.el('aaPolicyHistory').children.length,0);
});
