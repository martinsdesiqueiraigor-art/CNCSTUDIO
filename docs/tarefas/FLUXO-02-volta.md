## FLUXO-02: relatório
**Feito:** Fluxo Casillas Enxuto aplicado só nas instruções, com os textos aprovados pelo Igor em 14/11 (seções A, B e C de `FLUXO-ENXUTO-textos-finais-2026-11-14.md`). `CLAUDE.md` curto (7,6 KB → 3,0 KB), sem leitura obrigatória do `ESTADO.md`. Modelo novo de ficha (P/M/G por risco, pasta `C:\Projetos\_worktrees\<ID>`) e de relatório (5 itens). `/fechar` sem a suíte completa local; `/testar completo` só em G, CI vermelho ou a pedido. Feito pelo Claude Chat, direto no GitHub, porque o Igor estava fora do PC.
**Arquivos:** `CLAUDE.md`, `docs/tarefas/_MODELO.md`, `.github/PULL_REQUEST_TEMPLATE.md`, `.claude/commands/fechar.md`, `.claude/commands/testar.md`, `docs/ESTADO.md` (só a linha "Fichas:"), `docs/tarefas/FLUXO-02-volta.md` (este).
**Testes:**
- Aprovados: nenhum rodado (só documentos e instruções).
- Reprovados: nenhum.
- Não executados: build e suíte (não há código; o ramo `doc-*` não dispara a Prévia).
- Teste mestre (8 G-codes): não rodado (nada de código mudou).
- Testes alterados: nenhum.
**Limitações e pendências:**
- `/fechar` passo 4: tarefa P segue direto e M/G aguardam o "ok" (aplica a autorização P aprovada; não estava escrito palavra por palavra na ficha).
- Guarda: lendo `.claude/hooks/guard.py`, a raiz é `CLAUDE_PROJECT_DIR`; arquivo fora dela (ex.: `C:\Projetos\_worktrees\...` com a sessão aberta em `C:\Projetos\CncStudio`) não é conferido pela trava de arquivos protegidos. Comandos de shell continuam conferidos pelo texto. Não corrigido (fora da ficha).
- `docs/tarefas/MODELO.md` antigo mantido para GPT e DeepSeek.
**Como validar:**
1. Abrir este PR e conferir que só tem os 7 arquivos acima.
2. Abrir `CLAUDE.md` e comparar com a seção A dos textos aprovados.
3. Na próxima missão, abrir o Code e conferir que ele lê só o `CLAUDE.md` e a ficha.
