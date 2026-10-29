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
console.log('Production rate-limiter response and scoped policy contract: PASS');
