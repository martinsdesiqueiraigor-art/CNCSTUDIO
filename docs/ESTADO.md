# Estado atual

Atualizado no fim de cada tarefa (3–6 linhas). A conversa nova começa por aqui.

- **Na `main` (21/10):** Fase 1 (simulação 3D), CODE-06 interface enxuta (PR #5) e CODE-07 comparação na web/celular, animação do percurso e desktop em paridade (PRs #6 e #7). Web oficial publicada direto da `main`.
- **Interface:** barra fina de ícones, seletor 2D | 2D + 3D | 3D, menu **Ver**, faixa de baixo numa linha com marcas de troca de ferramenta, editor recolhível. Web: `ComparePanel` (lado a lado, destaque das palavras); simulação começa no início; no play a ferramenta percorre o passo e os retornos dos ciclos (Core: `StepAnimation` na web, `LinePacing` no desktop).
- **Mouse do 3D no desktop:** só chega ao `GlSceneView` por `ICustomHitTest`. Testado numa placa real (CODE-05) e por teste sem tela.
- **Conta mestre:** licença vitalícia (20 aparelhos, o máximo do banco) criada no Supabase "Casillas Code" para o e-mail do dono. Mexer no Supabase continua só com autorização dele.
- **Próximo:** CODE-09 ferramentas de torno (ficha `docs/tarefas/CODE-09-ferramentas-torno.md`), depois CODE-08 DNC. Pendências: corte gradual dentro de movimento longo; apagar ramos antigos (CODE-04, do Igor).
- **Prévia:** push em ramo `fase-*`/`ajuste-*` publica `https://<ramo>.casillas-code.pages.dev` e o .exe de teste em Actions → Artifacts. **Testes:** 473 passando.

- CODE-09 na `main` (21/10, PR #8): tipos de ferramenta de torno (desbaste, barra interna, bedame, rosca) com desenho e corte próprios; editor só na web. Entrega 2 (PR #15, 22/10): tipo e largura do inserto na tabela de ferramentas do desktop. Falta: perfil real da rosca.
- CODE-13 na `main` (22/10, PR #14): bedame externo (cabeça fina, alargamento a 45°, cabo 25×25, garra e parafuso), PCLNR com garra em cunha e fresa de topo com 4 canais helicoidais (só visuais). Falta: bedame interno e barras (referências MVQNR), inclinação de 10° da cabeça, ver no app com GPU. **Testes:** 509.
- Também na `main` (22/10): CODE-10 (6 programas de torno), MACRO-02 (macros O9010 furação em círculo e O9011 bolsão, rascunho, validadas no interpretador; PR #13) e CODE-11 (referência visual de ferramentas). Próximo: bedame interno e barras (MVQNR), Macro 01 (geradores), Academy piloto.
