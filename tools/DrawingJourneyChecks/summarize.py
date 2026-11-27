"""Summarize one batch. Exit successfully only for a complete, consistent 100-seed run."""

import argparse
import hashlib
import json
from collections import Counter, defaultdict
from pathlib import Path


def summarize(batch, manifest, game_version="0.114.0"):
    game_version = game_version.strip().lstrip("vV")
    samples = json.loads(manifest.read_bytes().decode("utf-8-sig"))
    digest = hashlib.sha256(manifest.read_bytes()).hexdigest()
    if len(samples) != 100 or len({s["seed"] for s in samples}) != 100:
        raise ValueError("Manifest must contain 100 unique seeds")
    environments = [
        json.loads(p.read_text()) for p in batch.glob("worker-*/environment.json")
    ]
    records = []
    by_worker = defaultdict(list)
    for path in batch.glob("worker-*/*.json"):
        if path.name in ("environment.json", "exit-status.json"):
            continue
        item = json.loads(path.read_text())
        ordinal = item["ordinal"]
        if (
            ordinal not in range(100)
            or item["sample"] != samples[ordinal]
            or item["sample"]["seed"] != path.stem
        ):
            raise ValueError(f"Receipt does not match manifest: {path}")
        if item["minimal"] != (ordinal < 50):
            raise ValueError(f"Wrong cohort: {path}")
        by_worker[path.parent].append(ordinal)
        steps = item["log"]
        nodes = [n for n in steps if n["stage"] == "node"]
        result = steps[-1]
        stages = Counter(n["stage"] for n in steps)
        last = nodes[-1] if nodes else {}
        local = [
            c["Code"]
            for c in last.get("causes", [])
            if c["NodeId"] == last["Position"]["NodeId"]
        ]
        fault = result.get("fault") or result.get("error")
        acts = sorted({n["Position"]["Act"] for n in nodes})
        cards_by_act = {}
        for act in acts:
            counts = Counter()
            for node in nodes:
                if node["Position"]["Act"] != act or node["room"] not in (
                    "Monster",
                    "Elite",
                    "Boss",
                ):
                    continue
                for reward in node["rewards"]:
                    if reward["kind"] == "Card" and reward["cards"]:
                        counts[node["room"] + "Groups"] += 1
                        counts[node["room"] + "Cards"] += len(reward["cards"])
            cards_by_act[act] = counts
        boss_acts = {
            n["Position"]["Act"]
            for n in nodes
            if n["room"] == "Boss"
            and any(r["kind"] == "Card" and len(r["cards"]) >= 3 for r in n["rewards"])
        }
        complete = (
            result.get("complete", False)
            and result.get("stage") == "result"
            and not fault
            and result.get("terminal") is None
            and result["nodes"] == len(nodes)
            and acts == [1, 2, 3]
            and last["Position"] == result["last"]
            and last["Position"]["Act"] == 3
            and last["Advanced"]
            and last["room"] in ("Boss", "Victory")
        )
        records.append(
            dict(
                ordinal=item["ordinal"],
                **item["sample"],
                minimal=item["minimal"],
                complete=complete,
                bossCardsKnown=result.get("bossCardsKnown", False)
                and {1, 2} <= boss_acts,
                visitedActs=acts,
                cardRewardsByAct=cards_by_act,
                bossCards=result.get("bossCards", []),
                nodes=len(nodes),
                last=last.get("Position"),
                lastModel=last.get("model"),
                causes=local,
                fault=fault,
                supplements=[step for step in steps if step["stage"] == "supplement"],
                rewardNodes=sum(
                    any(r["IsKnown"] or r["cards"] for r in n["rewards"]) for n in nodes
                ),
                stages=stages,
            )
        )
    ordinals = [r["ordinal"] for r in records]
    if len(set(ordinals)) != len(ordinals):
        raise ValueError("A sample was counted in more than one worker")
    if any(
        e["seedsSha256"].lower() != digest
        or e["release"]["version"].strip().lstrip("vV") != game_version
        or e.get("actualVersion", game_version) != game_version
        or e.get("expectedVersion", game_version) != game_version
        or e["policy"] != "LiveMapDefaults"
        for e in environments
    ):
        raise ValueError("Mismatched worker manifest or runtime")
    if len({e["loadedProductMvid"] for e in environments}) > 1:
        raise ValueError("Workers used different product assemblies")
    groups = {}
    for name, minimal in [("minimal", True), ("rich", False)]:
        rows = [r for r in records if r["minimal"] == minimal]
        supplements = Counter(t["reason"] for r in rows for t in r["supplements"])
        affected = Counter(
            reason for r in rows for reason in {s["reason"] for s in r["supplements"]}
        )
        rarities = Counter(
            rarity
            for row in rows
            for boss in row["bossCards"]
            for group in boss.get("rarities", [])
            for rarity in group
        )
        groups[name] = dict(
            receipts=len(rows),
            completed=sum(r["complete"] for r in rows),
            withBothBossOffers=sum(r["bossCardsKnown"] for r in rows),
            passed=sum(r["complete"] and r["bossCardsKnown"] for r in rows),
            passedWithoutSupplements=sum(
                r["complete"] and r["bossCardsKnown"] and not r["supplements"]
                for r in rows
            ),
            nodes=sum(r["nodes"] for r in rows),
            rewardNodes=sum(r["rewardNodes"] for r in rows),
            bossCardRarities=rarities,
            cardRewardsByAct={
                act: sum(
                    (r["cardRewardsByAct"].get(act, Counter()) for r in rows), Counter()
                )
                for act in (1, 2, 3)
            },
            supplementOperations=supplements,
            supplementSeeds=affected,
            actions=dict(sum((Counter(r["stages"]) for r in rows), Counter())),
        )
    statuses = []
    coverage_errors = []
    workers = sorted(p for p in batch.glob("worker-*") if p.is_dir())
    if not workers:
        coverage_errors.append("Missing worker directories")
    for worker in workers:
        status_path = worker / "exit-status.json"
        if not (worker / "environment.json").is_file() or not status_path.is_file():
            coverage_errors.append(f"{worker.name} missing environment or exit status")
            continue
        status = json.loads(status_path.read_text())
        statuses.append(status)
        expected = status.get(
            "ordinals", list(range(status["start"], status["start"] + status["count"]))
        )
        if (
            not expected
            or worker.name != f"worker-{expected[0]}"
            or status["start"] != expected[0]
            or status["count"] != len(expected)
            or sorted(by_worker[worker]) != sorted(expected)
        ):
            coverage_errors.append(
                f"{worker.name} receipt ordinals differ from declared slice"
            )
        if status["exitCode"] != 0:
            coverage_errors.append(f"{worker.name} exited with {status['exitCode']}")
    return dict(
        manifestSha256=digest,
        gameVersion=game_version,
        productMvid=environments[0]["loadedProductMvid"] if environments else None,
        workerStatus=statuses,
        coverageErrors=coverage_errors,
        passed=(
            not coverage_errors
            and set(ordinals) == set(range(100))
            and all(r["complete"] and r["bossCardsKnown"] for r in records)
            and groups["rich"]["actions"].get("reward-edit-clear-recompute", 0) > 0
        ),
        groups=groups,
        seeds=sorted(records, key=lambda r: r["ordinal"]),
    )


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("batch", type=Path)
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--game-version", default="0.114.0")
    args = parser.parse_args()
    report = summarize(args.batch, args.manifest, args.game_version)
    (args.batch / "summary.json").write_text(
        json.dumps(report, indent=2), encoding="utf-8"
    )
    print(json.dumps(report["groups"], indent=2))
    for row in report["seeds"]:
        if not (row["complete"] and row["bossCardsKnown"]):
            print(
                row["ordinal"],
                row["seed"],
                row["lastModel"],
                row["causes"],
                (row["fault"] or "").splitlines()[:1],
            )
    raise SystemExit(0 if report["passed"] else 1)
