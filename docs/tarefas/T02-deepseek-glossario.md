# Tarefa T02: glossário de códigos G e M (Academy)

**Para:** DeepSeek (sem acesso ao repositório: o dono cola o resultado no GitHub)  **Arquivo de destino:** `docs/academy/glossario-fanuc-siemens.md`

## Objetivo
Um glossário em português dos códigos G e M mais usados em **Fanuc e Siemens**, para fresa e torno, que vira material de aula do Casillas Academy.

## Formato da resposta
Um único arquivo Markdown, assim:

```markdown
# Glossário de códigos G e M

## G00 — Avanço rápido
**Máquinas:** fresa e torno · **Fanuc:** G00 · **Siemens:** G0
O que faz: …(2–3 frases simples)
Exemplo:
    G00 X50. Z2.
Cuidado: …(1 frase sobre erro comum ou risco)
```

## Conteúdo
- Códigos G: 00, 01, 02, 03, 04, 17, 18, 19, 20, 21, 28, 40, 41, 42, 43, 49, 50, 54–59, 70, 71, 72, 73, 74, 75, 76, 80, 81, 82, 83, 84, 85, 90, 91, 94, 95, 96, 97, 98, 99. Quando o mesmo número significa coisas diferentes na fresa e no torno (ex.: G73, G74, G90), explicar os dois.
- Códigos M: 00, 01, 03, 04, 05, 06, 08, 09, 30, 98, 99.
- Siemens: equivalentes (G0, G1, CYCLE81…CYCLE84, CYCLE95, CYCLE97, MCALL, L/SPF, R-parâmetros) em seção própria.
- Linguagem de chão de fábrica, frases curtas, sem marketing.

## Pronto quando
- [ ] Todos os códigos da lista, no formato acima
- [ ] Nenhum código inventado; se houver variação entre modelos de comando, dizer "varia conforme o comando"

## Como o dono publica
GitHub → repositório → ramo `ext-deepseek-t02-glossario` → Add file → Create new file → `docs/academy/glossario-fanuc-siemens.md` → colar → Commit → abrir PR.
