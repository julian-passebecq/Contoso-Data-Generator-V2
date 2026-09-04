"""Validate the client/server storage boundary before creating a Spark session."""
from __future__ import annotations

from pathlib import Path
from urllib.parse import urlsplit

SHARED_SCHEMES = {"gs", "s3", "s3a", "abfs", "abfss"}


def shared_uri(value):
    parsed = urlsplit(str(value))
    if parsed.scheme not in SHARED_SCHEMES or not parsed.netloc or parsed.query or parsed.fragment or parsed.password or (
            parsed.username and parsed.scheme not in ("abfs", "abfss")):
        raise ValueError("Remote Spark requires a shared gs://, s3://, s3a://, abfs:// or abfss:// URI without embedded credentials")
    if ".." in parsed.path.split("/"):
        raise ValueError("Shared Spark paths cannot contain traversal")
    return str(value).rstrip("/")


def lake_path(value, mode):
    if mode == "connect-remote":
        shared_uri(value)
        raise NotImplementedError("connect-remote session support is separate: the generated Forge adapter still needs a shared input/metadata transport. Uploading a client-local work package does not make it server-visible.")
    if urlsplit(str(value)).scheme and not (len(str(value)) >= 2 and str(value)[1] == ":"):
        raise ValueError("The Colab local adapter requires a local lake directory")
    return Path(value).resolve()
