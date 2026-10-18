# Peças-exemplo CNC

Exemplos didáticos: **não executar em máquina sem simulação e revisão técnica**.

| Arquivo | Peça | Operações | Ferramentas |
|---|---|---|---|
| fanuc-fresa/01_FLANGE_FUROS.nc | FLANGE COM FUROS | Preparação, usinagem e retorno | T1 M06 (FRESA D10), T2 M06 (BROCA D8) |
| fanuc-fresa/02_PLACA_BOLSA.nc | PLACA COM BOLSA | Preparação, usinagem e retorno | T1 M06 (FRESA D10) |
| fanuc-fresa/03_SUPORTE_RASGO.nc | SUPORTE COM RASGO | Preparação, usinagem e retorno | T1 M06 (FRESA D8), T2 M06 (BROCA D5) |
| fanuc-fresa/04_TAMPA_FACE.nc | TAMPA FACE CHANFRO | Preparação, usinagem e retorno | T1 M06 (FRESA D40), T2 M06 (CHANFRADOR D12) |
| fanuc-torno/01_EIXO_ROSCA.nc | EIXO ESCALONADO ROSCA | Preparação, usinagem e retorno | T0101 (DESBASTE EXTERNO R0.8), T0202 (ROSCA EXTERNA 60 GRAUS), G76 P020060 Q100 R0.05 |
| fanuc-torno/02_BUCHA_FURO.nc | BUCHA FURO CANAL | Preparação, usinagem e retorno | T0101 (DESBASTE EXTERNO R0.8), T0303 (CANAL EXTERNO L3) |
| fanuc-torno/03_PINO_CANAL.nc | PINO RAIO CANAL | Preparação, usinagem e retorno | T0101 (DESBASTE EXTERNO R0.8), T0202 (CANAL EXTERNO L3) |
| fanuc-torno/04_FLANGE_FACE.nc | FLANGE TORNEADA | Preparação, usinagem e retorno | T0101 (DESBASTE EXTERNO R0.8), T0202 (FERRAMENTA FRONTAL R0.4) |
| siemens-fresa/01_PLACA_FUROS.mpf | PLACA FUROS MCALL | Preparação, usinagem e retorno | T1 D1 ; (BROCA D8) |
| siemens-fresa/02_SUPORTE_BOLSA.mpf | SUPORTE BOLSA MCALL | Preparação, usinagem e retorno | T1 D1 ; (FRESA D10), T2 D1 ; (BROCA D6) |
| siemens-torno/01_EIXO_CYCLE95.mpf | EIXO CYCLE95 | Preparação, usinagem e retorno | T1 D1 ; (DESBASTE EXTERNO R0.8) |
| siemens-torno/02_PINO_CYCLE97.mpf | PINO CYCLE97 | Preparação, usinagem e retorno | T1 D1 ; (DESBASTE EXTERNO R0.8), T2 D1 ; (ROSCA EXTERNA 60 GRAUS) |

## Pendências
- CYCLE95 e CYCLE97 Siemens estão documentados, porém comentados até confirmar assinatura e versão do comando.
- Raio do pino e furação frontal excêntrica permanecem pendentes de geometria e recursos da máquina.
- Parâmetros e trajetórias exigem revisão no simulador e por profissional antes de qualquer execução.
