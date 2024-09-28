
FROM lightninglabs/lnd:v0.18.3-beta

RUN apk add --no-cache --update gettext jq curl

COPY ./docker/lnd/lnd.conf.template /app/lnd.conf.template
COPY ./docker/lnd/entrypoint.sh /app/entrypoint.sh

ENTRYPOINT ["/app/entrypoint.sh"]