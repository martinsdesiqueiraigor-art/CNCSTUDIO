# Estado atual

Atualizado no fim de cada tarefa (3–6 linhas). A conversa nova começa por aqui.

- **Na `main` (21/10):** Fase 1 (simulação 3D), CODE-06 interface enxuta (PR #5) e CODE-07 comparação na web/celular, animação do percurso e desktop em paridade (PRs #6 e #7). Web oficial publicada direto da `main`.
- **Interface:** barra fina de ícones, seletor 2D | 2D + 3D | 3D, menu **Ver**, faixa de baixo numa linha com marcas de troca de ferramenta, editor recolhível. Web: `ComparePanel` (lado a lado, destaque das palavras); simulação começa no início; no play a ferramenta percorre o passo e os retornos dos ciclos (Core: `StepAnimation` na web, `LinePacing` no desktop).
- **Mouse do 3D no desktop:** só chega ao `GlSceneView` por `ICustomHitTest`. Testado numa placa real (CODE-05) e por teste sem tela.
- **Conta mestre:** licença vitalícia (20 aparelhos, o máximo do banco) criada no Supabase "Casillas Code" para o e-mail do dono. Mexer no Supabase continua só com autorização dele.
- **Próximo:** CODE-09 ferramentas de torno (ficha `docs/tarefas/CODE-09-ferramentas-torno.md`), depois CODE-08 DNC. Pendências: corte gradual dentro de movimento longo; apagar ramos antigos (CODE-04, do Igor).
- **Prévia:** push em ramo `fase-*`/`ajuste-*` publica `https://<ramo>.casillas-code.pages.dev` e o .exe de teste em Actions → Artifacts. **Testes:** 473 passando.
