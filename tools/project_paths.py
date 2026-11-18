"""Resolve documentation in the public source tree and the local workspace."""
from pathlib import Path


def project_document(root, name):
    root = Path(root)
    for directory in ('docs/guides', 'docs/records', ''):
        path = root / directory / name
        if path.is_file():
            return path
    raise FileNotFoundError(f'Project document not found: {name}')


def publish_directory(root):
    """Store local release/test packages under E:\\工作流\\星引擎插件\\发布包 instead of inside 星枢源码."""
    root = Path(root)
    external = root.parent / '星引擎插件/发布包'
    if (root / 'tools/project_paths.py').is_file() and external.parent.is_dir():
        return external.resolve()
    return (root / '发布包').resolve()
