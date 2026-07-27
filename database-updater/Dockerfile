FROM alpine:3.20

RUN apk add --no-cache bash curl ca-certificates coreutils util-linux

WORKDIR /app
COPY update.sh /app/update.sh
RUN chmod +x /app/update.sh

ENTRYPOINT ["/app/update.sh"]