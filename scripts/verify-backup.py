#!/usr/bin/env python3
"""Restore a trusted Pharmacy POS backup into a NEW temporary database, then remove it."""
import argparse
import getpass
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import uuid


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('archive', type=Path)
    p.add_argument('--host', default='localhost')
    p.add_argument('--port', type=int, default=5432)
    p.add_argument('--user', default='postgres', help='PostgreSQL role allowed to create temporary databases')
    p.add_argument('--pg-bin', type=Path, default=Path('/Library/PostgreSQL/18/bin') if sys.platform == 'darwin' else None)
    p.add_argument('--no-password', action='store_true', help='Only for local test servers configured with trust authentication')
    args = p.parse_args()
    archive = args.archive.resolve(strict=True)
    manifest = json.loads(Path(str(archive) + '.json').read_text())
    if manifest['Version'] != 1 or manifest['Archive'] != archive.name:
        raise ValueError('Backup manifest does not match the archive.')
    digest = hashlib.sha256()
    with archive.open('rb') as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b''):
            digest.update(chunk)
    if digest.hexdigest().upper() != manifest['Sha256'].upper():
        raise ValueError('Checksum mismatch. Restore was not attempted.')
    expected = {}
    for t in manifest['Tables']:
        key = (t['Schema'], t['Table'])
        if key[0] != 'public' or not isinstance(key[1], str) or key in expected or not isinstance(t['Rows'], int) or t['Rows'] < 0:
            raise ValueError('Invalid table-count manifest.')
        expected[key] = t['Rows']
    if not expected:
        raise ValueError('The backup manifest contains no application tables.')
    env = {k: v for k, v in os.environ.items() if not k.upper().startswith('PG')}
    env.update(PGHOST=args.host, PGPORT=str(args.port), PGUSER=args.user, PGCONNECT_TIMEOUT='15')
    # Kept out of arguments, logs and source control.
    env['PGPASSWORD'] = '' if args.no_password else getpass.getpass('PostgreSQL verification role password: ')

    def run(tool, *argv):
        executable = tool + ('.exe' if os.name == 'nt' else '')
        if args.pg_bin:
            executable = str(args.pg_bin / executable)
        result = subprocess.run([executable, *argv], env=env, capture_output=True, text=True, timeout=1800)
        if result.returncode:
            raise RuntimeError(f'{tool} failed (exit {result.returncode}). Check role access, tool version and disk space.')
        return result.stdout.strip()

    def sql(database, query):
        return run('psql', '--no-password', '--no-psqlrc', '--set=ON_ERROR_STOP=1', '--tuples-only', '--no-align', '--dbname', database, '--command', query)

    major = int(sql('postgres', 'SHOW server_version_num')) // 10000
    if major != manifest['PostgreSqlMajor']:
        raise ValueError('Use a verification server with the same PostgreSQL major version as the backup.')
    scratch = 'pharmacy_restore_check_' + uuid.uuid4().hex
    created = False
    try:
        run('createdb', '--no-password', '--maintenance-db=postgres', '--template=template0', scratch)
        created = True
        run('pg_restore', '--no-password', '--exit-on-error', '--single-transaction', '--no-owner', '--no-acl', '--dbname', scratch, str(archive))
        tables = json.loads(sql(scratch, "SELECT COALESCE(json_agg(json_build_array(schemaname,tablename)), '[]'::json) FROM pg_tables WHERE schemaname='public'"))
        if {tuple(t) for t in tables} != set(expected):
            raise ValueError('Restored table list differs from the backup manifest.')
        def quote(identifier):
            return '"' + identifier.replace('"', '""') + '"'
        for (schema, table), count in expected.items():
            actual = int(sql(scratch, f'SELECT count(*) FROM {quote(schema)}.{quote(table)}'))
            if actual != count:
                raise ValueError('A restored table row count differs from the backup manifest.')
        print(f'Restore verified: checksum, schema restoration and row counts for all {len(expected)} application tables matched.')
    finally:
        if created:
            try:
                run('dropdb', '--no-password', '--maintenance-db=postgres', scratch)
                print('Temporary verification database removed. Your working database was not modified.')
            except Exception:
                print(f'Cleanup failed. Remove only this temporary database manually: {scratch}', file=sys.stderr)
                raise


if __name__ == '__main__':
    try:
        main()
    except (ValueError, KeyError, TypeError, OSError, RuntimeError, subprocess.TimeoutExpired) as error:
        print(f'Verification failed: {error}', file=sys.stderr)
        sys.exit(1)
