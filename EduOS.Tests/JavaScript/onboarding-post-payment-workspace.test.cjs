'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('Verified incomplete setup offers secure workspace entry without pretending onboarding is done', () => {
    const layout = read('EduOS.App/Views/Account/_OnboardingLayout.cshtml');
    const inline = layout.match(/<script>([\s\S]*?)<\/script>/)?.[1];
    assert.ok(inline, 'onboarding view needs its own workflow script');
    const js = inline.replace('const i18n = @Html.Raw(onboardingJson);',
        "const i18n = { steps: {}, culture: 'en-BD' };");
    new Function(js);
    assert.match(js, /!status\.hasWorkspaceAccess && !status\.canEnterWorkspace/);
    assert.match(js, /workspace\.dataset\.needsActivation/);
    assert.match(js, /\/api\/onboarding\/enter-workspace/);
    assert.match(js, /method: 'POST', credentials: 'same-origin'/);
    assert.match(js, /RequestVerificationToken: token/);
    assert.match(js, /window\.location\.assign\('\/Dashboard\/Index'\)/);
    assert.ok(!js.includes('innerHTML'), 'do not render API errors as HTML');
});

test('Identity and verified payment activate workspace without falsely completing setup', () => {
    const onboarding = read('EduOS.Service/Services/Tenants/OnboardingService.cs');
    const payment = read('EduOS.Service/Services/SaaS/SubscriptionPaymentService.cs');
    const policy = read('EduOS.Core/Common/OnboardingWorkspacePolicy.cs');
    assert.match(onboarding, /current == OnboardingStage\.Payment && target == OnboardingStage\.CampusSetup/);
    assert.match(onboarding, /OnboardingWorkspacePolicy\.CanActivate\(tenant, activeSubscription,/);
    assert.match(onboarding, /tenant\.State = TenantState\.Active/);
    assert.match(payment, /!subscription\.PreviousSubscriptionId\.HasValue/);
    assert.match(payment, /OnboardingWorkspacePolicy\.CanActivate\(tenant, subscription, now\)/);
    assert.match(payment, /tenant\.OnboardingStage = OnboardingStage\.CampusSetup/);
    assert.match(policy, /subscription\.TenantId == tenant\.Id/);
    assert.match(policy, /subscription\.State is SubscriptionState\.Active or SubscriptionState\.Trial/);
    assert.doesNotMatch(payment.slice(payment.indexOf('private async Task ActivatePaidSubscriptionAsync'),
        payment.indexOf('private async Task FailPendingGatewayAsync')), /OnboardingCompletedAt/);
});

test('Branding copy no longer claims a custom portal is required to open workspace', () => {
    for (const lang of ['SharedResource.resx', 'SharedResource.bn-BD.resx']) {
        const content = read('EduOS.App/Resources/Localization/' + lang);
        const line = content.match(/<data name="PortalAddressHelp"[^>]*><value>(.*?)<\/value><\/data>/)?.[1];
        assert.ok(line, lang + ' must contain portal guidance');
        assert.ok(!/required before|আবশ্যক/.test(line), lang + ' must not claim portal blocks workspace');
    }
});
