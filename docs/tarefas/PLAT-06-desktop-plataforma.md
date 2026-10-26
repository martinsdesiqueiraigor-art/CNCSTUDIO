# PLAT-06: Plataforma Casillas no desktop — plano (aprovado em ordem pelo Igor, 26/10)

**Status:** plano. Nada codado. Cada etapa é um ramo (`ajuste-desktop-...`), com prévia (.exe em Actions → Artifacts) e PR; nada na `main` sem OK.

## Situação
- O desktop (`CncStudio.App`, Avalonia 11.3) tem só o Code: uma janela (`MainWindow`) com abas de documento, barra de ferramentas e painéis.
- Início, Macro, Otimizador, Academy, Loja, Tema e conta única existem só na web (`platform-ui.js` e módulos HTML). O **Core é o mesmo**: falta só a parte visual.

## Decisão
Telas **nativas em Avalonia**, um módulo por vez, no mesmo programa. Sem navegador embutido (limitado no Avalonia gratuito, mais lento, não é "programa de verdade").

## Estrutura proposta
- Uma **barra de módulos** na lateral esquerda da janela (Início, Code, Macro, Otimizador, depois Academy, CAM, Loja) que troca o conteúdo central. O Code atual vira um módulo (`CodeView`) sem mudar o comportamento.
- Cada módulo é um `UserControl` em `src/CncStudio.App/Modules/`. A janela só hospeda e troca.
- **Tema** (padrão ↔ CMYK) em um recurso único, trocado por um botão fixo na barra de módulos (mesma paleta da web).

## Etapas (ordem aprovada)
| # | Etapa | Observação |
|---|---|---|
| 1 | Barra de módulos + Início + Tema CMYK | Cria o lugar do CAM; Code continua igual |
| 2 | Otimizador | Core pronto (`Core/Optimizer`); só a tela |
| 3 | Macro (geradores) | Core pronto (`Core/Generators`); formulário + prévia 3D |
| 4 | Conta e licença no desktop (PLAT-03) | Trava para vender; termos e página de vendas são do Igor; exige análise de risco |
| 5 | ~~Academy~~ | **Decidido (Igor, 26/10): só na versão web.** Não entra no desktop |
| 6 | Loja | ✅ feita (catálogo único com a web; pedido pelo WhatsApp) |
| — | **CAM** | Entra como módulo novo da barra (ver `CAM-01-plano.md`), a partir da etapa 1 |

## Regras
- Core sem NuGet, warnings = erro, testes antes de commit. Lógica no Core; telas finas.
- Textos em pt-BR com acentos; desktop sem placa de vídeo para testes: `CASILLAS_SOFTWARE_GL=1 xvfb-run ...`.
- Não mexer em Supabase, painel de ativação, termos e página de vendas.

## A definir
- Barra lateral ou abas superiores? (sugestão: lateral, cabe mais módulos)
- Quanto do Início da web trazer (8 cartões ou só os módulos disponíveis no desktop)?
- Instalador e assinatura do .exe (decisão do Igor, fora deste plano).
