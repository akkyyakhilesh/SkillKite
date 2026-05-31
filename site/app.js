// SkillKite landing — tiny, dependency-free.
// Handles language toggle + WhatsApp CTA wiring.

(function () {
  'use strict';

  // -------- Config --------
  // The bot's WhatsApp number — only whitelisted recipients can chat it
  // during the Meta sandbox phase. Used for the SECONDARY CTA shown to
  // users already on the early-access list.
  var BOT_PHONE = '15556472099'; // Meta WhatsApp test number

  // Founder's WhatsApp — used for the PRIMARY 'Get early access' CTA.
  // Visitors send a whitelist request here; founder adds their number to
  // Meta's allowed-recipients list, then they can chat the bot.
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
  // Two flavors:
  //   PRIMARY  ('cta-access')     → founder, asking to be whitelisted
  //   DIRECT   ('cta-bot-direct') → bot test number, for users already whitelisted

  var accessText = encodeURIComponent(
    "Hi Akkyy! I'd like to try SkillKite — please add me to the early-access list. 🪁"
  );
  var botText = encodeURIComponent('Hi SkillKite!');

  var accessUrl = 'https://wa.me/' + FOUNDER_PHONE + '?text=' + accessText;
  var botUrl    = 'https://wa.me/' + BOT_PHONE     + '?text=' + botText;

  // Wire the two flavors. Anything else (e.g. sample-PDF download with
  // .cta-secondary) is left untouched.
  Array.prototype.forEach.call(
    document.querySelectorAll('.cta-access'),
    function (a) { a.href = accessUrl; a.target = '_blank'; a.rel = 'noopener'; }
  );
  Array.prototype.forEach.call(
    document.querySelectorAll('.cta-bot-direct'),
    function (a) { a.href = botUrl; a.target = '_blank'; a.rel = 'noopener'; }
  );
})();
