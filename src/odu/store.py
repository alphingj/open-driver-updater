"""SQLite store: backups manifest, exclusions. Thread-safe via short connections."""
from __future__ import annotations
import os
import sqlite3
import time

_SCHEMA = (
    "CREATE TABLE IF NOT EXISTS backups(id INTEGER PRIMARY KEY AUTOINCREMENT,"
    " created REAL, instance_id TEXT, name TEXT, path TEXT)",
    "CREATE TABLE IF NOT EXISTS exclusions(hwid TEXT PRIMARY KEY)",
    "CREATE TABLE IF NOT EXISTS catalog_cache(hwid TEXT PRIMARY KEY, result TEXT,"
    " fetched REAL)",
)


def db_path(path: str | None = None) -> str:
    """Resolve the DB path lazily so ODU_DB can change at runtime (tests)."""
    if path:
        return path
    return os.environ.get("ODU_DB", os.path.join(os.path.expanduser("~"), ".odu.sqlite3"))


def connect(path: str | None = None) -> sqlite3.Connection:
    con = sqlite3.connect(db_path(path), timeout=30)
    for stmt in _SCHEMA:
        con.execute(stmt)
    return con


def get_exclusions(path: str | None = None) -> list[str]:
    con = connect(path)
    try:
        return [r[0] for r in con.execute("SELECT hwid FROM exclusions")]
    finally:
        con.close()


def add_exclusion(hwid: str, path: str | None = None) -> None:
    if not hwid:
        return
    con = connect(path)
    try:
        con.execute("INSERT OR IGNORE INTO exclusions(hwid) VALUES(?)", (hwid,))
        con.commit()
    finally:
        con.close()


def remove_exclusion(hwid: str, path: str | None = None) -> None:
    con = connect(path)
    try:
        con.execute("DELETE FROM exclusions WHERE hwid=?", (hwid,))
        con.commit()
    finally:
        con.close()


def record_backup(instance_id: str, name: str, path_: str, db: str | None = None) -> int:
    con = connect(db)
    try:
        cur = con.execute("INSERT INTO backups(created,instance_id,name,path) VALUES(?,?,?,?)",
                          (time.time(), instance_id, name, path_))
        con.commit()
        return int(cur.lastrowid)
    finally:
        con.close()


def list_backups(db: str | None = None) -> list[dict]:
    con = connect(db)
    try:
        return [dict(id=r[0], created=r[1], instance_id=r[2], name=r[3], path=r[4])
                for r in con.execute("SELECT id,created,instance_id,name,path FROM backups ORDER BY id DESC")]
    finally:
        con.close()


CACHE_TTL = 7 * 24 * 3600


def cache_get(hwid: str, db: str | None = None) -> str | None:
    con = connect(db)
    try:
        row = con.execute("SELECT result,fetched FROM catalog_cache WHERE hwid=?",
                          (hwid,)).fetchone()
        if row and (time.time() - row[1]) < CACHE_TTL:
            return str(row[0])
        return None
    finally:
        con.close()


def cache_put(hwid: str, result_json: str, db: str | None = None) -> None:
    con = connect(db)
    try:
        con.execute("INSERT OR REPLACE INTO catalog_cache(hwid,result,fetched)"
                    " VALUES(?,?,?)", (hwid, result_json, time.time()))
        con.commit()
    finally:
        con.close()
