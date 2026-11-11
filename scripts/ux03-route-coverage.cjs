#!/usr/bin/env node
'use strict';
// Source coverage register only. No static parser can certify tenant/role authorization
// or replace a two-institute authenticated browser/SQL acceptance run.
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const viewsRoot = path.join(root, 'EduOS.App/Views');
const controllersRoot = path.join(root, 'EduOS.App/Controllers');
const help = require('../EduOS.App/wwwroot/js/platform/page-context-help.js');

function walk(folder, extension) {
    return fs.readdirSync(folder, { withFileTypes: true }).flatMap(entry => {
        const name = path.join(folder, entry.name);
        return entry.isDirectory() ? walk(name, extension) : name.endsWith(extension) ? [name] : [];
    });
}
function relative(file) { return path.relative(root, file).replace(/\\/g, '/'); }
function attribute(text, name) {
    return [...text.matchAll(new RegExp('\\[' + name + '(?:\\(([^\\]]*)\\))?\\]', 'g'))]
        .map(match => match[1] || '');
}
function policy(attrs) {
    const auth = attribute(attrs, 'Authorize');
    const roles = auth.flatMap(value => [...value.matchAll(/Roles\s*=\s*"([^"]+)"/g)]
        .flatMap(match => match[1].split(',').map(role => role.trim())));
    const modules = attribute(attrs, 'RequireModule').flatMap(value =>
        [...value.matchAll(/"([^"]+)"/g)].map(match => match[1]));
    return { authenticated: auth.length > 0, roles: [...new Set(roles)], modules: [...new Set(modules)],
        allowAnonymous: attribute(attrs, 'AllowAnonymous').length > 0 };
}
function combinePolicy(controller, action) {
    // Method-level [Authorize(Roles=...)] is an additional ASP.NET policy,
    // not a substitute for the controller-level policy.
    return {
        authenticated: (controller.authenticated || action.authenticated) && !action.allowAnonymous,
        controllerRoles: controller.roles, actionRoles: action.roles,
        modules: [...new Set([...controller.modules, ...action.modules])],
        allowAnonymous: action.allowAnonymous || (controller.allowAnonymous && !action.authenticated),
        derivedFrom: 'source attributes; dynamic policies and effective role resolution NOT verified'
    };
}
function discoverControllers() {
    const results = new Map();
    for (const file of walk(controllersRoot, '.cs').filter(value => value.endsWith('Controller.cs'))) {
        const source = fs.readFileSync(file, 'utf8');
        const classMatch = /((?:\[[^\]]+\]\s*)*)public\s+(?:sealed\s+)?(?:partial\s+)?class\s+(\w+)Controller\b/.exec(source);
        if (!classMatch) continue;
        const name = classMatch[2];
        const classPolicy = policy(classMatch[1]);
        const actions = new Map();
        // Only MVC page actions with an explicit view/redirect return are included.
        // API methods, helper methods and guessed routes are excluded.
        const method = /((?:\[[^\]]+\]\s*)*)public\s+(?:async\s+)?(?:IActionResult|ActionResult|Task<IActionResult>|Task<ActionResult>)\s+(\w+)\s*\([^)]*\)\s*(?:=>|\{)/g;
        for (const match of source.matchAll(method)) {
            const section = source.slice(match.index + match[0].length,
                Math.min(source.length, match.index + match[0].length + 750));
            if (!/\b(?:View\s*\(|RedirectToAction\s*\(|Feature\s*\()/.test(section)) continue;
            if (/\[HttpPost\]|\[HttpPut\]|\[HttpDelete\]/.test(match[1])) continue;
            const actionName = match[2];
            actions.set(actionName.toLowerCase(), {
                name: actionName, policy: combinePolicy(classPolicy, policy(match[1])),
                route: '/' + name + '/' + actionName,
                viewHint: /\bView\s*\(\s*"([^"]+)"/.exec(section)?.[1] || null,
                source: relative(file)
            });
        }
        results.set(name.toLowerCase(), { file: relative(file), actions, classPolicy });
    }
    return results;
}
function audit() {
    const controllers = discoverControllers();
    const tests = walk(path.join(root, 'EduOS.Tests/JavaScript'), '.cjs');
    const records = [];
    for (const file of walk(viewsRoot, '.cshtml').sort()) {
        const rel = path.relative(viewsRoot, file).replace(/\\/g, '/');
        const [area, filename] = rel.split('/');
        const source = fs.readFileSync(file, 'utf8');
        const controller = controllers.get(area.toLowerCase());
        const action = filename?.replace(/\.cshtml$/, '') || '';
        const matches = controller ? [...controller.actions.values()].filter(item =>
            item.name.toLowerCase() === action.toLowerCase() ||
            item.viewHint?.toLowerCase() === action.toLowerCase()) : [];
        const formCount = [...source.matchAll(/<form\b/gi)].length +
            [...source.matchAll(/\bBeginForm\s*\(/g)].length;
        const referencedScripts = [...source.matchAll(/src\s*=\s*["'](?:~\/)?(?:\/?)(js\/[^"']+\.js)/gi)]
            .map(match => 'EduOS.App/wwwroot/' + match[1]);
        const candidateTests = tests.filter(test => {
            const text = fs.readFileSync(test, 'utf8');
            return text.includes(relative(file)) || referencedScripts.some(script => text.includes(script));
        }).map(relative);
        const rows = matches.length ? matches : [{ name: action, route: null, policy: null, source: controller?.file || null }];
        for (const route of rows) {
            const helpGuide = route.route ? help.guideFor(route.route, 'bn-BD') : null;
            const englishGuide = route.route ? help.guideFor(route.route, 'en-BD') : null;
            records.push({
                area, route: route.route, routeEvidence: route.route ? 'MVC action source' : 'unresolved: partial/view/action mapping',
                view: relative(file), controller: route.source, action: route.name,
                role: route.policy || { derivedFrom: 'unresolved; verify action and effective policy' },
                forms: formCount,
                dependentControlsHint: /\b(?:change|input)\b|onchange|dependent|[Ll]oad.*[Oo]ptions/.test(source),
                localFormGuard: /form-guard\.js|EduOSFormGuard/.test(source),
                localizedRazor: /@T\[|CultureInfo\.CurrentUICulture|IStringLocalizer/.test(source),
                helpEnReviewed: !!englishGuide, helpBnReviewed: !!helpGuide,
                scripts: referencedScripts, sourceTests: candidateTests,
                mobile: 'NOT VERIFIED: 360/390/768/1280 authenticated viewport',
                roleTenant: 'NOT VERIFIED: effective role, entitlement, campus and two-tenant IDOR',
                bilingual: 'NOT VERIFIED: rendered form/error/print/keyboard for en-BD and bn-BD',
                status: 'SOURCE INVENTORY ONLY — acceptance pending'
            });
        }
    }
    return { generatedFrom: 'current working tree (run git rev-parse HEAD separately)',
        note: 'Not a list of passed routes; fallback rows can be partials and source-only hints.',
        totals: {
            views: walk(viewsRoot, '.cshtml').length, rows: records.length,
            MVCActionRows: records.filter(item => !!item.route).length,
            unresolvedViewRows: records.filter(item => !item.route).length,
            formBearingRows: records.filter(item => item.forms > 0).length,
            reviewedBilingualManualRows: records.filter(item => item.helpEnReviewed && item.helpBnReviewed).length,
            sourceTestReferencedRows: records.filter(item => item.sourceTests.length > 0).length,
            authenticatedAcceptancePassed: 0
        }, records };
}
if (require.main === module) {
    const output = audit();
    if (process.argv.includes('--summary')) process.stdout.write(JSON.stringify(output.totals, null, 2) + '\n');
    else if (process.argv.includes('--json')) process.stdout.write(JSON.stringify(output, null, 2) + '\n');
    else {
        const clean = value => String(value).replace(/\|/g, '\\|').replace(/\r?\n/g, ' ');
        process.stdout.write('# UX-03 Page/Role/Locale/Device Evidence Register\n\n');
        process.stdout.write('SOURCE INVENTORY ONLY. Missing/unknown policies are not approvals; no route is browser-accepted by this script.\n\n');
        process.stdout.write('| Route (source) | View | Auth attributes (not effective permissions) | Forms | en/bn manual | Tests linked | Acceptance |\n');
        process.stdout.write('| --- | --- | --- | ---: | --- | ---: | --- |\n');
        for (const row of output.records) {
            const roles = row.role.actionRoles?.length ? row.role.actionRoles.join(',') :
                (row.role.controllerRoles?.join(',') || 'Unresolved');
            process.stdout.write('| ' + clean(row.route || 'Unresolved') + ' | ' + clean(row.view) +
                ' | ' + clean(roles) + ' | ' + row.forms + ' | ' +
                (row.helpEnReviewed && row.helpBnReviewed ? 'Reviewed' : 'Missing/unresolved') +
                ' | ' + row.sourceTests.length + ' | Pending live role/tenant/mobile/bilingual |\n');
        }
    }
}
module.exports = { audit, discoverControllers, policy, combinePolicy };
