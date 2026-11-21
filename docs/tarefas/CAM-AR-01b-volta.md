# CAM-AR-01b — Volta (começar no material: tela)

## O que foi feito
- **Aba Alturas:** seção "Topo a partir de" com Topo = Digitado / Material (bruto que sobrou). Com Material: topo do material aqui, folga de segurança, altura de avanço, passes em Z antes → depois, nota e aviso "Isto muda o G-code". No Chanfro, Furação e face girada aparece só a nota de por que não vale.
- **Árvore:** selo ao lado do selo do bruto: "Z-2 mat." (verde, ajustada), "digitado" (âmbar, não aplicado, com o motivo na dica) ou "mat.…" (calculando).
- **Faixa:** chave "Novas no material: Ligado/Desligado" (preferência do app `CamNewOpsFromStock`, nasce desligada). Faceamento, Contorno e Bolsão novos (menu e clique no 3D) nascem com Material. As operações que já existem não mudam.
- **Console:** os avisos de "começar no material" vão como **Conferir**. O aviso fixo do bruto por operação diz que o bruto entra no G-code quando alguma operação começa no material.
- **Programa sem travar a tela:** o G-code sai do bruto por operação calculado fora da tela (Core novo, por acréscimo: `CamJobBuilder.BuildFrom`). Com alguma operação em Material, o cálculo roda mesmo com "Bruto por operação" desligado. Enquanto calcula, a aba Código NC mostra "calculando o material…" e Enviar, Gerar e Copiar ficam travados (o programa digitado nunca sai). O app agora passa o banco ativo ao cálculo (forma das ferramentas).

## Comportamento de referência copiado
Fusion, aba Heights ("Top Height from Stock top", "Feed Height"), com o bruto em processo.

## Diferenças da imagem aprovada
- O corte lateral no 3D (topo digitado, topo do material, início do avanço) ficou fora, como dito no plano; os números estão na aba Alturas.
- O selo da árvore ficou curto ("Z-2 mat.") porque divide a linha com o tempo e o selo do bruto (240 px); o texto inteiro está na dica.

## Testes
- Novo no Core: `Programa_do_calculo_fora_da_tela_igual_ao_gerar` (o programa da tela é o mesmo do Gerar em sequência; sem cálculo, digitado com aviso; sem Material, igual ao de sempre).
- Core: **2157 verdes**, 8 G-codes intactos. **Nenhum teste existente alterado.** App compila sem avisos.
- Tela sem 3D, 1280 × 800 (Xvfb), bloco 70 × 50 com Faceamento Z0→Z−2 e Bolsão Ø24 Z0→Z−8 em Material, T1 "topo" Ø10 no banco: selo "Z-2 mat.", aba Alturas (topo −2, folga 1,175, avanço 0, passes 3 → 2), console Conferir, G-code do bolsão com `G0 Z0` e `G1 Z-2` antes da hélice. Chave da faixa cabe em 1280.

## O que o Igor confere no .exe
1. `BOLTED_FLANGE-3ops`: T1 no banco com tipo (topo). No Contorno, aba Alturas → Topo: Material. Aparece topo do material 0, folga ≈1,3, altura de avanço, passes 5 → 4 (com passe 4 de Z2 a Z−16) e o selo "Z0 mat." na árvore.
2. Código NC: o contorno desce em rápido até a altura de avanço e corta de Z0 para baixo; a simulação mostra a peça como antes.
3. Topo: Digitado volta o G-code ao de antes. Durante o cálculo, Enviar/Copiar ficam travados.
4. Ferramenta sem tipo no banco: selo "digitado" e aviso Conferir com o motivo.
5. "Novas no material: Ligado" e um Bolsão novo já nasce em Material. Salvar com Material → arquivo versão 9; abre igual.
6. Tempo do Gerar na peça (anotar).
