# Casillas Code — guia para agentes (Claude)

Lido no início de cada sessão. Curto de propósito: detalhe vai em `docs/`, não aqui.

## Produto
- Editor e simulador CNC (Fanuc e Siemens, fresa e torno), estilo CIMCO Edit. Nome interno: `CncStudio`.
- Dono: Igor Martins (vende como MEI). Respostas **em português, curtas** (ele lê no celular).
- Plataforma **Casillas CNC**: Code (este repositório), CAM, Macro (geradores de programa com prévia 3D), Academy, Biblioteca CNC e Ferramentas (a calculadora Casillas). Referência: MacroMill CNC.
- Plano mestre, missões, regras e modelo de handoff ficam no repositório **privado** `martinsdesiqueiraigor-art/casillas-plataforma` (cópias no Projeto "Casillas CNC — Plataforma" do claude.ai). Preços e estratégia nunca neste repositório (é público). O Projeto no claude.ai é a fonte de verdade; o repositório privado é cópia de segurança. O Code NÃO edita o `missoes.md` nem o Painel: ao terminar, entrega a Volta (formato em `docs/tarefas/MODELO.md`) ao Igor, e o Claude da nuvem atualiza.
- Ordem das fases: Fundação → Simulação → Macro e geradores → Academy piloto → CAM 2,5D → Avançado. O status de cada etapa fica no `missoes.md` (Projeto no claude.ai); o estado técnico do código, em `docs/ESTADO.md`. Não repetir status aqui.

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
- Trabalhar num ramo (`fase-N-...`, `ajuste-...`). **Fluxo:** ramo de tarefa → PR para o `dev` → o Igor testa no `dev` → o `dev` entra na `main` por PR do Igor. O `dev` recebe só PR, nunca trabalho direto.
- A `main` tem regra no GitHub (`proteger-main`): sem exclusão, sem force push, exige `build` e `testes` verdes. Ela publica o site; o merge é do dono, por PR.
- Nunca rodar `git remote update --prune` no espelho `C:\Projetos\backup\CNCSTUDIO.git`, nem `git worktree prune` por causa do aviso "prunable" (as pastas de trabalho existem; o aviso é falso).

## Como trabalhamos (economia)
1. **Uma conversa por tarefa ou parada.** Ao começar: `git fetch origin` e conferir o commit (`git log -1 origin/main`); se a mensagem de abertura trouxer um commit esperado e ele diferir, avisar o Igor antes de trabalhar. Ler este arquivo e o bloco "Estado atual" de `docs/ESTADO.md` (o histórico em `docs/historico/` só se a tarefa pedir). Não pedir resumo de conversas antigas. Trocar de ramo só com ordem do Igor.
2. **Prévia pelo GitHub**: todo push num ramo `fase-*`, `ajuste-*` ou `dev` roda `.github/workflows/previa.yml` → testes, web em `https://<ramo>.casillas-code.pages.dev` e o .exe de teste em Actions → Artifacts. Não montar prévia à mão nem publicar artefato com os arquivos do app.
3. **Conferir com testes, não com fotos.** No máximo uma foto por parada, só em entrega visual.
4. **Não ler arquivo grande inteiro**: buscar (grep) e ler só o trecho.
5. Ao terminar: atualizar o bloco "Estado atual" de `docs/ESTADO.md` (3–6 linhas; manter o arquivo abaixo de 90 linhas, o resto vai para `docs/historico/`), sem escrever contagem de testes (vale o resultado do CI), e relatar em ≤ 10 linhas, com uma caixa de perguntas se houver decisão.

## Roteamento (o que ler, por tarefa)
Sempre: este arquivo e o bloco "Estado atual" de `docs/ESTADO.md`. Além disso:
| Tarefa | Ler também |
|---|---|
| Academy (aulas, JSON v2) | `docs/tarefas/ACAD-01-academy-piloto.md`, `docs/tarefas/ACAD-02-modulos-2-a-4.md` |
| CAM, Trabalho, Assist | `docs/tarefas/CAM-01-plano.md`; o resto do CAM está em `docs/historico/` (buscar por grep, não ler inteiro) |
| STEP, Modelo 3D, seleção 3D | o STEP está na main (`src/CncStudio.Step`); a seleção 3D segue na seção "Em ramo" de `docs/ESTADO.md` |
| Desktop da plataforma | `docs/tarefas/PLAT-06-desktop-plataforma.md` |
| Macro, Otimizador | `docs/tarefas/MACRO-01-geradores.md`, `docs/tarefas/OTIM-01-otimizador.md` |
| Web (tela inicial, módulos) | `src/CncStudio.Web/wwwroot/platform-ui.js`. Mínimo de tela do desktop: 1280×720 lógicos |
| Conta, licença, Supabase, termos, vendas | **Parar e perguntar ao Igor** |

## Delegação (GPT, DeepSeek)
Ver `AGENTS.md` e `docs/tarefas/`. Agentes externos trabalham em ramo próprio `ext-<agente>-<tarefa>` e entregam por PR; o Claude revisa e roda os testes antes do merge. Tarefas com ID por produto (`CODE-<nº>`, `MACRO-`, `ACAD-`…). Ao fechar, a resposta segue a seção **Volta** de `docs/tarefas/MODELO.md`.
- Conteúdo técnico (exemplos, glossário, Academy) entra como `rascunho`; só o Igor marca `revisado`.
