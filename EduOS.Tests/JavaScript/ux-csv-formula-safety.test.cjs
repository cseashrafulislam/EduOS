const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');

for (const [name, relative] of [
    ['Student 360', 'EduOS.App/wwwroot/js/students/student-360.js'],
    ['My Work', 'EduOS.App/wwwroot/js/platform/central-work-center.js']
]) {
    test(name + ' CSV formula detection handles invisible prefixes', () => {
        const source = fs.readFileSync(path.join(root, relative), 'utf8');
        const match = source.match(/\/\^\[\\s\\uFEFF\\u200B-\\u200D\\u2060\]\*\[=\+@-\]\/u/);
        assert.ok(match, name + ' must use a real whitespace/Unicode regex, not double-escaped literals');
        const expression = vm.runInNewContext(match[0]);
        for (const value of ['=1+1', '  =1+1', '\t@SUM(1)', '\uFEFF+1', '\u200B-2', '\u2060=HYPERLINK(1)'])
            assert.equal(expression.test(value), true, 'unsafe prefix: ' + JSON.stringify(value));
        for (const value of ['normal', 'a=1', '123', 'Invoice-001'])
            assert.equal(expression.test(value), false, 'ordinary text: ' + value);
    });
}
