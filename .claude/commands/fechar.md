---
description: Fecha a missão: confere, escreve o relatório e entrega o PR rascunho. Nunca merge.
---
1. `git status`. Liste arquivos não versionados ou temporários. NÃO apague: pergunte ao Igor o que fazer com cada um.
2. Escreva o relatório em `docs/tarefas/<ID>-volta.md`, no formato de `.github/PULL_REQUEST_TEMPLATE.md` (testes aprovados, reprovados e não executados; como validar em 3 a 5 passos). A suíte completa fica com a Prévia do PR; rode "/testar completo" só em tarefa G, CI vermelho ou se a ficha pedir.
3. Mostre a lista de arquivos alterados e uma mensagem de commit em português (feat:, fix:, test:, docs:).
4. Tarefa P: siga direto. Tarefa M ou G: AGUARDE o Igor escrever "ok" ou "pode enviar".
5. Commit e push APENAS no ramo da tarefa; `gh pr create --draft --base dev --body-file docs/tarefas/<ID>-volta.md`. Nunca merge. Nunca push em main ou dev.
6. O `docs/ESTADO.md` só muda se a ficha pedir.
