# Supabase — Casillas Code

Projeto próprio (separado do Casillas), região São Paulo (`sa-east-1`), plano gratuito.

| Item | Valor |
|---|---|
| Projeto | `Casillas Code` (`jdjlcekavnwuiiaoevvd`) |
| URL da API | https://jdjlcekavnwuiiaoevvd.supabase.co |
| Chave pública (pode ir no app) | `sb_publishable_Rr3IWTffW7E8eENbKSOjkQ_WBGMnL9Z` |

A chave `service_role` / secreta **nunca** vai para o repositório nem para o app.

## Modelo

- `profiles` — um por conta, criado por gatilho; `trial_ends_at` = criação + 14 dias.
- `licenses` — `subscription` (até `expires_at`) ou `perpetual` (sem fim; atualizações até `updates_until`); `max_devices` (padrão 2). Só administradores escrevem.
- `devices` — aparelhos da conta (`web` ou `desktop`); o administrador pode bloquear (`revoked`).
- `admins` — quem usa o painel de ativação (cadastro por SQL).

## API usada pelo app

- `rpc/license_status(p_device_id, p_label, p_platform)` → `{ state, kind, expires_at, updates_until, trial_ends_at, max_devices, devices_used, server_time }`
  com `state` = `licensed` | `trial` | `expired` | `device_limit` | `device_revoked`.
- `rpc/release_device(p_device_id)` — "Sair da conta": libera a vaga do aparelho.

## Migrações

`migrations/` na ordem dos números; aplicadas no projeto em 12/10/2026 e testadas
(trial, limite de 2 aparelhos, sair libera vaga, trial vencido, assinatura ativa, RLS).

## Configuração no painel (Authentication)

- **URL Configuration › Site URL:** `https://casillas-code.pages.dev`
- **URL Configuration › Redirect URLs:** `https://casillas-code.pages.dev/**`
- **Sign In / Providers › Email:** ligado, com "Confirm email" ligado.
- O envio padrão de e-mails do Supabase tem limite baixo por hora: antes do lançamento,
  configurar um SMTP próprio (ex.: Resend) em **Emails › SMTP Settings**.
