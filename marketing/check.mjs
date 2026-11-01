import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const root = dirname(fileURLToPath(import.meta.url));
const [html, js] = await Promise.all([
  readFile(join(root, "public/index.html"), "utf8"),
  readFile(join(root, "public/assets/site.js"), "utf8")
]);

function assert(condition, message) { if (!condition) throw new Error(message); }
const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map((entry) => entry[1]);
assert(ids.length === new Set(ids).size, "Duplicate element IDs in landing page");
for (const [, anchor] of html.matchAll(/href="#([^"]+)"/g)) {
  assert(ids.includes(anchor), `Missing anchor target: #${anchor}`);
}

const translations = [...html.matchAll(/data-i18n(?:-html)?="([^"]+)"/g)].map((entry) => entry[1]);
const bn = js.match(/const bn = \{([\s\S]*?)\n  \};/);
const en = js.match(/const en = \{([\s\S]*?)\n  \};/);
assert(bn && en, "Both translation dictionaries must exist");
for (const key of translations) {
  const needle = new RegExp(`(?:^|[,{\\s])${key}:`);
  assert(needle.test(bn[1]), `Missing bn translation: ${key}`);
  assert(needle.test(en[1]), `Missing en translation: ${key}`);
}

const schema = html.match(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/);
assert(schema, "Missing JSON-LD structured data");
const entity = JSON.parse(schema[1]);
assert(entity.name === "EduOS", "Structured-data brand mismatch");
assert(html.includes("<!-- CANONICAL_AT_BUILD -->"), "Missing canonical build token");
assert(html.includes('name="description"'), "Missing SEO description");
assert(!/https?:\/\/example\.com|localhost:/.test(html), "Placeholder production URL in HTML");
console.log(`Marketing page checks passed (${ids.length} element IDs, ${new Set(translations).size} translated strings)`);
