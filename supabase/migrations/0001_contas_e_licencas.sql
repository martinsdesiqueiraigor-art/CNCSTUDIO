-- Casillas Code — contas, trial, licenças e dispositivos (fase 13)
--
-- Regras:
--  * Toda conta nova ganha 14 dias de trial com tudo liberado (perfil criado por gatilho).
--  * Licença: assinatura (vale até expires_at) ou perpétua (sem fim; atualizações até updates_until).
--    Só administradores criam/alteram licenças (painel de ativação Pix, fase 14).
--  * Cada licença (ou o trial) vale para um número máximo de dispositivos (padrão 2).
--    O app chama license_status(dispositivo) ao abrir: registra o aparelho e devolve o estado.
--  * "Sair da conta" chama release_device(dispositivo) e libera a vaga.

-- ───────────────────────── Tabelas ─────────────────────────

create table public.profiles (
    id uuid primary key references auth.users (id) on delete cascade,
    email text,
    full_name text,
    trial_started_at timestamptz not null default now(),
    trial_ends_at timestamptz not null default now() + interval '14 days',
    created_at timestamptz not null default now()
);

create table public.admins (
    user_id uuid primary key references auth.users (id) on delete cascade,
    created_at timestamptz not null default now()
);

create table public.licenses (
    id uuid primary key default gen_random_uuid(),
    user_id uuid not null references auth.users (id) on delete cascade,
    kind text not null check (kind in ('subscription', 'perpetual')),
    status text not null default 'active' check (status in ('active', 'revoked')),
    starts_at timestamptz not null default now(),
    expires_at timestamptz,            -- assinatura: fim; perpétua: null
    updates_until timestamptz,         -- perpétua: atualizações incluídas até
    max_devices int not null default 2 check (max_devices between 1 and 20),
    amount_cents int,                  -- valor recebido (Pix)
    payment_ref text,                  -- identificador do Pix / comprovante
    notes text,
    created_by uuid references auth.users (id) on delete set null,
    created_at timestamptz not null default now(),
    check (kind = 'perpetual' or expires_at is not null)
);

create index licenses_user_id_idx on public.licenses (user_id);
create index licenses_created_by_idx on public.licenses (created_by);

create table public.devices (
    id uuid primary key default gen_random_uuid(),
    user_id uuid not null references auth.users (id) on delete cascade,
    device_id text not null check (length(device_id) between 8 and 128),
    label text,
    platform text not null default 'web' check (platform in ('web', 'desktop')),
    revoked boolean not null default false,
    first_seen timestamptz not null default now(),
    last_seen timestamptz not null default now(),
    unique (user_id, device_id)
);

create index devices_device_id_idx on public.devices (device_id);

-- ───────────────────────── Funções auxiliares ─────────────────────────

create function public.is_admin()
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select exists (select 1 from public.admins a where a.user_id = auth.uid());
$$;

-- Perfil com trial para cada conta nova
create function public.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    insert into public.profiles (id, email, full_name)
    values (new.id, new.email, coalesce(new.raw_user_meta_data ->> 'full_name', new.raw_user_meta_data ->> 'name'));
    return new;
end;
$$;

create trigger on_auth_user_created
    after insert on auth.users
    for each row execute function public.handle_new_user();

-- Estado da conta neste aparelho. Registra o aparelho (se houver vaga) e devolve:
-- state = licensed | trial | expired | device_limit | device_revoked
create function public.license_status(p_device_id text, p_label text default null, p_platform text default 'web')
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_uid uuid := auth.uid();
    v_profile public.profiles;
    v_license public.licenses;
    v_device public.devices;
    v_max int;
    v_used int;
    v_state text;
begin
    if v_uid is null then
        raise exception 'não autenticado' using errcode = '28000';
    end if;

    if p_device_id is null or length(p_device_id) not between 8 and 128 then
        raise exception 'identificador de dispositivo inválido' using errcode = '22023';
    end if;

    select * into v_profile from public.profiles where id = v_uid;
    if not found then
        -- conta criada antes do gatilho: cria o perfil agora
        insert into public.profiles (id, email)
        select u.id, u.email from auth.users u where u.id = v_uid
        returning * into v_profile;
    end if;

    -- Licença válida: perpétua primeiro, depois a assinatura que vence por último
    select * into v_license
    from public.licenses l
    where l.user_id = v_uid
      and l.status = 'active'
      and l.starts_at <= now()
      and (l.expires_at is null or l.expires_at > now())
    order by (l.kind = 'perpetual') desc, l.expires_at desc nulls first
    limit 1;

    v_max := coalesce(v_license.max_devices, 2);

    -- Aparelho
    select * into v_device from public.devices d where d.user_id = v_uid and d.device_id = p_device_id;
    if found then
        if v_device.revoked then
            v_state := 'device_revoked';
        else
            update public.devices
               set last_seen = now(),
                   label = coalesce(p_label, label)
             where id = v_device.id;
        end if;
    else
        select count(*) into v_used from public.devices d where d.user_id = v_uid and not d.revoked;
        if v_used >= v_max then
            v_state := 'device_limit';
        else
            insert into public.devices (user_id, device_id, label, platform)
            values (v_uid, p_device_id, p_label,
                    case when p_platform in ('web', 'desktop') then p_platform else 'web' end);
        end if;
    end if;

    if v_state is null then
        v_state := case
            when v_license.id is not null then 'licensed'
            when v_profile.trial_ends_at > now() then 'trial'
            else 'expired'
        end;
    end if;

    select count(*) into v_used from public.devices d where d.user_id = v_uid and not d.revoked;

    return jsonb_build_object(
        'state', v_state,
        'kind', v_license.kind,
        'expires_at', v_license.expires_at,
        'updates_until', v_license.updates_until,
        'trial_ends_at', v_profile.trial_ends_at,
        'max_devices', v_max,
        'devices_used', v_used,
        'server_time', now()
    );
end;
$$;

-- "Sair da conta": libera a vaga deste aparelho
create function public.release_device(p_device_id text)
returns void
language sql
security definer
set search_path = ''
as $$
    delete from public.devices d
    where d.user_id = auth.uid() and d.device_id = p_device_id and not d.revoked;
$$;

revoke execute on function public.is_admin() from public, anon;
revoke execute on function public.handle_new_user() from public, anon, authenticated;
revoke execute on function public.license_status(text, text, text) from public, anon;
revoke execute on function public.release_device(text) from public, anon;
grant execute on function public.is_admin() to authenticated;
grant execute on function public.license_status(text, text, text) to authenticated;
grant execute on function public.release_device(text) to authenticated;

-- ───────────────────────── Segurança (RLS) ─────────────────────────

alter table public.profiles enable row level security;
alter table public.admins enable row level security;
alter table public.licenses enable row level security;
alter table public.devices enable row level security;

-- Perfil: o usuário vê o seu; trial e datas só o administrador muda
create policy "perfil: ver o próprio ou admin" on public.profiles
    for select to authenticated
    using (id = (select auth.uid()) or (select public.is_admin()));
create policy "perfil: admin altera" on public.profiles
    for update to authenticated
    using ((select public.is_admin())) with check ((select public.is_admin()));

-- Administradores: só ver a si mesmo (cadastro feito por SQL)
create policy "admins: ver a si mesmo" on public.admins
    for select to authenticated
    using (user_id = (select auth.uid()));

-- Licenças: usuário vê as suas; admin faz tudo
create policy "licencas: ver as próprias ou admin" on public.licenses
    for select to authenticated
    using (user_id = (select auth.uid()) or (select public.is_admin()));
create policy "licencas: admin cria" on public.licenses
    for insert to authenticated
    with check ((select public.is_admin()));
create policy "licencas: admin altera" on public.licenses
    for update to authenticated
    using ((select public.is_admin())) with check ((select public.is_admin()));
create policy "licencas: admin exclui" on public.licenses
    for delete to authenticated
    using ((select public.is_admin()));

-- Dispositivos: usuário vê e remove os seus; registro só pela função; admin bloqueia/desbloqueia
create policy "dispositivos: ver os próprios ou admin" on public.devices
    for select to authenticated
    using (user_id = (select auth.uid()) or (select public.is_admin()));
create policy "dispositivos: remover os próprios ou admin" on public.devices
    for delete to authenticated
    using ((user_id = (select auth.uid()) and not revoked) or (select public.is_admin()));
create policy "dispositivos: admin altera" on public.devices
    for update to authenticated
    using ((select public.is_admin())) with check ((select public.is_admin()));
