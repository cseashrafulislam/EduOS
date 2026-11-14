'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../..'),read=p=>fs.readFileSync(path.join(root,p),'utf8');
const routes=['Learning/Index','Learning/StudyPlan','TeacherStudio/Index','TeacherStudio/Public','TeacherStudio/AdaptiveGoals','TeacherStudio/Cpd','TeacherStudio/CpdReview','Learning/Quizzes','Learning/LiveClasses','Learning/CourseContent','Learning/AssignmentFiles','Learning/Certificates','Learning/CourseCompletions','Learning/MySubmissions'];
const view=p=>read('EduOS.App/Views/'+p+'.cshtml');
test('UX C learning menu owns exactly fourteen existing unique routes',()=>{
 const group=read('docs/ux/lanes/UX_C.md').split('## academics:learning (14)')[1].split('## academics:assessments')[0];
 assert.equal(routes.length,14);assert.equal(new Set(routes).size,14);
 for(const p of routes){assert.ok(group.includes(String.fromCharCode(96)+'/'+p+String.fromCharCode(96)),p);assert.ok(fs.existsSync(path.join(root,'EduOS.App/Views/'+p+'.cshtml')));}
});
test('shared shell is the only visible page title, no extra header or Info row',()=>{
 for(const p of routes){let s=view(p);assert.match(s,/ViewData\["Title"\]/);assert.doesNotMatch(s,/<h1\b|<header\b|content-header-card/i,p);}
});
test('learning forms are balanced and cards never share a grid row wrapper',()=>{
 for(const p of routes){let s=view(p);assert.equal((s.match(/<form\b/gi)||[]).length,(s.match(/<\/form>/gi)||[]).length,p);
  for(const m of s.matchAll(/class="([^"]+)"/g)){let cls=m[1].split(/\s+/);if(cls.includes('row'))assert.ok(!cls.some(x=>['card','card-body','content-card'].includes(x)),p+': '+m[0]);}
 }
});
test('all fourteen routes have their existing or newly reviewed bilingual Page Help',()=>{
 const guide=read('EduOS.App/wwwroot/js/platform/page-context-help.js').toLowerCase();
 for(const p of routes){const route='/'+p.toLowerCase(),k=route.endsWith('/index')?route:route+'/index';
 assert.ok(guide.includes("'"+k+"':")||guide.includes('"'+k+'":'),p);}
});
test('new LMS course code is server issued and retry-safe with authorized duplicate checks',()=>{
 const dto=read('EduOS.Core/DTOs/LMS/LmsDtos.cs'),srv=read('EduOS.Service/Services/LMS/LmsWorkflowService.cs'),ui=view('Learning/Index'),js=read('EduOS.App/wwwroot/js/learning/index.js');
 assert.match(dto,/public Guid\? ClientRequestId/);assert.match(ui,/<input id="learningCode" type="hidden"/);
 assert.match(srv,/request\.Code = "LMS-" \+ request\.ClientRequestId/);
 assert.match(srv,/x\.TenantId == tenant && x\.Code == code/);
 assert.match(srv,/await CanEditAsync\(matchingCode, token\)/);
 assert.match(js,/courseCreateRequestId:null/);assert.match(js,/state\.courseCreateFingerprint!==fingerprint/);
 assert.match(js,/body\.clientRequestId=state\.courseCreateRequestId/);
 assert.match(js,/Course saved, but the list did not refresh/);
});
test('important actions remain inside course, resource and report workspaces',()=>{
 const cases={'Learning/Index':['Course actions','id="learningSaveCourse"'],'TeacherStudio/Index':['id="studioFilter"','Teacher CPD'],'Learning/LiveClasses':['id="liveExport"','id="livePrint"'],'Learning/CourseContent':['id="contentPrint"','id="contentCsv"'],'Learning/Certificates':['id="lmsCertificateHelp"'],'Learning/CourseCompletions':['id="lmsCompletionHelp"'],'Learning/MySubmissions':['id="mySubmissionCsv"']};
 for(const [p,list] of Object.entries(cases))for(const term of list)assert.ok(view(p).includes(term),p+': missing '+term);
});
test('no orphan aria-labelledby after removing redundant title wrappers',()=>{
 for(const p of routes){const s=view(p);for(const m of s.matchAll(/aria-labelledby="([^"]+)"/g))assert.ok(s.includes('id="'+m[1]+'"'),p+': '+m[1]);}
});
test('all linked local JavaScript assets still exist',()=>{
 for(const p of routes)for(const m of view(p).matchAll(/<script[^>]*src=["']~\/([^"']+\.js)["']/g))
 assert.ok(fs.existsSync(path.join(root,'EduOS.App/wwwroot/'+m[1])),p+': '+m[1]);
});