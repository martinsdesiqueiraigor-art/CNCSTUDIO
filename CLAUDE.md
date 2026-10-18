# Casillas Code — guia para agentes (Claude)

Lido no início de cada sessão. Curto de propósito: detalhe vai em `docs/`, não aqui.

## Produto
- Editor e simulador CNC (Fanuc e Siemens, fresa e torno), estilo CIMCO Edit. Nome interno: `CncStudio`.
- Dono: Igor Martins (vende como MEI). Respostas **em português, curtas** (ele lê no celular).
- Plataforma **Casillas CNC**: Code (este repositório), CAM, Macro (geradores de programa com prévia 3D), Academy, Biblioteca CNC e Ferramentas (a calculadora Casillas). Referência: MacroMill CNC.
- Plano mestre, auditoria e missões ficam no Projeto "CNC Studio" do claude.ai.
- Ordem das fases: Fundação ✅ → Simulação ✅ (ajustes pendentes) → Macro e geradores → Academy piloto → CAM 2,5D → Avançado.

## Código
| Pasta | O que é |
|---|---|
| `src/CncStudio.Core` | Toda a lógica e geometria. **Sem pacotes NuGet**, warnings = erro. |
| `src/CncStudio.App` | Desktop Avalonia 11.3 (principal). 3D por OpenGL (`Graphics/GlSceneView`). |
| `src/CncStudio.Web` | WebAssembly (`net10.0-browser`, fora da .sln). 3D por three.js (`wwwroot/scene3d.js`). |
| `tests/CncStudio.Core.Tests` | xUnit, só do Core. |

Simulação 3D: `Core/Scene/SimulationScene` descreve o que desenhar; `Core/Simulation/SimulationFactory` monta a simulação igual nas duas cascas.

## Comandos
```bash
export PATH=~/.dotnet:$PATH            # sessão na nuvem: .NET 10 em ~/.dotnet (+ workload wasm-tools)
dotnet build -c Release                 # Core + App + testes
dotnet test tests/CncStudio.Core.Tests  # rodar sempre antes de commit
dotnet build src/CncStudio.Web -c Release
```
Desktop sem placa de vídeo (testes): `CASILLAS_SOFTWARE_GL=1 xvfb-run ...`.

## Convenções
- Textos da interface em pt-BR com acentos; comentários em português.
- Cálculo e geometria no Core, com teste. Uma fonte de verdade por dado.
- Commits pequenos, em português, autor `Claude <noreply@anthropic.com>`.
- Nada de prometer colisão garantida (simulação é 3 eixos, grade de alturas).

## Regras (sempre)
- **Nunca**: push/merge/rebase no `main`, `--force`, publicar web oficial ou instalador, mexer em Supabase, painel de ativação, termos ou página de vendas, apagar backup (`origin` local) ou `preview/`, commitar `bin/ obj/ publish/ preview/ screenshots/ .env` ou credenciais, adicionar NuGet ao Core.
- Trabalhar num ramo (`fase-N-...`, `ajuste-...`, `dev`). O merge no `main` é do dono, por PR.

## Como trabalhamos (economia)
1. **Uma conversa por tarefa ou parada.** Ao começar: ler este arquivo e `docs/ESTADO.md`. Não pedir resumo de conversas antigas.
2. **Prévia pelo GitHub**: todo push num ramo `fase-*`, `ajuste-*` ou `dev` roda `.github/workflows/previa.yml` → testes, web em `https://<ramo>.casillas-code.pages.dev` e o .exe de teste em Actions → Artifacts. Não montar prévia à mão nem publicar artefato com os arquivos do app.
3. **Conferir com testes, não com fotos.** No máximo uma foto por parada, só em entrega visual.
4. **Não ler arquivo grande inteiro**: buscar (grep) e ler só o trecho.
5. Ao terminar: atualizar `docs/ESTADO.md` (3–6 linhas) e relatar em ≤ 10 linhas, com uma caixa de perguntas se houver decisão.

## Delegação (GPT, DeepSeek)
Ver `AGENTS.md` e `docs/tarefas/`. Agentes externos trabalham em ramo próprio `ext-<agente>-<tarefa>` e entregam por PR; o Claude revisa e roda os testes antes do merge.
