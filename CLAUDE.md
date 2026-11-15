# Casillas Code: guia do Claude Code

Curto de propósito: é lido em toda sessão. Detalhe técnico fica em `docs/`, lido só quando a ficha mandar.

## Produto
Editor, simulador e CAM CNC (Fanuc e Siemens, fresa e torno). Nome interno `CncStudio`. Dono: Igor.
Respostas em português, curtas. Repositório público: nada de preço, estratégia ou margem.

## Código
| Pasta | O que é |
|---|---|
| `src/CncStudio.Core` | Lógica e geometria. Sem NuGet, warning = erro. Tudo com teste. |
| `src/CncStudio.App` | Desktop Avalonia (principal). |
| `src/CncStudio.Web` | WebAssembly (fora da .sln). |
| `src/CncStudio.Step` | STEP com OpenCASCADE (só desktop). |
| `tests/CncStudio.Core.Tests` | xUnit do Core. Teste mestre: `CamJobGoldenTests` (8 G-codes). |
| `tests/CncStudio.Step.Tests` | Fora da .sln e do CI: rodar à mão quando mexer em `src/CncStudio.Step`. |

## Comandos
`dotnet build -c Release` · `/testar rapido` · `/testar completo` (só quando pedido) · `/fechar`

## Missão
- Uma missão por sessão, dada por uma ficha (formato `docs/tarefas/_MODELO.md`). Ler só o que a ficha cita.
- Ramo novo a partir de `origin/dev`, sempre em `C:\Projetos\_worktrees\<ID>` (`git worktree add ..\_worktrees\<ID> -b ajuste-<ID> origin/dev`). Nenhuma pasta de missão fora de `_worktrees`. Código: `ajuste-<ID>`. Só documento: `doc-<ID>`. Conferir o commit da ficha; se diferir, parar e avisar.
- Remover a pasta da missão (`git worktree remove`) só com ordem do Igor, depois do merge.
- **P:** executar direto. **M e G:** Plan Mode, mostrar o plano e parar até o OK do Igor.
- Ficha aprovada autoriza: editar dentro do escopo, testar, commit e push **só no ramo da tarefa**, PR em rascunho para o `dev`.
- Testes: `/testar rapido` durante a missão. A suíte completa roda na Prévia do PR. `/testar completo` só em tarefa G, CI vermelho ou quando a ficha ou o Igor pedir.
- Teste existente e os 8 G-codes de referência só mudam se a ficha autorizar expressamente; o relatório diz por quê.
- Entrega: relatório em `docs/tarefas/<ID>-volta.md` (formato do modelo do PR) e `gh pr create --draft --base dev --body-file` desse arquivo.
- `docs/ESTADO.md`: atualizar só se a ficha pedir.

## Nunca (só o Igor, com ordem específica)
Merge, push em `main`/`dev`, force push, apagar ramo ou arquivo, tag, publicar web ou instalador, rodar workflow à mão, Supabase, conta, licença, termos, página de vendas, NuGet no Core.
Arquivos protegidos (a guarda bloqueia; pedir liberação em `.claude/liberados.local`): `account.js`, `manifest.webmanifest`, `service-worker.js`, `.github/workflows/`, `supabase/`, `PlatformModules.cs`, `.claude/`.
Nunca `git remote update --prune` no espelho de backup nem `git worktree prune`.

## Regras de produto
Nada suposto (campo vazio fica ausente, sem padrão escondido). `.casillas-job` antigo abre e gera igual. Tela mínima 1280×720. Simulação é verificação, nunca garantia. Interface em pt-BR; comentários em português; nomes no código em inglês; commits em português (`feat:`, `fix:`, `test:`, `docs:`).
