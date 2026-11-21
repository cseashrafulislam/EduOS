'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../..'),read=p=>fs.readFileSync(path.join(root,p),'utf8');
const adapter=require('../../EduOS.App/wwwroot/js/platform/ai-page-intent-review.js');
test('page intent adapter never shares field values or sends unsolicited requests',async()=>{
    const field={tagName:'INPUT',type:'text',name:'Search',id:'Search',isConnected:true,
        value:'sensitive student number',disabled:false,readOnly:false,
        labels:[{textContent:'Search'}],dataset:{},getClientRects:()=>[1],
        closest:()=>null,getAttribute:()=>null};
    const doc={getElementById:id=>id==='main-content'?{querySelectorAll:()=>[field]}:null};
    const targets=adapter.collectTargets(doc,{});
    assert.equal(targets.length,1);
    assert.equal(targets[0].kind,'readonly');
    const body=adapter.bodyOf('focus Search','/Student/Index',targets);
    assert.doesNotMatch(JSON.stringify(body),/sensitive student number/);
    assert.equal(body.targets[0].key,'t1');
    let calls=0;
    const fetcher=async(url,options)=>{calls++;return {ok:true,json:async()=>({
        decision:'Ready',action:'FocusField',targetKey:'t1',pagePath:'/Student/Index',
        requiresConfirmation:true,businessWriteExecuted:false
    })};};
    assert.equal(calls,0);
    const result=await adapter.preview(fetcher,'focus Search','/Student/Index',targets,'csrf');
    assert.equal(result.businessWriteExecuted,false);
    assert.equal(calls,1);
});
test('adapter rejects missing csrf, bad routes and unbounded transcripts',async()=>{
    const invalid=async()=>{throw Error('Must never send request');};
    await assert.rejects(adapter.preview(invalid,'focus Search','/Search',[], 'token'),/Invalid assistant/);
    await assert.rejects(adapter.preview(invalid,'focus Search','https://external', [{key:'t1'}], 'token'),/Invalid assistant/);
    await assert.rejects(adapter.preview(invalid,'x'.repeat(513),'/Search',[{key:'t1'}], 'token'),/Invalid assistant/);
    await assert.rejects(adapter.preview(invalid,'focus Search','/Search',[{key:'t1'}], ''),/Invalid assistant/);
});
test('global page adapter reuses existing assistant and never submits or uses an external provider',()=>{
    const js=read('EduOS.App/wwwroot/js/platform/ai-page-intent-review.js');
    const layout=read('EduOS.App/Views/Shared/_Layout.cshtml');
    assert.match(layout,/id="pageAssistantServerReview"/);
    assert.match(layout,/ai-page-intent-review\.js/);
    assert.match(js,/api\/ai\/voice\/page\/preview/);
    assert.match(js,/EduOSFieldDraft/);
    assert.match(js,/EduOSFilterDraft/);
    assert.match(js,/businessWriteExecuted!==false/);
    assert.doesNotMatch(js,/api\.openai|localStorage|\.submit\(|\.requestSubmit\(|innerHTML/);
});
