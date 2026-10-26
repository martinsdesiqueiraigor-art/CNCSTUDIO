# CAM-01: Casillas CAM 2,5D — plano (rascunho para aprovação do Igor)

**Status:** plano. Nada codado. Só começa depois do OK do Igor nas **decisões** abaixo.

## Objetivo
Do desenho ao programa: o usuário traz um contorno 2D (DXF ou desenhado), escolhe operações (faceamento, contorno, bolsão, furação), e o Casillas CAM gera o G-code Fanuc ou Siemens de fresa, já mostrado no simulador do Code. Referência de mercado: MacroMill CNC (inspirar, não copiar).

## Princípios (do plano mestre)
- Um Core só em C# (`src/CncStudio.Core/Cam/`), **sem NuGet**, com testes; telas finas.
- O programa gerado passa pelo mesmo `MachineExecutor` do Code: o teste de cada operação é "gera G-code → simula → confere o percurso e a profundidade".
- Aproveitar o que existe: `Generators/` (faceamento, bolsões, furação, rasgo), `Geometry/`, dialetos, simulação 3D.
- Colisão é "verificação", nunca garantia. Texto honesto: "Simule antes de rodar na máquina".
- CAM é módulo **pago à parte** (decisão vigente); valor fica no repositório privado.

## Entregas (cada uma = um ramo, uma prévia, um PR)
| # | Entrega | O que fica pronto | Teste |
|---|---|---|---|
| 1 | Geometria 2D | Segmentos, arcos, círculos, polilinhas; fechar contorno; orientação; **offset (deslocamento) de contorno** para dentro e para fora | Casos conhecidos (retângulo, círculo, cantos côncavos) |
| 2 | Importar DXF | LINE, ARC, CIRCLE, LWPOLYLINE, POLYLINE; unir segmentos soltos em contorno; unidade | DXFs de exemplo (feitos à mão) |
| 3 | Operações | Faceamento, **contorno** (fora/dentro, passes em Z, entrada/saída em arco), **bolsão** (por offsets), **furação** (G81/G83/G73, ordenar furos) | Gera, simula, confere limites e profundidade |
| 4 | Pós-processador | Fanuc e Siemens fresa: cabeçalho, ferramenta, G54, refrigerante, fim; compensação por G41/G42 **ou** coordenadas já deslocadas | Comparar com o dialeto do Code |
| 5 | Tela do CAM (web e desktop) | Abrir DXF, lista de operações, parâmetros, prévia 2D/3D, "Gerar programa" abre no editor | Prévia no celular/PC |
| 6 | Depois | Torno (G71/G70 a partir do perfil), 3D, banco de ferramentas | A definir |

## Decisões que preciso do Igor
1. **Entrada:** só DXF no começo, ou também desenhar na tela (retângulo, círculo, linha)? *Sugestão: DXF + formas básicas paramétricas.*
2. **Compensação:** programa com G41/G42 (raio no controle) ou coordenadas já deslocadas pelo CAM? *Sugestão: oferecer as duas; padrão G41/G42.*
3. **Onde roda primeiro:** desktop (plano mestre) ou web? *Sugestão: Core compartilhado; tela primeiro na web (prévia pelo GitHub), desktop em seguida.*
4. **Primeira máquina de teste:** qual controle e quais ferramentas reais para validar? (A definir.)
5. **Curso de CAM:** escrito junto, módulo a módulo, com o GPT (formato da Academy). Escopo e ordem: A definir.

## Riscos
| Risco | O que fazer |
|---|---|
| Offset de contorno é a parte mais difícil (cantos, ilhas) | Entrega 1 isolada e bem testada; ilhas só depois |
| DXF real é sujo (segmentos soltos, spline) | Entrega 2 com tolerância e aviso do que foi ignorado |
| Programa gerado perigoso | Sempre simular antes; avisos; nunca prometer colisão |
| Escopo crescer | Só 2,5D de fresa até a entrega 5 |

## Fora do escopo agora
CAD completo, CAM 3D/superfície, 4 e 5 eixos, banco de ferramentas na nuvem, torno.
