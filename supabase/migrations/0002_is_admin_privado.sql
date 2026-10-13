-- is_admin só é usado pelas políticas: passa para um esquema não exposto pela API
create schema if not exists private;
grant usage on schema private to authenticated;

create function private.is_admin()
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select exists (select 1 from public.admins a where a.user_id = auth.uid());
$$;
revoke execute on function private.is_admin() from public, anon;
grant execute on function private.is_admin() to authenticated;

alter policy "perfil: ver o próprio ou admin" on public.profiles
    using (id = (select auth.uid()) or (select private.is_admin()));
alter policy "perfil: admin altera" on public.profiles
    using ((select private.is_admin())) with check ((select private.is_admin()));
alter policy "licencas: ver as próprias ou admin" on public.licenses
    using (user_id = (select auth.uid()) or (select private.is_admin()));
alter policy "licencas: admin cria" on public.licenses
    with check ((select private.is_admin()));
alter policy "licencas: admin altera" on public.licenses
    using ((select private.is_admin())) with check ((select private.is_admin()));
alter policy "licencas: admin exclui" on public.licenses
    using ((select private.is_admin()));
alter policy "dispositivos: ver os próprios ou admin" on public.devices
    using (user_id = (select auth.uid()) or (select private.is_admin()));
alter policy "dispositivos: remover os próprios ou admin" on public.devices
    using ((user_id = (select auth.uid()) and not revoked) or (select private.is_admin()));
alter policy "dispositivos: admin altera" on public.devices
    using ((select private.is_admin())) with check ((select private.is_admin()));

-- A versão pública deixa de ser chamável pela API
revoke execute on function public.is_admin() from authenticated;
