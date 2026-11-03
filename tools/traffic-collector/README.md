# Kubernetes 靶机流量与系统日志归档

此目录提供原先仓库缺失的节点采集服务，替换「仅保留已解题流量」的外部脚本。旧采集服务应停止，避免同时抓取和重复占用磁盘。历史 `.status.json`、旧 keepFiles 和已有 OSS 对象不会被删除；新页面展示本次新链路创建的采集记录，旧对象仍保留在原 OSS 路径。

## 行为

- 管理后台「靶机流量监控」总开关默认关闭，设置保存在 PostgreSQL 的 Configs 中，无需数据库迁移。
- 开启后，所有新建 Kubernetes 题目 Pod（包括附加靶机、直连端口、代理连接）都会采集，忽略题目原有 EnableTrafficCapture 和解题状态。
- 每个新 Pod 增加一个受信任的 `capture-ready` 初始化容器。节点采集器进入 Pod 的网络命名空间，确认 tcpdump 已监听后才释放初始化门，随后启动题目容器，覆盖应用启动时的通信。若 120 秒内采集器未就绪，初始化失败，题目应用不会在漏采状态下启动。
- 在 Pod 的完整网络命名空间抓取全部接口、协议、端口，包含 Pod 内回环通信。抓包不通过往题目容器注入工具完成；应用容器不挂载采集目录，也拿不到平台内部密钥。
- 活动分片使用 `open-*.part.pcap`，保持 `.pcap` 后缀以兼容节点的 tcpdump AppArmor 规则；上传器仅处理封口后的 `traffic-*.pcap`。启动恢复也兼容旧版 `.pcap.part` 文件。
- 关闭时，采集器读取到新的控制状态后先向所有 tcpdump 发出停止信号，再刷盘、上传、回读校验和清理。控制轮询间隔为 1 秒；网络请求耗时会影响生效时间。即使快速关闭后再开启，旧启动批次也不会恢复抓包。
- 正常销毁时，抓包持续到 Pod 退出/消失，随后结束归档；不等待成功解题。开启前就已存在的 Pod 不补抓。
- 每 300 秒轮转一个完整 pcap，关闭或销毁时立即结束最后一个分片。每个封口文件进入上传队列，正常情况下 5 秒内开始上传，分片可用 Wireshark/mergecap 合并。上传不会阻塞控制轮询。
- 启动日志、提交 flag 的系统日志（提交内容、成功/失败判定）、销毁日志按采集 ID 复制到同一 OSS 目录。日志复用 Serilog 实际系统事件，包含 UTC 时间、原始内容、用户、IP、状态和来源。后台判题使用提交请求时持久化的用户/IP 快照，避免用户随后换 IP 导致日志失真。定时自动销毁的操作用户为 System，IP 为 null，并附带启动者及启动 IP。
- 日志事件产生后立即进入持久化上传队列，后台被唤醒上传；失败每 5 秒重试。关闭抓包后，该次靶机的后续销毁和判题日志仍会补入对应目录。
- 对每个对象执行 PUT、HEAD 大小检查及完整 GET 的 SHA-256 校验。节点还会将返回的对象路径、大小和 SHA-256 与本地文件再次比较，先持久化上传回执，再删除本地 pcap。日志同样仅在校验通过后删除内容，保留小型下载索引。
- OSS/平台故障时保留文件重试，重启恢复待上传分片及日志；控制面故障时已有采集继续，直到重新收到关闭指令。节点自身宕机或采集器重启期间可能有采集间断，后台超过 30 秒未收到活动节点状态会提示异常。

## 部署

1. 在后台「对象存储」配置 OSS Endpoint、Bucket、AccessKeyId、AccessKeySecret。凭据需要 PutObject、GetObject（包括 HEAD）权限；后台的连通性测试还需要 ListBucket 权限。
2. 平台部署配置 `InternalApiKey` 或 `GZCTF_INTERNAL_API_KEY`，采集器 Secret 必须使用同一个值。密钥仅分配给平台和受信任的节点采集器。
3. 构建并推送镜像：

   ```sh
   docker build -t YOUR_REGISTRY/gzctf-traffic-collector:VERSION tools/traffic-collector
   docker push YOUR_REGISTRY/gzctf-traffic-collector:VERSION
   ```

   镜像中的 crictl 固定为 [cri-tools v1.34.0](https://github.com/kubernetes-sigs/cri-tools/releases/tag/v1.34.0)，构建时校验官方发布的 amd64/arm64 SHA-256。

4. 修改 `kubernetes.yaml` 中的平台命名空间、题目命名空间、镜像、PLATFORM_URL、Secret 名称和运行时 Socket。示例使用 containerd `/run/containerd/containerd.sock`；k3s 等环境必须同时调整 `RUNTIME_ENDPOINT` 和 Socket 挂载路径。采集器通过 CRI 的 `inspectp.info.pid` 获取 sandbox PID；部署前确认所用 CRI 运行时提供此字段。
5. 在平台命名空间创建 `gzctf-internal-api` Secret，键名为 `key`，然后部署：

   ```sh
   kubectl apply -f tools/traffic-collector/kubernetes.yaml
   ```

6. 平台端配置：

   ```json
   {
     "TrafficCapture": {
       "ArchiveRoot": "files/traffic-archive",
       "NodeRoot": "/var/lib/gzctf-traffic",
       "GateImage": "busybox:1.37.0"
     }
   }
   ```

   `ArchiveRoot` 必须在平台持久化卷上；多平台副本必须共享同一目录。它保存采集元数据、停止标记、提交时的用户/IP 快照及待上传日志。`NodeRoot` 必须与 DaemonSet 的节点 hostPath 一致，且节点采集器应覆盖所有允许调度题目 Pod 的节点；有 taint 的题目节点请按实际配置 tolerations。采集器需要 hostPID、运行时 Socket 和抓包权限；题目命名空间的策略需允许平台添加的只读 hostPath 初始化容器。受限集群应由运维审核部署策略。
7. 确认每个题目节点上的采集器运行正常后再开启后台总开关。若平台经过反向代理，需为上传路由允许足够的请求体大小和超时；若只允许 HTTPS，请使用带有效证书的 PLATFORM_URL。

## OSS 结构

```text
traffic/<capture-id>/metadata.json
traffic/<capture-id>/traffic-<unique-part-id>.pcap
traffic/<capture-id>/logs/<utc-ticks>-<event-id>.json
```

主靶机和附加靶机各有唯一采集 ID，元数据包含题目、队伍、用户、IP、Pod 和启动时间。同一请求对应的启动、提交日志会关联到该次启动的全部靶机。后台保留已销毁记录，支持下载 OSS 中的流量及日志，显示待上传日志、采集异常和校验中的状态。

## 验证

```sh
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release
python -m unittest discover -s tools/traffic-collector -v
```

Linux 抓包冒烟测试（真实 tcpdump，检查应用首包、轮转和停止后的文件）：

部署验收应额外设置 `CAPTURE_TEST_ROOT=/var/lib/gzctf-traffic`，在采集器实际挂载目录运行同一测试，以验证 hostPath 和 AppArmor 写入权限。

```sh
docker run --rm --cap-add SYS_ADMIN --cap-add NET_RAW --cap-add NET_ADMIN \
  --mount type=bind,source="$PWD/tools/traffic-collector",target=/tests,readonly \
  --entrypoint python YOUR_REGISTRY/gzctf-traffic-collector:VERSION \
  -B -m unittest discover -s /tests -p test_linux_capture.py -v
```

部署环境还应依次验证：关闭时启动靶机 → 开启不补抓 → 新建未解出的靶机 → 成功/失败各提交一次 → 销毁 → 查看 OSS 对象 → 确认本地 pcap 删除；再验证抓包途中关闭、快速关开、OSS 权限失败后恢复、采集器重启和多靶机关联日志。
