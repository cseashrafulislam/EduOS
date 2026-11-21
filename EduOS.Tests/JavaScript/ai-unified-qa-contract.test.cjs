'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../..'),read=p=>fs.readFileSync(path.join(root,p),'utf8');

test('Ask EduOS presents one Q&A form for free canonical MIS and optional AI fallback',()=>{
    const view=read('EduOS.App/Views/AiWorkspace/Institution.cshtml');
    const js=read('EduOS.App/wwwroot/js/ai/institution-questions.js');
    assert.equal((view.match(/id="aiInstitutionForm"/g)||[]).length,1);
    assert.match(view,/id="aiInstitutionConsent"/);
    assert.match(view,/id="aiInstitutionSpeak"/);
    assert.match(js,/SpeechRecognition \|\| window.webkitSpeechRecognition/);
    assert.match(js,/input.dispatchEvent\(new Event\('input'/);
    assert.match(view,/Ask EduOS — Q&A/);
    assert.match(js,/\/api\/ai\/institution\/ask/);
    assert.match(js,/\/api\/ai\/institution\/catalog/);
    assert.match(js,/\/api\/ai\/institution\/general/);
    assert.ok(js.indexOf("'/api/ai/institution/ask'") <
        js.indexOf("'/api/ai/institution/general'"), 'local MUST run before provider fallback');
    assert.match(js,/result\.message\.startsWith\('Unsupported question\.'\)/);
    assert.match(js,/if \(!generalAiAvailable\)/);
    assert.match(js,/if \(!agreed\)/);
    assert.match(js,/consent\.checked = false/);
    assert.match(js,/AbortController/);
    assert.match(js,/sameSession\(\)/);
    assert.match(js,/RequestVerificationToken:token/);
    assert.match(js,/textContent/);
    assert.doesNotMatch(js,/innerHTML|localStorage|sessionStorage|api\.openai\.com/);
    new Function(js);
});

test('model-free institution API stays compatible; only provider action has billing limiter',()=>{
    const api=read('EduOS.App/Controllers/Api/AiInstitutionQueryController.cs');
    const provider=read('EduOS.Service/Services/AI/EduOsAiWorkspaceService.cs');
    assert.match(api,/HttpPost\("ask"\)/);
    assert.match(api,/service\.AskAsync\(request\.Question, request\.FromDate, request\.ToDate, ct\)/);
    assert.match(api,/HttpPost\("general"\)/);
    assert.match(api,/HttpGet\("status"\)/);
    assert.match(api,/HttpGet\("catalog"\)/);
    assert.match(api,/provider\.AnswerGeneralKnowledgeAsync/);
    assert.match(api,/RequireModule\("REPORTING"\)/);
    assert.match(api,/Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(api,/AutoValidateAntiforgeryToken/);
    const local=api.slice(api.indexOf('[HttpPost("ask")]'),api.indexOf('[HttpPost("general")]'));
    assert.doesNotMatch(local,/AiGenerationPolicy/);
    const paid=api.slice(api.indexOf('[HttpPost("general")]'));
    assert.match(paid,/AiGenerationPolicy/);
    assert.match(provider,/GeneralKnowledgeQuestionPolicy\.IsAllowed\(question\)/);
    assert.match(provider,/CentralPrivilegedAccess\.HasRoleAsync/);
    assert.match(provider,/await GenerateAsync\(instructions, question!\.Trim\(\), ct, "general"\)/);
});
