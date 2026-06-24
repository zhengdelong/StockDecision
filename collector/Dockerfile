FROM python:3.12-slim
WORKDIR /app
ENV PIP_DISABLE_PIP_VERSION_CHECK=1
ENV PIP_INDEX_URL=https://pypi.org/simple
COPY collector/pyproject.toml collector/README.md ./
COPY collector/stock_collector ./stock_collector
RUN pip install --no-cache-dir .
CMD ["python", "-m", "stock_collector.jobs", "health"]
