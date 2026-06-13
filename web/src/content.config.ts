import { defineCollection, z } from 'astro:content';
import { glob } from 'astro/loaders';

// Generic catalog guides produced by tools/catalog-gen (generate-once, curate
// later). One JSON file per path, e.g. guides/12th-pcm.json. Loaded statically
// at build time — zero runtime cost, fully indexable.
const guides = defineCollection({
  loader: glob({ pattern: '**/*.json', base: './src/content/guides' }),
  schema: z.object({
    category: z.string(),
    stream: z.string(),
    streamLabel: z.string(),
    seoTitle: z.string(),
    seoDescription: z.string(),
    guide: z.object({
      heading: z.string(),
      greeting: z.string(),
      sections: z.array(
        z.object({
          title: z.string(),
          intro: z.string().nullable().optional(),
          options: z.array(
            z.object({
              name: z.string(),
              whatIsIt: z.string(),
              whoFor: z.string(),
              leadsTo: z.string(),
              keyExams: z.string(),
              timeCommitment: z.string(),
            })
          ),
        })
      ),
      closingMessage: z.string(),
      flowLabel: z.string(),
    }),
  }),
});

export const collections = { guides };
