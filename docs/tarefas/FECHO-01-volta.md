## FECHO-01: relatório (fecho da versão 1.1.0)

Ramo `ajuste-FECHO-01`, base `origin/dev` em `efb7e31`. Executor: Claude (chat), em sprint, por ordem do Igor (18/11). Plano: `claude/plano-FECHO-01-fecho-da-versao-2026-11-18.md` (Projeto).

**Commits:** F1 aba Modelo 3D · F2 versão 1.1.0 · F3 ESTADO.md.

**Testes:**
- Aprovados: `dotnet test tests/CncStudio.Core.Tests` em Release, 1952. Build Release do App sem erro (Linux).
- Teste mestre (8 G-codes): verde. Nenhum teste alterado (F1 só mexe na tela do App, sem teste de Core).
- Não executados: nada no .exe; `CncStudio.Step.Tests`.

### F1 Aba Modelo 3D sem furação e bolsão
- Saíram da aba: botões "Gerar furação" e "Gerar bolsão", os campos deles (broca, rotação, avanço, bicada, fresa, passos e os dois fundos; vários nasciam com valor suposto) e os geradores soltos.
- O menu da face continua (mesma regra): escolher Furação ou Bolsão agora só diz o que a face é e manda para o Trabalho (Preparação → Peça pronta → STEP da peça, clicar na face no 3D).
- Ficam: abrir STL/STEP, girar, varredura e desbaste 3D, seleção de faces e arestas com a descrição.

### F2 Versão 1.1.0
- `CncStudio.App.csproj` e `installer/CncStudio.iss` em 1.1.0 (o CI ainda pode passar a versão pela linha de comando do instalador).
- O app não mostra a versão em nenhuma tela hoje; ela aparece nas propriedades do `.exe`.
- Nota da versão: `docs/versoes/1.1.0.md` (o que entrou, arquivos, limites; diz que nada foi testado em máquina).

### F3 ESTADO.md
- Reescrito curto para a 1.1.0. O resumo antigo foi copiado para `docs/historico/ESTADO-ate-2026-11-14.md` (nada apagado).

### Depois do teste (só com ordem do Igor)
1. Merge deste PR no `dev`.
2. PR `dev` → `main`: publica a web na hora (nenhum arquivo da web mudou; só recompila o Core).
3. Conferir `casillas.com.br`.
4. Tag `v1.1.0` na `main`.
5. Fechar o PR #181.
6. Apagar os ramos `ajuste-TORNO-T0`, `ajuste-TORNO-T1-T7`, `ajuste-M3D`, `ajuste-FECHO-01`.

### Como validar no .exe
1. Aba Modelo 3D: abrir um STEP, clicar numa face plana de fundo → menu → Bolsão: aparece o texto mandando para o Trabalho; não há mais botões nem campos de furação e bolsão. Varredura e desbaste continuam gerando.
2. Propriedades do `.exe` (Detalhes): versão 1.1.0.
3. Um trabalho de fresa e um de torno antigos abrem e geram igual.
