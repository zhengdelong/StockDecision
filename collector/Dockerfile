FROM python:3.12-slim
WORKDIR /app
COPY collector/pyproject.toml collector/README.md ./
COPY collector/stock_collector ./stock_collector
RUN pip install --no-cache-dir .
CMD ["python", "-m", "stock_collector.jobs", "health"]
