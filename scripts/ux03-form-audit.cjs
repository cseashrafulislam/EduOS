#!/usr/bin/env node
'use strict';
// Static coverage inventory, not a claim that a route has passed browser/role/tenant UAT.
// Usage: node scripts/ux03-form-audit.cjs [--json] [--summary]
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const views = path.join(root, 'EduOS.App/Views');
function walk(dir) {
    return fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
        const absolute = path.join(dir, entry.name);
        return entry.isDirectory() ? walk(absolute) : entry.name.endsWith('.cshtml') ? [absolute] : [];
    });
}
function inventory(file) {
    const content = fs.readFileSync(file, 'utf8');
    const htmlForms = [...content.matchAll(/<form\b([^>]*)>/gi)];
    const razorForms = [...content.matchAll(/(?:Html\.|<[^>]+\.)?BeginForm\s*\(/g)];
    if (!htmlForms.length && !razorForms.length) return null;
    const ids = htmlForms.map(match => /\bid\s*=\s*["']([^"']+)/i.exec(match[1])?.[1] || '(no literal id)');
    const scripts = [...content.matchAll(/<script\s+[^>]*src\s*=\s*["']([^"']+)/gi)]
        .map(match => match[1]).filter(value => !value.startsWith('http'));
    const routeHint = path.relative(views, file).replace(/\\/g, '/');
    return {
        view: routeHint, formCount: htmlForms.length + razorForms.length,
        formIds: ids.concat(Array(razorForms.length).fill('(Razor BeginForm)')),
        scripts,
        localFormGuard: /(?:src\s*=\s*["'][^"']*form-guard\.js|EduOSFormGuard)/i.test(content),
        hasSubmitMarkup: /type\s*=\s*["']submit["']|<button\b[^>]*>\s*(?:save|submit|create|update|confirm)/i.test(content),
        status: 'Needs route/role/tenant/browser acceptance evidence'
    };
}
const records = walk(views).map(inventory).filter(Boolean).sort((a, b) => a.view.localeCompare(b.view));
const totals = {
    totalViews: walk(views).length, formBearingViews: records.length,
    declaredForms: records.reduce((sum, x) => sum + x.formCount, 0),
    viewsWithLocalGuardReference: records.filter(x => x.localFormGuard).length,
    viewsWithoutLocalGuardReference: records.filter(x => !x.localFormGuard).length
};
if (process.argv.includes('--json')) {
    process.stdout.write(JSON.stringify({ totals, records }, null, 2) + '\n');
} else if (process.argv.includes('--summary')) {
    process.stdout.write(JSON.stringify(totals, null, 2) + '\n');
} else {
    const escape = value => String(value).replace(/\|/g, '\\|').replace(/\r?\n/g, ' ');
    process.stdout.write('# UX-03 Source-Form Inventory\n\n');
    process.stdout.write('Static evidence only. A view is not necessarily one route; partials and role-gated actions need manual mapping. ' +
        'A missing local guard reference is **not** proof of unprotected forms. No item is marked complete by this audit.\n\n');
    process.stdout.write('Views: ' + totals.totalViews + '; form-bearing views: ' + totals.formBearingViews +
        '; form declarations: ' + totals.declaredForms + '; local guard references: ' +
        totals.viewsWithLocalGuardReference + '.\n\n');
    process.stdout.write('| View / Route hint | Forms | Literal form IDs | Local guard reference | Review status |\n');
    process.stdout.write('| --- | ---: | --- | --- | --- |\n');
    records.forEach(x => process.stdout.write('| ' + escape(x.view) + ' | ' + x.formCount + ' | ' +
        escape(x.formIds.join(', ')) + ' | ' + (x.localFormGuard ? 'Yes' : 'No') +
        ' | Role/tenant/browser evidence required |\n'));
}
