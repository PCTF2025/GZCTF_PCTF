FROM python:3.13-slim
ARG TARGETARCH
RUN apt-get update && apt-get install -y --no-install-recommends tcpdump util-linux ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*
RUN case "${TARGETARCH}" in \
      amd64) checksum=a8ff2a3edb37a98daf3aba7c3b284fe0aa5bff24166d896ab9ef64c8913c9f51 ;; \
      arm64) checksum=c31d252e203df5f4cf37f314bd3092eb79087e791631c1e607087c74b6d0423f ;; \
      *) exit 1 ;; \
    esac \
    && curl -fsSL --retry 3 "https://github.com/kubernetes-sigs/cri-tools/releases/download/v1.34.0/crictl-v1.34.0-linux-${TARGETARCH}.tar.gz" -o /tmp/crictl.tar.gz \
    && echo "${checksum}  /tmp/crictl.tar.gz" | sha256sum -c - \
    && tar -xzf /tmp/crictl.tar.gz -C /usr/local/bin crictl \
    && rm /tmp/crictl.tar.gz
COPY collector.py /opt/collector.py
ENV PYTHONUNBUFFERED=1 LC_ALL=C
ENTRYPOINT ["python", "/opt/collector.py"]
