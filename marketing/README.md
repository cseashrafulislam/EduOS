# EduOS Marketing Website (Cloudflare Pages)

This isolated static website lives in `marketing/`. It does not build or deploy the ASP.NET EduOS application, access SQL Server, or include demo credentials.

## Cloudflare Pages setup

1. Connect GitHub repository `cseashrafulislam/EduOS` to a **new Cloudflare Pages project** (project name: `eduos` if available; the `*.pages.dev` name is not reserved by this repository).
2. Select **production branch `Ashraful`** (or your selected release branch).
3. Set **Root directory:** `marketing`; **Build command:** `npm run build`; **Build output directory:** `dist`.
4. In **Settings → Build → Build watch paths**, set **Include paths** to `marketing/*` and clear excludes. This pattern matches all nested files. Do not leave the default `*` include.
5. Disable preview deployments for unrelated branches if desired.
6. After Cloudflare gives the actual Pages URL, set production environment variable `SITE_URL` to `https://your-actual-project.pages.dev` (with no path), then redeploy **once** to emit the canonical URL and sitemap. Never guess the hostname.
7. Verify the page, `/robots.txt`, `/sitemap.xml`, mobile layout, and crawlers via Google Search Console and Bing Webmaster Tools.

Cloudflare's path filter is the deploy guard, not a file in this repository. It normally skips commits outside `marketing/`. Exceptions documented by Cloudflare include empty pushes and pushes with 20+ commits or 3,000+ changed files; those may still trigger a build.

## Content that still needs real owner inputs

- **Live demo login URL:** edit `site-config.js` (`demoUrl`). Until configured, the Demo button explains that the link is pending.
- **Email, telephone and WhatsApp:** edit `site-config.js`. Unset contact channels are not displayed or submitted anywhere.
- **Actual full-page screenshots:** add optimized, privacy-safe images at the following paths, preserving the filenames:
  - `public/assets/screenshots/dashboard.webp`
  - `public/assets/screenshots/student-management.webp`
  - `public/assets/screenshots/fees-billing.webp`
  - `public/assets/screenshots/academics.webp`
  - `public/assets/screenshots/reports.webp`

The page explicitly labels unsupplied screenshot slots as pending. No fabricated product screenshot is presented as an actual capture. For every image, remove student names, phone numbers, personal email addresses and sensitive financial/tenant data before upload.

## Local checks

```bash
cd marketing
npm run check
npm run build
```

This website has no runtime dependencies, API credentials, lead database or tracking cookies. Its bilingual client-side switch is accessible; the initial server-rendered English copy remains readable to search engines. When launching Bengali-targeted SEO campaigns, consider separate pre-rendered `/bn/` and `/en/` URLs with `hreflang`, while retaining the single-page experience within each language.

## SEO honesty

Google and AI search indexing are not guaranteed. `SITE_URL` is required for a correct canonical URL and sitemap, and Cloudflare bot protection must permit the respective crawlers. No fake reviews, clients, trial lengths, pricing or unsupported release-readiness statements are included.
