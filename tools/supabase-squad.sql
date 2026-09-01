-- Tarkovy squad rooms (run in the Supabase SQL editor).
-- Dashboard → SQL → New query → paste → Run.
-- Safe to run again after updates. Then Config in Tarkovy: Project URL + anon key.

do $$ begin
  create extension if not exists pgcrypto;
exception when others then
  null;
end $$;

create table if not exists public.squad_rooms (
  id uuid primary key default gen_random_uuid(),
  code text not null unique,
  pass_hash text not null,
  created_at timestamptz not null default now()
);

create table if not exists public.squad_positions (
  room_id uuid not null references public.squad_rooms(id) on delete cascade,
  nick text not null,
  map_id text not null default '',
  x double precision not null default 0,
  y double precision not null default 0,
  z double precision not null default 0,
  yaw double precision not null default 0,
  updated_at timestamptz not null default now(),
  primary key (room_id, nick)
);

alter table public.squad_rooms enable row level security;
alter table public.squad_positions enable row level security;

revoke all on public.squad_rooms from anon, authenticated, public;
revoke all on public.squad_positions from anon, authenticated, public;

-- Salted MD5 (no pgcrypto gen_salt). Room passwords are shared among friends, not accounts.
create or replace function public.squad_hash_password(p_password text)
returns text
language plpgsql
volatile
set search_path = public
as $$
declare
  v_salt text := replace(gen_random_uuid()::text, '-', '');
begin
  return v_salt || md5(v_salt || p_password);
end;
$$;

create or replace function public.squad_password_ok(p_password text, p_hash text)
returns boolean
language plpgsql
immutable
set search_path = public
as $$
declare
  v_salt text;
begin
  if p_hash is null or length(p_hash) < 33 then
    return false;
  end if;
  v_salt := left(p_hash, 32);
  return p_hash = v_salt || md5(v_salt || p_password);
end;
$$;

revoke all on function public.squad_hash_password(text) from public, anon, authenticated;
revoke all on function public.squad_password_ok(text, text) from public, anon, authenticated;

create or replace function public.squad_prune_stale()
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  delete from squad_positions where updated_at < now() - interval '20 minutes';
  delete from squad_rooms r
  where not exists (select 1 from squad_positions p where p.room_id = r.id);
end;
$$;

revoke all on function public.squad_prune_stale() from public, anon, authenticated;

drop function if exists public.squad_create(text, text);
drop function if exists public.squad_create(text, text, text);

create or replace function public.squad_create(p_password text, p_nick text, p_code text default '')
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_id uuid;
  v_code text;
  v_nick text := trim(p_nick);
  v_want text := upper(trim(coalesce(p_code, '')));
  i int;
  words text[] := array['CUSTOMS','FACTORY','DORM','MALL','WOODS','LABS','STREETS','GROUND','SHORE','LIGHTHOUSE','KORD','RAID'];
begin
  if v_nick is null or length(v_nick) < 1 or length(v_nick) > 20 then
    raise exception 'invalid nick';
  end if;
  if p_password is null or length(p_password) < 4 then
    raise exception 'password too short';
  end if;
  if v_want is not null and length(v_want) >= 4 then
    if v_want !~ '^[A-Z0-9-]{4,20}$' then
      raise exception 'invalid room name';
    end if;
    begin
      insert into squad_rooms(code, pass_hash)
      values (v_want, squad_hash_password(p_password))
      returning id into v_id;
    exception when unique_violation then
      raise exception 'room exists';
    end;
    v_code := v_want;
  else
    for i in 1..12 loop
      v_code := words[1 + floor(random() * array_length(words, 1))::int]
                || '-' || upper(substr(replace(gen_random_uuid()::text, '-', ''), 1, 4));
      begin
        insert into squad_rooms(code, pass_hash)
        values (v_code, squad_hash_password(p_password))
        returning id into v_id;
        exit;
      exception when unique_violation then
        v_id := null;
      end;
    end loop;
  end if;
  if v_id is null then
    raise exception 'could not allocate room code';
  end if;
  insert into squad_positions(room_id, nick) values (v_id, v_nick);
  perform squad_prune_stale();
  return jsonb_build_object('roomId', v_id, 'code', v_code, 'members', 1, 'max', 5);
end;
$$;

create or replace function public.squad_join(p_code text, p_password text, p_nick text)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_room squad_rooms%rowtype;
  v_nick text := trim(p_nick);
  v_code text := upper(trim(p_code));
begin
  if v_nick is null or length(v_nick) < 1 or length(v_nick) > 20 then
    raise exception 'invalid nick';
  end if;
  select * into v_room from squad_rooms where code = v_code;
  if not found then
    raise exception 'room not found';
  end if;
  if not squad_password_ok(p_password, v_room.pass_hash) then
    raise exception 'bad password';
  end if;
  perform squad_prune_stale();
  if exists (select 1 from squad_positions where room_id = v_room.id and nick = v_nick) then
    update squad_positions set updated_at = now()
    where room_id = v_room.id and nick = v_nick;
  else
    if (select count(*) from squad_positions where room_id = v_room.id) >= 5 then
      raise exception 'room full';
    end if;
    insert into squad_positions(room_id, nick) values (v_room.id, v_nick);
  end if;
  return jsonb_build_object(
    'roomId', v_room.id,
    'code', v_room.code,
    'members', (select count(*)::int from squad_positions where room_id = v_room.id),
    'max', 5);
end;
$$;

create or replace function public.squad_publish(
  p_code text,
  p_password text,
  p_nick text,
  p_map text,
  p_x double precision,
  p_y double precision,
  p_z double precision,
  p_yaw double precision)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_room squad_rooms%rowtype;
  v_nick text := trim(p_nick);
  v_code text := upper(trim(p_code));
begin
  select * into v_room from squad_rooms where code = v_code;
  if not found then
    raise exception 'room not found';
  end if;
  if not squad_password_ok(p_password, v_room.pass_hash) then
    raise exception 'bad password';
  end if;
  insert into squad_positions(room_id, nick, map_id, x, y, z, yaw, updated_at)
  values (v_room.id, v_nick, coalesce(p_map, ''), p_x, p_y, p_z, p_yaw, now())
  on conflict (room_id, nick) do update
    set map_id = excluded.map_id,
        x = excluded.x,
        y = excluded.y,
        z = excluded.z,
        yaw = excluded.yaw,
        updated_at = now();
  return jsonb_build_object('ok', true);
end;
$$;

drop function if exists public.squad_list(text, text);
drop function if exists public.squad_list(text, text, text);

create or replace function public.squad_list(p_code text, p_password text, p_nick text default '')
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_room squad_rooms%rowtype;
  v_code text := upper(trim(p_code));
  v_nick text := trim(p_nick);
begin
  select * into v_room from squad_rooms where code = v_code;
  if not found then
    raise exception 'room not found';
  end if;
  if not squad_password_ok(p_password, v_room.pass_hash) then
    raise exception 'bad password';
  end if;
  if length(v_nick) > 0 then
    update squad_positions set updated_at = now()
    where room_id = v_room.id and nick = v_nick;
  end if;
  perform squad_prune_stale();
  return coalesce((
    select jsonb_agg(jsonb_build_object(
      'nick', p.nick,
      'mapId', p.map_id,
      'x', p.x,
      'y', p.y,
      'z', p.z,
      'yaw', p.yaw,
      'updatedAt', p.updated_at
    ) order by p.nick)
    from squad_positions p
    where p.room_id = v_room.id
  ), '[]'::jsonb);
end;
$$;

create or replace function public.squad_leave(p_code text, p_password text, p_nick text)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_room squad_rooms%rowtype;
  v_nick text := trim(p_nick);
  v_code text := upper(trim(p_code));
begin
  select * into v_room from squad_rooms where code = v_code;
  if not found then
    return jsonb_build_object('ok', true);
  end if;
  if not squad_password_ok(p_password, v_room.pass_hash) then
    raise exception 'bad password';
  end if;
  delete from squad_positions where room_id = v_room.id and nick = v_nick;
  delete from squad_rooms r
  where r.id = v_room.id
    and not exists (select 1 from squad_positions p where p.room_id = r.id);
  perform squad_prune_stale();
  return jsonb_build_object('ok', true, 'deleted', not exists(select 1 from squad_rooms where id = v_room.id));
end;
$$;

create or replace function public.squad_name_taken(p_code text)
returns boolean
language plpgsql
volatile
security definer
set search_path = public
as $$
begin
  perform squad_prune_stale();
  return exists(
    select 1 from public.squad_rooms where code = upper(trim(coalesce(p_code, '')))
  );
end;
$$;

grant execute on function public.squad_create(text, text, text) to anon, authenticated;
grant execute on function public.squad_join(text, text, text) to anon, authenticated;
grant execute on function public.squad_publish(text, text, text, text, double precision, double precision, double precision, double precision) to anon, authenticated;
grant execute on function public.squad_list(text, text, text) to anon, authenticated;
grant execute on function public.squad_leave(text, text, text) to anon, authenticated;
grant execute on function public.squad_name_taken(text) to anon, authenticated;

notify pgrst, 'reload schema';
