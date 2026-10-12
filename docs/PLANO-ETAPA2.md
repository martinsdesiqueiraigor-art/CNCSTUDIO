# Plano da Etapa 2 — Produto final

Proposta para aprovação. Parte do protótipo validado (Etapa 1) e incorpora os pedidos feitos
durante os testes: torno, vista isométrica, recursos de edição e melhorias de interface.

Cada fase termina com `dotnet test` passando no CI, executável gerado e commit em português.
Ao fim de cada fase eu mostro o resultado antes de seguir.

## Decisões já tomadas

- **Stack:** C# / .NET 10 + Avalonia + AvaloniaEdit (validada no protótipo).
- **Dialetos:** Fanuc e Siemens, para fresa e torno.
- **Torno:** Fanuc Série T e Siemens; ciclos G71/G70 e rosca G76 (CYCLE95/CYCLE97 no Siemens).
- **Vista 3D girável com o mouse: fora do escopo.** Vistas fixas (superior, frontal, lateral, isométrica) continuam.

## Já pronto (vindo do protótipo)

- Estrutura da solução, CI com testes e executável Windows.
- Lexer, parser modal, trajetória com arcos, backplot (XY/XZ/YZ/isométrica), simulação 2.5D com 4 vistas.
- Editor: highlighting, procurar/substituir, ir para linha, fonte, renumerar, maiúsculas, faixa de comandos em abas.

## Fases

### Fase 1 — Fresa: comandos do MVP ✅
- G90/G91, G54–G59 (com tabela de zeros editável), G17/G18/G19.
- Furação: G81, G83 (Fanuc) e CYCLE81, CYCLE83 (Siemens).
- Ferramentas: T, M06, M04, tabela de ferramentas (diâmetro por T) usada na simulação.
- Subprogramas: M98/M99 (Fanuc) e L (Siemens); arcos por CR= (Siemens) e R (Fanuc).
- Opção para números sem ponto decimal no Fanuc (`X10` = 10 mm ou 0,010 mm).
- Validador: avisos de avanço zero em G01, rápido dentro do material, etc.

### Fase 1B — Interpretador: ciclos, repetições e programação paramétrica ✅
O parser deixa de ler linha a linha e passa a **executar** o programa como o comando faz,
para que backplot e simulação sigam saltos, repetições e variáveis.

- **Fanuc Macro B:** variáveis #1–#999 (locais, comuns), aritmética e funções
  (SIN, COS, TAN, ATAN, SQRT, ABS, ROUND, FIX, FUP), `IF [...] GOTO n`, `IF [...] THEN`,
  `WHILE [...] DO n` / `END n`, `GOTO n`, chamadas G65/G66 e M98/M99 com repetição (L/K).
- **Siemens:** parâmetros R, variáveis `DEF REAL/INT`, expressões e funções,
  `IF/ELSE/ENDIF`, `WHILE/ENDWHILE`, `FOR/ENDFOR`, `REPEAT/UNTIL`, `LOOP/ENDLOOP`,
  `GOTOF/GOTOB/GOTO`, `REPEAT rótulo P=n`, subprogramas com repetição (`L10 P3`).
- Ciclos de furação e de torno executados pelo mesmo mecanismo.
- Proteção contra laço infinito (limite de iterações, com aviso na linha).
- Uma linha dentro de um laço gera vários movimentos: o backplot destaca **todas** as passagens
  da linha do cursor, e a simulação mostra a passagem atual.
- Variáveis de sistema (posição atual etc.): só as mais usadas, sob demanda.
- Arredondamento e chanfro de canto do Fanuc (`,R` e `,C`), e `R` como raio de arco.
- **Compensação de raio G41/G42 básica** (retas e arcos, entrada/saída em G40): sem ela,
  programas como o CHAVETA simulam com a ferramenta em cima do contorno, e não ao lado.
- G43/H (comprimento) e G53 (coordenada de máquina) aceitos sem afetar o desenho da peça.
- Siemens: rótulos (`VOLTA:`), `GOTOF/GOTOB`, `TRANS/ATRANS` (deslocamento de origem),
  `MSG(...)` mostrado na barra de status; `CFTCP`, `D0` e similares aceitos sem efeito no desenho.
- Avisos úteis do interpretador: código depois do M30 que nunca executa, laço infinito,
  variável usada sem valor.
- Testes de referência (exemplos enviados pelo usuário), em `samples/macros/`:
  `ESPIRAL_WHILE.nc` (Fanuc, WHILE com 2.880 voltas, SIN/COS) e `CHAVETA.mpf`
  (Siemens, rótulos, IF/ELSE, GOTOF/GOTOB, REPEAT, TRANS, G41).

### Fase 2 — Estatísticas ✅
- Tempo estimado, distância de corte e de rápido, tempo por ferramenta.
- Painel de estatísticas na interface.
- Esperas (G04, G82 P, DTB dos ciclos Siemens) entram no tempo; velocidade de rápido configurável.

### Fase 3 — Torno: núcleo ✅
- Tipo de máquina (fresa/torno), com detecção automática e troca manual.
- X em diâmetro, plano G18, G96/G97 (velocidade de corte constante).
- Ciclos G71/G70/G76 (Fanuc) e CYCLE95/CYCLE97 (Siemens), expandidos em movimentos.
- Exemplos EXEMPLO_TORNO.nc e .mpf, com testes.
- Extras: G72 (desbaste de face), G90/G92/G94, G32/G33, U/W, G50 S / LIMS=, G98/G99 e G94/G95
  (avanço por rotação convertido com a rotação no ponto), vista "Torno (ZX)" no backplot.
- G71/G72 com rebaixo (tipo II): o desbaste entra em cada rebaixo, passando por cima dos ressaltos.
- G74 (furação/canal de face em bicadas) e G75 (canais em X), formato de dois blocos.
- G73 (repetição de padrão) ainda não.

### Fase 4 — Backplot passe a passe (substitui a simulação do torno)
A simulação de remoção de material do torno saiu do escopo; o torno usa o backplot. ✅
- Cores por movimento: G00 vermelho (tracejado), G01 verde, G02 azul-claro, G03 azul-escuro.
- Barra passe a passe: início, passe anterior/próximo (Page Up/Down), executar com velocidade,
  régua para qualquer ponto e fim. O passe atual fica em destaque, o resto apagado, e o editor
  acompanha a linha.
- Vale para fresa e torno (um passe = rápidos de aproximação + cortes seguintes).

### Fase 5 — Interface da simulação (inspirada no CIMCO)
- Barra de reprodução embaixo, com régua para arrastar a qualquer ponto do programa e "ir para linha".
- Painel de coordenadas: X, Y, Z, avanço, linha e distância.
- Ferramenta desenhada com suporte (porta-ferramenta).
- Trajetória desenhada por cima do material.
- Editor acompanha a linha em execução.

### Fase 6 — Editor e funções NC
- Barra de busca/substituição própria, em português.
- Painel de erros com filtro (erros/avisos) e contagem.
- Funções NC extras: remover/inserir espaços, remover comentários, remover números de bloco.

### Fase 7 — Polimento
- Tema claro/escuro, configurações salvas (fonte, tema, pastas recentes, ferramentas).
- Arquivos recentes, ícone, instalador e executável menor.
- Versão do Avalonia fixada.

## Fora do escopo

4º/5º eixos, CYCLE800, compensação G41/G42 em casos avançados (cantos agudos com
interferência, mudança de lado no meio do contorno), DNC/serial, CAD/CAM e vista 3D girável.
(Programação paramétrica saiu desta lista: entrou na Fase 1B.)
