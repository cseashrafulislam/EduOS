'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

const scripts = path.resolve(__dirname, '../../EduOS.App/wwwroot/js/inventory');
const reference = '11111111-2222-4333-8444-555555555577';
const rowVersion = 'AQIDBA==';
const injectedLot = '<img src=x onerror=alert(1)>-LOT';
const ok = data => ({contentType:'application/json',body:JSON.stringify({success:true,data})});

function fixture(lang, maker) {
    const opts = '<option value=""></option><option>Automatic</option><option>ManualFinal</option><option>AutoPlusManual</option>';
    return '<!doctype html><html lang="' + lang + '"><head>' +
        '<meta name="request-verification-token" content="csrf"><style>' +
        '.d-none{display:none} .row{display:flex;flex-wrap:wrap} input{max-width:100%} ' +
        '.table-responsive{overflow-x:auto} table{width:100%}</style></head><body>' +
        '<section id="inventoryMovements" data-create="' + maker + '" data-post="true" data-user-id="' +
        (maker ? '7' : '8') + '"><div id="movementAlert"></div><section id="physicalCounts">' +
        '<button id="countHelp" type="button" aria-expanded="false"></button>' +
        '<p id="countHelpText" class="d-none"></p><button id="countReload"></button>' +
        '<form id="countStartForm"><select id="countLocation"></select><input id="countItemSearch">' +
        '<select id="countItem"></select><input id="countLot"><button id="countAddLine" type="button"></button>' +
        '<ul id="countSelectedLines"></ul><input id="countStartReason" required>' +
        '<input id="countBlind" type="checkbox" checked><button id="countStart" type="submit"></button></form>' +
        '<select id="countStateFilter"><option value=""></option><option>Submitted</option></select>' +
        '<select id="countLocationFilter"></select><table><tbody id="countRegister"></tbody></table>' +
        '<button id="countPrevious"></button><button id="countNext"></button><span id="countPageLabel"></span>' +
        '<article id="countDetail" class="d-none"><h3 id="countDetailHeading"></h3><p id="countDetailMetadata"></p>' +
        '<button id="countPrint" type="button">Print</button>' +
        '<button id="countExportCsv" type="button">CSV</button>' +
        '<form id="countSubmitForm" class="d-none"><table><tbody id="countDraftLines"></tbody></table>' +
        '<button id="countSubmit" type="submit">Submit</button></form>' +
        '<section id="countAuditPanel" class="d-none"><table><tbody id="countAuditLines"></tbody></table>' +
        '<form id="countReviewForm" class="d-none"><select id="countDecision">' +
        '<option>Approve</option><option>Reject</option></select>' +
        '<select id="countVarianceMode">' + opts + '</select>' +
        '<div id="countCorrectionsContainer"><div id="countCorrections"></div></div>' +
        '<input id="countReviewReason"><button id="countReviewSubmit" type="submit">Review</button>' +
        '</form></section></article></section></section></body></html>';
}

async function scenario(browser, lang, maker) {
    const page = await browser.newPage({viewport:{width:390,height:820},acceptDownloads:true});
    const errors = [], writes = [];
    let state = maker ? 'Draft' : 'Submitted';
    page.on('pageerror', e => errors.push(e.message));
    await page.context().route('http://localhost/**', async route => {
        const req = route.request(), url = new URL(req.url());
        if (url.pathname.startsWith('/js/inventory/')) {
            const name = path.basename(url.pathname);
            if (!['movements-api.js','movements-physical-counts.js'].includes(name))
                return route.fulfill({status:404,body:'Unknown module'});
            return route.fulfill({contentType:'text/javascript',body:fs.readFileSync(path.join(scripts,name),'utf8')});
        }
        if (url.pathname === '/InventoryMovements/PrintPhysicalCount') {
            assert.equal(url.searchParams.get('reference'),reference);
            return route.fulfill({contentType:'text/html',body:'<!doctype html><title>Authorized printable count sheet</title>'});
        }
        if (!url.pathname.startsWith('/api/'))
            return route.fulfill({contentType:'text/html',body:fixture(lang,maker)});
        const line = {
            lineNo:1,inventoryItemId:501,lotOrBatchNo:injectedLot,
            bookQuantity:state === 'Draft' ? null : 10,
            bookValue:state === 'Draft' ? null : 50,
            reservedQuantity:state === 'Draft' ? null : 3,
            countedQuantity:state === 'Draft' ? null : 0,
            suggestedVariance:state === 'Draft' ? null : -10,
            postedVariance:state === 'Posted' ? -10 : null,
            postedMovementReference:state === 'Posted' ? reference : null
        };
        const count = {reference,locationId:31,stateCode:state,blind:true,
            reason:'Independent annual warehouse count',createdByUserId:7,
            reviewedByUserId:state === 'Posted' ? 8 : null,varianceMode:'Automatic',
            snapshotAtUtc:'2026-10-31T02:00:00Z',rowVersion,
            lines:[line]};
        if (req.method() !== 'GET') {
            const command = req.postDataJSON();
            writes.push({pathname:url.pathname,csrf:req.headers().requestverificationtoken,command});
            if (url.pathname.endsWith('/submit')) state = 'Submitted';
            if (url.pathname.endsWith('/review')) state = command.decision === 'Approve' ? 'Posted' : 'Rejected';
            count.stateCode = state;
            count.lines[0].bookQuantity = 10;
            count.lines[0].suggestedVariance = -10;
            count.lines[0].countedQuantity = 0;
            count.lines[0].postedVariance = state === 'Posted' ? -10 : null;
            return route.fulfill(ok(count));
        }
        if (url.pathname === '/api/inventory/catalog/locations')
            return route.fulfill(ok({items:[{id:31,code:'MAIN',name:'Main Store',isActive:true}],
                totalCount:1,page:1,pageSize:100,hasNextPage:false}));
        if (url.pathname === '/api/inventory/catalog/items')
            return route.fulfill(ok({items:[{id:501,code:'INK',name:'Ink',isActive:true,isStockTracked:true}]}));
        if (url.pathname === '/api/inventory/movements/counts')
            return route.fulfill(ok({items:[count],totalPages:1,totalCount:1}));
        if (url.pathname === '/api/inventory/movements/counts/' + reference)
            return route.fulfill(ok(count));
        return route.fulfill({status:404,body:'Not found'});
    });
    try {
        await page.goto('http://localhost/InventoryMovements');
        await page.evaluate(async () => {
            const {initPhysicalCounts} = await import('/js/inventory/movements-physical-counts.js');
            await initPhysicalCounts();
        });
        await page.locator('#countRegister button').waitFor();
        assert.equal(await page.locator('#countRegister button').innerText(),
            lang.startsWith('bn') ? 'দেখুন' : 'Open');
        await page.locator('#countHelp').click();
        assert.equal(await page.locator('#countHelp').getAttribute('aria-expanded'),'true');
        await page.locator('#countRegister button').click();
        await page.locator('#countDetailHeading').getByText(state).waitFor();
        assert.equal(await page.locator('#countDetail img').count(),0,'lot text must not execute markup');
        if (maker) {
            assert.match(await page.locator('#countDraftLines').innerText(),/—/);
            assert.doesNotMatch(await page.locator('#countDraftLines').innerText(),/10/);
            assert.equal(await page.locator('#countAuditPanel').isVisible(),false);
        } else {
            assert.match(await page.locator('#countAuditLines').innerText(),/10/);
            assert.equal(await page.locator('#countReviewForm').isVisible(),true);
        }
        const printedWindow = page.context().waitForEvent('page');
        await page.locator('#countPrint').click();
        const printable = await printedWindow;
        await printable.waitForLoadState('domcontentloaded');
        assert.match(printable.url(),/PrintPhysicalCount/);
        await printable.close();
        if (maker) {
            await page.locator('#countDraftLines input').fill('0');
            await page.locator('#countSubmit').click();
            await page.locator('#countDetailHeading').getByText('Submitted').waitFor();
            assert.equal(await page.locator('#countReviewForm').isVisible(),false,
                'maker must not see own checker decision form');
            assert.deepEqual(writes.map(x => x.command.lines?.[0]?.countedQuantity),[0]);
        } else {
            await page.locator('#countVarianceMode').selectOption('ManualFinal');
            await page.locator('[data-count-line="1"]').fill('-10');
            await page.locator('#countReviewReason').fill('Independent verified total physical shortage');
            await page.locator('#countReviewSubmit').click();
            await page.locator('#countDetailHeading').getByText('Posted').waitFor();
            const downloadEvent = page.waitForEvent('download');
            await page.locator('#countExportCsv').click();
            const download = await downloadEvent;
            assert.match(download.suggestedFilename(),/^physical-count-.*\.csv$/);
            assert.equal(writes[0].command.varianceMode,'ManualFinal');
            assert.equal(writes[0].command.corrections[0].quantity,-10);
        }
        assert.equal(writes.every(x => x.csrf === 'csrf'),true);
        assert.deepEqual(errors,[]);
        console.log('Inventory count responsive ' + lang + ' ' + (maker ? 'maker/blind' : 'checker/variance') + ' PASS');
    } finally { await page.close(); }
}

(async () => {
    const browser = await chromium.launch({headless:true,args:['--no-sandbox']});
    try {
        await scenario(browser,'en-BD',true);
        await scenario(browser,'bn-BD',false);
    } finally { await browser.close(); }
})().catch(error => {console.error(error);process.exitCode=1;});
