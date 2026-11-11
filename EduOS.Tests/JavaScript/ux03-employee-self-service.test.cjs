'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/employee-portal/index.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/EmployeePortal/Index.cshtml'), 'utf8');

function setup() {
    const nodes = new Map(), calls = [], queues = new Map();
    let serial = 0, tenant = '10';
    const mk = id => ({id, value:'', textContent:'', className:'', disabled:false, inert:false,
        children:[], options:[], handlers:{}, classList:{add() {},remove() {}},
        focus() {}, append(...items) {this.children.push(...items);},
        replaceChildren(...items) {this.children = [...items];this.options = [...items];
            if(this.id === 'employeeLeaveType')this.value=items[0]?.value||'';},
        add(item) {this.options.push(item);},
        reportValidity:()=>true,reset() {},
        addEventListener(name, fn) {this.handlers[name]=fn;}});
    const el = id => {if(!nodes.has(id))nodes.set(id,mk(id));return nodes.get(id);};
    const document = {body:{dataset:{authTenantId:'10',authSessionReference:'s1',authUserId:'44'}},
        getElementById:el, createElement:mk, querySelector:()=>({content:'csrf-meta'})};
    el('employeePortalRoot').querySelector = () => ({value:'csrf-token'});
    const guard = {dirty:false,cleans:0,isDirty() {return this.dirty;},markClean() {this.dirty=false;this.cleans++;}};
    class Option {constructor(text,value){this.textContent=text;this.value=String(value);}}
    const response = (data,ok=true,status=200) => ({ok,status,json:async()=>({success:ok,data,message:ok?'':'Denied'})});
    const defaultData = url => {
        if(url.includes('/profile'))return {employeeCode:'EMP1',fullName:'Employee',canTeach:true};
        if(url.includes('/attendance?'))return [{attendanceDate:'2026-11-10',state:1,inTime:'09:00:00',outTime:'17:00:00',remarks:'Office'}];
        if(url.includes('/balances?'))return [{leaveTypeId:7,leaveTypeName:'Annual',annualEntitlement:20,
            usedDays:2,pendingDays:0,remainingDays:18}];
        if(url.includes('/leave/page?'))return {items:[{leaveTypeName:'Annual',fromDate:'2026-11-01',toDate:'2026-11-02',
            totalDays:2,state:3,reason:'Approved'}],totalCount:1};
        return {};
    };
    let override = null;
    const fetch = (url,options) => {
        const call = {url,options};calls.push(call);
        return override ? override(url,options,call,response,defaultData) : Promise.resolve(response(defaultData(url)));
    };
    vm.runInNewContext(source,{document,window:{EduOSFormGuard:{create:()=>guard}},Option,
        fetch,URLSearchParams,crypto:{randomUUID:()=>`request-${++serial}`},Date,console},
        {filename:'employee-portal/index.js'});
    const submit = () => el('employeeLeaveForm').handlers.submit({preventDefault(){}});
    const flush = () => new Promise(resolve=>setImmediate(resolve));
    return {el,calls,guard,submit,flush,setFetch:fn=>override=fn,response,defaultData,
        switchTenant:value=>{tenant=value;document.body.dataset.authTenantId=tenant;},queues};
}

test('employee portal sends real CSRF and stable request key and blocks parallel submissions',async()=>{
    const ui=setup();await ui.flush();
    ui.el('employeeLeaveType').value='7';
    ui.el('employeeLeaveReason').value='Medical appointment';
    let release;
    ui.setFetch((url,options,call,response,data)=>url==='/api/employee-portal/leave'
        ? new Promise(resolve=>{release=()=>resolve(response({id:10}));})
        : Promise.resolve(response(data(url))));
    const first=ui.submit();await ui.submit();
    const posts=ui.calls.filter(x=>x.url==='/api/employee-portal/leave');
    assert.equal(posts.length,1);
    assert.equal(posts[0].options.headers.RequestVerificationToken,'csrf-token');
    const body=JSON.parse(posts[0].options.body);
    assert.equal(body.clientRequestId,'request-1');
    assert.equal(body.leaveTypeId,7);
    assert.equal(ui.el('employeeLeaveForm').inert,true);
    release();await first;
    assert.match(ui.el('employeePortalAlert').textContent,/submitted/i);
    assert.equal(ui.el('employeeLeaveForm').inert,false);
    assert.ok(ui.guard.cleans>0);
});

test('uncertain leave submit reuses its key, but editing the command rotates it',async()=>{
    const ui=setup();await ui.flush();
    ui.el('employeeLeaveType').value='7';ui.el('employeeLeaveReason').value='Family appointment';
    let failing=true;
    ui.setFetch((url,options,call,response,data)=>url==='/api/employee-portal/leave'
        ? failing ? Promise.reject(Error('Network interrupted')) : Promise.resolve(response({id:11}))
        : Promise.resolve(response(data(url))));
    await ui.submit();await ui.submit();
    const posts=ui.calls.filter(x=>x.url==='/api/employee-portal/leave');
    assert.equal(posts.length,2);
    assert.equal(JSON.parse(posts[0].options.body).clientRequestId,
        JSON.parse(posts[1].options.body).clientRequestId);
    ui.el('employeeLeaveReason').value='Family appointment updated';
    failing=false;await ui.submit();
    const later=ui.calls.filter(x=>x.url==='/api/employee-portal/leave').at(-1);
    assert.notEqual(JSON.parse(later.options.body).clientRequestId,
        JSON.parse(posts[0].options.body).clientRequestId);
});

test('confirmed leave remains successful when the follow-up history refresh fails',async()=>{
    const ui=setup();await ui.flush();
    ui.el('employeeLeaveType').value='7';ui.el('employeeLeaveReason').value='Planned family visit';
    ui.setFetch((url,options,call,response,data)=>url==='/api/employee-portal/leave'
        ? Promise.resolve(response({id:12}))
        : url.includes('/leave/page?') ? Promise.reject(Error('History offline'))
        : Promise.resolve(response(data(url))));
    await ui.submit();
    assert.match(ui.el('employeePortalAlert').textContent,/submitted.*could not refresh/i);
    assert.equal(ui.el('employeePortalAlert').className,'alert alert-success');
});

test('attendance response fields and stale filters do not show another date range',async()=>{
    const ui=setup();await ui.flush();
    const initial=ui.el('employeeAttendanceRows').children[0].children;
    assert.equal(initial[0].textContent,'2026-11-10');
    assert.equal(initial[1].textContent,'Present');
    let releaseOld;
    ui.setFetch((url,options,call,response,data)=>url.includes('/attendance?')
        ? url.includes('2026-01-01') ? new Promise(resolve=>{releaseOld=()=>resolve(response([{attendanceDate:'2026-01-01',state:2}]));})
            : Promise.resolve(response([{attendanceDate:'2026-02-01',state:3}]))
        : Promise.resolve(response(data(url))));
    ui.el('employeeFromDate').value='2026-01-01';ui.el('employeeToDate').value='2026-03-01';
    ui.el('employeeAttendanceFilter').handlers.submit({preventDefault(){}});
    await ui.flush();
    ui.el('employeeFromDate').value='2026-02-01';
    ui.el('employeeAttendanceFilter').handlers.submit({preventDefault(){}});
    await ui.flush();releaseOld();await ui.flush();
    assert.equal(ui.el('employeeAttendanceRows').children[0].children[0].textContent,'2026-02-01');
});

test('session rotation fails closed without displaying stale private records',async()=>{
    const ui=setup();await ui.flush();
    ui.el('employeeLeaveType').value='7';ui.el('employeeLeaveReason').value='Doctor visit';
    let release;
    ui.setFetch((url,options,call,response,data)=>url==='/api/employee-portal/leave'
        ? new Promise(resolve=>{release=()=>resolve(response({id:13}));})
        : Promise.resolve(response(data(url))));
    const task=ui.submit();ui.switchTenant('20');release();await task;
    assert.equal(ui.el('employeePortalRoot').inert,true);
    assert.equal(ui.el('employeeProfile').children.length,0);
    assert.equal(ui.el('employeeLeaveHistory').children[0].children[0].textContent,'No records found.');
});

test('portal view wires anti-forgery, draft guard and SQL-paged history',()=>{
    assert.match(view,/@Html\.AntiForgeryToken\(\)/);
    assert.match(view,/js\/platform\/form-guard\.js/);
    assert.match(source,/\/leave\/page\?/);
    assert.match(source,/pageSize:'20'/);
    assert.match(source,/leaveTypeName/);
    assert.match(source,/authSessionReference/);
});
