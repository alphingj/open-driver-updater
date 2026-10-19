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
    "CREATE TABLE IF NOT EXISTS odu_installs(oem TEXT PRIMARY KEY,"
    " instance_id TEXT, name TEXT, created REAL,"
    " prev_oem TEXT, prev_version TEXT, prev_path TEXT)",
)

# Added after v0.7.4: older databases lack these columns, so migrate in place
# instead of dropping the rollback allow-list.
_INSTALL_MIGRATIONS = (("prev_oem", "TEXT"), ("prev_version", "TEXT"),
                       ("prev_path", "TEXT"))


def _migrate(con: sqlite3.Connection) -> None:
    try:
        cols = {row[1] for row in con.execute("PRAGMA table_info(odu_installs)")}
        for name, typ in _INSTALL_MIGRATIONS:
            if name not in cols:
                con.execute(f"ALTER TABLE odu_installs ADD COLUMN {name} {typ}")
        con.commit()
    except sqlite3.Error:
        pass


def db_path(path: str | None = None) -> str:
    """Resolve the DB path lazily so ODU_DB can change at runtime (tests)."""
    if path:
        return path
    return os.environ.get("ODU_DB", os.path.join(os.path.expanduser("~"), ".odu.sqlite3"))


def connect(path: str | None = None) -> sqlite3.Connection:
    con = sqlite3.connect(db_path(path), timeout=30)
    for stmt in _SCHEMA:
        con.execute(stmt)
    _migrate(con)
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


def record_install(oem: str, instance_id: str, name: str, db: str | None = None,
                   prev_oem: str = "", prev_version: str = "",
                   prev_path: str = "") -> None:
    """Remember a driver package THIS app installed (rollback allow-list).

    ``prev_oem``/``prev_version``/``prev_path`` describe the driver that was
    bound to the device before the upgrade and are used to restore it on
    rollback. They are optional so older callers keep working.
    """
    con = connect(db)
    try:
        con.execute("INSERT OR REPLACE INTO odu_installs"
                    "(oem,instance_id,name,created,prev_oem,prev_version,prev_path)"
                    " VALUES(?,?,?,?,?,?,?)",
                    (oem.lower(), instance_id, name, time.time(),
                     (prev_oem or "").lower(), prev_version or "", prev_path or ""))
        con.commit()
    finally:
        con.close()


def install_info(oem: str, db: str | None = None) -> dict | None:
    """Return the recorded install row for an oem name, or None."""
    con = connect(db)
    try:
        row = con.execute(
            "SELECT oem,instance_id,name,created,prev_oem,prev_version,prev_path"
            " FROM odu_installs WHERE oem=?", (oem.lower(),)).fetchone()
        if not row:
            return None
        return {"oem": row[0], "instance_id": row[1] or "", "name": row[2] or "",
                "created": row[3], "prev_oem": row[4] or "",
                "prev_version": row[5] or "", "prev_path": row[6] or ""}
    finally:
        con.close()


def forget_install(oem: str, db: str | None = None) -> None:
    con = connect(db)
    try:
        con.execute("DELETE FROM odu_installs WHERE oem=?", (oem.lower(),))
        con.commit()
    finally:
        con.close()


def installed_by_odu(oem: str, db: str | None = None) -> bool:
    con = connect(db)
    try:
        row = con.execute("SELECT oem FROM odu_installs WHERE oem=?",
                          (oem.lower(),)).fetchone()
        return row is not None
    finally:
        con.close()


def list_installs(db: str | None = None) -> list[dict]:
    """All packages this app installed, newest first (rollback candidates)."""
    con = connect(db)
    try:
        rows = con.execute(
            "SELECT oem,instance_id,name,created,prev_oem,prev_version,prev_path"
            " FROM odu_installs ORDER BY created DESC").fetchall()
        return [{"oem": r[0], "instance_id": r[1] or "", "name": r[2] or "",
                 "created": r[3], "prev_oem": r[4] or "",
                 "prev_version": r[5] or "", "prev_path": r[6] or ""}
                for r in rows]
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
