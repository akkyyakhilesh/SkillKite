// @ts-check
import { defineConfig } from 'astro/config';

import sitemap from '@astrojs/sitemap';

// https://astro.build/config
export default defineConfig({
  // Canonical site URL — required for sitemap + absolute OG/canonical links.
  site: 'https://skillkite.in',
  integrations: [sitemap()],
});
