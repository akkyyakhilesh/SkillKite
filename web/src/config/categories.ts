// The four top-level guidance categories — matched 1:1 to the bot's flows.
// Single source of truth for the landing grid AND the catalog routes built
// in later steps. Option lists per category live in content collections.

export interface Category {
  /** URL slug + route, e.g. "after-12th" → /after-12th */
  slug: string;
  /** Bot flow this maps to (10th | 12th | career | upskill). */
  botFlow: '10th' | '12th' | 'career' | 'upskill';
  emoji: string;
  title: string;
  /** Hindi title for the bilingual treatment. */
  titleHi: string;
  /** One-line description shown on the card. */
  blurb: string;
  /** CSS gradient class key (k1..k4) from the Bold Friendly design. */
  card: 'k1' | 'k2' | 'k3' | 'k4';
  /** Card action label. */
  cta: string;
}

export const CATEGORIES: Category[] = [
  {
    slug: 'after-10th',
    botFlow: '10th',
    emoji: '📚',
    title: 'After 10th',
    titleHi: '10वीं के बाद',
    blurb: 'Stream selection — Science, Commerce, Arts, polytechnic & paramedical. See what each one opens up.',
    card: 'k1',
    cta: 'Explore streams →',
  },
  {
    slug: 'after-12th',
    botFlow: '12th',
    emoji: '🎯',
    title: 'After 12th',
    titleHi: '12वीं के बाद',
    blurb: 'Course & entrance-exam options matched to your stream — PCM, PCB, Commerce, Arts, BBA.',
    card: 'k2',
    cta: 'Explore courses →',
  },
  {
    slug: 'after-graduation',
    botFlow: 'career',
    emoji: '💼',
    title: 'After Graduation',
    titleHi: 'ग्रेजुएशन के बाद',
    blurb: 'A personalized, week-by-week career roadmap with free learning resources to get job-ready.',
    card: 'k3',
    cta: 'Build roadmap →',
  },
  {
    slug: 'skill-upgrade',
    botFlow: 'upskill',
    emoji: '🌱',
    title: 'Skill Upgrade',
    titleHi: 'स्किल अपग्रेड',
    blurb: 'Already working? Find your next rung — the skills and moves to level up.',
    card: 'k4',
    cta: 'Level up →',
  },
];

/** 12th-flow streams — order + short blurb for the stream-picker page.
 *  Labels mirror the generated guide entries (content/guides/12th-*.json). */
export interface Stream {
  slug: string;
  label: string;
  emoji: string;
  blurb: string;
}

export const TWELFTH_STREAMS: Stream[] = [
  { slug: 'pcm', label: 'Science (PCM)', emoji: '🔬', blurb: 'Physics, Chemistry, Maths — B.Tech, B.Sc, BCA, B.Arch, NDA & more.' },
  { slug: 'pcb', label: 'Science (PCB)', emoji: '🧬', blurb: 'Physics, Chemistry, Biology — MBBS, BDS, paramedical, B.Pharm & more.' },
  { slug: 'commerce', label: 'Commerce', emoji: '📊', blurb: 'CA, CS, CMA, B.Com, BBA, B.Com LLB, MBA track & more.' },
  { slug: 'arts', label: 'Arts / Humanities', emoji: '🎨', blurb: 'BA LLB, BA, BJMC, B.Des, government-exam prep & more.' },
];

/** 10th-flow interest areas — order + blurb for the after-10th picker.
 *  Slugs match the generated guide entries (content/guides/10th-*.json). */
export const TENTH_INTERESTS: Stream[] = [
  { slug: 'science', label: 'Science', emoji: '🔬', blurb: 'PCM, PCB, polytechnic, paramedical — what each science path opens up.' },
  { slug: 'commerce', label: 'Commerce', emoji: '📊', blurb: 'Accounts, CA foundation track, B.Com path & business careers.' },
  { slug: 'arts', label: 'Arts / Humanities', emoji: '🎨', blurb: 'Law, design, journalism, government jobs & creative paths.' },
  { slug: 'explore', label: "Not sure yet", emoji: '🤔', blurb: 'Still deciding? See every stream and option compared side by side.' },
];

/** Skill-Upgrade fields — order + blurb for the skill-upgrade picker.
 *  Slugs match the generated guide entries (content/guides/upskill-*.json). */
export const SKILL_FIELDS: Stream[] = [
  { slug: 'software_it', label: 'Software / IT', emoji: '💻', blurb: 'Dev, cloud, DevOps — skills to add and the next rung up.' },
  { slug: 'data_analytics', label: 'Data & Analytics', emoji: '📊', blurb: 'SQL, Power BI, data science — certifications and roles that pay.' },
  { slug: 'design_creative', label: 'Design & Creative', emoji: '🎨', blurb: 'UI/UX, motion, branding — skills, roles and freelance moves.' },
  { slug: 'content_marketing', label: 'Content & Marketing', emoji: '✍️', blurb: 'SEO, performance, social — the next step in marketing.' },
  { slug: 'banking_finance', label: 'Banking & Finance', emoji: '🏦', blurb: 'Certifications, analyst tracks and salary bands to target.' },
  { slug: 'healthcare', label: 'Healthcare', emoji: '🩺', blurb: 'Allied-health skills, roles and ways to level up.' },
  { slug: 'teaching_edu', label: 'Teaching & Education', emoji: '📖', blurb: 'Ed-tech, online tutoring and the next move in education.' },
  { slug: 'ops_support', label: 'Operations & Support', emoji: '🛠️', blurb: 'Process, tools and roles to grow into beyond support.' },
];

/** After-Graduation career groupings (CareerPath.Category enum → display).
 *  Order = display order on the browse page. */
export const CAREER_CATEGORIES: { key: string; label: string; emoji: string }[] = [
  { key: 'Tech', label: 'Tech & IT', emoji: '💻' },
  { key: 'Creative', label: 'Creative & Design', emoji: '🎨' },
  { key: 'Government', label: 'Government & Banking', emoji: '🏛️' },
  { key: 'Gig', label: 'Gig & Freelance', emoji: '🚀' },
  { key: 'Trades', label: 'Skilled Trades', emoji: '🔧' },
  { key: 'Emerging', label: 'Emerging Fields', emoji: '🌐' },
];

/** URL-safe slug from a career title. Titles are unique, so slugs are too. */
export const slugify = (s: string) =>
  s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');

/** ₹ formatting for salary bands (Indian digit grouping). */
export const inr = (n: number) => '₹' + n.toLocaleString('en-IN');

/** Production WhatsApp bot number (digits only, for wa.me links). */
export const WHATSAPP_NUMBER = '916201226351';
export const WHATSAPP_CTA_TEXT = 'Hi SkillKite!';
export const whatsappLink = (text: string = WHATSAPP_CTA_TEXT) =>
  `https://wa.me/${WHATSAPP_NUMBER}?text=${encodeURIComponent(text)}`;
