"""The services of a docker-compose file and their images, read without a YAML library (the runner has none).

Used by check-pins.py, which checks that each image is pinned to a digest, and by ci-image.py, which gives CI the image
of each service of CI_IMAGES. It reads the shape these files have: a top-level `services:`, one service per key and its
`image:` or `build:` one level below.
"""
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
# The images that CI starts with `docker run` (.github/workflows/ci.yml), where Dependabot keeps them up to date. Out of
# .github: Dependabot writes there only for the actions, and refuses its pull request otherwise.
CI_IMAGES = ROOT / 'ci' / 'docker-compose.yml'


def compose_services(path):
    """Each service with its name, whether it builds its image, and its image as (line number, reference) or None."""
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
            services.append({'name': line.strip().rstrip(':'), 'build': False, 'image': None})
            continue
        child_indent = child_indent or indent
        if indent != child_indent:
            continue
        key, _, value = line.strip().partition(':')
        if key == 'build':
            services[-1]['build'] = True
        elif key == 'image':
            services[-1]['image'] = (number, value.strip().strip('"\''))
    return services
