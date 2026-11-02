# Missão ACAD-02 · Módulo 2 (Fresa › Iniciante), entrega por texto

**Para:** GPT  **Sem acesso de escrita ao GitHub.** Você entrega o conteúdo como **blocos de texto** na conversa; o Claude cria os arquivos, valida e abre o PR. Só o módulo 2 agora; o 3 e o 4 só quando o Igor mandar.

## Leia antes de escrever (repositório público, ramo `main`)
Base: `https://github.com/martinsdesiqueiraigor-art/CNCSTUDIO/blob/main/`
1. `docs/tarefas/ACAD-02-modulos-2-a-4.md` (ficha geral)
2. `docs/tarefas/ACAD-01-academy-piloto.md` (regras de texto, quiz e código G)
3. Modelo do módulo 1, em `content/academy/` e `samples/academy/`: `acad-m1-a05.json`, `acad-m1-a05.nc`, `acad-m1-teste-final.json` (se puder, leia também a01 a a04)
Se não conseguir abrir os links, use os arquivos que o Igor anexou. **Copie a estrutura exata dos campos do módulo 1. Não invente campos.**

## O que entregar (módulo 2 · Fresa · Iniciante · Fanuc)
Seis aulas, um teste final e seis programas. Divisão sugerida (pode ajustar, explicando na Volta):
| Aula | Id | Tema |
|---|---|---|
| 1 | `acad-m2-a01` | Movimento rápido G00 |
| 2 | `acad-m2-a02` | Movimento linear G01 e avanço F |
| 3 | `acad-m2-a03` | Rotação S e M03/M05 |
| 4 | `acad-m2-a04` | Arcos G02/G03 com raio R |
| 5 | `acad-m2-a05` | Arcos G02/G03 com I e J |
| 6 | `acad-m2-a06` | Lendo um programa simples (G00 a G03) |
- Pré-requisito da aula 1: `acad-m1-a05`; das demais: a aula anterior.
- Campos comuns: `modulo: "acad-m2"`, `nivel: "Iniciante"`, `caminho: "Fresa"`, `status: "rascunho"`.

## Arquivos (nomes exatos)
- `content/academy/acad-m2-a01.json` … `acad-m2-a06.json`
- `content/academy/acad-m2-teste-final.json`
- `samples/academy/acad-m2-a01.nc` … `acad-m2-a06.nc`

## Regras do conteúdo (o validador do Core reprova se faltar)
- `status` sempre `rascunho`. Só o Igor marca `revisado`.
- Texto de 300 a 500 palavras; parágrafos marcados com `[conceito]`, `[atenção]`, `[dica]` ou `[exemplo]`; um erro comum de chão de fábrica.
- `resumo` com **exatamente 3** pontos.
- `aviso`: "Simule antes de rodar na máquina".
- `exercicio` completo, com gabarito e a parte `editor` como no módulo 1.
- `quiz` com **exatamente 4 perguntas**, **4 alternativas** cada, 1 correta, explicação de pelo menos 20 letras; pelo menos 1 pergunta lendo trecho de programa; corretas distribuídas entre A e D.
- Teste final: no mínimo 12 perguntas (10 das aulas + 2 de leitura de programa), `nota_minima_percentual: 70`, `pode_pular` como no módulo 1.
- Só Fanuc. Fato técnico incerto = **A definir**. Nunca invente rpm, avanço ou tolerância: use "exemplo didático" ou A definir. Texto próprio, sem copiar manual.

## Regras dos programas `.nc` (conferidas no simulador)
- Comentários **sem acento**, ponto decimal em todos os números, cabeçalho como no módulo 1, aviso "SIMULE ANTES DE RODAR NA MAQUINA".
- Programas curtos, sem rotação nem corte reais quando for só demonstração, como no módulo 1.
- G02/G03: o simulador executa com R e com I/J. Confira o sentido e que o arco fecha na geometria.
- Não use compensação G41/G42, G43/H nem D neste módulo (o simulador não os aplica).
- Evite ciclos e macros neste módulo.

## Como entregar
Uma mensagem por aula, cada bloco com o **nome do arquivo no título**: primeiro o `.json`, depois o `.nc`, ambos em bloco de código. O teste final em mensagem própria. No fim, a **Volta**, no formato abaixo. Depois pare.

## Pronto quando
- [ ] 6 aulas + teste final + 6 programas, nos nomes acima
- [ ] Mesma estrutura de campos do módulo 1
- [ ] Nenhum valor técnico inventado; dúvidas marcadas A definir

## Fora do escopo
Módulos 1, 3 a 7, vídeo, certificado, Siemens, compensação, código do app, tela da Academy.

## Volta
```
ID: ACAD-02 · M2
O que foi feito (até 5 linhas):
Arquivos entregues:
Testes: não rodados (sem acesso ao simulador)
Decisões tomadas (só as autorizadas):
Pendências e dúvidas (A definir):
Riscos ou contradições encontradas:
.nc que usam algo fora das regras acima (se houver):
Próximo passo sugerido (1):
```
