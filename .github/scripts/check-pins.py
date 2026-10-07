"""Fails when an action of a workflow is not pinned to a commit or a Docker image is not pinned to a digest.

A tag can be moved to other code tomorrow; a commit SHA or an image digest cannot. The version stays next to each pin,
for whoever reads it: a comment after an action (`uses: actions/checkout@<sha> # v5.1.0`), the tag before an image's
digest (`mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:<digest>`). It checks:
  - every `uses:` of .github/workflows (a local action, `./...`, needs no pin);
  - every image that a workflow passes to `docker run`, `docker create` or `docker pull`;
  - every `FROM` of a Dockerfile that is not one of its own stages;
  - every `image:` of a docker-compose service that does not build it.
"""
import pathlib
import re
import shlex
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
COMMIT = re.compile(r'@[0-9a-f]{40}$')
DIGEST = re.compile(r'@sha256:[0-9a-f]{64}$')
# Options of `docker run` that take a value: the image is the first argument that is neither an option nor its value.
VALUED_OPTIONS = {'--name', '--publish', '-p', '--env', '-e', '--env-file', '--volume', '-v', '--mount', '--network',
                  '--workdir', '-w', '--entrypoint', '--user', '-u', '--platform', '--label', '-l', '--hostname', '-h'}


def workflow_problems(path, name):
    text = path.read_text(encoding='utf-8')
    for number, line in enumerate(text.splitlines(), 1):
        match = re.match(r'^\s*(?:- )?uses:\s*([^\s#]+)', line)
        if match and not match.group(1).startswith('./') and not COMMIT.search(match.group(1)):
            yield f'{name}:{number}: the action {match.group(1)} is not pinned to the full SHA of a commit'
    # A command continues on the next line after a backslash.
    for line in re.sub(r'\\\n', ' ', text).splitlines():
        if 'docker' not in line:
            continue
        try:
            tokens = shlex.split(line)
        except ValueError:
            continue
        for index in range(len(tokens) - 1):
            if tokens[index] == 'docker' and tokens[index + 1] in ('run', 'create', 'pull'):
                image = first_argument(tokens[index + 2:])
                if image and not DIGEST.search(image):
                    yield f'{name}: the image {image} is not pinned to a digest'


def first_argument(tokens):
    skip = False
    for token in tokens:
        if skip:
            skip = False
        elif token.startswith('-'):
            skip = '=' not in token and token in VALUED_OPTIONS
        else:
            return token
    return None


def dockerfile_problems(path, name):
    stages = {'scratch'}
    for number, line in enumerate(path.read_text(encoding='utf-8').splitlines(), 1):
        match = re.match(r'^\s*FROM\s+(?:--platform=\S+\s+)?(\S+)(?:\s+AS\s+(\S+))?', line, re.IGNORECASE)
        if not match:
            continue
        image, stage = match.group(1), match.group(2)
        if image.lower() not in stages and not DIGEST.search(image):
            yield f'{name}:{number}: the image {image} is not pinned to a digest'
        if stage:
            stages.add(stage.lower())


def compose_problems(path, name):
    services = []
    in_services = False
    service_indent = child_indent = None
    for number, line in enumerate(path.read_text(encoding='utf-8').splitlines(), 1):
        if not line.strip() or line.lstrip().startswith('#'):
            continue
        indent = len(line) - len(line.lstrip())
        if indent == 0:
            in_services = line.rstrip() == 'services:'
            continue
        if not in_services:
            continue
        if service_indent is None or indent <= service_indent:
            service_indent, child_indent = indent, None
            services.append({'build': False, 'image': None})
            continue
        child_indent = child_indent or indent
        if indent != child_indent:
            continue
        key, _, value = line.strip().partition(':')
        if key == 'build':
            services[-1]['build'] = True
        elif key == 'image':
            services[-1]['image'] = (number, value.strip().strip('"\''))
    for service in services:
        if service['image'] and not service['build'] and not DIGEST.search(service['image'][1]):
            yield f'{name}:{service["image"][0]}: the image {service["image"][1]} is not pinned to a digest'


def problems(root):
    for path in sorted((root / '.github' / 'workflows').glob('*.y*ml')):
        yield from workflow_problems(path, path.relative_to(root))
    for path in sorted(root.rglob('Dockerfile*')):
        if 'node_modules' not in path.parts:
            yield from dockerfile_problems(path, path.relative_to(root))
    for path in sorted(root.glob('docker-compose*.y*ml')):
        yield from compose_problems(path, path.relative_to(root))


if __name__ == '__main__':
    found = list(problems(pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT))
    for problem in found:
        print(problem)
    if found:
        print('::error::Pin each action to the full SHA of a release commit (its version as a comment) and each '
              'Docker image to its digest (its tag before the digest).')
    sys.exit(1 if found else 0)
