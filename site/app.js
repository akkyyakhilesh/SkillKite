// SkillKite landing — tiny, dependency-free.
// Handles language toggle + WhatsApp CTA wiring.

(function () {
  'use strict';

  // -------- Config --------
  // The bot's WhatsApp number — verified Meta WhatsApp Business sender.
  // No more sandbox allowlist — anyone with WhatsApp can chat directly.
  var BOT_PHONE = '916201226351'; // SkillKite — +91 62012 26351

  // Founder's WhatsApp — kept for "swap notes / DM me about the build"
  // sort of outreach. Not used for whitelisting any more (no longer needed).
  var FOUNDER_PHONE = '919492040362';

  // -------- Language toggle --------
  var html = document.documentElement;
  var saved = localStorage.getItem('lang');
  var detected = (navigator.language || 'en').toLowerCase().indexOf('hi') === 0 ? 'hi' : 'en';
  setLang(saved || detected);

  document.getElementById('lang-toggle').addEventListener('click', function () {
    setLang(html.getAttribute('data-lang') === 'en' ? 'hi' : 'en');
  });

  function setLang(lang) {
    html.setAttribute('data-lang', lang);
    html.setAttribute('lang', lang);
    localStorage.setItem('lang', lang);

    // Sample PDF download is language-aware — show the student the variant
    // matching their current reading preference.
    var samplePdf = document.getElementById('sample-pdf');
    if (samplePdf) {
      samplePdf.href = lang === 'hi' ? 'sample-roadmap-hi.pdf' : 'sample-roadmap-en.pdf';
    }
  }

  // -------- WhatsApp CTAs --------
  // SkillKite is out of Meta's sandbox now — anyone can message the bot directly
  // and get a reply. The old "DM founder → manual whitelist → then chat" path
  // is no longer needed. Both primary and secondary buttons go straight to the
  // bot's verified number.
  var botText = encodeURIComponent('Hi SkillKite!');
  var botUrl  = 'https://wa.me/' + BOT_PHONE + '?text=' + botText;

  Array.prototype.forEach.call(
    document.querySelectorAll('.cta-bot-direct'),
    function (a) { a.href = botUrl; a.target = '_blank'; a.rel = 'noopener'; }
  );
})();
