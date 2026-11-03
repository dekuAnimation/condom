-- Run in the project's SQL editor after setup. All test rows are rolled back.
begin;
select set_config('test.owner1', gen_random_uuid()::text, true),
       set_config('test.owner2', gen_random_uuid()::text, true);
insert into auth.users(id,email) values
 (current_setting('test.owner1')::uuid,'storage-test1@example.invalid'),
 (current_setting('test.owner2')::uuid,'storage-test2@example.invalid');
insert into public.boards(owner_id,name) values
 (current_setting('test.owner1')::uuid,'Condominio con nome diverso dal percorso'),
 (current_setting('test.owner2')::uuid,'Altra bacheca');
select set_config('test.board1',(select id::text from public.boards where owner_id=current_setting('test.owner1')::uuid),true),
       set_config('test.board2',(select id::text from public.boards where owner_id=current_setting('test.owner2')::uuid),true);
select set_config('request.jwt.claim.sub',current_setting('test.owner1'),true);
set local role authenticated;
do $$
declare object_id uuid:=gen_random_uuid(); n integer; denied boolean:=false;
begin
 insert into storage.objects(id,bucket_id,name)
 values(object_id,'notices',current_setting('test.board1')||'/'||object_id::text||'.jpg');
 select count(*) into n from storage.objects where id=object_id;
 if n<>1 then raise exception 'Owner cannot read own upload'; end if;
 begin
  insert into storage.objects(bucket_id,name)
  values('notices',current_setting('test.board2')||'/'||gen_random_uuid()::text||'.jpg');
 exception when insufficient_privilege then denied:=true;
 end;
 if not denied then raise exception 'Cross-owner upload allowed'; end if;
 perform set_config('test.object',object_id::text,true);
end $$;
select set_config('request.jwt.claim.sub',current_setting('test.owner2'),true);
do $$ declare n integer; begin
 select count(*) into n from storage.objects where id=current_setting('test.object')::uuid;
 if n<>0 then raise exception 'Cross-owner image read allowed'; end if;
end $$;
reset role;
rollback;
