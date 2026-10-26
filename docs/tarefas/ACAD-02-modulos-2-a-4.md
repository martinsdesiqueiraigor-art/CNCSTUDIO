# Tarefa ACAD-02: Academy, módulos 2 a 4 (fresa Fanuc)

**Para:** GPT  **Ramo base:** `main`  **Ramo da entrega:** `ext-gpt-acad02-m<N>` (um PR por módulo, na ordem 2, 3, 4)

## Objetivo
Escrever os módulos 2, 3 e 4 da Academy no **mesmo formato do módulo 1** (aprovado pelo Igor), tudo como `rascunho`.

## Módulos (de `ACAD-01-academy-piloto.md`)
| # | Nível | Módulo | Aulas |
|---|---|---|---|
| 2 | Iniciante (fresa) | G00/G01/G02/G03, avanço e rotação, ler programa simples | 6 |
| 3 | Básico (fresa) | Ciclos G81/G83, subprograma, G41/G42, corretor | 6 |
| 4 | Avançado (fresa) | Macro: variáveis, IF/WHILE, G65 | 6 |

## Pode mexer em
- `content/academy/` (aulas e testes finais em JSON) e `samples/academy/` (programas `.nc`) — só isto.

## Siga o módulo 1 como modelo
- Aulas: `content/academy/acad-m<N>-a0<k>.json`; teste final: `acad-m<N>-teste-final.json`; programa: `samples/academy/acad-m<N>-a0<k>.nc`.
- Formato, quizzes (4 perguntas por aula, 4 alternativas, explicação, 1 pergunta lendo programa), regras do texto e do código G: **`ACAD-01-academy-piloto.md`** e `content/academy/README.md`.
- Abra primeiro um arquivo do módulo 1 e copie a estrutura dos campos; não invente campos.

## Pronto quando
- [ ] 6 aulas + teste final por módulo, com a mesma estrutura do módulo 1
- [ ] Todo `.nc` roda no editor/3D sem erro (o Claude confere); comentários sem acento, ponto decimal, aviso "Simule antes de rodar na máquina"
- [ ] Tudo `status: rascunho`; fato incerto como **A definir**
- [ ] PR aberto, sem merge

## Fora do escopo
Módulos 1 e 5–7, vídeos, Siemens completo, código do app, tela da Academy.

## Volta (no PR)
Seguir `docs/tarefas/MODELO.md`, seção **Volta**.
