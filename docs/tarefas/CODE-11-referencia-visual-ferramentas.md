# Tarefa CODE-11: referência técnica para o desenho 3D das ferramentas

**Para:** GPT (com acesso ao GitHub e à internet)  **Ramo base:** `main`  **Ramo da entrega:** `ext-gpt-code11-ferramentas-visual`

## Contexto
O Casillas Code desenha ferramentas e suportes em 3D (fresa, broca, macho, fresa de facear; torno: desbaste, barra interna, bedame, rosca). O desenho atual ficou "razoável", mas não parece ferramenta de usinagem de verdade. **Você não vai escrever código nem imagens.** Você vai levantar a especificação real (medidas e características visuais) em catálogos de fabricantes, para o Claude modelar com fidelidade.

## Pode mexer em
- `docs/referencia/ferramentas-visual.md` (arquivo novo). Nada fora dele.

## O que levantar (uma seção por item)
**Torno:** porta-ferramenta de desbaste externo (série tipo PCLNR/DCLNR/MCLNR, inserto CNMG 12 04 08); barra de mandrilar (tipo S..-PCLNR, Ø16 a Ø25); bedame externo (lâmina + inserto de canal, largura 2 a 4 mm); bedame interno; rosca externa (inserto 16ER/16 IR) e interna.
**Fresa:** fresa de topo de metal duro (2 e 4 cortes, Ø6 a Ø16); fresa esférica; fresa toroidal; broca helicoidal de metal duro; macho; fresa de facear (inserto quadrado/octogonal, Ø50 a Ø80); suporte BT40 com porca ER32 (e ER20); cone, flange, ranhuras e pino de tração.

Para **cada item**, informe, em tabela:
1. Medidas reais em mm (seção do cabo, comprimentos, diâmetros, ângulos de hélice e de ponta, raios, número de cortes, espessura do inserto, largura do canal etc.), com a **fonte** (catálogo ou norma, com link).
2. Características visuais que identificam a ferramenta (ex.: hélice e canais da fresa, quebra-cavaco do inserto, tipo de fixação: parafuso, grampo, alavanca; chanfros; marcações).
3. Acabamento e cor típicos (aço, metal duro, cobertura TiN/TiAlN dourada ou preta).
4. Proporções entre as partes (cabeça × cabo, parte cortante × haste).

## Regras
- **Não copie imagens nem texto de catálogos**; descreva com suas palavras e cite a fonte.
- Medida sem fonte confiável: escreva **A definir**. Nada de inventar número.
- Português do Brasil; no máximo 4 páginas; tabelas curtas.
- Diga também, em um parágrafo, as 5 coisas que mais fariam o desenho parecer "de verdade" (por prioridade).

## Pronto quando
- [ ] `docs/referencia/ferramentas-visual.md` com todos os itens acima
- [ ] PR aberto, sem merge

## Volta (no PR)
```
ID: CODE-11
O que foi feito (até 5 linhas):
Arquivos alterados/criados:
Testes: não se aplica
Decisões tomadas (só as autorizadas):
Pendências e dúvidas (A definir):
Riscos ou contradições encontradas:
Próximo passo sugerido (1):
```
Conteúdo técnico vai como `rascunho`; só o Igor marca revisado.
