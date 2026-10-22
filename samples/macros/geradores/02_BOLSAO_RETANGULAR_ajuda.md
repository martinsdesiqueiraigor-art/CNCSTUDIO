# Gerador 02 — Bolsão retangular

**Estado:** rascunho técnico. A macro `O9011` executa passes em Z e varreduras internas com fresa de topo. O sobremetal `U` é preservado nas paredes; **não há passe final de acabamento**. A estratégia demonstrativa pressupõe ferramenta de centro cortante para entrada vertical, condição a validar.

| Campo (G65) | Unidade | Exemplo |
|---|---|---|
| Centro X (`X`) | mm | 50 |
| Centro Y (`Y`) | mm | 40 |
| Comprimento (`I`) | mm | 30 |
| Largura (`J`) | mm | 20 |
| Profundidade (`Z`) | mm | -4 |
| Passe Z (`Q`) | mm | 1 |
| Diâmetro da fresa (`D`) | mm | 10 |
| Sobremetal paredes (`U`) | mm | 0,2 (padrão) |
| Avanço (`F`) | mm/min | 100 |
| Rotação (`S`) | rpm | 1800 |

**Exemplo:** `G65 P9011 X50. Y40. I30. J20. Z-4. Q1. D10. U0.2 F100. S1800.`

**Três erros comuns:** (1) escolher bolsão menor que fresa mais sobremetal; (2) usar fresa sem corte ao centro para mergulho; (3) confundir sobremetal lateral `U` com sobremetal de fundo.

**Conferência antes da máquina:** conferir G54, altura do bruto, comprimento da ferramenta, fixação, estratégia de entrada, cavaco e refrigeração. Simular todas as camadas e avaliar o primeiro mergulho em condição segura. Não há garantia de ausência de colisões.

**Atenção:** a varredura cobre todas as linhas, inclusive a última. Centro X/Y omitido vale 0; `U` omitido ou 0 vira 0,2. Validada no interpretador do Casillas Code (profundidade, linhas e passes); falta validar em controle real.

**A definir:** estratégia industrial de entrada (rampa, pré-furo ou mergulho), acabamento de paredes/fundo, controle de ilhas, canto residual, compatibilidade do interpretador com macro B e validação de trajetórias. **Não executar esta macro na máquina sem revisão técnica.**
