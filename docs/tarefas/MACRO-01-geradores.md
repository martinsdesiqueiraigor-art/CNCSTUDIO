# MACRO-01 — Geradores de programa (Fase 2)

**Estado:** entregas 1 e 2 em prévia (ramo `fase-2-macro-geradores`). Conteúdo técnico = rascunho.

## O que é
Formulário → programa Fanuc → prévia 3D. A prévia é o próprio programa gerado passando pelo analisador e pelo simulador do editor (nada de motor 3D novo).

## Entrega 1 (web)
- Core `src/CncStudio.Core/Generators/`: `ProgramGenerator` (campos, validação, geração), `BhcGenerator` (furação em círculo), `PocketGenerator` (bolsão retangular), `GeneratorsJson` (ponte com a tela).
- Saída à escolha: **programa pronto** (G81/G83 ou G01 calculados) ou **G65 + macro** (O9010/O9011 embutidas do `samples/macros/geradores/`, uma fonte de verdade).
- Web: menu Arquivos → **Geradores de programa…** (`wwwroot/generators-ui.js`, formulário em HTML; lembra os últimos valores). Botões: *Abrir no editor* e *Ver em 3D*.
- Testes: programa pronto e macro geram os mesmos furos e os mesmos movimentos de corte (rodando no interpretador); validação com mensagens em português.

## Entrega 2 (web, mesmo ramo)
Mais 6 geradores, cada um com macro (`samples/macros/geradores/0N_*_macro.nc`) e programa pronto:
| Gerador | Macro | Observação |
|---|---|---|
| Furação em grade | O9012 | colunas × linhas, ângulo, serpentina, G81/G83 |
| Bolsão circular | O9015 | anéis do centro para a parede, G03 (concordante) |
| Rasgo | O9014 | linhas ao longo do rasgo, extremidades com raio da fresa |
| Faceamento | O9013 | zigue-zague entrando e saindo por fora, passo lateral em % |
| Furo helicoidal | O9016 | hélice G03 + volta final; furo de 1× a 2× a fresa |
| Rosca helicoidal | O9017 | interna/externa, direita/esquerda, passe único, sem arco de entrada |
Teste genérico: programa pronto e macro geram os mesmos movimentos de corte nos 8 geradores (551 testes).

## Limites conhecidos
- Rosca helicoidal: passe único na profundidade e aproximação radial (sem arco de entrada); a simulação 3D mostra o percurso, não o perfil do filete.
- Só Fanuc, fresa. Bolsão: entrada vertical (fresa com corte ao centro), sem passe de acabamento, sem ilhas.
- Em G65 o número de furos vai em `H` (a letra `N` não é argumento no Fanuc); `U` omitido ou 0 vira 0,2 na macro (o gerador exige U > 0).
- Desktop: ainda não tem tela (mesmo Core, próxima entrega).
- A ferramenta e os cortes são sugestões; confira com a máquina.

## Próximos
Ciclo G65 próprio, apalpador, geradores de torno (G76), tela no desktop.
