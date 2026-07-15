# /// script
# requires-python = ">=3.11"
# dependencies = ["requests", "python-dotenv"]
# ///
"""Upload store-page media (main image, banner, gallery screenshots/videos) to glitch.fun.

Auth: POST /auth/login with GLITCH_EMAIL (or GLITCH_USERNAME) + GLITCH_PASSWORD from
tools/glitch-media/.env (git-ignored) -> response field `token` (JWT), sent as
`Authorization: Bearer <token>`. A ready GLITCH_AUTH_TOKEN in .env skips the login.

Endpoints (verified against https://api.glitch.fun/api/docs and the official JS SDK):
  POST /titles/{title_id}/uploadMainImage    multipart field `image`
  POST /titles/{title_id}/uploadBannerImage  multipart field `image`
  POST /media                                multipart field `media` (image, video, ...)
  POST /titles/{title_id}/addMedia           JSON {media_id, order}
  DELETE /titles/{title_id}/removeMedia/{media_id}  detach media from the title
  GET  /titles/{title_id}                    current title incl. media list

Usage (run via uv, from the repo root):
  uv run tools/glitch-media/upload_media.py --list
  uv run tools/glitch-media/upload_media.py --main-image docs/screenshots/en/space_flight.png
  uv run tools/glitch-media/upload_media.py --media docs/screenshots/en/planet_surface.png
  uv run tools/glitch-media/upload_media.py --defaults --dry-run
"""

from __future__ import annotations

import argparse
import json
import mimetypes
import sys
from pathlib import Path

import requests
from dotenv import dotenv_values

API_BASE = "https://api.glitch.fun/api"
DEFAULT_TITLE_ID = "80f5dc18-dc0f-45de-9a57-8599e08669ed"
REPO_ROOT = Path(__file__).resolve().parents[2]
ENV_FILE = Path(__file__).resolve().parent / ".env"

# Curated default gallery: the English trailer short first, then the English press screenshots.
DEFAULT_GALLERY = [REPO_ROOT / "media/videos/BlocksBeyondTheStars_Short_EN.mp4"] + sorted(
    (REPO_ROOT / "docs/screenshots/en").glob("*.png")
)


def fail(message: str) -> "NoReturn":  # noqa: F821 - typing only
    print(f"ERROR: {message}", file=sys.stderr)
    sys.exit(1)


def unwrap(response: requests.Response) -> dict:
    """Glitch responses are usually wrapped in {"data": ...}; tolerate both shapes."""
    body = response.json()
    return body.get("data", body) if isinstance(body, dict) else body


def get_token(env: dict[str, str | None]) -> str:
    token = (env.get("GLITCH_AUTH_TOKEN") or "").strip()
    if token:
        return token

    email = (env.get("GLITCH_EMAIL") or "").strip()
    username = (env.get("GLITCH_USERNAME") or "").strip()
    password = (env.get("GLITCH_PASSWORD") or "").strip()
    if not password or not (email or username):
        fail(f"fill in GLITCH_EMAIL/GLITCH_USERNAME + GLITCH_PASSWORD (or GLITCH_AUTH_TOKEN) in {ENV_FILE}")

    credentials = {"email": email} if email else {"username": username}
    credentials["password"] = password
    response = requests.post(f"{API_BASE}/auth/login", json=credentials, timeout=30)
    if response.status_code != 200:
        fail(f"login failed ({response.status_code}): {response.text[:500]}")
    token = unwrap(response).get("token")
    if isinstance(token, dict):  # login returns {token: {access_token: "eyJ...", ...}}
        token = token.get("access_token")
    if not token:
        fail(f"login succeeded but no token in response: {response.text[:500]}")
    print("Logged in to glitch.fun.")
    return token


def multipart_upload(session: requests.Session, url: str, field: str, path: Path) -> requests.Response:
    mime = mimetypes.guess_type(path.name)[0] or "application/octet-stream"
    with path.open("rb") as fh:
        return session.post(url, files={field: (path.name, fh, mime)}, timeout=600)


def upload_title_image(session: requests.Session, title_id: str, kind: str, path: Path) -> None:
    response = multipart_upload(session, f"{API_BASE}/titles/{title_id}/upload{kind}Image", "image", path)
    if response.status_code not in (200, 201):
        fail(f"{kind} image upload failed ({response.status_code}): {response.text[:500]}")
    print(f"{kind} image set: {path.name}")


def attach_media(session: requests.Session, title_id: str, media_id: str, order: int, label: str) -> None:
    response = session.post(
        f"{API_BASE}/titles/{title_id}/addMedia",
        json={"media_id": media_id, "order": order},
        timeout=60,
    )
    if response.status_code not in (200, 201):
        fail(f"addMedia failed for {label} ({response.status_code}): {response.text[:500]}")
    print(f"Gallery [{order:2}]: {label} (media {media_id})")


def upload_gallery_item(session: requests.Session, title_id: str, path: Path, order: int) -> None:
    response = multipart_upload(session, f"{API_BASE}/media", "media", path)
    if response.status_code not in (200, 201):
        fail(f"media upload failed for {path.name} ({response.status_code}): {response.text[:500]}")
    media_id = unwrap(response).get("id")
    if not media_id:
        fail(f"media upload for {path.name} returned no id: {response.text[:500]}")
    attach_media(session, title_id, media_id, order, path.name)


def update_title(session: requests.Session, title_id: str, payload: dict) -> dict:
    response = session.put(f"{API_BASE}/titles/{title_id}", json=payload, timeout=60)
    if response.status_code != 200:
        fail(f"title update failed ({response.status_code}): {response.text[:500]}")
    return unwrap(response)


def remove_media(session: requests.Session, title_id: str, media_id: str) -> None:
    response = session.delete(f"{API_BASE}/titles/{title_id}/removeMedia/{media_id}", timeout=60)
    if response.status_code not in (200, 204):
        fail(f"removeMedia failed for {media_id} ({response.status_code}): {response.text[:500]}")
    print(f"Removed: {media_id}")


def list_title_media(session: requests.Session, title_id: str) -> None:
    response = session.get(f"{API_BASE}/titles/{title_id}", timeout=60)
    if response.status_code != 200:
        fail(f"could not fetch title ({response.status_code}): {response.text[:500]}")
    title = unwrap(response)
    print(f"Title: {title.get('name')} ({title_id})")
    for key, value in title.items():
        if "image" in key or "banner" in key:
            print(f"  {key}: {value}")
    media = title.get("media") or []
    print(f"  gallery    : {len(media)} item(s)")
    for item in media:
        print(f"    - {item.get('id')}  {item.get('type', '?')}  {item.get('url', '')}")


def existing_file(raw: str) -> Path:
    path = Path(raw)
    if not path.is_file():
        raise argparse.ArgumentTypeError(f"not a file: {raw}")
    return path


def main() -> None:
    parser = argparse.ArgumentParser(description="Upload store-page media to glitch.fun.")
    parser.add_argument("--title-id", default=None, help=f"Glitch title id (default: env or {DEFAULT_TITLE_ID})")
    parser.add_argument("--main-image", type=existing_file, help="set the title's main image")
    parser.add_argument("--banner", type=existing_file, help="set the title's banner image")
    parser.add_argument("--media", nargs="*", type=existing_file, default=[], help="gallery files (images/videos)")
    parser.add_argument("--defaults", action="store_true",
                        help="upload the curated gallery (docs/screenshots/en + EN trailer short)")
    parser.add_argument("--remove", nargs="*", default=[], metavar="MEDIA_ID",
                        help="detach these media ids from the title (runs before uploads)")
    parser.add_argument("--attach", nargs="*", default=[], metavar="MEDIA_ID",
                        help="attach already-uploaded media ids to the title gallery")
    parser.add_argument("--instructions-file", type=existing_file,
                        help="set the title's gameplay instructions from this text file")
    parser.add_argument("--genres", nargs="*", type=int, default=None, metavar="GENRE_ID",
                        help="set the title's genres (ids from GET /util/genres)")
    parser.add_argument("--update-json", type=existing_file,
                        help="PUT arbitrary title fields from a JSON object file (e.g. deep-dive texts)")
    parser.add_argument("--list", action="store_true", help="show the title's current media and exit")
    parser.add_argument("--dry-run", action="store_true", help="print what would be uploaded, no requests")
    args = parser.parse_args()

    env = dotenv_values(ENV_FILE)
    title_id = args.title_id or (env.get("GLITCH_TITLE_ID") or "").strip() or DEFAULT_TITLE_ID

    gallery = list(args.media)
    if args.defaults:
        missing = [p for p in DEFAULT_GALLERY if not p.is_file()]
        if missing:
            fail("default gallery files missing: " + ", ".join(str(p) for p in missing))
        gallery += DEFAULT_GALLERY

    if not (args.list or args.main_image or args.banner or gallery or args.remove or args.attach
            or args.instructions_file or args.genres is not None or args.update_json):
        parser.error("nothing to do — pass --list, --main-image, --banner, --media, --defaults, "
                     "--attach, --remove, --instructions-file, --genres or --update-json")

    if args.dry_run:
        print(f"[dry-run] title {title_id}")
        for media_id in args.remove:
            print(f"[dry-run] remove: {media_id}")
        if args.main_image:
            print(f"[dry-run] main image: {args.main_image}")
        if args.banner:
            print(f"[dry-run] banner: {args.banner}")
        for order, path in enumerate(gallery, start=1):
            print(f"[dry-run] gallery [{order:2}]: {path} ({path.stat().st_size / 1e6:.1f} MB)")
        return

    session = requests.Session()
    session.headers["Authorization"] = f"Bearer {get_token(env)}"
    session.headers["Accept"] = "application/json"

    if args.list:
        list_title_media(session, title_id)
        return

    payload = {}
    if args.update_json:
        payload.update(json.loads(args.update_json.read_text(encoding="utf-8")))
    if args.instructions_file:
        payload["instructions"] = args.instructions_file.read_text(encoding="utf-8")
    if args.genres is not None:
        payload["genres"] = args.genres
    if payload:
        title = update_title(session, title_id, payload)
        if "instructions" in payload:
            print(f"Instructions set ({len(payload['instructions'])} chars).")
        if "genres" in payload:
            names = [g.get("name") for g in title.get("genres") or [] if isinstance(g, dict)]
            print(f"Genres now: {', '.join(names) or title.get('genres')}")
        for key in payload:
            if key not in ("instructions", "genres"):
                print(f"Updated field: {key}")

    for media_id in args.remove:
        remove_media(session, title_id, media_id)
    if args.main_image:
        upload_title_image(session, title_id, "Main", args.main_image)
    if args.banner:
        upload_title_image(session, title_id, "Banner", args.banner)
    order = 0
    for order, path in enumerate(gallery, start=1):
        upload_gallery_item(session, title_id, path, order)
    for offset, media_id in enumerate(args.attach, start=order + 1):
        attach_media(session, title_id, media_id, offset, media_id)

    print("Done. Check the store page on glitch.fun.")


if __name__ == "__main__":
    main()
