import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));
const read = path => readFile(join(root, "public", path), "utf8");
const paths = ["index.html","bn/index.html","solutions/index.html","bn/solutions/index.html","modules/index.html","bn/modules/index.html","ai/index.html","bn/ai/index.html","demo/index.html"];
const htmlPages = await Promise.all(paths.map(read));
const js = await read("assets/site.js");
const cfg = await readFile(join(root,"site-config.js"),"utf8");
const assert = (condition,message) => { if(!condition) throw new Error(message); };
for (let i=0;i<paths.length;i++) {
  const html = htmlPages[i];
  assert(/<html lang="(en|bn)"/.test(html), paths[i] + ": missing language");
  assert(/<title>[^<]+<\/title>/.test(html), paths[i] + ": missing title");
  assert(html.includes('name="description"'), paths[i] + ": missing meta description");
  assert(html.includes('application/ld+json'), paths[i] + ": missing structured data");
  assert(html.includes("<h1"), paths[i] + ": missing main heading");
  if (i>0) assert(html.includes('rel="canonical"'), paths[i] + ": missing canonical");
  if (i===0) assert(html.includes("<!-- CANONICAL_AT_BUILD -->"), "Homepage canonical token missing");
  assert(!/\b(?:5|6)\s+(?:AI\s+)?agents\b/i.test(html), paths[i] + ": fixed AI count");
}
const home=htmlPages[0], demo=htmlPages[8];
const brand="EduOS — The Intelligent Education Operating System";
const bnBrand="EduOS — শিক্ষাপ্রতিষ্ঠানের ইন্টেলিজেন্ট অপারেটিং সিস্টেম";
const plainJs=js.replaceAll(/<[^>]+>/g,"");
assert(home.includes(brand) && plainJs.includes(brand),"Canonical English brand headline is missing");
assert(htmlPages[1].includes(bnBrand) && plainJs.includes(bnBrand),"Canonical Bangla brand headline is missing");
assert(home.includes("AI-Powered Education ERP for Schools, Colleges, Universities") &&
  home.includes('"alternateName":"The Intelligent Education Operating System"'),
  "Homepage SEO positioning/structured data is not aligned with brand");
for (const page of htmlPages) assert(page.includes("INTELLIGENT EDUCATION</small>"),
  "One marketing page still uses the old navigation brand strapline");
const ids=[...home.matchAll(/\bid="([^"]+)"/g)].map(x=>x[1]);
assert(ids.length===new Set(ids).size,"Duplicate homepage IDs");
for(const [,target] of home.matchAll(/href="#([^"]+)"/g)) assert(ids.includes(target), "Missing homepage anchor #"+target);
const translations=[...home.matchAll(/data-i18n(?:-html)?="([^"]+)"/g)].map(x=>x[1]);
const bn=js.match(/const bn = \{([\s\S]*?)\n  \};/),en=js.match(/const en = \{([\s\S]*?)\n  \};/);
assert(Boolean(bn&&en),"Translation dictionaries missing");
for (const key of translations) {
  const pattern = new RegExp("(?:^|[,\\s])"+key+":");
  assert(pattern.test(bn[1]) && pattern.test(en[1]),"Missing translation "+key);
}
assert(home.includes('/solutions/') && home.includes('/modules/') && home.includes('/ai/'),"Homepage discovery links missing");
assert(home.includes('id="contact"'),"Contact CTA missing");
assert(demo.includes("cseashrafulwork@gmail.com") && demo.includes("8801733293938"),"Demo contact links missing");
assert(cfg.includes("cseashrafulwork@gmail.com") && cfg.includes("8801733293938"),"Homepage public contacts missing");
assert(js.includes(".svg") && js.includes("/demo/"),"Fallback image or demo links missing");
assert(!js.includes("infoDialog.showModal()"),"Old inactive demo modal still in use");
for (const shot of ["dashboard","student-management","fees-billing","academics","reports"]) {
 const img=await read("assets/screenshots/"+shot+".svg");
 assert(img.includes("SAMPLE DATA"),"Image preview should identify sample data: "+shot);
}
for (const p of ["solutions/index.html","bn/solutions/index.html"]) {
 const doc=await read(p);
 assert(doc.includes("private-tutor") && doc.includes("coaching-center"),"Audience use case missing: "+p);
}
for (const p of ["modules/index.html","bn/modules/index.html"]) {
 const doc=await read(p);
 assert(doc.includes("Research") || doc.includes("গবেষণা"),"Module catalogue incomplete: "+p);
}
assert(!demo.includes("/bn/demo/"),"Demo page must not link to an unavailable Bengali route");
assert(demo.includes('/assets/demo.js') && htmlPages[1].includes('/assets/demo.js'),"Screenshot replacement script not loaded");
const screenshotUpdater = await read("assets/demo.js");
assert(screenshotUpdater.includes("probe.onload") && screenshotUpdater.includes(".webp"),"Real screenshot replacement is missing");
for (let i = 0; i < paths.length; i++) {
  const html = htmlPages[i];
  const unfinished = /under development|coming soon|not yet implemented|not production-ready|proposed local-first design|not ready for live use|not a production-verified feature|উন্নয়নাধীন|পরিকল্পিত|সম্পন্ন হয়নি|যাচাই করতে হবে/i;
  assert(!unfinished.test(html), paths[i] + ": visitor-visible unfinished-work banner or claim remains");
}
assert(!/coming soon|under development|উন্নয়নাধীন|শিগগিরই আসছে/i.test(js),
  "Translation strings must not revive unfinished-work copy after hydration");
console.log("Marketing content checks passed for "+paths.length+" routes and "+translations.length+" translated labels");
