FROM python:3.12-slim
ARG PIP_INDEX_URL=https://pypi.tuna.tsinghua.edu.cn/simple
WORKDIR /app
ENV PIP_DISABLE_PIP_VERSION_CHECK=1
ENV PIP_INDEX_URL=${PIP_INDEX_URL}
ENV PIP_DEFAULT_TIMEOUT=120
ENV PIP_RETRIES=5
COPY collector/pyproject.toml collector/README.md ./
COPY collector/stock_collector ./stock_collector
RUN pip install --no-cache-dir --prefer-binary .
CMD ["python", "-m", "stock_collector.jobs", "health"]
