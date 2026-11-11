'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const project = path.resolve(__dirname,'../..');
const read = filename => fs.readFileSync(path.join(project,filename),'utf8');
const campusJs = read('EduOS.App/wwwroot/js/hr/campus-assignments.js');
const leaveJs = read('EduOS.App/wwwroot/js/hr/index.js');
const campusView = read('EduOS.App/Views/HR/CampusAssignments.cshtml');
const leaveView = read('EduOS.App/Views/HR/Index.cshtml');

function dom() {
    const nodes = new Map(),calls = [],guards = [],beforeUnload = [];
    let override = null,allowed = true,serial = 0;
    function node(tag='DIV',id='') {
        return {tagName:tag.toUpperCase(),id,value:'',textContent:'',className:'',type:'',
            checked:false,disabled:false,hidden:false,inert:false,dataset:{},handlers:{},
            children:[],options:[],colSpan:1,maxLength:0,focus(){},scrollIntoView(){},
            addEventListener(name,callback){(this.handlers[name] ||= []).push(callback);},
            fire(name,event={}){const handlers = this.handlers[name] || [];
                return Promise.all(handlers.map(callback => callback({preventDefault(){},currentTarget:this,...event})));},
            append(...children){this.children.push(...children);},
            replaceChildren(...children){this.children = [...children];this.options = [...children];
                if (this.tagName === 'SELECT') this.value = children[0]?.value || '';},
            add(option){this.children.push(option);this.options.push(option);},
            reportValidity(){return true;},reset(){},
            querySelector(selector) {
                if (selector.includes('__RequestVerificationToken')) return {value:'csrf-token'};
                if (selector === '[data-remarks]') return this.children.flatMap(child=>child.children || [])
                    .find(child=>child.dataset?.remarks) || null;
                return null;
            },
            querySelectorAll(selector) {
                const result = [];
                const walk = item => {for (const child of item.children || []) {
                    if (selector === 'button' && child.tagName === 'BUTTON') result.push(child);
                    walk(child);
                }};
                walk(this);return result;
            },closest:()=>el('hrWorkspaceRoot')};
    }
    const tagFor = id => /Form$|Search$/.test(id) ? 'FORM' :
        /Rows$/.test(id) ? 'TBODY' : /Employee$|CampusId$/.test(id) ? 'SELECT' : 'DIV';
    const el = id => {if (!nodes.has(id))nodes.set(id,node(tagFor(id),id));return nodes.get(id);};
    const document={body:{dataset:{authTenantId:'10',authSessionReference:'s1',authUserId:'6'}},
        getElementById:el,createElement:tag=>node(tag),querySelector:()=>({content:'meta-token'})};
    el('hrWorkspaceRoot').dataset.activeFeature='leaves';
    const guard={dirty:false,discard:true,cleans:0,isDirty(){return this.dirty;},
        confirmDiscard(){return this.discard;},markClean(){this.dirty=false;this.cleans++;}};
    class Option {constructor(label,value){this.textContent=label;this.value=String(value);}}
    const response = (data,ok=true,status=200)=>({ok,status,json:async()=>({success:ok,data,message:ok?'':'Forbidden'})});
    const campuses=[{id:1,name:'North',code:'N',isActive:true},{id:2,name:'South',code:'S',isActive:true}];
    const employees={items:[{reference:'ref-one',employeeCode:'A',fullName:'Alpha'},
        {reference:'ref-two',employeeCode:'B',fullName:'Beta'}],totalCount:2,page:1};
    const history={items:[{campusName:'North',isPrimary:true,effectiveFrom:'2026-01-01',
        effectiveTo:null,isCurrent:true}],totalCount:1,page:1};
    const leaves={items:[{id:11,employeeName:'Alpha',employeeCode:'A',leaveTypeName:'Annual',
        fromDate:'2026-11-15',toDate:'2026-11-17',totalDays:3,state:2,rowVersion:'rv1',reason:'Family'}],
        totalCount:1,page:1};
    const defaults = url => {
        if (url.includes('/campus-list'))return campuses;
        if (url.includes('/campus-assignments'))return history;
        if (url.includes('/api/hr/employees?'))return employees;
        if (url.includes('/api/hr/leaves?'))return leaves;
        return true;
    };
    const fetch = (url,options)=>{calls.push({url,options});return override
        ? override(url,options,response,defaults) : Promise.resolve(response(defaults(url)));};
    const window={confirm:()=>allowed,addEventListener:(name,cb)=>{if(name==='beforeunload')beforeUnload.push(cb);},
        EduOSFormGuard:{create:()=>{guards.push(guard);return guard;}}};
    const globals={document,window,Option,fetch,URLSearchParams,console,Date,
        crypto:{randomUUID:()=> 'req-' + ++serial}};
    const flush = async()=>{await new Promise(resolve=>setImmediate(resolve));
        await new Promise(resolve=>setImmediate(resolve));};
    return {el,document,window,guards,calls,flush,response,defaults,setFetch:fn=>override=fn,
        setConfirm:value=>allowed=value,switchTenant:value=>document.body.dataset.authTenantId=value,
        submit:id=>el(id).fire('submit'),run:src=>vm.runInNewContext(src,globals),beforeUnload};
}
async function campusReady() {
    const ui=dom();ui.run(campusJs);await ui.flush();ui.el('hrCampusSearch').value='Al';
    await ui.el('hrCampusFind').fire('click');ui.el('hrCampusEmployee').value='ref-one';
    await ui.el('hrCampusEmployee').fire('change');await ui.flush();return ui;
}
async function leaveReady() {const ui=dom();ui.run(leaveJs);await ui.flush();return ui;}
function leaveRow(ui) {return ui.el('hrLeaveRows').children[0];}
function actionInput(ui) {return leaveRow(ui).children[5].children[0];}
function actionButton(ui,index) {return leaveRow(ui).children[5].children[index+1];}

test('campus assignment rejects employee switches that would discard an unsaved draft',async()=>{
    const ui=await campusReady();
    ui.guards[0].dirty=true;ui.guards[0].discard=false;
    ui.el('hrCampusEmployee').value='ref-two';await ui.el('hrCampusEmployee').fire('change');
    assert.equal(ui.el('hrCampusEmployee').value,'ref-one');
    ui.guards[0].discard=true;ui.el('hrCampusEmployee').value='ref-two';
    await ui.el('hrCampusEmployee').fire('change');await ui.flush();
    assert.equal(ui.el('hrCampusEmployee').value,'ref-two');
    assert.equal(ui.guards[0].dirty,false);
});
test('campus assignment is single-flight and retains request key for identical uncertain retry',async()=>{
    const ui=await campusReady();ui.el('hrCampusId').value='1';ui.el('hrCampusFrom').value='2026-11-15';
    let release,writes=0;
    ui.setFetch((url,options,response,defaults)=>url==='/api/hr/employees/campuses'
        ? ++writes===1 ? new Promise((resolve,reject)=>{release=()=>reject(Error('Network lost'));})
            : Promise.reject(Error('Network lost'))
        : Promise.resolve(response(defaults(url))));
    const first=ui.submit('hrCampusForm');await ui.submit('hrCampusForm');
    assert.equal(ui.calls.filter(x=>x.options.method==='POST').length,1);
    assert.equal(ui.el('hrCampusWorkspace').inert,true);
    release();await first;
    const initial=ui.calls.filter(x=>x.options.method==='POST')[0];
    assert.equal(initial.options.headers.RequestVerificationToken,'csrf-token');
    await ui.submit('hrCampusForm');
    const retry=ui.calls.filter(x=>x.options.method==='POST')[1];
    assert.equal(JSON.parse(initial.options.body).clientRequestId,JSON.parse(retry.options.body).clientRequestId);
    ui.el('hrCampusId').value='2';await ui.submit('hrCampusForm');
    const changed=ui.calls.filter(x=>x.options.method==='POST')[2];
    assert.notEqual(JSON.parse(retry.options.body).clientRequestId,JSON.parse(changed.options.body).clientRequestId);
    assert.match(ui.el('hrCampusAlert').textContent,/check campus history before retry/i);
});
test('campus commit remains confirmed if history refresh subsequently fails',async()=>{
    const ui=await campusReady();ui.el('hrCampusId').value='1';
    ui.setFetch((url,options,response,defaults)=>url.includes('/campus-assignments')
        ? Promise.reject(Error('Read timed out')) : Promise.resolve(response(defaults(url))));
    await ui.submit('hrCampusForm');
    assert.match(ui.el('hrCampusAlert').textContent,/recorded; history could not refresh/i);
    assert.equal(ui.el('hrCampusWorkspace').inert,false);
    assert.equal(ui.calls.filter(x=>x.options.method==='POST').length,1);
});
test('campus history never overwrites newer employee selection',async()=>{
    const ui=await campusReady();const pending = [];
    ui.setFetch((url,options,response,defaults)=>url.includes('/campus-assignments')
        ? new Promise(resolve=>pending.push({url,resolve}))
        : Promise.resolve(response(defaults(url))));
    ui.el('hrCampusEmployee').value='ref-two';const second=ui.el('hrCampusEmployee').fire('change');
    await ui.flush();ui.el('hrCampusEmployee').value='ref-one';
    const first=ui.el('hrCampusEmployee').fire('change');await ui.flush();
    assert.equal(pending.length,2);
    pending[1].resolve(ui.response({items:[{campusName:'North',isPrimary:true,effectiveFrom:'2026-11-01',isCurrent:true}],totalCount:1}));
    await first;await ui.flush();
    pending[0].resolve(ui.response({items:[{campusName:'Wrong',isPrimary:false,effectiveFrom:'2026-11-01',isCurrent:true}],totalCount:1}));
    await second;await ui.flush();
    assert.equal(ui.el('hrCampusRows').children[0].children[0].textContent,'North');
});
test('campus tenant change clears private history instead of drawing a delayed response',async()=>{
    const ui=await campusReady();let release;
    ui.setFetch((url,options,response,defaults)=>url.includes('/campus-assignments')
        ? new Promise(resolve=>{release=()=>resolve(response(defaults(url)));})
        : Promise.resolve(response(defaults(url))));
    const task=ui.el('hrCampusNext').fire('click'); // disabled button cannot advance; trigger explicit employee reload.
    ui.el('hrCampusEmployee').value='ref-two';const fetchTask=ui.el('hrCampusEmployee').fire('change');
    await ui.flush();ui.switchTenant('20');release();await Promise.all([task,fetchTask]);await ui.flush();
    assert.equal(ui.el('hrCampusWorkspace').inert,true);
    assert.equal(ui.el('hrCampusRows').children.length,0);
});
test('campus page loads local antiforgery and shared transactional draft protection',()=>{
    assert.match(campusView,/@Html\.AntiForgeryToken/);
    assert.match(campusView,/js\/platform\/form-guard\.js/);
    assert.match(campusJs,/authSessionReference/);
    assert.match(campusJs,/requestKey/);
    assert.match(campusJs,/pageSize:'20'/);
});
test('leave review sends rowversion and CSRF, blocks double decision and exposes leave reason',async()=>{
    const ui=await leaveReady();
    assert.equal(leaveRow(ui).children[1].children[0].textContent,'Reason: Family');
    let release,writes=0;
    ui.setFetch((url,options,response,defaults)=>url.endsWith('/leaves/review')
        ? ++writes===1 ? new Promise(resolve=>{release=()=>resolve(response(true));}) : Promise.resolve(response(true))
        : Promise.resolve(response(defaults(url))));
    const first=actionButton(ui,0).fire('click');
    await actionButton(ui,1).fire('click');
    assert.equal(writes,1);
    const call=ui.calls.find(x=>x.options.method==='POST');
    assert.equal(call.options.headers.RequestVerificationToken,'csrf-token');
    assert.equal(JSON.parse(call.options.body).rowVersion,'rv1');
    release();await first;
    assert.match(ui.el('hrAlert').textContent,/approved/i);
});
test('leave review note draft survives refresh and guards paging/filter switches',async()=>{
    const ui=await leaveReady(),note=actionInput(ui);
    note.value='Check documentation';await note.fire('input');
    ui.setConfirm(false);const count=ui.calls.length;
    await ui.el('hrLeaveSearch').fire('submit');
    assert.equal(ui.calls.length,count);
    ui.setConfirm(true);await ui.el('hrLeaveSearch').fire('submit');await ui.flush();
    assert.equal(actionInput(ui).value,'');
    const before={preventDefault(){this.prevented=true;},returnValue:null};
    await actionInput(ui).fire('input'); // blank note should not dirty
    ui.beforeUnload[0](before);assert.equal(before.prevented,undefined);
});
test('leave confirmed write is distinct from failed list refresh and cannot repost stale row',async()=>{
    const ui=await leaveReady();ui.setFetch((url,options,response,defaults)=>
        url.includes('/api/hr/leaves?') ? Promise.reject(Error('Read failed')) :
            Promise.resolve(response(defaults(url))));
    const row=leaveRow(ui);await actionButton(ui,0).fire('click');
    assert.match(ui.el('hrAlert').textContent,/decision recorded; the list could not refresh/i);
    assert.match(row.children[5].textContent,/decision recorded/i);
    assert.equal(ui.calls.filter(x=>x.options.method==='POST').length,1);
});
test('leave search ignores out-of-order responses and prevents stale list overwrite',async()=>{
    const ui=await leaveReady(),pending=[];
    ui.setFetch((url,options,response,defaults)=>url.includes('/api/hr/leaves?')
        ? new Promise(resolve=>pending.push({url,resolve}))
        : Promise.resolve(response(defaults(url))));
    ui.el('hrLeaveText').value='Al';const old=ui.el('hrLeaveSearch').fire('submit');await ui.flush();
    ui.el('hrLeaveText').value='Be';const current=ui.el('hrLeaveSearch').fire('submit');await ui.flush();
    pending[1].resolve(ui.response({items:[{...ui.defaults('/api/hr/leaves?').items[0],id:22,employeeName:'Beta'}],totalCount:1}));
    await current;await ui.flush();
    pending[0].resolve(ui.response({items:[{...ui.defaults('/api/hr/leaves?').items[0],id:11,employeeName:'Wrong'}],totalCount:1}));
    await old;await ui.flush();
    assert.match(leaveRow(ui).children[0].textContent,/Beta/);
});
test('leave review session switch clears PII and disables stale decisions',async()=>{
    const ui=await leaveReady();let release;
    ui.setFetch((url,options,response,defaults)=>url.includes('/api/hr/leaves?')
        ? new Promise(resolve=>{release=()=>resolve(response(defaults(url)));})
        : Promise.resolve(response(defaults(url))));
    const task=ui.el('hrLeaveSearch').fire('submit');await ui.flush();
    ui.switchTenant('20');release();await task;await ui.flush();
    assert.equal(ui.el('hrWorkspaceRoot').inert,true);
    assert.equal(ui.el('hrLeaveRows').children.length,0);
});
test('leave page retains role separation, server concurrency and request verification controls',()=>{
    assert.match(leaveView,/@Html\.AntiForgeryToken/);
    assert.match(leaveJs,/dataset\.activeFeature === 'leaves'/);
    assert.match(leaveJs,/rowVersion:leave\.rowVersion/);
    assert.match(leaveJs,/authSessionReference/);
    assert.match(leaveJs,/pendingNotes/);
});
