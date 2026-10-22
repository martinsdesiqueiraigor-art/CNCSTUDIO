# MACRO-01 — Geradores de programa (Fase 2)

**Estado:** entrega 1 em prévia (ramo `fase-2-macro-geradores`). Conteúdo técnico = rascunho.

## O que é
Formulário → programa Fanuc → prévia 3D. A prévia é o próprio programa gerado passando pelo analisador e pelo simulador do editor (nada de motor 3D novo).

## Entrega 1 (web)
- Core `src/CncStudio.Core/Generators/`: `ProgramGenerator` (campos, validação, geração), `BhcGenerator` (furação em círculo), `PocketGenerator` (bolsão retangular), `GeneratorsJson` (ponte com a tela).
- Saída à escolha: **programa pronto** (G81/G83 ou G01 calculados) ou **G65 + macro** (O9010/O9011 embutidas do `samples/macros/geradores/`, uma fonte de verdade).
- Web: menu Arquivos → **Geradores de programa…** (`wwwroot/generators-ui.js`, formulário em HTML; lembra os últimos valores). Botões: *Abrir no editor* e *Ver em 3D*.
- Testes: programa pronto e macro geram os mesmos furos e os mesmos movimentos de corte (rodando no interpretador); validação com mensagens em português.

## Limites conhecidos
- Só Fanuc, fresa. Bolsão: entrada vertical (fresa com corte ao centro), sem passe de acabamento, sem ilhas.
- Em G65 o número de furos vai em `H` (a letra `N` não é argumento no Fanuc); `U` omitido ou 0 vira 0,2 na macro (o gerador exige U > 0).
- Desktop: ainda não tem tela (mesmo Core, próxima entrega).
- A ferramenta e os cortes são sugestões; confira com a máquina.

## Próximos geradores
Furo circular / interpolação, fresamento de rosca helicoidal, cabeçalho G65 próprio, depois apalpador.
