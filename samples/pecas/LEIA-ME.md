# Peças-exemplo CNC

Doze peças completas (várias operações cada), para demonstrar a simulação 3D e servir de base às aulas do Academy.
Todas em aço 1045. Fresa: bruto 100 × 60 × 20 (a flange, 100 × 100 × 20), zero no canto (flange: no centro), Z0 no topo.
Torno: bruto Ø50 × 70, zero na face, X em diâmetro.

**Não executar em máquina sem simulação e revisão técnica.** Avanços e rotações são valores de referência.

| Arquivo | Peça | Operações | Ferramentas |
|---|---|---|---|
| fanuc-fresa/01_FLANGE_FUROS.nc | Flange com 6 furos em círculo D70 | centrar, furar D8.5 (G83), canal de anel D30 × D50 com G03 | broca de centro D6, broca D8.5, fresa D10 |
| fanuc-fresa/02_PLACA_BOLSA.nc | Placa com bolsão 60 × 30 × 6 | desbaste em 2 passes, acabamento das paredes com G41 | fresa D10 |
| fanuc-fresa/03_SUPORTE_RASGO.nc | Suporte com rasgo e 4 furos M10 | rasgo 60 × 10 × 5, furar D8.5 (G83), rosquear (G84) | fresa D10, broca D8.5, macho M10 |
| fanuc-fresa/04_TAMPA_FACE.nc | Tampa com face, rebaixo e bolsão circular | facear 1 mm, rebaixo perimetral, bolsão D30 | fresa de facear D50, fresa D16, fresa D10 |
| fanuc-torno/01_EIXO_ROSCA.nc | Eixo escalonado D30 / D40 com rosca M30 × 2 | G71 + G70, rosca G76 | desbaste R0.8, acabamento R0.4, rosca 60° |
| fanuc-torno/02_BUCHA_FURO.nc | Bucha D46 com furo D20 e canal | G71 + G70, furar D16 (G74), mandrilar, canal L3 | desbaste, broca D16, mandril interno, canal L3 |
| fanuc-torno/03_PINO_CANAL.nc | Pino D30 com ponta esférica R15 e canal | G71 + G70 com arco G03, canal L3 | desbaste, canal L3 |
| fanuc-torno/04_FLANGE_FACE.nc | Flange com piloto D30 e furo central D18 | G71 + G70, furar D18 (G74) | desbaste, broca D18 |
| siemens-fresa/01_PLACA_FUROS.mpf | Placa com 4 furos D8.5 | centrar (MCALL CYCLE81), furar (MCALL CYCLE83) | broca de centro D6, broca D8.5 |
| siemens-fresa/02_SUPORTE_BOLSA.mpf | Suporte com bolsão 40 × 20 × 6 e 4 furos D6 | bolsão em 2 passes, furar (MCALL CYCLE81) | fresa D10, broca D6 |
| siemens-torno/01_EIXO_CYCLE95.mpf | Eixo escalonado D30 / D40 | CYCLE95 desbaste e acabamento | desbaste R0.8, acabamento R0.4 |
| siemens-torno/02_PINO_CYCLE97.mpf | Pino M30 × 2 | CYCLE95 completo, rosca CYCLE97 | desbaste R0.8, rosca 60° |

## O que o teste confere
`PecasLibraryTests` abre cada programa no simulador: sem erro nem aviso, máquina certa (fresa/torno), bruto lido do comentário, nada abaixo do fundo do bruto e, fora dos ciclos de desbaste, nenhum rápido no material.

## Limites conhecidos
- Furação frontal fora do centro e chanfro com ferramenta cônica ficaram de fora: o simulador só modela ferramenta de topo reto na fresa (sem setup) e não há ferramenta acionada no torno.
- Nos ciclos de desbaste (G71, CYCLE95) o simulador pode marcar como "rápido no material" os afastamentos que o próprio ciclo gera; é artefato da ferramenta pontual, igual ao que acontece em `samples/exemplos`.
- O canal do torno é simulado com largura de ponta (sem os 3 mm da pastilha).
