// Service worker do Casillas Code: faz o app abrir sem internet depois da primeira visita.
//  - Página (index.html): tenta a rede primeiro (pega atualizações); sem rede, usa a cópia guardada.
//  - Demais arquivos (_framework com nome versionado, ícones, scripts): cópia guardada primeiro;
//    o que não estiver guardado vem da rede e é guardado.
// Ao publicar uma versão nova, os arquivos .wasm mudam de nome, então não há risco de misturar versões.

const CACHE = 'casillas-code-v1';

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

    if (request.mode === 'navigate') {
        event.respondWith((async () => {
            try {
                const response = await fetch(request);
                const cache = await caches.open(CACHE);
                cache.put('./index.html', response.clone());
                return response;
            } catch {
                return (await caches.match('./index.html')) || Response.error();
            }
        })());
        return;
    }

    event.respondWith((async () => {
        const cached = await caches.match(request);
        if (cached) return cached;
        const response = await fetch(request);
        if (response.ok) {
            const cache = await caches.open(CACHE);
            cache.put(request, response.clone());
        }
        return response;
    })());
});
