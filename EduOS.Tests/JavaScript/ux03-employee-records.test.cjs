'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const project = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(project, 'EduOS.App/wwwroot/js/hr/employee-records.js'), 'utf8');
const view = fs.readFileSync(path.join(project, 'EduOS.App/Views/HrEmployeeRecords/Index.cshtml'), 'utf8');

function setup() {
    const nodes = new Map(), calls = [], guards = [];
    let serial = 0, override = null;
    const mk = id => ({id, value:'', textContent:'', className:'', readOnly:false, disabled:false,
        checked:false, hidden:false, inert:false, options:[], children:[], handlers:{},
        dataset:{canCreate:'true'},classList:{add() {},remove() {}},
        addEventListener(name,callback){this.handlers[name]=callback;},
        focus(){},scrollIntoView(){},reportValidity:()=>true,reset(){},
        append(...elements){this.children.push(...elements);},
        replaceChildren(...elements){this.children=[...elements];this.options=[...elements];
            if(['employeeDesignation','employeeDepartment','employeeState'].includes(this.id))this.value=elements[0]?.value||'';},
        add(option){this.options.push(option);}});
    const el = id => {if(!nodes.has(id))nodes.set(id,mk(id));return nodes.get(id);};
    const document={body:{dataset:{authTenantId:'10',authSessionReference:'session',authUserId:'44'}},
        getElementById:el,createElement:mk,querySelector:()=>({content:'csrf-meta'})};
    el('employeeRecords').querySelector=()=>({value:'csrf-token'});
    class Option {constructor(text,value){this.textContent=text;this.value=String(value);}}
    const guardFactory=()=>{
        const guard={dirty:false,discard:false,cleans:0,isDirty(){return this.dirty;},
            confirmDiscard(){return this.discard;},markClean(){this.dirty=false;this.cleans++;}};
        guards.push(guard);return guard;
    };
    const record=(ref,code,state=1)=>({reference:ref,employeeCode:code,fullName:'Worker '+code,
        designationId:1,organizationUnitId:4,joiningDate:'2025-01-10',state,rowVersion:'v1',canTeach:true});
    const initialList={items:[record('ref-one','EMP-1'),record('ref-two','EMP-2')],totalCount:2,page:1};
    const response=(data,ok=true,status=200)=>({ok,status,json:async()=>({success:ok,data,message:ok?'':'Unauthorized'})});
    const defaults=url=>{
        if(url.includes('setup/designations'))return [{id:1,name:'Teacher',isActive:true}];
        if(url.includes('setup/departments'))return {items:[{id:4,name:'Science',isActive:true}],totalCount:1};
        if(url.includes('/assignment-history'))return {items:[],totalCount:0};
        if(url.includes('/api/hr/employees?'))return initialList;
        if(url.includes('/api/hr/employees/ref-one'))return record('ref-one','EMP-1');
        if(url.includes('/api/hr/employees/ref-two'))return record('ref-two','EMP-2');
        return record('new-ref','EMP-3');
    };
    const fetch=(url,options)=>{calls.push({url,options});return override
        ? override(url,options,response,defaults) : Promise.resolve(response(defaults(url)));};
    vm.runInNewContext(source,{document,window:{EduOSFormGuard:{create:guardFactory},confirm:()=>true},
        Option,fetch,URLSearchParams,location:{search:''},Date,crypto:{randomUUID:()=>`req-${++serial}`},console},
        {filename:'hr/employee-records.js'});
    const flush=()=>new Promise(resolve=>setImmediate(resolve));
    const submit=id=>el(id).handlers.submit({preventDefault(){}});
    return {el,calls,guards,flush,record,response,defaults,submit,setFetch:fn=>override=fn,
        switchTenant:id=>document.body.dataset.authTenantId=id};
}

test('employee editor and status drafts cannot be discarded silently',async()=>{
    const ui=setup();await ui.flush();
    ui.el('employeeNew').handlers.click();
    assert.equal(ui.el('employeeEditorPanel').hidden,false);
    ui.guards[0].dirty=true;ui.guards[0].discard=false;
    ui.el('employeeCancel').handlers.click();
    assert.equal(ui.el('employeeEditorPanel').hidden,false);
    ui.guards[0].discard=true;ui.el('employeeClose').handlers.click();
    assert.equal(ui.el('employeeEditorPanel').hidden,true);
    assert.equal(ui.guards[0].dirty,false);
});

test('employee registration is single-flight, sends CSRF and retains retry key after uncertain failure',async()=>{
    const ui=setup();await ui.flush();ui.el('employeeNew').handlers.click();
    for(const [id,value] of Object.entries({employeeCode:'EMP-3',employeeName:'Worker Three',
        employeeDesignation:'1',employeeDepartment:'4',employeeJoin:'2026-11-10'}))ui.el(id).value=value;
    let release, writes=0;
    ui.setFetch((url,options,response,defaults)=>url==='/api/hr/employees'&&options.method==='POST'
        ? ++writes===1 ? new Promise((resolve,reject)=>{release=()=>reject(Error('Network disconnected'));})
            : Promise.reject(Error('Network disconnected'))
        : Promise.resolve(response(defaults(url))));
    const first=ui.submit('employeeForm');await ui.submit('employeeForm');
    assert.equal(ui.calls.filter(x=>x.options.method==='POST').length,1);
    assert.equal(ui.el('employeeRecords').inert,true);
    release();await first;
    const old=ui.calls.filter(x=>x.options.method==='POST')[0];
    assert.equal(old.options.headers.RequestVerificationToken,'csrf-token');
    await ui.submit('employeeForm');
    const retry=ui.calls.filter(x=>x.options.method==='POST')[1];
    assert.equal(JSON.parse(old.options.body).clientRequestId,JSON.parse(retry.options.body).clientRequestId);
    ui.el('employeeName').value='Worker Three Revised';
    await ui.submit('employeeForm');
    const changed=ui.calls.filter(x=>x.options.method==='POST')[2];
    assert.notEqual(JSON.parse(old.options.body).clientRequestId,JSON.parse(changed.options.body).clientRequestId);
});

test('a committed employee create remains saved when subsequent directory lookup fails',async()=>{
    const ui=setup();await ui.flush();ui.el('employeeNew').handlers.click();
    ui.el('employeeCode').value='EMP-3';ui.el('employeeName').value='Worker Three';
    ui.el('employeeDesignation').value='1';
    ui.setFetch((url,options,response,defaults)=>url==='/api/hr/employees'&&options.method==='POST'
        ? Promise.resolve(response(ui.record('new-ref','EMP-3')))
        : url.includes('/api/hr/employees?') ? Promise.reject(Error('List timeout'))
        : Promise.resolve(response(defaults(url))));
    await ui.submit('employeeForm');
    assert.equal(ui.el('employeeCode').value,'EMP-3');
    assert.match(ui.el('employeeMessage').textContent,/saved.*could not refresh/i);
    assert.equal(ui.el('employeeRecords').inert,false);
});

test('later employee selection wins over older profile response',async()=>{
    const ui=setup();await ui.flush();
    const buttons=ui.el('employeeRows').children.map(row=>row.children.at(-1).children[0]);
    const deferred=new Map();
    ui.setFetch((url,options,response,defaults)=>url.includes('/api/hr/employees/ref-')&&
        !url.includes('assignment-history') ? new Promise(resolve=>deferred.set(url.split('/').at(-1),resolve))
        : Promise.resolve(response(defaults(url))));
    const first=buttons[0].handlers.click(),second=buttons[1].handlers.click();
    deferred.get('ref-two')(ui.response(ui.record('ref-two','EMP-2')));
    await second;
    deferred.get('ref-one')(ui.response(ui.record('ref-one','EMP-1')));
    await first;
    assert.equal(ui.el('employeeCode').value,'EMP-2');
    assert.equal(ui.el('employeeForm').inert,false);
});

test('switched institution never applies a late profile response',async()=>{
    const ui=setup();await ui.flush();
    const button=ui.el('employeeRows').children[0].children.at(-1).children[0];
    let release;
    ui.setFetch((url,options,response,defaults)=>url.endsWith('/ref-one')
        ? new Promise(resolve=>{release=()=>resolve(response(ui.record('ref-one','EMP-1')));})
        : Promise.resolve(response(defaults(url))));
    const task=button.handlers.click();ui.switchTenant('20');release();await task;
    assert.equal(ui.el('employeeRecords').inert,true);
    assert.equal(ui.el('employeeEditorPanel').hidden,true);
    assert.equal(ui.el('employeeRows').children.length,0);
});

test('employee record form includes shared guard and SQL-bounded page reads',()=>{
    assert.match(view,/js\/platform\/form-guard\.js/);
    assert.match(source,/authSessionReference/);
    assert.match(source,/pageSize:'15'/);
    assert.match(source,/pageSize:'10'/);
    assert.match(source,/requestSignature/);
    assert.match(source,/employeeStateForm/);
});
