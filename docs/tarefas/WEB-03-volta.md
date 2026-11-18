## WEB-03: relatório (Code web mais rápido e G58/G59 do Siemens)

Ramo `ajuste-WEB-03`, base `dev` em `0e3037a` (sem o WEB-02, que está no PR #191). Executor: Claude (chat), em sprint, por ordem do Igor (18/11): sugestões 1 (velocidade da primeira visita) e 2 (G58/G59 do Siemens).

**Commits:** S2 (G58/G59) · teste temporário do Cloudflare (pasta `brtest`, já removida no commit seguinte) · S1 (velocidade).

**Testes:**
- Aprovados: `dotnet test tests/CncStudio.Core.Tests` em Release, 1964 (2 novos em `MachineExecutorTests`). Nenhum teste existente mudou; os 8 G-codes do CAM não foram tocados.
- Medido na prévia real (Cloudflare), somando o que o navegador baixa de `_framework`: **7,76 MB → 5,53 MB (−29%)**. Antes: `casillas-code.pages.dev` (a `main` de hoje).
- Conferido num Chrome sem tela: a prévia abre e carrega o Code; local com o mesmo cabeçalho: simulação 3D, editor com `Ø ⚠ ✓ · →`, segunda visita pelo service worker.

### S1 Primeira visita mais leve
1. **.wasm em brotli máximo.** O Cloudflare comprimia na hora com brotli leve. Agora a publicação troca cada `.wasm` pelo `.wasm.br` que o .NET já gera, e o `_headers` manda `Content-Encoding: br` (testado na prévia: o Cloudflare entrega os bytes como estão e descomprime sozinho para quem não aceita brotli). Uma trava na publicação para se algum `.wasm` não tiver `.br`.
2. **Fontes só com o usado.** Saiu o pacote `Avalonia.Fonts.Inter` (6 pesos, todos os alfabetos). Entrou a Inter Regular, SemiBold e Bold só com latim, grego, pontuação e símbolos (mesmo nome `fonts:Inter`, o tema não muda). A DejaVu Sans Mono do editor ficou com os mesmos alfabetos. Conferido: todo caractere fora do ASCII usado no código continua coberto. Licença OFL da Inter ao lado das fontes.
3. **`Avalonia.DesignerSupport` cortado** (só serve à pré-visualização do editor visual).
- Testado e descartado: compilar o nativo com `-Oz` (ganho de 1%).
- Arquivo protegido (`service-worker.js`) e workflows: sem mudança. O README diz que o servidor precisa do cabeçalho.
- Risco: um servidor sem o cabeçalho `Content-Encoding: br` (ex.: abrir o artefato `CasillasCode-web` num servidor qualquer) não abre o app. O GitHub Pages do ramo `ci-web` não está no ar.

### S2 Siemens G58/G59
- No 840D, G58 e G59 não são zeros de peça 5 e 6: são deslocamento programável por eixo. G58 troca só os eixos escritos (`G58 X20`), G59 soma (`G59 Y3`); a ferramenta não se move, como no TRANS/ATRANS. A ajuda do comando no Siemens diz isso.
- Fanuc sem mudança (G58/G59 continuam zeros de peça 5 e 6). Vale para web e desktop.
- `docs/ESTADO.md` ainda cita o erro (linha 27); não mexi (só com ficha). A aula da Academy segue com o texto genérico para G58/G59.

### Teste do Igor (prévia `https://ajuste-web-03.casillas-code.pages.dev`)
1. Abrir no celular numa aba anônima (primeira visita) e sentir o tempo até o Code abrir.
2. Fechar e abrir de novo (segunda visita, pela cópia guardada).
3. Siemens: `TRANS X100` / `G58 X20` / `G0 X0 Y0` → a ferramenta vai a X20 Y0 (não ao zero G58 da tabela).

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01B7m9Dz8ujacpQtkJpoFA8D
