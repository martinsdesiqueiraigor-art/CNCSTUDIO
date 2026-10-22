# CODE-13 — Fidelidade geométrica das ferramentas 3D (piloto)

**Estado:** rascunho — revisão do proprietário e Claude pendente  
**Repositório:** `martinsdesiqueiraigor-art/CNCSTUDIO`  
**Ramo base:** `main`  
**Ramo sugerido:** `ext-gpt-code13-biblioteca-3d`  
**Integração:** Pull Request para `main`, **sem merge**.

## Objetivo
Melhorar a identificação visual das ferramentas no Casillas Code sem trocar o motor de cena ou modificar a simulação de corte. Entregar três modelos-piloto: (1) bedame externo de 3 mm; (2) porta-ferramenta externo PCLNR 2525 com inserto CNMG 120408; (3) fresa de topo de metal duro Ø10, quatro cortes, com hélices visíveis.

## Diagnóstico confirmado no código de `main`
- `src/CncStudio.Core/Scene/LatheToolShape.cs` define perfis 2D, dimensões e a referência da ponta para torno. `CutOf` define o bedame a partir do canto do lado da face, com corte ocupando Z de 0 a −largura.
- `src/CncStudio.Core/Scene/SceneMeshes.cs` gera `SolidMesh` de ferramenta e suporte; o torno já possui inserto, sede, quebra-cavaco e grampo/parafusos; fresas e brocas são geradas sobretudo por revolução, sem canais helicoidais reais.
- `src/CncStudio.Core/Scene/SimulationScene.cs` usa `SceneMeshes.LatheTool` e `SceneMeshes.MillTool`, posiciona ambos pela ponta e mantém malhas em cache por número da ferramenta.
- Web: `src/CncStudio.Web/wwwroot/scene3d.js` recebe vértices/normais/cores/índices e renderiza com three.js. Desktop: OpenGL. A geometria compartilhada no Core deve continuar sendo a fonte única.

## Arquivos autorizados para implementação pelo Claude
- `src/CncStudio.Core/Scene/LatheToolShape.cs`
- `src/CncStudio.Core/Scene/SceneMeshes.cs`
- Novos arquivos auxiliares **somente** em `src/CncStudio.Core/Scene/`, se indispensáveis para geração de malhas.
- Novos testes **somente** em `tests/CncStudio.Core.Tests/`.

**Não alterar:** cálculo de remoção de material, interpolação de G-code, `SimulationScene.SetTool`, Setup e persistência, renderizadores web/desktop, dependências, arquivos de distribuição, licenciamento e CI. Qualquer necessidade de alteração fora do escopo exige autorização antes da implementação.

## Etapa 1 — Bedame externo 3 mm
- Manter largura **exatamente 3 mm** quando `InsertWidth = 3`; preservar a referência do **canto esquerdo do inserto (lado da face)** e ocupação de Z = 0 a −3 mm no sistema de torno.
- Separar visualmente lâmina, inserto de metal duro e bloco de fixação; representar assento e detalhes simplificados de fixação sem inventar dimensões de fabricante.
- Evitar interferência aparente entre lâmina, inserto e bloco; manter a ponta na origem do modelo.
- Para dimensões de fabricante não confirmadas: **A definir**. Os valores geométricos existentes não devem ser apresentados como cotas oficiais.

## Etapa 2 — PCLNR 2525 / CNMG 120408
- Preservar seção de cabo 25 × 25 mm e geometria de inserto de 80°; referência de ponta na origem.
- Diferenciar corpo quadrado, cabeça inclinada, assento, inserto com espessura nominal 4,76 mm, quebra-cavaco e sistema de fixação.
- Revisar a geometria do inserto e a disposição do grampo para impedir sobreposições ou peças flutuantes.
- **A definir:** comprimento exato do suporte e detalhes de grampo de um fabricante/código específico; não alegar fidelidade dimensional de catálogo sem referência.

## Etapa 3 — Fresa de topo Ø10, 4 cortes
- Criar quatro canais helicoidais **visíveis** ao longo da parte cortante; distinguir haste cilíndrica e região de corte.
- Hélice com continuidade visual e arestas sem descontinuidades; ponta de corte na origem, eixo em +Z, como o contrato atual de `MillTool`.
- Manter diâmetro externo nominal de 10 mm e quatro arestas; comprimento cortante e ângulo de hélice: **A definir** até escolha de modelo de catálogo ou aprovação explícita de valores ilustrativos.
- Não modificar `ToolProfile` nem a geometria usada para remoção de material: as hélices são **apenas visuais**.
- Fornecer uma versão de malha de baixa complexidade se a nova geometria impactar o smartphone; não introduzir formatos externos nem dependências.

## Regras técnicas obrigatórias
1. `SolidMesh` permanece como contrato de saída; geometria única para web e desktop.
2. Separar `Cutter` e `Holder` e preservar a referência da ponta; não deslocar a ferramenta ao alternar tipos.
3. Normais coerentes, faces sem degenerações/NaN, sem inversão de orientação ou fendas perceptíveis.
4. Nenhuma mudança no motor de corte, na trajetória CNC ou nas regras de colisão.
5. Não criar geometrias complexas em cada quadro: gerar/cachar conforme arquitetura atual.
6. Não usar fotografias ou modelos CAD de terceiros como assets sem checar licenças; preferir geometria própria parametrizada.
7. Todo conteúdo é **rascunho técnico**, sujeito à revisão humana.

## Critérios de aceitação
- [ ] Bedame de 3 mm: largura, referência Z e orientação visual corretas.
- [ ] PCLNR: inserto 80°, espessura nominal e corpo 25 × 25 visíveis; fixação plausível.
- [ ] Fresa Ø10: quatro hélices/canais distinguíveis na vista lateral e na vista de topo.
- [ ] Mesma posição da ponta antes/depois da mudança, no torno e na fresa.
- [ ] Geometria visível no desktop e na web, sem regressão no corte simulado.
- [ ] Testes unitários de limites geométricos, contagem de elementos e valores finitos.
- [ ] Capturas comparativas de três ferramentas em duas vistas cada, mais verificação de desempenho em smartphone.
- [ ] `dotnet test` (ou justificativa documentada se ambiente indisponível).
- [ ] PR aberto, sem merge, apenas com arquivos autorizados.

## Sequência e pontos de controle
1. Implementar **somente o bedame**, gerar capturas e testes; revisar geometria antes de avançar.
2. Implementar PCLNR, conferir dimensões/encaixes e regressão do bedame.
3. Implementar fresa helicoidal e comparar desempenho com a malha atual.
4. Consolidar evidências e abrir PR. O proprietário e Claude validam antes de qualquer merge.

## Volta no PR
```
ID: CODE-13
O que foi feito (até 5 linhas):
Arquivos alterados/criados:
Testes e evidências:
Decisões tomadas (só as autorizadas):
Pendências e dúvidas (A definir):
Riscos ou contradições encontradas:
Próximo passo sugerido (1):
```

## Pendências de governança
- A CODE-13 é uma **proposta nova**, não uma ficha previamente aprovada no repositório.
- Alterações em `src/CncStudio.Core` precisam de autorização explícita pela regra de `AGENTS.md`; esta ficha delimita o escopo proposto, mas não substitui a aprovação do proprietário.
- A tarefa não autoriza merge nem deploy.
