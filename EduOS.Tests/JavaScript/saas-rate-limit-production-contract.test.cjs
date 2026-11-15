'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const limiter = read('EduOS.App/Extensions/RateLimiterExtensions.cs');
const program = read('EduOS.App/Program.cs');

assert.match(limiter, /StatusCodes\.Status429TooManyRequests/);
assert.match(limiter, /Headers\.CacheControl = "no-store"/);
assert.doesNotMatch(limiter, /Response\.Redirect\("\/Error\/429"\)/);
assert.match(limiter, /WriteAsJsonAsync/);
assert.match(program, /UseStatusCodePagesWithReExecute\("\/Error\/\{0\}"\)/);
assert.match(program, /UseRateLimiter\(\)/);
assert.match(limiter, /AddPolicy\("LoginPolicy"/);
assert.match(limiter, /AddPolicy\("ForgotPasswordPolicy"/);
assert.match(limiter, /AddPolicy\("PaymentCallbackPolicy"/);

// The rate limiter must see authenticated user identity before partitioning.
// Otherwise every school sharing one NAT IP shares a single rate-limit quota.
const authenticated = program.indexOf('app.UseAuthentication();');
const rateLimited = program.indexOf('app.UseRateLimiter();');
const authorized = program.indexOf('app.UseAuthorization();');
assert(authenticated > 0 && authenticated < rateLimited &&
    rateLimited < authorized, 'Rate limiting must follow authentication and precede authorization');
const apiPolicy = limiter.slice(limiter.indexOf('options.AddPolicy("ApiPolicy"'),
    limiter.indexOf('options.AddPolicy("LearnerIdentityPolicy"'));
assert.match(apiPolicy, /var authenticated = !string\.IsNullOrWhiteSpace\(userId\)/);
assert.match(apiPolicy, /FindFirstValue\("TenantId"\)/);
assert.match(apiPolicy, /var budget = authenticated && read \? 240 : 60/);
assert.match(apiPolicy, /authenticated \? \(read \? "read" : "write"\) : "all"/);
assert.match(apiPolicy, /var bucket = authenticated/);
assert.doesNotMatch(apiPolicy, /CreateOptions\([^)]*,\s*10\)/);

console.log('Production rate-limiter response and scoped policy contract: PASS');
