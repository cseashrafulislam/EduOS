const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'EduOS.App/Views/Account/_OnboardingLayout.cshtml'), 'utf8');
const css = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/css/site.css'), 'utf8');
const script = source.match(/<script>([\s\S]*?)<\/script>/)?.[1];

function createNode(tag) {
    const classes = new Set();
    return {
        tag, children: [], style: {}, attributes: {},
        classList: { add: x => classes.add(x), remove: x => classes.delete(x), contains: x => classes.has(x) },
        append(...children) { this.children.push(...children); },
        replaceChildren(...children) { this.children = children; },
        setAttribute(name, value) { this.attributes[name] = value; },
        removeAttribute(name) { delete this.attributes[name]; },
        addEventListener(name, callback) { this[`on${name}`] = callback; }
    };
}

function boot(stages, currentPath = '/Account/InstitutionProfile') {
    const elements = Object.fromEntries(['progressBar', 'progressText', 'progressTrack', 'completedSummary', 'stepsList', 'onboardingContinue']
        .map(id => [id, createNode(id)]));
    let domReady;
    const sandbox = {
        console: { warn: () => {} },
        window: { location: { pathname: currentPath } },
        document: {
            getElementById: id => elements[id],
            createElement: createNode,
            addEventListener: (name, handler) => { if (name === 'DOMContentLoaded') domReady = handler; }
        },
        fetch: async () => ({ ok: true, json: async () => ({ success: true, data: {
            progressPercentage: 30, completedStages: 3, totalStages: 10, stages
        } }) })
    };
    const i18n = { culture: 'en-BD', optional: 'Optional', loadFailed: 'Cannot load', retry: 'Retry',
        stepsCompletedTemplate: '{completed} of {total}',
        phases: [{ name: 'Identity & plan', description: 'Account' },
            { name: 'Academic essentials', description: 'School' },
            { name: 'Finish setup', description: 'Launch' }], steps: {} };
    const compiled = script.replace(/const i18n = @Html.Raw\(onboardingJson\);/,
        'const i18n = ' + JSON.stringify(i18n) + ';');
    assert.ok(compiled !== script, 'Razor must inject the existing culture-safe JSON catalogue');
    vm.runInNewContext(compiled, sandbox, { timeout: 1500 });
    return { ready: () => domReady(), elements, window: sandbox.window };
}

test('onboarding visually groups all 10 original server-defined stages into 3 accessible phases', async () => {
    const stages = Array.from({ length: 10 }, (_, i) => ({
        stage: i + 1, code: `step${i + 1}`, name: `Step ${i + 1}`,
        displayOrder: i + 1, isCompleted: i < 3, isCurrent: i === 5,
        isLocked: i >= 7, isSkippable: i === 9
    }));
    const { ready, elements, window } = boot(stages);
    await ready();
    assert.equal(elements.stepsList.children.length, 3);
    const [account, academic, finish] = elements.stepsList.children;
    assert.equal(account.open, false);
    assert.equal(academic.open, true);
    assert.equal(finish.open, false);
    assert.equal(account.children[1].children.length, 4);
    assert.equal(academic.children[1].children.length, 3);
    assert.equal(finish.children[1].children.length, 3);
    const active = academic.children[1].children[1];
    assert.equal(active.href, '/Account/AcademicSetup');
    assert.equal(active.attributes['aria-current'], 'step');
    assert.equal(finish.children[1].children[1].attributes['aria-disabled'], 'true');
    assert.equal(finish.children[1].children[1].href, undefined);
    assert.equal(elements.progressTrack.attributes['aria-valuenow'], '30');
    assert.equal(elements.onboardingContinue.href, '/Account/AcademicSetup');
    assert.equal(elements.onboardingContinue.classList.contains('d-none'), false);
    assert.equal(window.eduosOnboardingStageUrl(10), '/Account/GatewaySetup');
});

test('new setup grouping is bilingual, keyboard-friendly, and not a second onboarding API', () => {
    assert.match(source, /"পরিচয় ও পরিকল্পনা" : "Identity & plan"/);
    assert.match(source, /"শিক্ষার ভিত্তি" : "Academic essentials"/);
    assert.match(source, /"প্রকাশের প্রস্তুতি" : "Finish setup"/);
    assert.match(source, /step\.isLocked/);
    assert.match(source, /step\.isSkippable/);
    assert.match(source, /fetch\('\/api\/onboarding\/status'/);
    assert.match(source, /document\.createElement\('details'\)/);
    assert.match(css, /\.onboarding-phase-toggle:focus-visible/);
    assert.match(css, /\.onboarding-phase \.step-item:not\(\.current\) \.step-desc \{ display: none; \}/);
    assert.match(css, /#stepsList \{[\s\S]*?grid-auto-flow: row/);
    assert.doesNotMatch(script, /innerHTML|localStorage|eval\(/);
});

test('continue shortcut is hidden when already on the current unlocked stage', async () => {
    const stages = Array.from({ length: 10 }, (_, i) => ({
        stage: i + 1, name: `Step ${i + 1}`, displayOrder: i + 1,
        isCompleted: i < 5, isCurrent: i === 5, isLocked: i > 5
    }));
    const { ready, elements } = boot(stages, '/Account/AcademicSetup');
    await ready();
    assert.equal(elements.onboardingContinue.href, undefined);
    assert.equal(elements.onboardingContinue.classList.contains('d-none'), true);
});
