const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

test('Playwright Chromium workflows use a supported pinned Ubuntu runner', () => {
    const root = path.resolve(__dirname, '../../.github/workflows');
    const playwrightWorkflows = fs.readdirSync(root).filter(name => name.endsWith('.yml'))
        .map(name => ({name, content:fs.readFileSync(path.join(root,name),'utf8')}))
        .filter(file => file.content.includes('npx playwright install'));
    assert.ok(playwrightWorkflows.length >= 10, 'Expected the institutional browser QA workflows.');
    for (const workflow of playwrightWorkflows) {
        assert.doesNotMatch(workflow.content, /runs-on:\s*ubuntu-latest/,
            `${workflow.name} can resolve to unsupported Ubuntu 26.04`);
        assert.match(workflow.content, /runs-on:\s*ubuntu-24\.04/,
            `${workflow.name} must pin the Playwright-compatible runner`);
    }
});
