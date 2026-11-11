const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/platform/central-work-center.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/CentralWorkCenter/Index.cshtml'), 'utf8');

function harness(fetchResponse) {
  const nodes = {}, requests = [], exports = [], prints = [];
  function element(tag = 'div') {
    return { tag, children: [], handlers: {}, textContent: '', value: '', disabled: false,
      className: '', href: '', download: '', colSpan: 1,
      appendChild(child) { this.children.push(child); return child; },
      replaceChildren(...children) { this.children = children; },
      addEventListener(type, cb) { this.handlers[type] = cb; },
      click() { this.handlers.click?.(); },
      remove() {}
    };
  }
  for (const id of ['centralWorkCenter','cwcAlert','cwcFilters','cwcCampus','cwcRows',
    'cwcPrevious','cwcNext','cwcPage','cwcSource','cwcPageSize','cwcScope','cwcSummary','cwcExport','cwcPrint'])
    nodes[id] = element();
  nodes.cwcPageSize.value = '25';
  const document = { documentElement: { lang: 'en-BD' }, body: element('body'),
    getElementById: id => nodes[id], createElement: tag => {
      const el = element(tag);
      if (tag === 'a') el.click = () => exports.push(el.download);
      return el;
    } };
  const window = { addEventListener() {}, print() { prints.push(true); } };
  const URL = { createObjectURL: () => 'blob:work', revokeObjectURL() {} };
  vm.runInNewContext(source, { document, window, URL, URLSearchParams, AbortController,
    Blob, Date, Number, String, Object,
    fetch: async (url, opts) => {
      requests.push({url,opts});
      return fetchResponse(url,opts);
    } }, {timeout:1000});
  return { nodes, requests, exports, prints };
}
const tick = () => new Promise(resolve => setImmediate(resolve));
const item = {source:'ProcurementRequisition',sourceId:12,campusId:3,
  reference:'REQ-12',state:'Submitted',submittedAtUtc:'2026-11-12T00:00:00Z',
  workspaceUrl:'/ProcurementRequisitions/Index'};
const success = (totalCount=1,items=[item],page=1) =>
  ({ok:true,json:async()=>({success:true,data:{inventoryIncluded:true,
    sourceCounts: totalCount ? [{source:'ProcurementRequisition',count:totalCount}] : [],
    pending:{page,pageSize:25,totalCount,items}}})});

test('current-page CSV/print only enable after authorized successful response',async()=>{
  const h=harness(()=>success());
  assert.equal(h.nodes.cwcExport.disabled,true);
  await tick();
  assert.equal(h.nodes.cwcExport.disabled,false);
  assert.equal(h.nodes.cwcPrint.disabled,false);
  h.nodes.cwcExport.click(); h.nodes.cwcPrint.click();
  assert.deepEqual(h.exports,['my-work-page-1.csv']);
  assert.equal(h.prints.length,1);
  assert.match(view,/id="cwcExport"/);
  assert.match(view,/id="cwcPrint"/);
  assert.match(view,/Only authorized rows on this page/);
});

test('failed requests keep navigation and export disabled even after previous successful page',async()=>{
  let calls=0;
  const h=harness(()=> ++calls===1 ? success(60) :
    ({ok:false,json:async()=>({success:false,message:'Unavailable'})}));
  await tick();
  assert.equal(h.nodes.cwcNext.disabled,false);
  h.nodes.cwcNext.click();
  await tick();
  assert.equal(h.nodes.cwcNext.disabled,true);
  assert.equal(h.nodes.cwcPrevious.disabled,true);
  assert.equal(h.nodes.cwcExport.disabled,true);
  assert.equal(h.nodes.cwcPrint.disabled,true);
  assert.equal(h.nodes.cwcRows.children.length,0);
  assert.match(h.nodes.cwcPage.textContent,/unavailable/i);
});

test('empty response and malformed response never enable export',async()=>{
  for(const response of [success(0,[]),success(1,[item],2)]){
    const h=harness(()=>response);
    await tick();
    assert.equal(h.nodes.cwcExport.disabled,true);
    assert.equal(h.nodes.cwcNext.disabled,true);
  }
});

test('server controls tenant, role, campus and source; export never requests extra records',()=>{
  const api=fs.readFileSync(path.join(root,'EduOS.App/Controllers/Api/CentralWorkCenterApiController.cs'),'utf8');
  const service=fs.readFileSync(path.join(root,'EduOS.Service/Services/Platform/CentralWorkCenterService.cs'),'utf8');
  assert.match(api,/Authorize\(Roles = "TenantAdmin,Principal,InventoryManager"\)/);
  assert.match(service,/visibleCampuses\.Contains/);
  assert.match(service,/pending = pending\.Where\(x => x.Kind == kind\)/);
  assert.match(source,/currentItems = result\.items\.slice\(\)/);
  assert.match(source,/if \(!resultReady \|\| loading \|\| !currentItems\.length\) return/);
  assert.match(source,/createObjectURL/);
  assert.doesNotMatch(source,/\/api\/.*export/);
});
