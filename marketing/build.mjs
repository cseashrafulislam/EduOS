import { cp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));
const output = join(root, "dist");
const declared = (process.env.SITE_URL || "https://myeduos.pages.dev").trim();
const url = new URL(declared);
if (url.protocol !== "https:" || url.username || url.password || url.search || url.hash || url.pathname !== "/") {
  throw new Error("SITE_URL must be one HTTPS origin without a path, query or credentials");
}
const siteUrl = url.origin;
const defaultUrl = "https://myeduos.pages.dev";
const routes = ["", "bn/", "solutions/", "bn/solutions/", "modules/", "bn/modules/", "ai/", "bn/ai/", "demo/"];

await rm(output, { recursive: true, force: true });
await mkdir(output, { recursive: true });
await cp(join(root, "public"), output, { recursive: true });
for (const route of routes) {
  const target = join(output, route, "index.html");
  let html = await readFile(target, "utf8");
  if (route === "") {
    const canonical = '<link rel="canonical" href="' + siteUrl + '/"/>' +
      '<meta property="og:url" content="' + siteUrl + '/"/>' +
      '<link rel="alternate" hreflang="en" href="' + siteUrl + '/"/>' +
      '<link rel="alternate" hreflang="bn-BD" href="' + siteUrl + '/bn/"/>' +
      '<link rel="alternate" hreflang="x-default" href="' + siteUrl + '/"/>';
    if (!html.includes("<!-- CANONICAL_AT_BUILD -->")) throw new Error("Homepage canonical token missing");
    html = html.replace("<!-- CANONICAL_AT_BUILD -->", canonical);
  } else if (siteUrl !== defaultUrl) {
    html = html.replaceAll(defaultUrl, siteUrl);
  }
  if (!html.includes('<link rel="canonical"')) throw new Error("Canonical missing on " + route);
  await writeFile(target, html);
}
await cp(join(root, "site-config.js"), join(output, "site-config.js"));
const robots = [
  "User-agent: *", "Allow: /", "",
  "User-agent: OAI-SearchBot", "Allow: /", "",
  "Sitemap: " + siteUrl + "/sitemap.xml", ""
].join("\n");
await writeFile(join(output, "robots.txt"), robots);
const entries = routes.map(route => "  <url><loc>" + siteUrl + "/" + route + "</loc></url>").join("\n");
await writeFile(join(output, "sitemap.xml"),
  '<?xml version="1.0" encoding="UTF-8"?>\n' +
  '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n' + entries + '\n</urlset>\n');
console.log("EduOS marketing built for " + siteUrl + " with " + routes.length + " indexable routes");
