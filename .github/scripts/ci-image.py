"""Prints the image of a service of ci/docker-compose.yml: the images that CI starts with `docker run`.

    docker run ... "$(python3 .github/scripts/ci-image.py mssql)"

They live in that file so Dependabot keeps their tag and digest up to date. check-pins.py checks that each one is pinned
to a digest and that each service a workflow asks for exists.
"""
import pathlib
import sys

# Nothing to cache: the checkout stays as it was.
sys.dont_write_bytecode = True
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from compose_images import CI_IMAGES, ROOT, compose_services  # noqa: E402

if __name__ == '__main__':
    if len(sys.argv) != 2:
        sys.exit('usage: ci-image.py <service>')
    images = {service['name']: service['image'][1] for service in compose_services(CI_IMAGES) if service['image']}
    if sys.argv[1] not in images:
        sys.exit(f'{CI_IMAGES.relative_to(ROOT)}: there is no service {sys.argv[1]} with an image')
    print(images[sys.argv[1]])
