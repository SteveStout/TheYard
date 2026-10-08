# The rendering service's image (ADR: A rendering service beside the API).
#
# Two stages. The first builds the service from the site's own sources with the
# site's own config (render/vite.config.ts), so every class name it writes is the
# class name the browser's bundle uses; the build carries React and everything
# else inside it. The second is Node and that build, with no node_modules, no
# sources and no package manager: what runs is one folder of JavaScript and the
# door, render/server.mjs.
#
# It is built from the repository's root, the same context as the API's image:
#   docker build -f render/Dockerfile -t theyard-render .

# #region build
FROM node:24-alpine AS build
WORKDIR /src
COPY package*.json ./
RUN npm ci
COPY vite.config.ts tsconfig.json tsconfig.app.json tsconfig.node.json ./
COPY src ./src
COPY render ./render
RUN npx vite build --config render/vite.config.ts
# #endregion build

# #region run
FROM node:24-alpine AS run
WORKDIR /app
# React reads this when it loads: production is its faster renderer, without the development checks.
ENV NODE_ENV=production
COPY --from=build --chown=node:node /src/dist-render ./dist-render
COPY --chown=node:node render/server.mjs ./render/server.mjs
# The build it was made from, which it must match before it draws for an API (render/render.ts).
ARG APP_VERSION=dev
ARG APP_COMMIT=local
ENV APP_VERSION=${APP_VERSION}
ENV APP_COMMIT=${APP_COMMIT}
ENV PORT=8080
EXPOSE 8080
USER node
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 CMD wget -qO- http://localhost:8080/healthz || exit 1
CMD ["node", "render/server.mjs"]
# #endregion run
