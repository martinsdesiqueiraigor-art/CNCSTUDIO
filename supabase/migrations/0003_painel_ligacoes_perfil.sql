-- Painel de ativação: licenças e aparelhos ligados ao perfil (permite listar tudo junto pela API)
alter table public.licenses
    add constraint licenses_user_profile_fkey foreign key (user_id) references public.profiles (id) on delete cascade;
alter table public.devices
    add constraint devices_user_profile_fkey foreign key (user_id) references public.profiles (id) on delete cascade;
create index devices_user_id_idx on public.devices (user_id);
