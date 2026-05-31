// SkillKite landing — tiny, dependency-free.
// Handles language toggle + WhatsApp CTA wiring.

(function () {
  'use strict';

  // -------- Config --------
  // Replace BOT_PHONE with the Meta WhatsApp test number (or your prod number)
  // once you have it. International format, digits only, no '+' or spaces.
  // Example: '15550123456' for the US test number.
  var BOT_PHONE = ''; // <-- set me

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
  }

  // -------- WhatsApp CTA --------
  // Deep-link format: https://wa.me/<number>?text=<prefilled message>
  // On phones this opens WhatsApp directly. On desktop it opens web.whatsapp.com.
  var ctaText = encodeURIComponent('Hi SkillKite!');
  var waUrl = BOT_PHONE
    ? 'https://wa.me/' + BOT_PHONE + '?text=' + ctaText
    : '#bot-not-configured';

  Array.prototype.forEach.call(document.querySelectorAll('.cta-button'), function (a) {
    a.href = waUrl;
    a.target = '_blank';
    if (!BOT_PHONE) {
      a.addEventListener('click', function (e) {
        e.preventDefault();
        alert(
          'Bot phone number is not configured yet. ' +
          'Edit site/app.js and set BOT_PHONE to your WhatsApp Cloud API test number.'
        );
      });
    }
  });
})();
