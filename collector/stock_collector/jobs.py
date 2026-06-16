import argparse
import json

from stock_collector.akshare_client import AkshareClient


def main() -> None:
    parser = argparse.ArgumentParser(description="Stock data collector jobs")
    parser.add_argument("job", choices=["health"], help="Job name to run")
    args = parser.parse_args()

    if args.job == "health":
        print(json.dumps(AkshareClient().health(), ensure_ascii=False))


if __name__ == "__main__":
    main()
