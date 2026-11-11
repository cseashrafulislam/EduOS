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
        // Multiple MVC controllers can share one file (e.g., Account/Pricing).
        // Exclude ControllerBase API classes: they must not shadow MVC pages.
        const declaration = /((?:\[[^\]]+\]\s*)*)public\s+(?:sealed\s+)?(?:partial\s+)?class\s+(\w+)Controller(?:\s*\([^)]*\))?\s*:\s*Controller\b/g;
        const classes = [...source.matchAll(declaration)];
        for (let i = 0; i < classes.length; i++) {
            const classMatch = classes[i];
            const name = classMatch[2];
            const classPolicy = policy(classMatch[1]);
            const actions = new Map();
            const scoped = source.slice(classMatch.index + classMatch[0].length,
                i + 1 < classes.length ? classes[i + 1].index : source.length);
            // Only MVC page actions with an explicit view/redirect return are included.
            // API methods, helper methods and guessed routes are excluded.
            const method = /((?:\[[^\]]+\]\s*)*)public\s+(?:async\s+)?(?:IActionResult|ActionResult|Task<IActionResult>|Task<ActionResult>)\s+(\w+)\s*\([^)]*\)\s*(?:=>|\{)/g;
            const methods = [...scoped.matchAll(method)];
            for (let j = 0; j < methods.length; j++) {
                const match = methods[j];
                const section = scoped.slice(match.index + match[0].length,
                    j + 1 < methods.length ? methods[j + 1].index : scoped.length);
                const helperName = /\breturn\s+(RenderReport)\s*\(/.exec(section)?.[1];
                const helper = helperName ? /private\s+IActionResult\s+RenderReport\s*\([^)]*\)\s*\{([\s\S]*)/.exec(scoped)?.[1] : null;
                const viewSource = /\bView\s*\(/.test(section) ? section : (helper || section);
                if (!/\b(?:View\s*\(|RedirectToAction\s*\(|Feature\s*\()/.test(viewSource)) continue;
                if (/\[HttpPost\]|\[HttpPut\]|\[HttpDelete\]/.test(match[1])) continue;
                const actionName = match[2];
                const explicitRoute = /\[HttpGet\("([^"]+)"\)\]/.exec(match[1])?.[1];
                actions.set(actionName.toLowerCase(), {
                    name: actionName, policy: combinePolicy(classPolicy, policy(match[1])),
                    route: explicitRoute || '/' + name + '/' + actionName,
                    viewHint: /\bView\s*\(\s*"([^"]+)"/.exec(viewSource)?.[1] || null,
                    source: relative(file)
                });
            }
            results.set(name.toLowerCase(), { file: relative(file), actions, classPolicy });
        }
    }
    return results;
}
function audit() {
    const controllers = discoverControllers();
    const explicitlyMappedViews = new Map();
    for (const controller of controllers.values()) {
        for (const action of controller.actions.values()) {
            if (!/^~?\/Views\/[^\s]+\.cshtml$/i.test(action.viewHint || '')) continue;
            const view = 'EduOS.App/' + action.viewHint.replace(/^~?\//, '');
            if (!explicitlyMappedViews.has(view)) explicitlyMappedViews.set(view, []);
            explicitlyMappedViews.get(view).push(action);
        }
    }
    const tests = walk(path.join(root, 'EduOS.Tests/JavaScript'), '.cjs');
    const viewFiles = walk(viewsRoot, '.cshtml').sort();
    const viewSources = new Map(viewFiles.map(file => [file, fs.readFileSync(file, 'utf8')]));
    const records = [];
    for (const file of viewFiles) {
        const rel = path.relative(viewsRoot, file).replace(/\\/g, '/');
        const [area, filename] = rel.split('/');
        const source = viewSources.get(file);
        const controller = controllers.get(area.toLowerCase());
        const action = filename?.replace(/\.cshtml$/, '') || '';
        const pageType = !area || !filename ? 'framework/import (not routable)' :
            action.startsWith('_') ? 'partial/component (not independently routable)' :
            /(?:Print|Document|Pdf)$/i.test(action) ? 'print/document (route may be specialized)' : 'view/action';
        const referencedBy = action.startsWith('_') ? viewFiles.filter(other => other !== file &&
            viewSources.get(other).includes(action)).map(relative) : [];
        const localMatches = controller ? [...controller.actions.values()].filter(item =>
            item.name.toLowerCase() === action.toLowerCase() ||
            item.viewHint?.toLowerCase() === action.toLowerCase()) : [];
        const matches = [...localMatches, ...(explicitlyMappedViews.get(relative(file)) || [])]
            .filter((item, index, all) => all.findIndex(other => other.route === item.route) === index);
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
                pageType, referencedBy: referencedBy.length ? referencedBy : [],
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
            views: viewFiles.length, rows: records.length,
            MVCActionRows: records.filter(item => !!item.route).length,
            unresolvedViewRows: records.filter(item => !item.route).length,
            partialViewRows: records.filter(item => item.pageType.startsWith('partial')).length,
            frameworkViewRows: records.filter(item => item.pageType.startsWith('framework')).length,
            unresolvedNonPartialRows: records.filter(item => !item.route &&
                !item.pageType.startsWith('partial') && !item.pageType.startsWith('framework')).length,
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
                ' | ' + row.sourceTests.length + ' | ' + clean(row.pageType) + ': pending live role/tenant/mobile/bilingual |\n');
        }
    }
}
module.exports = { audit, discoverControllers, policy, combinePolicy };
