// Service worker do Casillas Code: faz o app abrir sem internet depois da primeira visita.
//  - Arquivos do _framework com hash no nome (nunca mudam): cópia guardada primeiro.
//  - Todo o resto (página, web.js, main.js, css, ícones): rede primeiro, para as atualizações
//    chegarem; sem internet, usa a cópia guardada.
// Ao publicar uma versão nova, os arquivos .wasm mudam de nome, então não há risco de misturar versões.

const CACHE = 'casillas-code-v3';

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
    // Só os arquivos com hash no nome (ex.: CncStudio.Core.mapcqfpkrd.wasm) nunca mudam;
    // dotnet.js e avalonia.js não têm hash e mudam a cada versão
    const immutable = /\/_framework\/.+\.[a-z0-9]{8,}\.(wasm|js|dat)$/.test(url.pathname);

    event.respondWith((async () => {
        const cache = await caches.open(CACHE);
        // Só a abertura do app (raiz ou index.html) usa a página do app guardada; outras páginas
        // (termos, privacidade, conheca) são guardadas com o próprio endereço
        const appShell = request.mode === 'navigate' && /\/(index\.html)?$/.test(url.pathname);
        const key = appShell ? './index.html' : request;

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
