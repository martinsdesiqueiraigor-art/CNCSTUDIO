# Tarefa OTIM-01: Casillas Otimizador (análise e otimização de programas)

**Dono:** Igor. **Ramo:** `fase-5-otimizador` (sobre `fase-4-plataforma-inicio`).
**Origem:** repositório privado `GCodeOptimizer` (núcleo copiado em 24/10/2026; a partir daqui o desenvolvimento continua aqui).

## O que faz
Acha repetições num programa CAM Fanuc (níveis em Z, cópias em XY, passos lineares, bolsões, grades, círculos de furos, hélices, subprogramas M98) e propõe laços Macro B **somente para simulador**. Cada proposta é conferida bloco a bloco contra o original por um interpretador independente. O programa original nunca é alterado. Nada é liberado para máquina (`machineReady` é sempre falso).

## Onde está
| Parte | Arquivo |
|---|---|
| Núcleo | `src/CncStudio.Core/Optimizer/` (namespace `CncStudio.Core.Optimizer`, sem NuGet) |
| Ponte JSON | `Optimizer/OptimizerJson.cs` (sem reflexão; comandos `analyze` e `anonymize`) |
| Tela web | `src/CncStudio.Web/wwwroot/optimizer-ui.js`, `MainView.Optimizer.cs` |
| Entrada | cartão e barra inferior da plataforma (`platform-ui.js`), `#otimizador`, menu Arquivos |
| Testes | `tests/CncStudio.Core.Tests/Optimizer/` (127 testes portados + 3 da ponte JSON) + programas sintéticos em `Optimizer/fixtures/` |

## Fluxo do usuário
Programa aberto no editor → Otimizador → escolhe a entrada decimal do controle (3401#0) → Analisar → vê propostas, bloqueios e economia de linhas → Abrir no editor, Ver em 3D ou Comparar com o original → baixa revisão (.txt) ou cópia anonimizada.

## Regras
- Aviso fixo "Somente para simulador. Não executar na máquina." em todas as telas.
- Programas dos testes são sintéticos (nenhum programa de cliente no repositório público).
- Mensagens vindas do núcleo ainda estão sem acento (herdadas): ajustar junto com os testes que as conferem.

## Falta
- Trocar o interpretador Macro B próprio pelo do Code (uma só fonte de verdade para a conferência e o 3D).
- Acentuar as mensagens do núcleo; imagem do cartão (Igor gera).
- Tela no desktop (Avalonia).
- Testar com programas reais de CAM (anonimizados) e validar em controle real antes de qualquer promessa comercial.
- Licença: módulo `otimizador` incluído no plano PLAT-03 (pacote, avulso, liberado no teste de 30 dias, travado depois com "Comprar licença"); o travamento em si vem com a PLAT-03.
