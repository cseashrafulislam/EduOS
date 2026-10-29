const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "../..");
const layout = fs.readFileSync(path.join(root, "EduOS.App/Views/Shared/_Layout.cshtml"), "utf8");
const handler = fs.readFileSync(path.join(root, "EduOS.App/Authorization/ModuleAccessHandler.cs"), "utf8");

function nearestConditionalBefore(href) {
    const link = layout.indexOf('href="' + href + '"');
    assert.ok(link > 0, href + " must remain a navigable route outside pilot");
    const guard = layout.lastIndexOf("@if (", link);
    assert.ok(guard >= 0, href + " must have a conditional");
    return layout.slice(guard, link);
}

test("V1 pilot hides LMS and Library entry points in shared shell", () => {
    assert.match(nearestConditionalBefore("/Learning/Index"), /^@if \(!pilotCoreOnly && /);
    assert.match(nearestConditionalBefore("/Library/Index"), /^@if \(!pilotCoreOnly\)/);
    assert.match(layout, /href="\/Library\/Circulation"/);
    assert.match(layout, /href="\/Learning\/CourseCompletions"/);
});

test("server pilot gate covers all V2 modules and V3 without moving V1 HR", () => {
    const excluded = handler.match(/PilotExcludedModules\s*=\s*new\(StringComparer\.OrdinalIgnoreCase\)\s*\{([^}]+)\}/);
    assert.ok(excluded, "explicit pilot exclusion set must exist");
    for (const code of ["LMS", "LIBRARY", "HOSTEL", "TRANSPORT", "INVENTORY"]) {
        assert.ok(excluded[1].includes('"' + code + '"'), code + " must be excluded");
    }
    assert.ok(!excluded[1].includes('"HR"'), "V1 HR must remain governed by normal entitlement");
    assert.match(handler, /IsCurrentTenantModuleAvailableAsync\(requirement\.ModuleCode\)/);
});
