"""Install isolated, hash-verified development tools; no global PATH changes."""
from pathlib import Path
import concurrent.futures, hashlib, json, urllib.request, zipfile

ROOT = Path(__file__).resolve().parents[1]
TOOLS = ROOT / '.tools'
TOOLS.mkdir(exist_ok=True)

def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'Mimicraft-dev'}), timeout=60) as r:
        return json.load(r)

def install(item):
    name, url, expected, algorithm = item
    archive = TOOLS / (name + '.zip')
    destination = TOOLS / name
    if archive.exists() and destination.exists():
        digest=hashlib.new(algorithm,archive.read_bytes()).hexdigest()
        ready=(destination/'dotnet.exe').exists() if name=='dotnet' else any(destination.rglob('ffmpeg.exe'))
        if digest.lower()==expected.lower() and ready:
            print('Already prepared '+name,flush=True)
            return {'name':name,'url':url,'hash':digest,'algorithm':algorithm}
    print('Downloading ' + name, flush=True)
    # Short, individually length-checked requests avoid truncated long downloads.
    request = urllib.request.Request(url, headers={'Range': 'bytes=0-0', 'User-Agent': 'Mimicraft-dev'})
    with urllib.request.urlopen(request, timeout=45) as response:
        total = int(response.headers['Content-Range'].split('/')[-1])
    start = archive.stat().st_size if archive.exists() else 0
    if start > total:
        raise ValueError('Existing archive is larger than server size')
    ranges = [(i, min(i + 4 * 1024 * 1024 - 1, total - 1)) for i in range(start, total, 4 * 1024 * 1024)]
    def chunk(bounds):
        begin, end = bounds
        for attempt in range(3):
            try:
                req = urllib.request.Request(url, headers={'Range': f'bytes={begin}-{end}', 'User-Agent': 'Mimicraft-dev'})
                with urllib.request.urlopen(req, timeout=45) as response:
                    data = response.read()
                    if response.status != 206 or len(data) != end - begin + 1:
                        raise ValueError('Incomplete range response')
                    return data
            except Exception:
                if attempt == 2:
                    raise
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as workers, archive.open('ab') as target:
        for data in workers.map(chunk, ranges):
            target.write(data)
    digest = hashlib.new(algorithm, archive.read_bytes()).hexdigest()
    if digest.lower() != expected.lower():
        raise ValueError(name + ' hash mismatch; refusing extraction')
    if destination.exists():
        raise ValueError('Refusing to replace existing tool directory ' + str(destination))
    with zipfile.ZipFile(archive) as z:
        for entry in z.namelist():
            if not (destination / entry).resolve().is_relative_to(destination.resolve()):
                raise ValueError('Unsafe archive path')
        z.extractall(destination)
    print('Verified and extracted ' + name, flush=True)
    return {'name': name, 'url': url, 'hash': digest, 'algorithm': algorithm}

if __name__ == '__main__':
    lock=json.loads((ROOT/'scripts/toolchain.lock.json').read_text(encoding='utf-8'))
    items=[(tool['name'],tool['url'],tool['hash'],tool['algorithm']) for tool in lock['tools']]
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        result = list(pool.map(install, items))
    (TOOLS / 'installed.json').write_text(json.dumps({'sdk': lock['sdk'], 'tools': result}, indent=2), encoding='utf-8')
    print('Tool installation finished. SDK ' + lock['sdk'], flush=True)
