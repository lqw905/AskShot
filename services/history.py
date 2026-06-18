"""File-based history storage — zero dependencies, just JSON files."""

import json
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Optional


class HistoryStore:
    def __init__(self, data_dir: Path = Path("data")):
        self.data_dir = data_dir
        self.history_dir = data_dir / "history"
        self.screenshots_dir = data_dir / "screenshots"
        self.favorites_file = data_dir / "favorites.json"

        # Ensure directories exist
        self.history_dir.mkdir(parents=True, exist_ok=True)
        self.screenshots_dir.mkdir(parents=True, exist_ok=True)
        if not self.favorites_file.exists():
            self.favorites_file.write_text("[]", encoding="utf-8")

    def save(
        self,
        ocr_text: str,
        analysis: str,
        user_question: str = "",
        screenshot_path: Optional[str] = None,
        image_hash: str = "",
        tags: Optional[list[str]] = None,
    ) -> str:
        """Save an analysis record. Returns the record ID."""
        ts = datetime.utcnow()
        hash_part = image_hash[:8] if image_hash else "00000000"
        record_id = f"{ts.strftime('%Y-%m-%d_%H%M%S')}_{hash_part}"

        record = {
            "id": record_id,
            "timestamp": ts.isoformat(),
            "ocr_text": ocr_text,
            "analysis": analysis,
            "user_question": user_question,
            "screenshot_path": screenshot_path,
            "tags": tags or [],
            "is_favorite": False,
        }

        file_path = self.history_dir / f"{record_id}.json"
        file_path.write_text(
            json.dumps(record, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        return record_id

    def get_recent(self, limit: int = 10, hours: int = 24) -> list[dict]:
        """Get the most recent records within the time window."""
        cutoff = datetime.now(timezone.utc) - timedelta(hours=hours)
        results = []

        files = sorted(
            self.history_dir.glob("*.json"),
            key=lambda f: f.stat().st_mtime,
            reverse=True,
        )

        for f in files:
            if len(results) >= limit:
                break
            mtime = datetime.fromtimestamp(f.stat().st_mtime, tz=timezone.utc)
            if mtime < cutoff:
                continue
            try:
                results.append(json.loads(f.read_text(encoding="utf-8")))
            except (json.JSONDecodeError, OSError):
                continue

        return results

    def search(self, query: str, limit: int = 10) -> list[dict]:
        """Keyword search across all history records."""
        keywords = query.lower().split()
        scored: list[tuple[int, dict]] = []

        for f in self.history_dir.glob("*.json"):
            try:
                data = json.loads(f.read_text(encoding="utf-8"))
            except (json.JSONDecodeError, OSError):
                continue

            text = (
                f"{data.get('ocr_text', '')} "
                f"{data.get('analysis', '')} "
                f"{data.get('user_question', '')}"
            ).lower()

            score = sum(text.count(kw) for kw in keywords)
            if score > 0:
                scored.append((score, data))

        scored.sort(key=lambda x: x[0], reverse=True)
        return [item for _, item in scored[:limit]]

    def get_favorites(self) -> list[dict]:
        """Get all favorited records."""
        try:
            favs: list[str] = json.loads(self.favorites_file.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            return []

        results = []
        for fid in favs:
            f = self.history_dir / f"{fid}.json"
            if f.exists():
                try:
                    results.append(json.loads(f.read_text(encoding="utf-8")))
                except (json.JSONDecodeError, OSError):
                    continue
        return results

    def toggle_favorite(self, record_id: str) -> bool:
        """Toggle favorite status. Returns the new status."""
        try:
            favs: list[str] = json.loads(self.favorites_file.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            favs = []

        if record_id in favs:
            favs.remove(record_id)
            is_fav = False
        else:
            favs.append(record_id)
            is_fav = True

        self.favorites_file.write_text(json.dumps(favs, indent=2), encoding="utf-8")

        # Sync to the record file
        record_file = self.history_dir / f"{record_id}.json"
        if record_file.exists():
            try:
                record = json.loads(record_file.read_text(encoding="utf-8"))
                record["is_favorite"] = is_fav
                record_file.write_text(
                    json.dumps(record, ensure_ascii=False, indent=2),
                    encoding="utf-8",
                )
            except (json.JSONDecodeError, OSError):
                pass

        return is_fav
