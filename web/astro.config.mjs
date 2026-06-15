// @ts-check
import { defineConfig } from 'astro/config';

import sitemap from '@astrojs/sitemap';

// https://astro.build/config
export default defineConfig({
  // Canonical site URL — required for sitemap + absolute OG/canonical links.
  site: 'https://skillkite.in',
  integrations: [
    sitemap({
      // Keep noindex utility pages out of the sitemap (they carry a noindex
      // meta tag too — see Base.astro). 404 isn't emitted as a static URL.
      filter: (page) =>
        !page.endsWith('/terms/') &&
        !page.endsWith('/terms') &&
        !page.endsWith('/privacy/') &&
        !page.endsWith('/privacy'),
    }),
  ],
});
