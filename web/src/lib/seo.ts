// Structured-data (JSON-LD) builders for SEO. Pure functions returning plain
// objects — rendered via <script type="application/ld+json"> in the layouts.
// Google reads JSON-LD anywhere in the document (head or body).

export interface Crumb {
  name: string;
  /** Root-relative path, e.g. "/after-12th". Resolved to absolute via `site`. */
  path: string;
}

interface FaqOption {
  name: string;
  whatIsIt: string;
  whoFor: string;
  leadsTo: string;
  keyExams: string;
  timeCommitment: string;
}
interface FaqSection {
  options: FaqOption[];
}

const abs = (site: URL, path: string) => new URL(path, site).href;

/** Breadcrumb trail → schema.org BreadcrumbList. */
export function breadcrumbListSchema(site: URL, items: Crumb[]) {
  return {
    '@context': 'https://schema.org',
    '@type': 'BreadcrumbList',
    itemListElement: items.map((c, i) => ({
      '@type': 'ListItem',
      position: i + 1,
      name: c.name,
      item: abs(site, c.path),
    })),
  };
}

/** Guide option cards (the <details> blocks) → schema.org FAQPage.
 *  Each option becomes a Question; its details concatenate into the Answer. */
export function faqPageSchema(sections: FaqSection[]) {
  const mainEntity = [];
  for (const s of sections) {
    for (const o of s.options) {
      const answer = [o.whatIsIt, o.whoFor, o.leadsTo, o.keyExams, o.timeCommitment]
        .filter((p) => p && p.trim())
        .join(' ')
        .trim();
      if (!o.name?.trim() || !answer) continue;
      mainEntity.push({
        '@type': 'Question',
        name: o.name.trim(),
        acceptedAnswer: { '@type': 'Answer', text: answer },
      });
    }
  }
  return {
    '@context': 'https://schema.org',
    '@type': 'FAQPage',
    mainEntity,
  };
}

/** Brand entity for the homepage. */
export function organizationSchema(site: URL) {
  return {
    '@context': 'https://schema.org',
    '@type': 'Organization',
    name: 'SkillKite',
    url: abs(site, '/'),
    logo: abs(site, '/og-image.png'),
    description:
      'Free AI-powered career guidance for Indian students and working professionals, in Hindi and English.',
  };
}

/** Site entity for the homepage. */
export function webSiteSchema(site: URL) {
  return {
    '@context': 'https://schema.org',
    '@type': 'WebSite',
    name: 'SkillKite',
    url: abs(site, '/'),
    description:
      'Free AI career guidance for Indian students — stream selection after 10th, courses after 12th, and career roadmaps.',
  };
}
