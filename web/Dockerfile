FROM node:22-slim
WORKDIR /app
COPY web/package.json web/package-lock.json* ./
RUN npm install
COPY web/ .
CMD ["npm", "run", "dev", "--", "--host", "0.0.0.0"]
