# Etapa 3 — Casillas Code como produto comercial

Decisões tomadas com o dono do produto:

| Tema | Decisão |
|---|---|
| Nome | **Casillas Code** — "EDITOR E SIMULADOR CNC" |
| Marca | Base da logo A do Casillas + linhas de código (variação 05). Fontes em `branding/` |
| Versões | **Desktop** (completo, sólido 3D) e **Web/PWA** (leve, PC e celular). Uma licença vale para as duas |
| Cobrança | Assinatura anual **e** licença perpétua (com 1 ano de atualizações) |
| Conta | Separada do Casillas (projeto Supabase próprio); mesmo ecossistema, produtos complementares |
| Pagamento | Pix direto. Início: ativação manual num painel; depois, opcionalmente, gateway com confirmação automática |
| Trial | Libera tudo. Depois de expirar, continua funcionando com restrições |
| Repositório | O mesmo; novo projeto `CncStudio.Web` ao lado do desktop, com o mesmo Core |

## Fases

| Fase | Entrega | Status |
|---|---|---|
| 9 | Identidade visual no desktop: nome, ícones (.ico/.png gerados dos SVGs), instalador, README | ✅ |
| 10 | **Web — protótipo**: projeto `CncStudio.Web` (Avalonia no navegador); editor com edição pelo editor nativo do celular; backplot torno ZX e fresa 2,5D/topo/frente; simulação passo a passo (passe a passe nos ciclos) com alarme em erro; erros marcados no editor; menu Exibir; layout para celular em pé e deitado. CI publica no ramo `ci-web`. Pendente: confirmar o botão Abrir no celular depois do PWA (fase 12) | ✅ |
| 11 | **Web — estatísticas e folha de setup** (tempos por ferramenta, limites, folha de setup para compartilhar) | |
| 12 | **PWA** no ar em https://casillas-code.pages.dev — manifesto, ícones, service worker (offline), botão Instalar; publicação automática no Cloudflare Pages a cada commit. Falta: testar instalação e Abrir no celular | 🔶 |
| 13 | **Contas e licenças**: Supabase (login e-mail/Google), trial de 14 dias, limite de dispositivos, restrições pós-trial; no desktop, licença assinada (Ed25519) com tolerância offline | |
| 14 | **Painel de ativação** (Pix manual): lista de clientes, ativar/renovar/revogar licença, e-mails de boas-vindas | |
| 15 | **Lançamento**: termos de uso (com aviso de que a simulação não garante ausência de colisão), política de privacidade (LGPD), página de vendas | |

## Restrições depois do trial (proposta)

| Recurso | Depois do trial |
|---|---|
| Abrir, editar, backplot, simulação | Liberado |
| Salvar / exportar | Bloqueado (ou só programas pequenos) |
| Folha de setup | Com marca d'água "Versão de avaliação" |
| Estatísticas por ferramenta, comparação | Bloqueado |

## O que depende do dono do produto

- Fase 12: conta na hospedagem (Cloudflare Pages ou Netlify) ligada ao GitHub.
- Fase 13: criação do projeto Supabase (ou autorização para eu criar).
- Fase 14: chave Pix e e-mail remetente.
- Fase 15: revisão jurídica dos termos e consulta ao contador sobre o recebimento.
