import { getCollection } from 'astro:content';

export interface RelatedPost {
  title: string;
  description: string;
  path: string;
}

/** Blog posts that declared the given catalog path in their `relatedPages`
 *  frontmatter. Used to render "Related reading" blocks on catalog pages.
 *  Newest first. The post is the single source of truth — adding a path to a
 *  post's frontmatter makes it appear here automatically. */
export async function getRelatedPosts(path: string): Promise<RelatedPost[]> {
  const posts = await getCollection('blog');
  return posts
    .filter((p) => p.data.relatedPages?.includes(path))
    .sort((a, b) => new Date(b.data.date).getTime() - new Date(a.data.date).getTime())
    .map((p) => ({
      title: p.data.title,
      description: p.data.description,
      path: `/blog/${p.id}/`,
    }));
}
