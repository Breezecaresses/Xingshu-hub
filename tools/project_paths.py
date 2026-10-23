"""Resolve documentation in the public source tree and the local workspace."""
from pathlib import Path


def project_document(root, name):
    root = Path(root)
    for directory in ('docs/guides', 'docs/records', ''):
        path = root / directory / name
        if path.is_file():
            return path
    raise FileNotFoundError(f'Project document not found: {name}')
