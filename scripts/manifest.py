#!/usr/bin/env python3
import hashlib
import json
import sys
from datetime import datetime
from urllib.request import urlopen

# Settings
git_user = "hexxone"
git_project = "TeleJelly"
git_manifest_branch = "dist"
git_manifest_path = "manifest.json"


def md5sum(filename):
    with open(filename, 'rb') as f:
        return hashlib.md5(f.read()).hexdigest()


def fix_version_string(version_str):
    if "-alpha.0" in version_str:
        version_str = version_str.replace("-alpha.0", "")
        parts = version_str.split(".")
        parts[2] = str(int(parts[2]) - 1)
        return ".".join(parts)
    return version_str


def make_manifest_version(checksum, source_url, version, timestamp):
    return {
        'checksum': checksum,
        'changelog': f'Automatic Release by Github Actions: https://github.com/{git_user}/{git_project}/releases/tag/{version}',
        'targetAbi': '10.8.0.0',
        'sourceUrl': source_url,
        'timestamp': timestamp,
        'version': fix_version_string(version)
    }


def add_manifest_version(manifest_version):
    with urlopen(f'https://raw.githubusercontent.com/{git_user}/{git_project}/{git_manifest_branch}/{git_manifest_path}') as f:
        manifest = json.load(f)

    manifest[0]['versions'].insert(0, manifest_version)

    with open(git_manifest_path, 'w') as f:
        json.dump(manifest, f, indent=2)


def update_meta(version, timestamp):
    print("TODO update meta.json timestamp, version, changelog")


def make_zip(target_file, source_files):
    print("TODO make zip of meta.json and tj.dll")


def main():
    # TODO change how script is called in GH Actions
    version = sys.argv[1]
    dll_path = sys.argv[2]
    
    zip_filename = f'{git_project}_v{version}.zip'
    source_url = f'https://github.com/{git_user}/{git_project}/releases/download/{version}/{zip_filename}'

    timestamp = datetime.now().strftime('%Y-%m-%dT%H:%M:%SZ')
    
    # TODO update_meta
    meta_path = update_meta(version, timestamp)
    
    # TODO remove ZIP from msbuild
    # TODO make_zip
    zip_path = make_zip(zip_filename, [dll_path, meta_path])
    checksum = md5sum(zip_path)
    manifest_version = make_manifest_version(checksum, version, timestamp)

    add_manifest_version(manifest_version)


if __name__ == '__main__':
    main()
