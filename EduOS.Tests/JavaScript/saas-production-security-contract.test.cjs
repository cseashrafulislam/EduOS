'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const program = read('EduOS.App/Program.cs');
const policy = read('EduOS.App/Security/ProductionStoragePathPolicy.cs');
const headers = read('EduOS.App/Middleware/SecurityHeadersMiddleware.cs');
const limiter = read('EduOS.App/Extensions/RateLimiterExtensions.cs');

assert.match(program, /builder\.Environment\.IsProduction\(\)/);
assert.match(program, /ProductionStoragePathPolicy\.Validate\(/);
assert.match(program, /DataProtection:KeysPath/);
assert.match(program, /ProtectKeysWithDpapi\(protectToLocalMachine: true\)/);
assert.match(policy, /FileStorage:PrivateBasePath/);
assert.match(policy, /DataProtection:KeysPath/);
assert.match(policy, /Path\.GetRelativePath\(root, path\)/);
assert.match(headers, /Request\.Path\.StartsWithSegments\("\/api"\)/);
assert.match(headers, /CacheControl = "no-store"/);
assert.match(headers, /Pragma = "no-cache"/);
assert.match(limiter, /StatusCodes\.Status429TooManyRequests/);
assert.doesNotMatch(limiter, /Response\.Redirect\("\/Error\/429"\)/);
console.log('SaaS production storage, browser cache, rate-limit and Windows key protection contracts: PASS');
