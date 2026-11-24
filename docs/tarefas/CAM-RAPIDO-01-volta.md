# CAM-RAPIDO-01 — Volta (rápido no material, por operação)

## O que foi feito
- O bruto por operação (`CamStockTimeline`) passa a guardar, em cada operação, os **rápidos (G0) que tiram material** do bruto que as anteriores deixaram. É a mesma conta do simulador do Code (`RapidCollisions` do motor). Guarda também o ponto do primeiro.
- O console mostra um **Erro** por operação: "Operação 1: 6 rápidos (G0) entram no material que as operações anteriores ainda não tiraram (ex.: X… Y… Z…). Colisão provável: confira a ordem das operações ou as alturas."
- O erro não trava o Gerar, como o gouge (verificação, nunca garantia). Só aparece com o bruto por operação ligado e calculado.
- Nada muda no G-code, no arquivo nem nos motores de simulação. O Core só ganhou acréscimos em `CamStockTimeline.cs` (`RapidHits`, `RapidPoint`, `RapidNote`, `RapidNotes`). No App, uma linha em `JobView.axaml.cs`.

## Testes
- Novo `CamRapidInStockTests` (3), com bloco 60 × 60 × 20, bolsão até Z−10 e furos com R em Z−6:
  - furo antes do bolsão: a furação acusa;
  - bolsão antes do furo: nada acusa;
  - o resultado reaproveitado mantém o aviso.
- **Nenhum teste existente alterado.** Core: **2187 verdes**, 8 G-codes intactos. A solução compila sem avisos.

## O que o Igor confere no .exe
1. BOLTED_FLANGE com a Furação antes do Bolsão: Erro na Furação no console.
2. Descer a Furação para depois do Bolsão: o erro some.
