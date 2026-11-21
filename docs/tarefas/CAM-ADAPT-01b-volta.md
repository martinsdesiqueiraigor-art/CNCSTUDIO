# CAM-ADAPT-01b — Volta (desbaste adaptativo: borda aberta, raio mínimo, sentidos)

## O que foi feito
- **Borda aberta, entrando por fora** (Fusion 2D Adaptive):
  - **Material de verdade:** no bolsão automático com borda aberta, o adaptativo recebe o corte do bruto naquela altura (`PocketParams.AdaptiveStock`, calculado pelo trabalho como o ponto do pré-furo; não vai para o arquivo). O material conta só dentro do bruto.
  - **Espaço em volta:** o contorno de fora fica com 2 × Ø + 1 mm de folga em volta do bruto, e não ganha volta na parede (lá não há parede).
  - **Entrada:** desce em avanço no ar, fora do bruto, no ponto livre mais perto do material, e entra de lado com a carga controlada. A hélice só entra quando uma região não se alcança por fora; aí, sem hélice informada, dá erro com mensagem.
  - **Entrada da operação:** com borda aberta não precisa ser hélice nem pré-furo.
- **Os dois sentidos:**
  - **Desligado:** cada passo procura primeiro as direções concordantes (material novo à direita do avanço). Sem nenhuma, dá um passo discordante curto em vez de subir para ligar.
  - **Ligado:** vale qualquer lado.
- **Raio mínimo:** cada passo procura primeiro as direções que ficam dentro de uma curva desse raio. Sem nenhuma, fica com o passo livre.

## Diferenças do plano (importante)
- **Raio mínimo ficou como preferência, não garantia.** Medido no retângulo 80 × 50 (Ø10, carga 1, raio 4): o motor só achou uma direção suave em ~58% dos passos; nos outros, para não passar da carga, precisou virar mais. As juntas fechadas quase não mudaram (1868 × 1935 sem raio). Garantir o raio exige replanejar o caminho (e não só escolher passo a passo). **Proposta:** ficha própria depois da tela, ou aceitar como está.
- **"Só concordante" ficou como preferência.** Medido: ~80% do material sai concordante (com os dois sentidos, ~75%). Na versão estrita, toda ponta de faixa vira uma ligação por baixo, e o cálculo ficou ~8 vezes mais lento (5 s × 0,6 s num bolsão de 80 × 50). Fica a escolha do Igor: estrito e lento, ou como está.
- A conferência de passe em Z × comprimento de corte vai para a fatia c (tela), como avisado.

## Testes
- Novos em `AdaptiveTests` (3):
  - **Borda aberta:** área em volta de um ressalto Ø20 num bruto 80 × 50. A primeira descida fica fora do bruto, com a ferramenta toda no ar, e sem hélice. A largura de corte respeita a carga no material do bruto. Nunca encosta no ressalto. O caminho tem menos de 60% do que teria tratando o espaço livre como material.
  - **Concordante:** sem os dois sentidos, pelo menos 75% do material sai concordante, e nunca menos que com os dois sentidos. Com os dois sentidos, a largura respeita a carga.
  - **Raio mínimo:** gera, e as juntas fechadas não aumentam.
- Os 17 da fatia a continuam verdes. **Nenhum teste existente alterado.** O arquivo não muda de versão.
- Core: **2177 verdes**, 8 G-codes intactos. App compila sem avisos.

## O que o Igor confere no .exe
Nada ainda (tela na fatia c).

## Decisões pendentes com o Igor
1. Raio mínimo: aceitar como preferência, ou ficha própria para garantir (replanejamento do caminho)?
2. Só concordante: preferência rápida (como está) ou estrita (bem mais lenta)?
