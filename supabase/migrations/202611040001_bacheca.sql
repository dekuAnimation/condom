-- Run once in the Supabase SQL editor, or with supabase db push.
create extension if not exists pgcrypto with schema extensions;
create table public.boards (
 id uuid primary key default gen_random_uuid(),
 owner_id uuid not null unique references auth.users(id) on delete cascade,
 name text not null default 'Il tuo condominio' check (length(name) between 1 and 80),
 created_at timestamptz not null default now()
);
create table public.slides (
 id uuid primary key default gen_random_uuid(),
 board_id uuid not null references public.boards(id) on delete cascade,
 title text not null check (length(title) between 1 and 120),
 path text not null unique,
 sha256 text not null check (sha256 ~ '^[0-9a-f]{64}$'),
 width integer not null check (width between 1 and 1600),
 height integer not null check (height between 1 and 1200),
 position integer not null default 0,
 active boolean not null default true,
 created_at timestamptz not null default now(),
 check (path = board_id::text || '/' || id::text || '.jpg')
);
create index slides_board_order on public.slides(board_id,position);
create table public.devices (
 id uuid primary key default gen_random_uuid(),
 board_id uuid not null references public.boards(id) on delete cascade,
 name text not null check (length(name) between 1 and 80),
 token_hash text not null unique,
 revoked boolean not null default false,
 last_seen timestamptz,
 created_at timestamptz not null default now()
);
alter table public.boards enable row level security;
alter table public.slides enable row level security;
alter table public.devices enable row level security;
revoke all on public.boards,public.slides,public.devices from anon,authenticated;
grant select on public.boards to authenticated;
grant update(name) on public.boards to authenticated;
grant select,insert,update,delete on public.slides to authenticated;
grant select(id,board_id,name,revoked,last_seen,created_at) on public.devices to authenticated;
grant update(revoked) on public.devices to authenticated;
create policy owner_boards on public.boards for all to authenticated using (owner_id=auth.uid()) with check (owner_id=auth.uid());
create policy owner_slides on public.slides for all to authenticated
 using (exists(select 1 from public.boards b where b.id=board_id and b.owner_id=auth.uid()))
 with check (exists(select 1 from public.boards b where b.id=board_id and b.owner_id=auth.uid()));
create policy owner_devices on public.devices for all to authenticated
 using (exists(select 1 from public.boards b where b.id=board_id and b.owner_id=auth.uid()))
 with check (exists(select 1 from public.boards b where b.id=board_id and b.owner_id=auth.uid()));
create function public.ensure_board() returns jsonb language plpgsql security definer set search_path='' as $$
declare b public.boards;
begin
 if auth.uid() is null then raise exception 'Authentication required'; end if;
 insert into public.boards(owner_id) values(auth.uid()) on conflict(owner_id) do nothing;
 select * into b from public.boards where owner_id=auth.uid();
 return jsonb_build_object('id',b.id,'name',b.name);
end $$;
create function public.create_device(p_board uuid,p_name text) returns jsonb language plpgsql security definer set search_path='' as $$
declare secret text; device_id uuid;
begin
 if not exists(select 1 from public.boards where id=p_board and owner_id=auth.uid()) then raise exception 'Access denied'; end if;
 if (select count(*) from public.devices where board_id=p_board and not revoked)>=10 then raise exception 'Massimo 10 dispositivi attivi'; end if;
 secret:=encode(extensions.gen_random_bytes(32),'hex');
 insert into public.devices(board_id,name,token_hash) values(p_board,p_name,encode(extensions.digest(secret,'sha256'),'hex')) returning id into device_id;
 return jsonb_build_object('id',device_id,'token',secret);
end $$;
create function public.reorder_slides(p_board uuid,p_ids uuid[]) returns void language plpgsql security definer set search_path='' as $$
begin
 if not exists(select 1 from public.boards where id=p_board and owner_id=auth.uid()) then raise exception 'Access denied'; end if;
 perform pg_advisory_xact_lock(hashtext(p_board::text));
 if cardinality(p_ids)<>(select count(*) from public.slides where board_id=p_board)
 or cardinality(p_ids)<>(select count(distinct x) from unnest(p_ids) x)
 or exists(select 1 from unnest(p_ids) x where not exists(select 1 from public.slides where id=x and board_id=p_board)) then raise exception 'Elenco cambiato: ricarica la bacheca'; end if;
 update public.slides s set position=u.n-1 from unnest(p_ids) with ordinality u(id,n) where s.id=u.id and s.board_id=p_board;
end $$;
create function public.screen_manifest(p_hash text) returns jsonb language plpgsql security definer set search_path='' as $$
declare d public.devices; result jsonb;
begin
 select * into d from public.devices where token_hash=p_hash and not revoked for update;
 if not found then return null; end if;
 update public.devices set last_seen=now() where id=d.id;
 select jsonb_build_object('name',b.name,'interval_seconds',30,'slides',coalesce((select jsonb_agg(jsonb_build_object('id',s.id,'title',s.title,'path',s.path,'sha256',s.sha256) order by s.position,s.created_at) from public.slides s where s.board_id=b.id and s.active),'[]'::jsonb)) into result from public.boards b where b.id=d.board_id;
 return result;
end $$;
revoke all on function public.ensure_board(),public.create_device(uuid,text),public.reorder_slides(uuid,uuid[]),public.screen_manifest(text) from public,anon,authenticated;
grant execute on function public.ensure_board(),public.create_device(uuid,text),public.reorder_slides(uuid,uuid[]) to authenticated;
grant execute on function public.screen_manifest(text) to service_role;
insert into storage.buckets(id,name,public,file_size_limit,allowed_mime_types) values('notices','notices',false,5242880,array['image/jpeg']) on conflict(id) do nothing;
create policy notices_owner_select on storage.objects for select to authenticated using(bucket_id='notices' and exists(select 1 from public.boards b where b.id::text=split_part(name,'/',1) and b.owner_id=auth.uid()));
create policy notices_owner_insert on storage.objects for insert to authenticated with check(bucket_id='notices' and exists(select 1 from public.boards b where b.id::text=split_part(name,'/',1) and b.owner_id=auth.uid()));
create policy notices_owner_delete on storage.objects for delete to authenticated using(bucket_id='notices' and exists(select 1 from public.boards b where b.id::text=split_part(name,'/',1) and b.owner_id=auth.uid()));
