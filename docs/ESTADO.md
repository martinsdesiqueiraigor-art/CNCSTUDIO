# Estado atual

Atualizado no fim de cada tarefa (3–6 linhas). A conversa nova começa por aqui.

- **Ramo ativo:** `ajuste-interface` (CODE-06). Fase 1 na `main` desde 18/10 (PR #3), com a web oficial e o painel publicados.
- **Feito (ajustes do 3D):** A) fundo e material foscos, só o trecho atual da trajetória; B) contorno liso com arestas marcadas; C) porta-ferramenta BT40, porca ER, morsa, placa e grampo; voltar passo com até 16 pontos de retorno; bruto redondo e tubo na fresa.
- **3D do desktop:** testado numa placa de vídeo real (gira, move, zoom). O mouse só chega ao `GlSceneView` por `ICustomHitTest` (sem ele, nada responde).
- **CODE-06 feito (aguarda revisão do Igor):** barra fina de ícones, seletor 2D | 2D + 3D | 3D, menu Ver, faixa de baixo numa linha com marcas de troca de ferramenta e distância percorrida/total, editor recolhível, fundo em degradê e material transparente — desktop e web. Falta testar o mouse no 3D numa placa real e ver a web pela prévia (celular).
- **CODE-06 na `main` (PR #5, 21/10).** **CODE-07 em andamento** (ramo `ajuste-comparar`): comparação de programas na web/celular — painel "Comparar" no menu Arquivos (`ComparePanel`), com `Core/Editing/DiffNavigation` (blocos, resumo, contexto, linha do editor). Falta o Igor testar no celular.
- **Depois:** CODE-08 (DNC). PRs #1 (peças) e #2 (glossário) já estão na `main`.
- **Prévia:** `https://fase-1-simulacao.casillas-code.pages.dev` e o .exe em Actions → Artifacts. Actions fixadas por SHA (exigência do repositório).
- **Testes:** 463 passando (14 novos de comparação).
