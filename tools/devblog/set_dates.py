# /// script
# requires-python = ">=3.11"
# dependencies = ["requests"]
# ///
"""Backdate published blog posts to a natural timeline.

Release posts get their real GitHub release date (+ a small "wrote the
announcement afterwards" offset). The standalone articles are spread into the
quiet days between releases. Today's school post is left untouched, as are
release posts whose date already sits close to the actual release.

Usage (from tools/devblog/):
  uv run set_dates.py           # plan only: show current -> target dates
  uv run set_dates.py --apply   # PATCH the posts
"""

from __future__ import annotations

import argparse
import re
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

import requests

ENV_FILE = Path(__file__).resolve().parent / ".env"
API = "https://www.wixapis.com"

# GitHub release publishedAt (gh release list, 2026-07-10)
RELEASES = {
    "0.1.0": "2026-06-20T02:24:35Z",
    "0.2.0": "2026-06-20T11:38:51Z",
    "0.3.0": "2026-06-20T16:39:01Z",
    "0.3.1": "2026-06-20T17:37:48Z",
    "0.3.2": "2026-06-21T09:40:54Z",
    "0.4.0": "2026-06-21T17:06:04Z",
    "0.4.1": "2026-06-22T18:54:52Z",
    "0.4.2": "2026-06-23T21:25:25Z",
    "0.5.0": "2026-06-25T23:14:36Z",
    "0.6.0": "2026-06-27T17:03:15Z",
    "0.6.1": "2026-06-28T15:39:09Z",
    "0.6.2": "2026-06-29T22:31:33Z",
    "0.7.0": "2026-07-05T17:56:24Z",
    "0.7.1": "2026-07-06T00:08:56Z",
    "0.7.2": "2026-07-06T21:01:20Z",
    "0.7.3": "2026-07-07T19:46:32Z",
    "0.7.4": "2026-07-08T22:04:34Z",
}

# Standalone articles -> natural dates in the gaps between releases.
# Keyed by a distinctive title substring (matches DE and EN titles).
SPECIALS = {
    "Minecraft":   "2026-06-20T19:32:00Z",  # origin story, on launch day evening
    "Open Source": "2026-06-24T17:21:00Z",  # between v0.4.2 and v0.5.0
    "100%":        "2026-06-26T20:15:00Z",  # between v0.5.0 and v0.6.0
    "YouTube":     "2026-07-02T19:08:00Z",  # quiet stretch before v0.7.0
}

SKIP_SUBSTRINGS = ("Schule", "school")  # today's post stays today
EN_EXTRA_MINUTES = 5   # EN version published shortly after DE
KEEP_IF_WITHIN = timedelta(hours=3)  # already-natural release posts stay


def load_env() -> dict[str, str]:
    env: dict[str, str] = {}
    for line in ENV_FILE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            k, _, v = line.partition("=")
            env[k.strip()] = v.strip()
    return env


def parse_dt(s: str) -> datetime:
    return datetime.fromisoformat(s.replace("Z", "+00:00"))


def offset_minutes(version: str) -> int:
    """Deterministic pseudo-natural delay between release and announcement."""
    return 25 + sum(ord(c) for c in version) % 45


def target_date(post: dict) -> datetime | None:
    title = post["title"]
    if any(s in title for s in SKIP_SUBSTRINGS):
        return None

    m = re.search(r"[vV](?:ersion)?\s?(0\.\d+\.\d+)", title)
    if m and m.group(1) in RELEASES:
        version = m.group(1)
        release = parse_dt(RELEASES[version])
        current = parse_dt(post["firstPublishedDate"])
        if timedelta(0) <= current - release <= KEEP_IF_WITHIN:
            return None  # already natural
        base = release + timedelta(minutes=offset_minutes(version))
    else:
        base = None
        for key, date in SPECIALS.items():
            if key in title:
                base = parse_dt(date)
                break
        if base is None:
            print(f"  !! no rule for: {title!r} — skipping")
            return None

    if post["language"] == "en":
        base += timedelta(minutes=EN_EXTRA_MINUTES)
    return base


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="PATCH the posts (default: plan only)")
    args = parser.parse_args()

    env = load_env()
    session = requests.Session()
    session.headers.update({
        "Authorization": env["WIX_API_KEY"],
        "wix-site-id": env["WIX_SITE_ID"],
        "Content-Type": "application/json",
    })

    r = session.get(f"{API}/blog/v3/posts?paging.limit=100", timeout=60)
    r.raise_for_status()
    posts = r.json()["posts"]
    print(f"{len(posts)} published posts fetched.")

    changes = []
    for post in posts:
        target = target_date(post)
        if target is None:
            continue
        changes.append((post, target))

    changes.sort(key=lambda c: c[1])
    for post, target in changes:
        print(f"  {post['firstPublishedDate'][:16]} -> {target.isoformat()[:16]}"
              f"  [{post['language']}] {post['title'][:60]}")
    print(f"{len(changes)} posts to change.")

    if not args.apply:
        print("Plan only — run with --apply to write.")
        return

    for post, target in changes:
        iso = target.isoformat().replace("+00:00", "Z")
        r = session.patch(
            f"{API}/blog/v3/draft-posts/{post['id']}",
            json={"draftPost": {"id": post["id"], "firstPublishedDate": iso},
                  "action": "UPDATE_PUBLICATION"},
            timeout=60,
        )
        if not r.ok:
            sys.exit(f"PATCH {post['title']!r} -> HTTP {r.status_code}: {r.text[:300]}")
        print(f"  set {iso}  [{post['language']}] {post['title'][:60]}")

    # verify against the live posts endpoint
    r = session.get(f"{API}/blog/v3/posts?paging.limit=100", timeout=60)
    r.raise_for_status()
    live = {p["id"]: p["firstPublishedDate"] for p in r.json()["posts"]}
    bad = [(p["title"], live.get(p["id"])) for p, t in changes
           if abs(parse_dt(live[p["id"]]) - t) > timedelta(minutes=1)]
    if bad:
        print("VERIFY FAILED for:")
        for title, got in bad:
            print(f"  {title[:60]} -> live date {got}")
        sys.exit(1)
    print(f"Verified: all {len(changes)} live posts show their new dates.")


if __name__ == "__main__":
    main()
