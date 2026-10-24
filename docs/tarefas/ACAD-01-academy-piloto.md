# Tarefa ACAD-01: Academy piloto (7 módulos, níveis, quizzes)

**Para:** GPT (módulos 1–4) e DeepSeek (módulos 5–7). Começa só pelo módulo 1 (teste de formato); os demais saem depois da aprovação do Igor. Claude: estrutura técnica. **Ramo base:** `main`  **Ramo da entrega:** `ext-<agente>-acad01-<modulo>`

## Objetivo
Conteúdo do piloto da Academy em português, em pacotes de dados (sem código), todo como `rascunho`: 7 módulos, 4 a 6 aulas cada, com quiz por aula e teste final por módulo.

## Estrutura (caminhos e níveis)
| # | Caminho | Nível | Módulo | Aulas |
|---|---|---|---|---|
| 1 | Comum | Introdução | Introdução ao CNC: máquinas, eixos, zeros, segurança, estrutura do programa | 5 |
| 2 | Fresa | Iniciante | G00/G01/G02/G03, avanço e rotação, ler programa simples | 6 |
| 3 | Fresa | Básico | Ciclos G81/G83, subprograma, G41/G42, corretor | 6 |
| 4 | Fresa | Avançado | Macro: variáveis, IF/WHILE, G65 | 6 |
| 5 | Fresa | Especialista | Apalpador, ciclos próprios G65, tempo de ciclo, noções de 5 eixos | 5 |
| 6 | Torno | Iniciante | Eixos X/Z (diâmetro), G00–G03, G96/G97, ler programa | 6 |
| 7 | Torno | Intermediário | G71, G70, P/Q/U/W/D/F, erros comuns | 6 |
Níveis sem módulo no piloto aparecem como "em breve".

## Regras
- Fanuc primeiro; Siemens só como nota de comparação, marcada `rascunho`.
- Tudo `rascunho` (selo visível). Só o Igor marca `revisado`.
- Fato técnico incerto: escrever **A definir**, nunca inventar.
- Sem manual de fabricante copiado; texto próprio.
- Código G dos exemplos: comentários sem acento, ponto decimal e o aviso "Simule antes de rodar na máquina".
- Texto curto para o celular (aula de 300 a 500 palavras).

## Modelo de aula (um arquivo por aula)
1. `id`, `titulo`, `nivel`, `caminho`, `status: rascunho`
2. Objetivo (1 frase) e pré-requisito (id da aula anterior)
3. Texto (300–500 palavras), com 1 erro comum do chão de fábrica
4. Programa de exemplo comentado (`samples/academy/<id>.nc`), que roda no editor e no 3D
5. Exercício: "corrija este programa" ou "complete a linha", com gabarito
6. Quiz da aula (veja abaixo)

## Quizzes (obrigatórios)
- **Por aula:** 4 perguntas de múltipla escolha, 4 alternativas, 1 correta, com explicação curta da correta e por que as outras erram. Pelo menos 1 pergunta lê um trecho de programa.
- **Teste final do módulo:** 10 perguntas sorteadas das aulas mais 2 de programa para ler. Nota mínima 70% (aprovada pelo Igor em 22/10); o aluno pode pular se já souber.
- Formato de dados: `{ id, enunciado, alternativas[4], correta, explicacao, dica? }`.
- Distratores plausíveis (erros reais), nada de "pegadinha"; respostas corretas distribuídas entre A–D.

## Pode mexer em
- `content/academy/` (aulas e quizzes em JSON/Markdown) e `samples/academy/` (programas) — só isto.

## Entregas
Um PR por módulo (`ext-<agente>-acad01-m<N>`): aulas, programas, quizzes e teste final.

## Pronto quando
- [ ] Todos os arquivos seguem o modelo e passam na validação de formato (Claude fornece)
- [ ] Todo programa de exemplo abre no editor sem erro (Claude roda a checagem)
- [ ] Cada aula tem quiz de 4 perguntas; cada módulo tem teste final
- [ ] Tudo marcado `rascunho`

## Fora do escopo
Vídeos, certificado, nota com valor comercial, Siemens completo, comunidade, código do app.

## Do lado do Claude (separado)
Estrutura técnica: tela de trilhas por caminho e nível, leitor de aula, quiz com correção e explicação, progresso salvo no aparelho, abrir exemplo no editor/3D, validador de formato. Sem login no piloto; conta única depois (ACAD-00).

## Volta (no PR)
Seguir `docs/tarefas/MODELO.md`, seção **Volta**.
