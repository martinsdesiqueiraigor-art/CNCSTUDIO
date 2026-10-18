# Tarefa T01: peças-exemplo completas (programas CNC comentados)

**Para:** GPT (com acesso ao GitHub)  **Ramo base:** `fase-1-simulacao`  **Ramo da entrega:** `ext-gpt-t01-pecas`

## Objetivo
Criar 12 programas de **peças completas** (várias operações numa peça só), para demonstrar a simulação 3D e servir de base às aulas do Academy. Os programas atuais em `samples/exemplos/` mostram um comando de cada vez; estes mostram uma peça real do começo ao fim.

## Pode mexer em
- `samples/pecas/` (pasta nova). Nada fora dela.

## Entregas
| Pasta | Quantidade | Exemplos de peça |
|---|---|---|
| `samples/pecas/fanuc-fresa/` | 4 | flange com furação em círculo; placa com bolsão e contorno; suporte com rasgo e furos roscados; tampa com face e chanfro |
| `samples/pecas/fanuc-torno/` | 4 | eixo escalonado com rosca; bucha com furo e canal interno; pino com raio e canal; flange torneada com furação frontal |
| `samples/pecas/siemens-fresa/` | 2 | as mesmas ideias, com CYCLE e MCALL |
| `samples/pecas/siemens-torno/` | 2 | com CYCLE95 e CYCLE97 |

Cada programa:
1. Nome: `NN_DESCRICAO_CURTA.nc` (Fanuc) ou `.mpf` (Siemens), em maiúsculas, sem acento.
2. Cabeçalho em comentário com: nome da peça, máquina e comando, **bruto no formato `(BRUTO 100 X 60 X 20)`** na fresa ou **`(BRUTO D50 X 70)`** no torno, onde fica o zero da peça.
3. Em cada troca de ferramenta, comentário com o diâmetro: `T1 M06 (FRESA D10)`, `(BROCA D8.5)`, `(MACHO M10)`. No torno: `T0101 (DESBASTE EXTERNO R0.8)`.
4. Comentário curto antes de cada operação: `(2 - BOLSAO 40 X 30 PROF 5)`.
5. Valores reais de oficina (avanço, rotação, profundidade de passe) para aço 1045, e `M30` no fim.
6. **Nada de G00 dentro do material** e nada de ferramenta descendo abaixo do fundo do bruto.

Um arquivo `samples/pecas/LEIA-ME.md` com uma tabela: arquivo, peça, operações, ferramentas.

## Pronto quando
- [ ] 12 programas nas 4 pastas, mais o LEIA-ME.md
- [ ] Todos com BRUTO e diâmetro de cada ferramenta em comentário
- [ ] PR aberto para `fase-1-simulacao`, listando as peças e qualquer dúvida

## Fora do escopo
- Código C#, testes, interface, outras pastas.

## Referências
- Estilo dos comentários: `samples/exemplos/fanuc-fresa/01_G81_FURACAO_SIMPLES.nc` e `samples/EXEMPLO.nc`.
- Regras gerais: `AGENTS.md`.

## Revisão (Claude)
Abre cada programa no simulador por teste automático: sem erros, sem rápido no material, bruto lido do comentário.
