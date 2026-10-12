// Service worker do Casillas Code: faz o app abrir sem internet depois da primeira visita.
//  - Arquivos do _framework (nome com hash, nunca mudam): cópia guardada primeiro.
//  - Todo o resto (página, web.js, main.js, css, ícones): rede primeiro, para as atualizações
//    chegarem; sem internet, usa a cópia guardada.
// Ao publicar uma versão nova, os arquivos .wasm mudam de nome, então não há risco de misturar versões.

const CACHE = 'casillas-code-v2';

self.addEventListener('install', event => {
    event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(['./', './index.html'])).catch(() => {}));
    self.skipWaiting();
});

self.addEventListener('activate', event => {
    event.waitUntil((async () => {
        for (const key of await caches.keys()) {
            if (key !== CACHE) await caches.delete(key);
        }
        await self.clients.claim();
    })());
});

self.addEventListener('fetch', event => {
    const request = event.request;
    if (request.method !== 'GET' || new URL(request.url).origin !== self.location.origin) return;

    const url = new URL(request.url);
    const immutable = url.pathname.includes('/_framework/');

    event.respondWith((async () => {
        const cache = await caches.open(CACHE);
        const key = request.mode === 'navigate' ? './index.html' : request;

        if (immutable) {
            const cached = await cache.match(key);
            if (cached) return cached;
        }

        try {
            const response = await fetch(request);
            if (response.ok) cache.put(key, response.clone());
            return response;
        } catch {
            return (await cache.match(key)) || Response.error();
        }
    })());
});
