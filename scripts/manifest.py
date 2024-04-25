#!/usr/bin/env python3
import hashlib
import json
import sys
from datetime import datetime
from urllib.request import urlopen


def md5sum(filename):
    with open(filename, 'rb') as f:
        return hashlib.md5(f.read()).hexdigest()


def fix_version_string(version_str):
    # Check if the version string contains "-alpha.0"
    if "-alpha.0" in version_str:
        version_str = version_str.replace("-alpha.0", "")
        parts = version_str.split(".")
        parts[2] = str(int(parts[2]) - 1)
        return ".".join(parts)
    return version_str

def generate(filename, version):
    return {
        'checksum': md5sum(filename),
        'changelog': f'Automatic Release by Github Actions: https://github.com/hexxone/TeleJelly/releases/tag/{version}',
        'targetAbi': '10.8.0.0',
        'sourceUrl': 'https://github.com/hexxone/TeleJelly/releases/download/'
                     f'{version}/TeleJelly_v{version}.zip',
        'timestamp': datetime.now().strftime('%Y-%m-%dT%H:%M:%SZ'),
        'version': fix_version_string(version)
    }


def main():
    filename = sys.argv[1]
    version = filename.split('_', maxsplit=1)[1] \
        .removeprefix('v') \
        .removesuffix('.zip')

    with urlopen('https://raw.githubusercontent.com/hexxone/TeleJelly/dist/manifest.json') as f:
        manifest = json.load(f)

    manifest[0]['versions'].insert(0, generate(filename, version))

    with open('manifest.json', 'w') as f:
        json.dump(manifest, f, indent=2)


if __name__ == '__main__':
    main()
