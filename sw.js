const CACHE_NAME = 'anrc-quiz-v1';
const ASSETS = [
  './',
  './index.html',
  './manifest.json',
  './Stratum2-Medium.otf',
  './Stratum2-Light.otf',
  './KievitOffc-Medi.ttf',
  './images/logo-dark.png',
  './images/Untitled-1.png',
  './images/designer.jpg',
  './images/social-media.jpg',
  './images/digital-content.jpg',
  './images/photographer.jpg',
  './images/branding.jpg',
  './images/talking.jpg',
  './images/journalist.jpg',
  './images/video-call.jpg',
  './images/writer.jpg',
  './images/marketing.jpg',
  './images/strategist.jpg',
  './images/analyst.jpg',
  './images/public-affairs.jpg',
  './images/planner.jpg',
  './images/clipboard.jpg',
  './images/hangout.jpg',
  './images/walkie-talkie.jpg',
  './images/business.jpg',
  './images/energy.jpg',
  './images/people-meeting.jpg',
  './images/seminar.jpg',
  './images/idea.jpg',
  './images/teaching.jpg',
  './images/specialist.jpg',
  './images/icon-192.png',
  './images/icon-512.png'
];

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then(cache => cache.addAll(ASSETS))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys().then(keys =>
      Promise.all(
        keys.filter(k => k !== CACHE_NAME).map(k => caches.delete(k))
      )
    ).then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', event => {
  event.respondWith(
    caches.match(event.request)
      .then(cached => cached || fetch(event.request))
  );
});
