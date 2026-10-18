# Glossário de códigos G e M

## G00 — Avanço rápido

**Máquinas:** fresa e torno · Fanuc: G00 · Siemens: G0

**O que faz:** Move a ferramenta na velocidade máxima da máquina, sem cortar material. Serve para posicionar rápido entre operações.

**Exemplo:**
```
G00 X50. Z2.
```

**Cuidado:** Nunca use G00 para entrar no material; o impacto quebra a ferramenta.

## G01 — Interpolação linear

**Máquinas:** fresa e torno · Fanuc: G01 · Siemens: G1

**O que faz:** Move a ferramenta em linha reta na velocidade de avanço programada (F). É o corte reto normal.

**Exemplo:**
```
G01 X50. F0.2
```

**Cuidado:** Sem F programado, a máquina usa o último avanço ou dá alarme.

## G02 — Interpolação circular horária

**Máquinas:** fresa e torno · Fanuc: G02 · Siemens: G2

**O que faz:** Faz um arco no sentido horário. Usa I, J, K (centro) ou R (raio) para definir a trajetória.

**Exemplo:**
```
G02 X50. Y0. I25. J0. F0.15
```

**Cuidado:** No torno, o G02 é visto de frente para o eixo Z; o sentido pode inverter conforme o plano ativo.

## G03 — Interpolação circular anti-horária

**Máquinas:** fresa e torno · Fanuc: G03 · Siemens: G3

**O que faz:** Faz um arco no sentido anti-horário. Mesma lógica do G02, sentido contrário.

**Exemplo:**
```
G03 X50. Y0. I25. J0. F0.15
```

**Cuidado:** Sentido errado gera arco no lado oposto e pode colidir com a peça.

## G04 — Tempo de espera (dwell)

**Máquinas:** fresa e torno · Fanuc: G04 · Siemens: G4

**O que faz:** Para a ferramenta no ponto por um tempo programado (em segundos ou rotações). Usado para quebrar cavaco ou aliviar pressão.

**Exemplo:**
```
G04 X1.0 (ou G04 P1000)
```

**Cuidado:** O formato do tempo muda: Fanuc aceita X (segundos) ou P (milissegundos); Siemens usa F ou S. Consulte o comando.

## G17 / G18 / G19 — Seleção de plano

**Máquinas:** fresa e torno · Fanuc: G17 (XY), G18 (ZX), G19 (YZ) · Siemens: G17, G18, G19

**O que faz:** Define em qual plano os arcos (G02/G03) e as compensações de raio atuam. Na fresa, o padrão é G17; no torno, G18.

**Exemplo:**
```
G18 (plano ZX para torno)
```

**Cuidado:** Plano errado faz o arco sair no eixo errado ou a compensação não funcionar.

## G20 / G21 — Unidades

**Máquinas:** fresa e torno · Fanuc: G20 (polegada), G21 (métrico) · Siemens: G70 (polegada), G71 (métrico)

**O que faz:** Troca a unidade de programação. G20/G70 para polegadas; G21/G71 para milímetros.

**Exemplo:**
```
G21 (programação em mm)
```

**Cuidado:** Esquecer a unidade certa multiplica ou divide as cotas por 25,4 e a peça sai fora de medida.

## G28 — Retorno ao ponto de referência

**Máquinas:** fresa e torno · Fanuc: G28 · Siemens: G74 (ou G75, varia conforme o comando)

**O que faz:** Leva a ferramenta ao ponto zero da máquina (home), passando por um ponto intermediário.

**Exemplo:**
```
G28 G91 Z0. (retorna Z ao home)
```

**Cuidado:** Sem ponto intermediário seguro, a ferramenta pode bater na peça ou na placa.

## G40 — Cancela compensação de raio

**Máquinas:** fresa e torno · Fanuc: G40 · Siemens: G40

**O que faz:** Desativa o uso do raio da ferramenta no perfil. É o estado normal antes de ligar G41/G42.

**Exemplo:**
```
G40 X0. Y0.
```

**Cuidado:** Cancelar no meio de um perfil deixa a medida errada por um raio de ferramenta.

## G41 — Compensação de raio à esquerda

**Máquinas:** fresa e torno · Fanuc: G41 · Siemens: G41

**O que faz:** Desloca a trajetória para a esquerda do perfil programado, usando o raio da ferramenta. Muito usada em fresamento de contornos.

**Exemplo:**
```
G41 D01 X50. Y0.
```

**Cuidado:** No torno, o G41 depende da posição do porta-ferramenta; o lado pode inverter. Consulte o manual da máquina.

## G42 — Compensação de raio à direita

**Máquinas:** fresa e torno · Fanuc: G42 · Siemens: G42

**O que faz:** Desloca a trajetória para a direita do perfil programado. Usada para contornar ilhas ou perfis externos.

**Exemplo:**
```
G42 D01 X50. Y0.
```

**Cuidado:** Se o perfil tiver um raio menor que o da ferramenta, a compensação gera alarme ou corte errado.

## G43 — Compensação de comprimento de ferramenta positiva

**Máquinas:** fresa (principal) e torno (varia conforme o comando) · Fanuc: G43 · Siemens: não é padrão; usa-se a tabela de ferramentas (D)

**O que faz:** Soma o comprimento da ferramenta ao eixo Z (ou ao eixo de aproximação) para a ponta ficar na cota correta.

**Exemplo:**
```
G43 H01 Z50.
```

**Cuidado:** H errado ou G43 sem H faz a ferramenta afundar ou ficar alta demais.

## G49 — Cancela compensação de comprimento

**Máquinas:** fresa · Fanuc: G49 · Siemens: varia conforme o comando (normalmente troca de ferramenta/D)

**O que faz:** Desativa a compensação de comprimento aplicada pelo G43.

**Exemplo:**
```
G49 Z100.
```

**Cuidado:** Cancelar antes de sair da peça pode deixar a ponta fora da referência e causar colisão no próximo movimento.

## G50 — Varia conforme o comando

**Máquinas:** fresa e torno · Fanuc: G50 · Siemens: não tem equivalente direto

**O que faz na fresa:** Em alguns comandos, cancela escala ou define offset global. No torno: limita a rotação máxima do spindle (G50 S…).

**Exemplo (torno):**
```
G50 S2500
```

**Cuidado:** O significado muda muito entre modelos; confirmar no manual da máquina.

## G54–G59 — Sistemas de coordenadas de trabalho

**Máquinas:** fresa e torno · Fanuc: G54 a G59 · Siemens: G54 a G59 (e outros via frames)

**O que faz:** Cada código chama um zero-peça diferente (G54 = zero 1, G55 = zero 2, até G59 = zero 6). Serve para trabalhar várias peças ou faces sem reprogramar.

**Exemplo:**
```
G54 G00 X0. Y0.
```

**Cuidado:** Usar o zero errado desloca toda a peça; conferir sempre o zero ativo no visor.

## G70 — Ciclo de acabamento

**Máquinas:** torno · Fanuc: G70 · Siemens: no modo nativo G70 = programação em **polegadas** (o acabamento é feito com CYCLE95); no **modo ISO** (G291 liga, G290 volta ao Siemens), G70 funciona como no Fanuc

**O que faz:** Executa o passe final de acabamento após um ciclo de desbaste (G71/G72). Usa a mesma definição de perfil.

**Exemplo:**
```
G70 P10 Q20
```

**Cuidado:** Só funciona se o perfil de desbaste estiver definido e o G70 vier depois do ciclo de desbaste.

## G71 — Ciclo de desbaste longitudinal (torneamento)

**Máquinas:** torno · Fanuc: G71 · Siemens: CYCLE95 (no modo nativo G71 = programação em **milímetros**; no modo ISO, G291, funciona como no Fanuc)

**O que faz:** Remove material no sentido do eixo Z, em passes paralelos, deixando sobremetal para acabamento. Define o perfil entre blocos P e Q.

**Exemplo:**
```
G71 U2.0 R0.5
G71 P10 Q20 U0.5 W0.1 F0.25
```

**Cuidado:** Se o perfil tiver cotas menores que o sobremetal, o ciclo pode dar alarme ou cortar a peça.

## G72 — Ciclo de desbaste transversal (faceamento)

**Máquinas:** torno · Fanuc: G72 · Siemens: CYCLE95 (com parâmetros de faceamento)

**O que faz:** Remove material no sentido do eixo X, em passes paralelos ao eixo Z (faceamento). Deixa sobremetal para acabamento.

**Exemplo:**
```
G72 W2.0 R0.5
G72 P10 Q20 U0.1 W0.5 F0.2
```

**Cuidado:** Perfil mal definido gera passes vazios ou colisão com a placa.

## G73 — Varia conforme a máquina

**Máquinas:** fresa e torno · Fanuc: G73 · Siemens: não tem equivalente direto

**O que faz na fresa:** Ciclo de furação com quebra de cavaco (peck drilling) de alta velocidade. No torno: ciclo de desbaste com repetição de contorno (padrão).

**Exemplo (torno):**
```
G73 U2.0 W0.5 R3
G73 P10 Q20 U0.3 W0.1 F0.2
```

**Cuidado:** O mesmo número tem funções totalmente diferentes; usar o ciclo errado pode quebrar a ferramenta ou não remover material.

## G74 — Macho à esquerda (fresa) / furação e canal frontal (torno)

**Máquinas:** fresa e torno · Fanuc: G74 · Siemens: varia conforme o comando

**O que faz na fresa:** Ciclo de roscamento à esquerda (macho).

**No torno:** dois usos com quebra de cavaco em Z: **furação frontal** (só Z, no centro) ou, informando X(U) e P, **canal frontal ou desbaste em Z**, repetindo os passes e deslocando em X.

**Exemplo (torno):**
```
G74 R1.0
G74 X0. Z-20. Q5000 F0.1
```

**Cuidado:** Na fresa, inverter o sentido do spindle sem preparar o macho quebra a ferramenta.

## G75 — Ciclo de canal radial

**Máquinas:** torno · Fanuc: G75 · Siemens: CYCLE93 (ou CYCLE94, varia conforme o comando)

**O que faz:** Abre canais no sentido radial (no eixo X), com quebra de cavaco. Usado para sangramento e canais de anel.

**Exemplo:**
```
G75 R0.5
G75 X20. Z-10. P2000 Q3000 F0.08
```

**Cuidado:** Profundidade de corte alta sem refrigeração quebra a pastilha de canal.

## G76 — Rosca (torno) / mandrilamento fino (fresa)

**Máquinas:** torno e fresa (funções diferentes) · Fanuc: G76 · Siemens: CYCLE97 (rosca no torno), CYCLE86 (mandrilamento na fresa)

**O que faz no torno:** Executa rosca em múltiplos passes, com profundidade decrescente e ângulo de entrada. Define passo, altura e profundidade final.

**O que faz na fresa:** Mandrilamento fino: no fundo do furo o fuso orienta, a ferramenta se afasta da parede (Q) e só então sobe, sem riscar o furo.

**Exemplo:**
```
G76 P010060 Q100 R0.05
G76 X18. Z-30. P1000 Q300 F2.0
(fresa) G76 X20. Y20. Z-15. R2. Q0.2 F80.
```

**Cuidado:** Passo ou altura errados fazem a rosca passar ou não pegar a porca.

## G80 — Cancela ciclos fixos

**Máquinas:** fresa e torno · Fanuc: G80 · Siemens: CYCLE81…CYCLE84 são chamados individuais; não há um G80 geral (varia conforme o comando)

**O que faz:** Desativa qualquer ciclo de furação, rosqueamento ou canal ativo, voltando ao modo de movimentos normais.

**Exemplo:**
```
G80 G00 Z100.
```

**Cuidado:** Sair do ciclo sem G80 pode fazer o próximo movimento repetir a furação no lugar errado.

## G81 — Ciclo de furação simples

**Máquinas:** fresa e torno (com eixo Y ou ferramenta acionada) · Fanuc: G81 · Siemens: CYCLE81

**O que faz:** Fura um furo único: aproxima em avanço, fura até a profundidade e retorna em rápido. Sem quebra de cavaco.

**Exemplo:**
```
G81 X20. Y20. Z-15. R2. F120.
```

**Cuidado:** Em furos profundos, o cavaco não sai e a broca trava. Na fresa os ciclos seguem o modo de avanço ativo: em G94 (padrão) F é mm/min; só com G95 no programa F passa a mm/rot.

## G82 — Ciclo de furação com dwell

**Máquinas:** fresa e torno · Fanuc: G82 · Siemens: CYCLE82

**O que faz:** Igual ao G81, mas para no fundo do furo por um tempo (dwell) para limpar a face. Usado em furos de centro ou assentos.

**Exemplo:**
```
G82 X20. Y20. Z-5. R2. P500 F80.
```

**Cuidado:** Dwell longo demais queima a aresta da broca ou marca o fundo.

## G83 — Ciclo de furação profunda com quebra de cavaco

**Máquinas:** fresa e torno · Fanuc: G83 · Siemens: CYCLE83

**O que faz:** Fura em pequenos incrementos (peck), retirando a broca para limpar o cavaco a cada avanço.

**Exemplo:**
```
G83 X20. Y20. Z-30. R2. Q5. F100.
```

**Cuidado:** Q muito grande enche o canal de cavaco; Q muito pequeno desgasta a broca rápido.

## G84 — Ciclo de rosqueamento com macho

**Máquinas:** fresa e torno · Fanuc: G84 · Siemens: CYCLE84

**O que faz:** Rosqueia com macho, sincronizando o avanço com a rotação do spindle. Entra e sai no mesmo passo.

**Exemplo:**
```
G84 X20. Y20. Z-15. R2. F1200. (G94: S800 × passo 1,5)
```

**Cuidado:** Em G94 (padrão) F = rotação × passo; em G95, F = passo. F errado quebra o macho ou espana a rosca.

## G85 — Ciclo de alargamento/calibragem

**Máquinas:** fresa e torno · Fanuc: G85 · Siemens: CYCLE85 (ou variação de alargamento)

**O que faz:** Entra em avanço, alarga o furo e sai em avanço (não em rápido). Usado para alargadores e calibragem.

**Exemplo:**
```
G85 X20. Y20. Z-20. R2. F60.
```

**Cuidado:** Sair em rápido (se o comando não for bem configurado) risca o furo alargado.

## G90 — Varia conforme a máquina

**Máquinas:** fresa e torno · Fanuc: G90 · Siemens: G90

**O que faz na fresa:** Programa em coordenadas absolutas (a cota é sempre a partir do zero-peça). No torno: em alguns comandos, é ciclo de torneamento longitudinal (O.D./I.D. turning cycle).

**Exemplo (fresa):**
```
G90 G01 X50. Y20.
```

**Cuidado:** No torno, usar G90 como absoluto quando o comando espera ciclo de torneamento causa movimento inesperado.

## G91 — Programação incremental

**Máquinas:** fresa e torno · Fanuc: G91 · Siemens: G91

**O que faz:** As cotas passam a ser a distância a partir da posição atual (incremental), não a partir do zero.

**Exemplo:**
```
G91 G01 X10. (avança 10 mm a partir de onde está)
```

**Cuidado:** Misturar absoluto e incremental sem trocar o modo desloca a peça na soma dos incrementos.

## G94 — Avanço por minuto (fresa) / ciclo de faceamento (torno Fanuc)

**Máquinas:** fresa e torno · Fanuc: G94 · Siemens: G94

**O que faz na fresa:** Define o avanço F em mm/min. É o padrão ao ligar a máquina.

**No torno Fanuc (sistema A, o mais comum):** G94 é o **ciclo de faceamento**; o avanço por minuto é o G98.

**Exemplo:**
```
G94 G01 X50. F150.
```

**Cuidado:** No torno Fanuc, confirmar o sistema de códigos (A, B ou C) no manual: no sistema A, G94 faceia e G98/G99 escolhem o tipo de avanço.

## G95 — Avanço em mm/rotação

**Máquinas:** fresa · Fanuc: G95 (no torno sistema A o equivalente é G99) · Siemens: G95

**O que faz:** Define o avanço F em milímetros por rotação do spindle. Na fresa é usado, por exemplo, no macho (F = passo).

**Exemplo:**
```
G95 G84 X20. Y20. Z-15. R2. F1200. (G94: S800 × passo 1,5)
```

**Cuidado:** Esquecer de voltar a G94 depois deixa os cortes seguintes com F em mm/rot, muito mais lentos que o esperado.

## G96 — Velocidade de corte constante

**Máquinas:** torno · Fanuc: G96 · Siemens: G96

**O que faz:** Mantém a velocidade de corte (m/min) constante; a rotação sobe ou desce conforme o diâmetro. Usado para faceamento e torneamento externo.

**Exemplo:**
```
G96 S200 M03
```

**Cuidado:** Sem limitar a rotação máxima (G50 ou G92), o spindle dispara em diâmetros pequenos.

## G97 — Rotação constante

**Máquinas:** torno · Fanuc: G97 · Siemens: G97

**O que faz:** Fixa a rotação do spindle em rpm, independente do diâmetro. É o modo para furação, rosqueamento com macho e rosqueamento rígido.

**Exemplo:**
```
G97 S1200 M03
```

**Cuidado:** Usar G97 para torneamento externo deixa a velocidade de corte errada no diâmetro.

## G98 — Retorno ao plano inicial / avanço por minuto

**Máquinas:** fresa e torno · Fanuc: G98 · Siemens: varia conforme o comando

**O que faz na fresa:** Após o ciclo fixo, a ferramenta retorna ao plano inicial (R). No torno: define avanço em mm/min (equivalente ao G94 em alguns comandos).

**Exemplo (fresa):**
```
G98 G83 Z-30. R2. Q5. F100.
```

**Cuidado:** No torno, G98 e G94 podem ter significados trocados conforme o modelo; confirmar no manual.

## G99 — Retorno ao plano R / avanço por rotação

**Máquinas:** fresa e torno · Fanuc: G99 · Siemens: varia conforme o comando

**O que faz na fresa:** Após o ciclo, a ferramenta retorna ao plano R (mais perto da peça), economizando tempo. No torno: define avanço em mm/rotação (equivalente ao G95 em alguns comandos).

**Exemplo (fresa):**
```
G99 G83 Z-30. R2. Q5. F100.
```

**Cuidado:** Sair de G99 para G98 sem ajustar o R pode dar colisão no próximo ciclo.

## M00 — Parada programada

**Máquinas:** fresa e torno · Fanuc: M00 · Siemens: M00

**O que faz:** Para o programa e o spindle. A máquina fica aguardando o operador apertar ciclo-start para continuar.

**Exemplo:**
```
M00
```

**Cuidado:** Usar M00 no meio de um corte deixa a ferramenta parada dentro do material e pode marcar a peça.

## M01 — Parada condicional

**Máquinas:** fresa e torno · Fanuc: M01 · Siemens: M01

**O que faz:** Só para o programa se o botão “Optional Stop” estiver ativo no painel. Serve para inspeções sem alterar o programa.

**Exemplo:**
```
M01
```

**Cuidado:** Se o operador esquecer o botão ligado, a máquina para sem aviso e pode interromper a produção.

## M03 — Spindle no sentido horário

**Máquinas:** fresa e torno · Fanuc: M03 · Siemens: M03

**O que faz:** Liga a rotação do spindle no sentido horário (visto de frente para o eixo). É o sentido padrão para ferramentas de corte à direita.

**Exemplo:**
```
M03 S1500
```

**Cuidado:** Sentido errado para a ferramenta (por exemplo, macho à esquerda) quebra a ferramenta ou não corta.

## M04 — Spindle no sentido anti-horário

**Máquinas:** fresa e torno · Fanuc: M04 · Siemens: M04

**O que faz:** Liga o spindle no sentido anti-horário. Usado para macho à esquerda, rosqueamento à esquerda ou ferramentas de corte à esquerda.

**Exemplo:**
```
M04 S800
```

**Cuidado:** Usar M04 com ferramenta de corte à direita faz a pastilha trabalhar no sentido errado e quebrar.

## M05 — Para o spindle

**Máquinas:** fresa e torno · Fanuc: M05 · Siemens: M05

**O que faz:** Desliga a rotação do spindle, mantendo o programa ativo.

**Exemplo:**
```
M05
```

**Cuidado:** Parar o spindle com a ferramenta dentro do furo pode deixar marca ou prender a ferramenta.

## M06 — Troca de ferramenta

**Máquinas:** fresa e torno (com torre ou magazine) · Fanuc: M06 · Siemens: M06 (ou chamada de ferramenta via T)

**O que faz:** Aciona a troca de ferramenta. Na fresa, costuma vir com T… para selecionar a ferramenta; no torno, pode ser automático com T….

**Exemplo:**
```
T02 M06
```

**Cuidado:** Chamar M06 sem a ferramenta posicionada ou sem magazine referenciado dá alarme e pode travar a máquina.

## M08 — Liga refrigeração

**Máquinas:** fresa e torno · Fanuc: M08 · Siemens: M08

**O que faz:** Liga o fluido de corte (refrigeração) para resfriar a ferramenta e limpar o cavaco.

**Exemplo:**
```
M08
```

**Cuidado:** Ligar M08 antes de o spindle girar pode inundar a peça e dificultar a visualização.

## M09 — Desliga refrigeração

**Máquinas:** fresa e torno · Fanuc: M09 · Siemens: M09

**O que faz:** Desliga o fluido de corte.

**Exemplo:**
```
M09
```

**Cuidado:** Desligar no meio de um corte pesado superaquece a ferramenta e queima a pastilha.

## M30 — Fim de programa e reset

**Máquinas:** fresa e torno · Fanuc: M30 · Siemens: M30

**O que faz:** Encerra o programa, reseta o cabeçote e volta ao início. É o fim normal de um programa principal.

**Exemplo:**
```
M30
```

**Cuidado:** Usar M30 no lugar de M99 em subprograma encerra o programa principal antes da hora.

## M98 — Chamada de subprograma

**Máquinas:** fresa e torno · Fanuc: M98 · Siemens: L… (ou CALL)

**O que faz:** Chama um subprograma para ser executado e depois retorna ao programa principal.

**Exemplo:**
```
M98 P1000
```

**Cuidado:** Subprograma sem M99 no fim faz a máquina continuar executando os blocos seguintes como se fossem do programa principal.

## M99 — Fim de subprograma / retorno

**Máquinas:** fresa e torno · Fanuc: M99 · Siemens: RET (ou M17, varia conforme o comando)

**O que faz:** Encerra o subprograma e volta ao programa que o chamou.

**Exemplo:**
```
M99
```

**Cuidado:** M99 no programa principal (em vez de M30) faz a máquina repetir o programa em loop infinito.

---

# Seção Siemens — equivalentes e comandos específicos

## G0 / G1 / G2 / G3 / G4

**Siemens:** G0, G1, G2, G3, G4

**O que faz:** São os mesmos movimentos básicos do Fanuc (rápido, linear, arco horário, arco anti-horário e dwell). A sintaxe é praticamente igual; a diferença está nos parâmetros de ciclo e na forma de chamar subprogramas.

**Exemplo:**
```
G1 X50. Z-20. F0.2
```

**Cuidado:** O G4 no Siemens aceita F (segundos) ou S (rotações); não usar X como no Fanuc sem confirmar o comando.

## CYCLE81 — Furação/centragem

**Siemens:** CYCLE81

**O que faz:** Equivalente ao G81 Fanuc. Fura ou centra um furo único.

**Exemplo:**
```
CYCLE81(5, 0, 2, -15)
```

**Cuidado:** A ordem dos parâmetros muda entre versões do SINUMERIK; conferir a lista de parâmetros do comando.

## CYCLE82 — Furação com dwell

**Siemens:** CYCLE82

**O que faz:** Equivalente ao G82. Fura e para no fundo para limpar a face.

**Exemplo:**
```
CYCLE82(5, 0, 2, -5, 0.5)
```

**Cuidado:** O tempo de dwell é programado em segundos; valor alto queima a aresta.

## CYCLE83 — Furação profunda

**Siemens:** CYCLE83

**O que faz:** Equivalente ao G83. Fura em incrementos, retirando a broca para limpar o cavaco.

**Exemplo:**
```
CYCLE83(5, 0, 2, -30, 5, 0.5, 0.2, 0.1, 1)
```

**Cuidado:** Parâmetros de incremento e avanço de retirada mal ajustados enchem o canal de cavaco.

## CYCLE84 — Rosqueamento com macho

**Siemens:** CYCLE84

**O que faz:** Equivalente ao G84. Rosqueia com macho, sincronizando spindle e avanço.

**Exemplo:**
```
CYCLE84(5, 0, 2, -15, , 0, 3, , 1.5, 0, 300, 300)
```

**Cuidado:** O passo (PIT) é o 9º parâmetro, não o 5º, e a ordem varia entre versões do SINUMERIK: conferir no manual. Passo diferente do macho quebra a ferramenta.

## CYCLE95 — Desbaste de contorno

**Siemens:** CYCLE95

**O que faz:** Equivalente ao G71/G72 no torno. Remove material seguindo um contorno definido, em passes de desbaste, deixando sobremetal.

**Exemplo:**
```
CYCLE95("CONTORNO", 2, 0.2, 0.5, 0.3, 0.25, 0.15,,1)
```

**Cuidado:** O contorno precisa existir e estar fechado; contorno aberto ou mal definido gera passes vazios ou colisão.

## CYCLE97 — Rosqueamento

**Siemens:** CYCLE97

**O que faz:** Equivalente ao G76 no torno. Executa rosca em múltiplos passes com profundidade decrescente.

**Exemplo:**
```
CYCLE97(2, 0, -30, 18, 18, 1.5, 0.1, 0.05, 0.1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
```

**Cuidado:** Altura e passo errados fazem a rosca não encaixar; conferir sempre com o calibrador.

## MCALL — Chamada modal de subprograma

**Siemens:** MCALL

**O que faz:** Chama um subprograma de forma modal: ele é executado automaticamente a cada bloco de movimento até ser cancelado com MCALL sem argumento.

**Exemplo:**
```
MCALL MYSUB
...
MCALL
```

**Cuidado:** Esquecer de cancelar o MCALL faz o subprograma rodar em todos os movimentos seguintes e pode causar colisão.

## L / SPF — Subprogramas

**Siemens:** L… (número) ou nome.SPF

**O que faz:** L chama um subprograma por número; .SPF é a extensão de arquivo de subprograma. O retorno é feito com RET ou M17 (varia conforme o comando).

**Exemplo:**
```
L1000
```

**Cuidado:** Nome de subprograma igual ao do programa principal pode fazer o comando chamar o programa errado.

## R-parâmetros — Variáveis

**Siemens:** R0 a R999 (R1000+ em alguns modelos)

**O que faz:** São variáveis para armazenar valores e fazer cálculos no programa. Muito usadas para parametrizar subprogramas e ciclos.

**Exemplo:**
```
R1 = 50
G1 X=R1 F0.2
```

**Cuidado:** Usar R já ocupado por ciclo ou macro do sistema sobrescreve valores e gera comportamento estranho.

**Nota final:** Os códigos aqui listados seguem a nomenclatura mais comum em Fanuc e Siemens. Alguns números (G50, G73, G74, G90, G98, G99) mudam de função conforme o modelo do comando, a versão do software ou o tipo de máquina (fresa/torno). Em caso de dúvida, a referência final é sempre o manual da máquina.
