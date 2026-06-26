from __future__ import annotations

import argparse
from datetime import date
from stock_collector.candidate_industry_backfill import backfill_candidate_industries
from stock_collector.mysql_writer import MySqlSettings
from stock_collector.mysql_writer import RawDataWriter
from stock_collector.mysql_writer import create_mysql_engine


def main() -> None:
    parser = argparse.ArgumentParser(description="Backfill candidate/signal industries with cninfo granular mappings.")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=3306)
    parser.add_argument("--user", default="stock_decision")
    parser.add_argument("--password", default="stock_decision_dev")
    parser.add_argument("--database", default="stock_decision")
    parser.add_argument("--trade-date", default="2026-06-25")
    parser.add_argument("--snapshot-version", default="end_of_day_final")
    args = parser.parse_args()

    trade_date = date.fromisoformat(args.trade_date)
    settings = MySqlSettings(
        host=args.host,
        port=args.port,
        database=args.database,
        user=args.user,
        password=args.password,
    )
    writer = RawDataWriter(create_mysql_engine(settings))
    result = backfill_candidate_industries(
        writer,
        trade_date=trade_date,
        snapshot_version=args.snapshot_version,
    )

    if not result.ready:
        print("No candidate/signal rows found for the requested trade date and snapshot version.")
        return

    if result.scanned_count == 0:
        print("No generic candidate/signal industries found.")
        return

    if result.repaired_count == 0:
        print("No candidate/signal industries could be resolved from cninfo.")
        return

    for line in result.updates:
        print(line)


if __name__ == "__main__":
    main()
