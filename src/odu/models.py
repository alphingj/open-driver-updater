"""Data models (JSON-serializable dicts)."""
from __future__ import annotations
from dataclasses import asdict, dataclass, field


@dataclass
class Device:
    instance_id: str
    friendly_name: str = ""
    dev_class: str = ""
    manufacturer: str = ""
    hwid: list[str] = field(default_factory=list)
    status: str = "OK"  # OK | ERROR | UNKNOWN
    problem_code: int = 0
    driver_version: str = ""
    driver_date: str = ""
    driver_provider: str = ""

    def to_dict(self) -> dict:
        return asdict(self)


@dataclass
class DriverUpdate:
    hwid: str
    device_name: str
    instance_id: str
    installed_version: str = ""
    available_version: str = ""
    source: str = ""  # windows-update | ms-catalog | vendor | sdio | local
    download_url: str = ""
    arch: str = ""
    title: str = ""
    update_id: str = ""  # MS Catalog GUID; enables one-click CAB download
    vendor: str = ""
    vendor_url: str = ""

    def to_dict(self) -> dict:
        return asdict(self)
