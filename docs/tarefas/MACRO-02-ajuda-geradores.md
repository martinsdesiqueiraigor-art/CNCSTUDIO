# Tarefa MACRO-02: macros Fanuc e texto de ajuda dos 2 primeiros geradores

**Para:** GPT (com acesso ao GitHub)  **Ramo base:** `main`  **Ramo da entrega:** `ext-gpt-macro02-geradores`

## Contexto
A fase Macro do Casillas Code terá geradores de programa: o usuário preenche um formulário e recebe uma macro Fanuc parametrizada, com prévia 3D. Esta tarefa define, **para os 2 primeiros geradores**, a macro de referência e o texto de ajuda. O Claude vai implementar os geradores em cima do que você entregar, e valida cada macro no interpretador do Casillas Code.

## Pode mexer em
- `samples/macros/geradores/` (pasta nova). Nada fora dela.

## Geradores e parâmetros (não mude os nomes nem inclua outros)
### 1. Furação em círculo (BHC)
| Parâmetro | Argumento G65 | Significado |
|---|---|---|
| Centro X | X | posição X do centro do círculo |
| Centro Y | Y | posição Y do centro |
| Diâmetro do círculo | D | diâmetro onde ficam os furos (mm) |
| Número de furos | N | inteiro de 2 a 36 |
| Ângulo inicial | A | graus, a partir de +X, sentido anti-horário (padrão 0) |
| Fundo do furo | Z | Z final, negativo |
| Plano de retorno | R | Z de aproximação (padrão 2) |
| Passo de picada | Q | se informado, usa G83; senão G81 |
| Avanço | F | mm/min |

### 2. Bolsão retangular
| Parâmetro | Argumento G65 | Significado |
|---|---|---|
| Centro X | X | centro do bolsão |
| Centro Y | Y | centro do bolsão |
| Comprimento | I | medida em X (mm) |
| Largura | J | medida em Y (mm) |
| Profundidade | Z | Z final, negativo |
| Passe em Z | Q | profundidade por passada |
| Diâmetro da fresa | D | mm |
| Sobremetal de acabamento | U | mm nas paredes (padrão 0,2) |
| Avanço | F | mm/min |
| Rotação | S | rpm |

## Entregas (por gerador, 3 arquivos)
1. `NN_<nome>_macro.nc` — a macro Fanuc **Macro B** (`O9010`, `O9011`), com `#1..#33` documentados em comentário, sem G-code Siemens, usando só `IF/GOTO/WHILE/DO/END`, `SIN`, `COS`, `#` e G65. Cálculo das posições dos furos (BHC) e dos passes (bolsão) feito na macro.
2. `NN_<nome>_exemplo.nc` — programa que chama a macro com G65 (um exemplo completo, bruto 100 x 80 x 20 em aço 1045, comentário `(BRUTO 100 X 80 X 20)` e zero da peça no canto, topo em Z0).
3. `NN_<nome>_ajuda.md` — texto de ajuda em português do Brasil, até 1 página: o que o gerador faz, tabela de parâmetros com unidade e valor típico, 3 erros comuns, como conferir antes de rodar na máquina. Sem prometer segurança de colisão.

## Regras
- Valores de oficina realistas (aço 1045). Sem G00 dentro do material.
- Nada de variáveis de sistema que o Fanuc não tenha. Se não souber, escreva **A definir** no PR.
- O que não foi decidido fica **A definir**; não escolha por omissão.

## Pronto quando
- [ ] 6 arquivos em `samples/macros/geradores/`
- [ ] PR aberto, sem merge, com a lista de arquivos e as dúvidas

## Fora do escopo
- Código C#, Siemens, apalpador, rosca helicoidal, G65 genérico.

## Volta (no PR)
```
ID: MACRO-02
O que foi feito (até 5 linhas):
Arquivos alterados/criados:
Testes: não rodados (o Claude valida no interpretador)
Decisões tomadas (só as autorizadas):
Pendências e dúvidas (A definir):
Riscos ou contradições encontradas:
Próximo passo sugerido (1):
```
Conteúdo técnico vai como `rascunho`; só o Igor marca revisado.
