const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = pathName => fs.readFileSync(path.join(__dirname, '../../EduOS.App/wwwroot/js', pathName), 'utf8');
const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; };
const flush = async () => { for (let i = 0; i < 8; i++) await new Promise(setImmediate); };
const ok = data => ({ ok:true, json:async () => ({ success:true, data }) });
const fail = () => ({ ok:false, status:503, json:async () => ({ success:false, message:'Readback failed' }) });
function harness(file, rootId, responder, permission = {}) {
    const elements = new Map(), requests = [], observers = [], events = new Map();
    const session = 'A'.repeat(64);
    let discard = true, dirty = false, transition = false;
    function create(tag = 'div') {
        const handlers = new Map();
        return { tagName:tag.toUpperCase(), handlers, children:[], id:'', textContent:'', value:'',
            type:'', checked:false, required:false, disabled:false, open:false, dataset:{}, className:'',
            classList:{ values:new Set(['d-none']), add(x) { this.values.add(x); },
                remove(x) { this.values.delete(x); }, toggle(x, force) {
                    if(force === undefined ? !this.values.has(x) : force)this.values.add(x);
                    else this.values.delete(x);return this.values.has(x);
                } },
            addEventListener(name,fn) { handlers.set(name,fn); },
            append(...items) { this.children.push(...items); },
            appendChild(item) { this.children.push(item); return item; },
            replaceChildren(...items) { this.children=items; },
            get options() { return this.children; }, add(item) { this.children.push(item); },
            querySelectorAll() { return this.controls || []; },
            querySelector() { return null; },
            setAttribute(name,value) { this[name]=value; },
            scrollIntoView() {}, focus() {}, reset() {}, reportValidity() { return true; },
            showModal() { this.open=true; }, close() { this.open=false;this.handlers.get('close')?.(); } };
    }
    function get(id) {
        if(!elements.has(id)){const node=create();node.id=id;elements.set(id,node);}
        return elements.get(id);
    }
    get(rootId).dataset=permission;
    get(rootId).querySelectorAll=() => [];
    get(rootId).querySelector=() => ({ value:'approve' });
    const body={dataset:{authTenantId:'11',authSessionReference:session},append() {}};
    const document={body,documentElement:{lang:'en-BD'},getElementById:get,
        createElement:create,createTextNode:text => ({textContent:text}),
        querySelector:() => ({content:'csrf',value:'csrf'}),
        querySelectorAll:selector => selector.includes('#') ? [] : []};
    function MutationObserver(callback) { observers.push(callback);this.observe=()=>{};this.disconnect=()=>{}; }
    const window={ EduOSSessionTransitionPending:() => transition,
        EduOSFormGuard:{create:() => ({markClean() {dirty=false;},confirmDiscard:() => discard,
            isDirty:() => dirty})},
        addEventListener(type,handler) {events.set(type,handler);},
        location:{assign(url) {events.set('navigation',url);},search:''},print() {} };
    const fetch=(url,options={})=>{requests.push({url,options});return responder(url,options);};
    const Option=function Option(label,value){this.textContent=label;this.value=value;};
    const runtime={document,window,fetch,Option,MutationObserver,URL,URLSearchParams,
        AbortController,confirm:() => true,console,setTimeout:() => 1,clearTimeout:() => {},
        crypto:{randomUUID:(()=>{let n=0;return ()=>'key-'+ ++n;})()}};
    if(file)vm.runInNewContext(script(file),runtime);
    const dispatch=(id,name) => {
        const element=get(id),handler=element.handlers.get(name);
        assert.ok(handler,id+' missing '+name);
        return handler({preventDefault() {},currentTarget:element,target:element});
    };
    const click=node=>{
        assert.ok(node.handlers.has('click'),'Missing click callback');
        return node.handlers.get('click')({preventDefault() {},currentTarget:node,target:node});
    };
    const switchTenant=()=>{body.dataset.authTenantId='12';observers.forEach(fn=>fn());};
    return {get,requests,dispatch,click,switchTenant,window,runtime,events,
        setDirty:value=>{dirty=value;},setDiscard:value=>{discard=value;},
        setTransition:value=>{transition=value;},
        writes:()=>requests.filter(x=>x.options.method && x.options.method!=='GET')};
}

const supplierRow = {id:21,orderNumber:'PO-1',supplierId:9,supplierReference:'ACK-1',
    responseDate:'2026-11-10',promisedDeliveryDate:'2026-11-14',stateCode:'Draft',
    createdBy:'maker',purchaseOrderId:15,rowVersion:'v1',clientRequestId:'existing-key'};
const supplierData={items:[supplierRow],totalPages:1};
function supplier(responder) {
    return harness('procurement/supplier-confirmations.js','ackWorkspace',
        responder || ((url,options)=>options.method&&options.method!=='GET' ? ok({}) : ok(supplierData)),
        { maker:'true',checker:'true',userId:'maker' });
}
test('Supplier response draft cannot be double-saved or closed mid-write',async()=>{
    const gate=deferred();
    const page=supplier((url,options)=>options.method==='POST' ? gate.promise : ok(supplierData));
    await flush();page.dispatch('ackNew','click');
    page.get('ackOrderId').value='15';page.get('ackResponseDate').value='2026-11-11';
    page.get('ackPromisedDate').value='2026-11-15';
    page.get('ackReference').value='ACK-NEW';
    page.dispatch('ackForm','submit');page.dispatch('ackForm','submit');
    page.dispatch('ackCancel','click');await flush();
    assert.equal(page.writes().length,1);assert.equal(page.get('ackDialog').open,true);
    gate.resolve(ok({}));await flush();
    assert.equal(page.get('ackDialog').open,false);
    assert.match(page.get('ackAlert').textContent,/Saved/);
});
test('Supplier response old edit response cannot replace a new editor, and tenant switch wipes rows',async()=>{
    const gate=deferred();
    const page=supplier((url,options)=>url.endsWith('/21') ? gate.promise : ok(supplierData));
    await flush();
    page.click(page.get('ackRows').children[0].children.at(-1).children[0]);
    page.dispatch('ackNew','click');
    gate.resolve(ok(supplierRow));await flush();
    assert.equal(page.get('ackId').value,'');
    page.setDiscard(false);page.setDirty(true);
    page.dispatch('ackCancel','click');assert.equal(page.get('ackDialog').open,true);
    page.setDiscard(true);page.switchTenant();
    assert.equal(page.get('ackRows').children.length,0);
    page.dispatch('ackForm','submit');await flush();
    assert.equal(page.writes().length,0);
});
test('Supplier uncertain create repeats the same idempotency key only for unchanged evidence',async()=>{
    const page=supplier((url,options)=>options.method==='POST' ? fail() : ok(supplierData));
    await flush();page.dispatch('ackNew','click');
    page.get('ackOrderId').value='15';page.get('ackResponseDate').value='2026-11-11';
    page.get('ackPromisedDate').value='2026-11-15';
    page.get('ackReference').value='ACK-NEW';
    page.dispatch('ackForm','submit');await flush();
    page.dispatch('ackForm','submit');await flush();
    page.get('ackReference').value='ACK-CHANGED';
    page.dispatch('ackForm','submit');await flush();
    const keys=page.writes().map(x=>JSON.parse(x.options.body).clientRequestId);
    assert.equal(keys[0],keys[1]);assert.notEqual(keys[1],keys[2]);
});
test('Supplier checker decision is single-flight even across opposing stale row actions',async()=>{
    const gate=deferred();
    const row={...supplierRow,stateCode:'Submitted',createdBy:'another'};
    const page=supplier((url,options)=>options.method==='POST' ? gate.promise :
        ok({items:[row],totalPages:1}));
    await flush();
    const actions=page.get('ackRows').children[0].children.at(-1).children;
    page.click(actions[0]);page.dispatch('ackDecisionForm','submit');
    page.dispatch('ackDecisionForm','submit');await flush();
    assert.equal(page.writes().length,1);
    page.dispatch('ackDecisionClose','click');assert.equal(page.get('ackDecisionDialog').open,true);
    gate.resolve(ok({}));await flush();
    assert.equal(page.get('ackDecisionDialog').open,false);
});

const field={id:401,fieldKey:'VISITOR',label:'Visitor preference',dataType:1,
    isRequired:false,isActive:true,displayOrder:2,rowVersion:'v1'};
const fieldList=(page,sz)=>({items:page===4 && sz===100 ? [field] :
    sz===20 ? [field] : [],totalPages:sz===100?4:1});
function custom(responder) {
    return harness('custom-fields/admin.js','unusedCustomFields',responder ||
        ((url,options)=>{
            if(options.method&&options.method!=='GET')return ok({});
            if(url.includes('tenant-values'))return ok([{customFieldDefinitionId:401,label:'Visitor preference',value:'yes'}]);
            const query=new URL(url,'https://example.test').searchParams;
            return ok(fieldList(Number(query.get('page')),Number(query.get('pageSize'))));
        }));
}
test('Custom Fields loads tenant values beyond the former three-page definition cap',async()=>{
    const page=custom();await flush();
    assert.equal(page.get('cfValueInputs').children.length,1);
    assert.equal(page.get('cfValueInputs').children[0].children[0].children[0].value,'yes');
    assert.equal(page.requests.filter(x=>x.url.includes('page=4&pageSize=100')).length,1);
});
test('Custom Fields definition save single-flight and confirmed write survives failed readback',async()=>{
    const gate=deferred();let pages=0;
    const page=custom((url,options)=>{
        if(options.method==='POST')return gate.promise;
        if(url.includes('tenant-values'))return ok([]);
        if(url.includes('pageSize=20')&&++pages>1)return fail();
        return ok(fieldList(1,20));
    });
    await flush();
    page.get('cfForm').controls=[page.get('cfKey')];
    page.get('cfKey').value='KEY';page.get('cfLabel').value='Field';
    page.dispatch('cfForm','submit');page.dispatch('cfForm','submit');
    await flush();assert.equal(page.writes().length,1);assert.equal(page.get('cfKey').disabled,true);
    gate.resolve(ok({}));await flush();
    assert.equal(page.get('cfKey').disabled,false);
    assert.match(page.get('cfNotice').textContent,/Definition saved; refresh/);
});
test('Custom Fields discard denial and institution change prevent stale modifications',async()=>{
    const page=custom();await flush();
    page.setDiscard(false);
    page.click(page.get('cfRows').children[0].children.at(-1).children[0]);
    assert.equal(page.get('cfId').value,'');
    page.setDiscard(true);
    page.click(page.get('cfRows').children[0].children.at(-1).children[0]);
    assert.equal(page.get('cfId').value,401);
    page.switchTenant();page.dispatch('cfForm','submit');await flush();
    assert.equal(page.writes().length,0);
    assert.equal(page.get('cfRows').children.length,0);
    assert.equal(page.get('cfValueInputs').children.length,0);
});
test('Custom Fields profile values remain single-flight without losing input during save',async()=>{
    const gate=deferred();
    const page=custom((url,options)=>{
        if(options.method==='PUT')return gate.promise;
        if(url.includes('tenant-values'))return ok([{customFieldDefinitionId:401,label:'Visitor preference',value:'yes'}]);
        const query=new URL(url,'https://example.test').searchParams;
        return ok(fieldList(Number(query.get('page')),Number(query.get('pageSize'))));
    });
    await flush();
    const input=page.get('cfValueInputs').children[0].children[0].children[0];
    input.value='no';input.dataset.fieldId='401';input.dataset.version='v1';
    page.get('cfValueInputs').controls=[input];
    page.get('cfValuesForm').controls=[input];
    page.dispatch('cfValuesForm','submit');page.dispatch('cfValuesForm','submit');
    await flush();
    assert.equal(page.writes().length,1);assert.equal(input.disabled,true);
    assert.equal(JSON.parse(page.writes()[0].options.body)[0].value,'no');
    gate.resolve(ok({}));await flush();
    assert.equal(page.get('cfValueInputs').children.length,1);
});

function liabilities(responder) {
    return harness('library/liabilities.js','libraryLiabilitiesApp',responder ||
        ((url,options)=>{
            if(options.method==='POST')return ok({});
            if(url.includes('incidents?'))return ok({items:[{issueReference:'ISSUE-1',
                studentName:'Student',studentCode:'S-1',bookTitle:'Math',
                accessionNumber:'A-1',kind:1,replacementPrice:100,assessed:false}],totalPages:1});
            return ok({items:[],totalPages:1});
        }));
}
test('Library lost-book assessment is single-flight and cannot be cancelled mid-write',async()=>{
    const gate=deferred();
    const page=liabilities((url,options)=>{
        if(options.method==='POST')return gate.promise;
        if(url.includes('incidents?'))return ok({items:[{issueReference:'ISSUE-1',
            studentName:'Student',studentCode:'S-1',bookTitle:'Math',
            accessionNumber:'A-1',kind:1,replacementPrice:100,assessed:false}],totalPages:1});
        return ok({items:[],totalPages:1});
    });
    await flush();
    page.click(page.get('incidentRows').children[0].children.at(-1).children[0]);
    page.dispatch('assessmentForm','submit');page.dispatch('assessmentForm','submit');
    page.dispatch('assessmentCancel','click');await flush();
    assert.equal(page.writes().length,1);
    assert.equal(page.get('assessmentDialog').open,true);
    gate.resolve(ok({}));await flush();
    assert.equal(page.get('assessmentDialog').open,false);
});
test('Library assessment retry keeps original request key, but changed reason gets a new one',async()=>{
    const page=liabilities((url,options)=>{
        if(options.method==='POST')return fail();
        if(url.includes('incidents?'))return ok({items:[{issueReference:'ISSUE-1',
            studentName:'Student',studentCode:'S-1',bookTitle:'Math',
            accessionNumber:'A-1',kind:1,replacementPrice:100,assessed:false}],totalPages:1});
        return ok({items:[],totalPages:1});
    });
    await flush();
    page.click(page.get('incidentRows').children[0].children.at(-1).children[0]);
    page.dispatch('assessmentForm','submit');await flush();
    page.dispatch('assessmentForm','submit');await flush();
    page.get('assessmentReason').value='New evidence';
    page.dispatch('assessmentForm','submit');await flush();
    const keys=page.writes().map(x=>JSON.parse(x.options.body).clientRequestId);
    assert.equal(keys[0],keys[1]);assert.notEqual(keys[1],keys[2]);
});
test('Library liability export is discarded if filters change before its blob completes',async()=>{
    const gate=deferred();
    const page=liabilities((url,options)=>url.includes('/export?') ? gate.promise :
        ok({items:[],totalPages:1}));
    await flush();
    page.dispatch('liabilityExport','click');await flush();
    page.get('liabilityState').value='Waived';
    gate.resolve({ok:true,blob:async()=>new Blob(['a,b'])});await flush();
    assert.equal(page.get('liabilityExport').disabled,false);
    assert.equal(page.get('liabilityAlert').textContent,'');
});
test('Library liability maker-checker review cannot double-post or close during write',async()=>{
    const gate=deferred(), assessment={reference:'L-1',studentName:'Student',bookTitle:'Math',
        accessionNumber:'A-1',amount:100,dueAmount:100,state:1,rowVersion:'v1'};
    const page=liabilities((url,options)=>{
        if(options.method==='POST')return gate.promise;
        if(url.includes('posting-options'))return ok({items:[{id:3,label:'Nov'}],totalPages:1});
        if(url.includes('incidents?'))return ok({items:[],totalPages:1});
        if(url.includes('liabilities?'))return ok({items:[assessment],totalPages:1});
        return ok({items:[],totalPages:1});
    });
    await flush();
    page.click(page.get('liabilityRows').children[0].children.at(-1).children[0]);await flush();
    page.get('postingPeriod').value='3';page.get('postingHead').value='3';
    page.dispatch('reviewForm','submit');page.dispatch('reviewForm','submit');
    page.dispatch('reviewCancel','click');await flush();
    assert.equal(page.writes().length,1);
    assert.equal(page.get('reviewDialog').open,true);
    gate.resolve(ok({}));await flush();
    assert.equal(page.get('reviewDialog').open,false);
});
test('Library reversal cannot submit until canonical previous reversal lookup finishes',async()=>{
    const gate=deferred();
    const assessment={reference:'L-2',studentName:'Student',bookTitle:'Math',accessionNumber:'A-1',
        amount:100,paidAmount:0,dueAmount:100,state:3,rowVersion:'v2',invoiceNumber:'INV-2'};
    const page=liabilities((url,options)=>{
        if(options.method==='POST')return ok({});
        if(url.endsWith('/L-2/reversal'))return gate.promise;
        if(url.includes('posting-options'))return ok({items:[{id:4,label:'Nov'}],totalPages:1});
        if(url.includes('liabilities?'))return ok({items:[assessment],totalPages:1});
        return ok({items:[],totalPages:1});
    });
    await flush();
    const actions=page.get('liabilityRows').children[0].children.at(-1).children;
    page.click(actions.at(-1));page.dispatch('reversalForm','submit');
    await flush();assert.equal(page.writes().length,0);
    gate.resolve(ok(null));await flush();
    page.get('reversalDate').value='2026-11-11';
    page.get('reversalPeriod').value='4';
    page.get('reversalReason').value='Wrong issue';
    page.dispatch('reversalForm','submit');await flush();
    assert.equal(page.writes().length,1);
});

function journals(responder) {
    const page=harness(null,'accountingJournals',()=>ok({}));
    const src=script('finance/accounting-journals-register.js')
        .replace('export function setupJournalRegister(ctx)', 'function setupJournalRegister(ctx)')+
        '\nwindow.__setupJournalRegister=setupJournalRegister;';
    vm.runInNewContext(src,page.runtime);
    let live=true;
    const ctx={get:page.get,writable:true,today:()=> '2026-11-11',
        live:()=>live,api:responder,notify:message=>{page.get('journalNotice').textContent=message;},
        run:async fn=>{try{await fn();}catch(error){page.get('journalNotice').textContent=error.message;}}};
    page.window.__setupJournalRegister(ctx);
    return {page,ctx,setLive:value=>{live=value;}};
}
const journalRow={reference:'J-1',journalNumber:'GL-1',journalDate:'2026-11-11',
    accountingPeriodName:'Nov',state:1,sourceType:'Manual',rowVersion:'v1',
    totalDebit:100,totalCredit:100,lines:[]};
test('Accounting Journal submit is single-flight and blocks closing selected entry mid-write',async()=>{
    const gate=deferred(), posts=[];
    const {page,ctx}=journals((path,method,body)=>{
        if(method==='POST'){posts.push({path,body});return gate.promise;}
        if(path.startsWith('/'))return Promise.resolve({...journalRow});
        return Promise.resolve({items:[journalRow],totalPages:1,totalCount:1});
    });
    await ctx.reload();page.click(page.get('journalRegisterRows').children[0].children.at(-1).children[0]);
    await flush();
    page.dispatch('journalSubmit','click');page.dispatch('journalSubmit','click');await flush();
    assert.equal(posts.length,1);
    page.dispatch('journalCloseDetail','click');
    assert.equal(page.get('journalDetailPanel').classList.values.has('d-none'),false);
    gate.resolve({reference:'J-1'});await flush();
    assert.match(page.get('journalNotice').textContent,/Journal submit completed/);
});
test('Accounting Journal accepted action is not reposted if post-write readback fails',async()=>{
    let reads=0,posts=0;
    const {page,ctx}=journals((path,method)=>{
        if(method==='POST'){posts++;return Promise.resolve({reference:'J-1'});}
        if(path.startsWith('/'))return Promise.resolve(journalRow);
        if(++reads>1)return Promise.reject(Error('Readback failed'));
        return Promise.resolve({items:[journalRow],totalPages:1,totalCount:1});
    });
    await ctx.reload();page.click(page.get('journalRegisterRows').children[0].children.at(-1).children[0]);
    await flush();
    page.dispatch('journalSubmit','click');await flush();
    page.dispatch('journalSubmit','click');await flush();
    assert.equal(posts,1);
    assert.match(page.get('journalNotice').textContent,/Journal submit saved; refresh/);
});
test('Accounting Journal reversal replays unchanged key but rotates when reason changes',async()=>{
    const row={...journalRow,state:4},posts=[];
    const {page,ctx}=journals((path,method,body)=>{
        if(method==='POST'){posts.push({path,body});return Promise.reject(Error('Acknowledgement lost'));}
        if(path.startsWith('/'))return Promise.resolve(row);
        return Promise.resolve({items:[row],totalPages:1,totalCount:1});
    });
    await ctx.reload();page.click(page.get('journalRegisterRows').children[0].children.at(-1).children[0]);
    await flush();
    page.get('journalReverseDate').value='2026-11-11';
    page.get('journalReverseReason').value='Bank reversal';
    page.dispatch('journalReverseForm','submit');await flush();
    page.dispatch('journalReverseForm','submit');await flush();
    assert.equal(posts.length,2);
    assert.equal(posts[0].body.clientRequestId,posts[1].body.clientRequestId);
    page.get('journalReverseReason').value='Correction with new evidence';
    page.dispatch('journalReverseForm','submit');await flush();
    assert.notEqual(posts[1].body.clientRequestId,posts[2].body.clientRequestId);
});
test('Accounting API drops in-flight old-institution data and refuses later posts',async()=>{
    const gate=deferred(),page=harness(null,'accountingJournals',()=>gate.promise);
    const src=script('finance/accounting-journals-api.js')
        .replace('export const journalSessionLive','const journalSessionLive')
        .replace('export async function journalRequest','async function journalRequest')
        .replace('export async function journalSetup','async function journalSetup')+
        '\nwindow.__journalRequest=journalRequest;';
    vm.runInNewContext(src,page.runtime);
    const pending=page.window.__journalRequest('?page=1');
    page.switchTenant();gate.resolve(ok({items:[{reference:'private'}]}));
    await assert.rejects(pending,/session changed/i);
    await assert.rejects(page.window.__journalRequest('/J-1','POST',{}),/session changed/i);
    assert.equal(page.requests.length,1);
});
