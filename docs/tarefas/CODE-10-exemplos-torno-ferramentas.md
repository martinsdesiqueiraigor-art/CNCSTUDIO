# Tarefa CODE-10: programas de exemplo de torno com cada tipo de ferramenta

**Para:** GPT (com acesso ao GitHub)  **Ramo base:** `main`  **Ramo da entrega:** `ext-gpt-code10-torno-ferramentas`

## Objetivo
Criar 6 programas Fanuc de torno, cada um usando um **tipo de ferramenta** diferente, para testar o simulador (que agora desenha e corta de forma diferente por tipo) e servir de base às aulas. O Claude valida cada programa no simulador antes de aceitar.

## Pode mexer em
- `samples/ferramentas-torno/` (pasta nova). Nada fora dela.

## Entregas (Fanuc, nome `NN_DESCRICAO.nc`, maiúsculas, sem acento)
| # | Programa | Ferramenta principal |
|---|---|---|
| 01 | Eixo com 2 canais externos de 3 mm (G75 ou G01) e chanfro | bedame externo, largura 3 mm |
| 02 | Pino com canal e corte final da peça (cut-off) | bedame externo, largura 3 mm |
| 03 | Bucha com furo e mandrilamento de acabamento (G71/G70 interno) | barra interna |
| 04 | Bucha com canal interno de 3 mm de largura | bedame interno |
| 05 | Eixo com rosca externa M20x1,5 (G76) | rosca externa |
| 06 | Bucha com rosca interna M20x1,5 (G76) | rosca interna |

Cada programa:
1. Cabeçalho em comentário: nome da peça, máquina/comando, **bruto no formato `(BRUTO D50 X 70)`**, zero da peça na face.
2. **Tabela de ferramentas no cabeçalho**, uma linha por T, com o tipo escrito assim: `(T02 = BEDAME EXTERNO 3 MM)`, `(T03 = BARRA INTERNA)`, `(T04 = ROSCA EXTERNA)`. O simulador escolhe o desenho pelo tipo cadastrado no Setup, não pelo comentário; a tabela serve para quem abrir o programa saber o que cadastrar.
3. Em cada troca: `T0202 (BEDAME EXTERNO 3MM)`. Furação: `(BROCA D18)`.
4. Comentário curto antes de cada operação.
5. Valores de oficina para aço 1045 (rotação, avanço, passe). Bedame: avanço baixo (0,05 a 0,1 mm/rot). Rosca: use G76 com todos os parâmetros.
6. **Referência do bedame:** o ponto programado é o canto do inserto do lado da face (+Z); o canal ocupa de Z até Z − largura (em direção ao mandril). No simulador do Casillas Code é assim que o bedame corta. Programe com isso em mente.
7. Nada de G00 dentro do material; `M30` no fim. Ferramentas internas nunca saem do furo com G00 encostado na parede.

## Pronto quando
- [ ] 6 arquivos em `samples/ferramentas-torno/`, todos seguindo os itens acima
- [ ] Nenhum arquivo fora da pasta alterado
- [ ] PR aberto (sem merge) com a tabela: programa, ferramentas e tipo de cada T

## Fora do escopo
- Siemens (fica para outra tarefa); fresa; mexer em código; inventar comandos que o Fanuc não tem.
- Perfil real da rosca no simulador: ainda não existe; a rosca simula como sulco fino. Não é erro do programa.

## Referências
- `samples/torno/REBAIXO_CANAL.nc` (cabeçalho e estilo), `samples/exemplos/fanuc-torno/` (ciclos).
- `AGENTS.md` (regras de entrega) e `docs/tarefas/MODELO.md` (formato da Volta).

## Volta (no PR)
```
ID: CODE-10
O que foi feito (até 5 linhas):
Arquivos alterados/criados:
Testes: não rodados (o Claude valida no simulador)
Decisões tomadas (só as autorizadas):
Pendências e dúvidas (A definir):
Riscos ou contradições encontradas:
Próximo passo sugerido (1):
```
Conteúdo técnico vai como `rascunho`; só o Igor marca revisado.
