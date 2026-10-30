# 外部动态 Flag 兑换接口

面向「共用一台常开服务器」的题目（典型场景：Minecraft 服务器、常驻 TCP 服务、实时对局）。
平台不为此类题目下发容器，而是提供**内部兑换接口**：外部服务拿选手提供的**队伍 Token** 换取该队伍专属的 flag。

- 接口实现：[`src/GZCTF/Controllers/InternalApiController.cs`](src/GZCTF/Controllers/InternalApiController.cs)
- 生成与落库：[`src/GZCTF/Repositories/GameInstanceRepository.cs`](src/GZCTF/Repositories/GameInstanceRepository.cs) `EnsureExternalFlag`
- 题型定义：[`src/GZCTF/Utils/Enums.cs`](src/GZCTF/Utils/Enums.cs) `ChallengeType.DynamicExternal`

---

## 1. 题型与数据流

题目类型 `ChallengeType.DynamicExternal = 0b100`，判分完全依赖平台的动态 flag 机制：

```
选手                MC/外部服务                    平台
 |  进服 / 输入码        |                             |
 |--------------------->|                             |
 |  /register ... token |                             |
 |--------------------->|  POST /api/internal/flag     |
 |                      |  {gameId, challengeId, token}|
 |                      |---------------------------->|
 |                      |         200 {flag}           |
 |                      |<----------------------------|
 |   服务器把 flag 给选手|                             |
 |<---------------------|                             |
 |  在平台提交 flag                                    |
 |--------------------------------------------------->| 与 FlagContext 比对 → Accepted / WrongAnswer
```

关键点：

- **每队 flag 不同**：flag 由 `(比赛私钥, 题目 ID, 队伍 Token)` 经两次 SHA-256 派生，同一队伍重复请求永远得到同一个值。
- **平台是唯一裁判**：外部服务只负责把 flag 发出去，不参与判分；判分在选手提交时用 `GameInstance.FlagContext` 比对。
- **无容器**：题目不需要镜像、端口、附件（附件仍可选，用于放置题目材料）。

---

## 2. 接口规范

### 请求

```
POST /api/internal/flag
Content-Type: application/json
X-Internal-Key: <内部密钥>
```

```json
{
  "gameId": 1,
  "challengeId": 22,
  "teamToken": "28:3H16H2ZAGj+Wd92YzNc+14xTgDkaPsfowRHblMFkDx2UT0og/7JWb9P9vy7qt3Df4GsCk0qqu7qdaPg52FuyAg=="
}
```

| 字段 | 类型 | 说明 |
|---|---|---|
| `gameId` | int ≥ 1 | 比赛 ID（必填） |
| `challengeId` | int ≥ 1 | 题目 ID，必须是该比赛下的**外部动态 flag** 题（必填） |
| `teamToken` | string ≤ 200 | 队伍 Token，比赛页「队伍卡片」里可复制。格式 `{队伍ID}:{Base64(64字节)}`，通常 90 字符左右（必填） |

### 响应

成功（`200`）：

```json
{ "flag": "flag{mc_56f6fd8b9117}" }
```

失败：

| 状态码 | 响应体 `title` | 含义 |
|---|---|---|
| `403` | `内部接口未启用` | 平台没配置 `InternalApiKey`，接口整体关闭 |
| `403` | `内部密钥错误` | 请求头 `X-Internal-Key` 缺失或不匹配 |
| `404` | `题目未开启外部动态 flag，或队伍不存在` | 题目不属于该比赛 / 题型不是外部动态 / Token 在该比赛中查不到 |
| `400` | 模型校验信息 | `gameId`、`challengeId` 越界，或 `teamToken` 超长/为空 |

> 注意：`404` 是「三类原因合并」的结果，不区分具体是哪一类（对外不泄露信息）。排查时请按第 8 节的顺序逐项确认。

---

## 3. 鉴权密钥

平台侧配置项 `InternalApiKey`，也可用环境变量 `GZCTF_INTERNAL_API_KEY` 覆盖：

```yaml
# compose.yml
environment:
  InternalApiKey: "5facff47f07701e040060f9f567f28b74e5ea8bb5d38a269"
```

未配置时接口**始终拒绝**（403 `内部接口未启用`）。改动后需重启平台容器。

调用方（如 MC 插件）把同一串密钥写在 `X-Internal-Key` 头里。

---

## 4. flag 生成算法

```
TeamHashSalt = SHA256("GZCTF@" + Game.PrivateKey + "@PK")
salt         = SHA256(TeamHashSalt + "::" + challengeId)
hash         = SHA256(salt + "::" + participation.Token)
teamHash     = hash[12..24]          # 12 位十六进制
flag         = FlagTemplate.replace("[TEAM_HASH]", teamHash)
```

- `FlagTemplate` 必须包含 `[TEAM_HASH]` 占位符，例如 `flag{mc_[TEAM_HASH]}`。
- 模板为空时会退化成 `flag{<随机GUID>}`（每队首次请求随机生成并落库），**不可控也不可复现**，务必配好模板。
- 同队重复请求返回**已落库的同一个值**；平台迁移/重启不影响（存在 `FlagContexts` 表）。
- 队伍 Token 泄露 = flag 泄露（token 是唯一输入），这也是接口只允许内部调用的原因。

---

## 5. 题目侧配置要求

管理端编辑题目时：

| 项 | 要求 |
|---|---|
| 类型 | 选「外部动态 flag」 |
| Flag 模板 | 必填，含 `[TEAM_HASH]`，如 `flag{mc_[TEAM_HASH]}`；此题型**不需要**预置 flag，Flag 页只显示模板输入框 |
| 容器镜像 / 端口 | 不使用（即使填了也不会启动） |
| 流量捕获 | 不允许开启（仅容器题支持） |
| 附件 | 可选，用于放题目材料 |
| 启用时机 | 见下 |

### ⚠️ 启用时机与「实例」

平台在**队伍报名那一刻**为当时已启用的每道题创建一条 `GameInstance`。因此：

- 题目在报名**之后**才启用 → 老队伍名下没有实例。此前这类题目点开会 404，前端表现为**一直转圈加载**；现在 `GetChallenge` 会自动补建（提交 `f7370936` 起）。
- 仍建议：**先配好题目并启用，再开放报名**，或在启用后让选手重新进入题目页触发补建。
- 手工补救（PostgreSQL）：

```sql
INSERT INTO "GameInstances" ("ChallengeId","ParticipationId","IsLoaded","LastContainerOperation")
SELECT 176, p."Id", false, now()
FROM "Participations" p
WHERE p."GameId" = 3 AND p."Status" = 1
  AND NOT EXISTS (SELECT 1 FROM "GameInstances" gi
                  WHERE gi."ChallengeId" = 176 AND gi."ParticipationId" = p."Id");
```

---

## 6. 调用示例

### curl

```bash
curl -s -X POST https://ctf.example.com/api/internal/flag \
  -H 'Content-Type: application/json' \
  -H 'X-Internal-Key: <内部密钥>' \
  -d '{"gameId":1,"challengeId":22,"teamToken":"28:xxx..."}'
```

### Java（MC 插件写法）

```java
HttpURLConnection conn = (HttpURLConnection) new URL(apiUrl).openConnection();
conn.setRequestMethod("POST");
conn.setConnectTimeout(5000);
conn.setReadTimeout(5000);
conn.setDoOutput(true);
conn.setRequestProperty("Content-Type", "application/json");
conn.setRequestProperty("X-Internal-Key", apiKey);

String body = String.format("{\"gameId\":%d,\"challengeId\":%d,\"teamToken\":\"%s\"}",
        gameId, challengeId, token);
try (OutputStream os = conn.getOutputStream()) {
    os.write(body.getBytes(StandardCharsets.UTF_8));
}

if (conn.getResponseCode() != 200) { /* 统一按 Token 无效处理 */ }
String resp = new String(conn.getInputStream().readAllBytes(), StandardCharsets.UTF_8);
// {"flag":"flag{...}"}
```

### Python

```python
import requests

r = requests.post(
    "https://ctf.example.com/api/internal/flag",
    headers={"X-Internal-Key": INTERNAL_KEY},
    json={"gameId": 1, "challengeId": 22, "teamToken": token},
    timeout=5,
)
r.raise_for_status()
flag = r.json()["flag"]
```

---

## 7. 幂等性与并发

- 首次请求生成 flag 并写入 `FlagContexts`（绑定 `GameInstance.FlagId`）；之后命中已有记录直接返回。
- 并发请求同一队伍同一题：可能各生成一次，最终以先落库者为准（唯一约束在 `(ParticipationId, ChallengeId)` 的实例上），不影响正确性。
- 清理：删除队伍/重置题目实例后，下次请求会重新生成（flag 随之变化）。

---

## 8. 故障排查

按顺序确认（示例值需替换）：

1. **密钥对不对**
   ```bash
   curl -s -o /dev/null -w '%{http_code}\n' -X POST https://<域名>/api/internal/flag \
     -H 'Content-Type: application/json' -H 'X-Internal-Key: <密钥>' -d '{}'
   ```
   - `403` → 平台没配 `InternalApiKey` 或与调用方不一致
   - `400`/`404` → 密钥已通过，继续下一步

2. **题目存在且题型是外部动态 flag**
   ```sql
   SELECT "Id","GameId","Title","Type","IsEnabled","FlagTemplate"
   FROM "GameChallenges" WHERE "Id" = 22;
   -- Type = 4 才是 DynamicExternal；FlagTemplate 必须含 [TEAM_HASH]
   ```

3. **Token 属于该比赛**
   ```sql
   SELECT p."Id", p."Status", t."Name"
   FROM "Participations" p JOIN "Teams" t ON t."Id" = p."TeamId"
   WHERE p."GameId" = 1 AND p."Token" = '<完整token>';
   ```
   - 查不到 → `gameId` 填错（测试赛/正式赛搞混是常见原因）或 Token 复制不全（90 字符，很容易只复制一半）

4. **队伍有实例**
   ```sql
   SELECT gi.*, fc."Flag" FROM "GameInstances" gi
   LEFT JOIN "FlagContexts" fc ON fc."Id" = gi."FlagId"
   WHERE gi."ChallengeId" = 22 AND gi."ParticipationId" = <上一步的 p.Id>;
   ```
   没有则由接口自动补一条，或用第 5 节的 SQL 批量补。

5. **选手侧现象对照**

   | 现象 | 根因 |
   |---|---|
   | 游戏里提示「队伍 Token 无效」 | 上面 1~3 任一项不通过；插件把任何非 200 都当成无效 |
   | 平台提交正确 flag 判 `WrongAnswer` | 提交的是**别队**的 flag，或该题的 flag 模板改过（旧 flag 不会自动重算，需要清 `FlagContexts` 重建） |
   | 题目点开一直转圈 | `GameInstance` 缺失（见第 5 节） |
   | 每题只能领一次 | 调用方（插件）自身做了缓存，如 MC 插件的 `accounts.yml` 里 `claims` |

---

## 9. 安全注意事项

- 内部密钥等同于「可枚举所有队伍 flag」的权限，**只放在服务端**，不要写进前端、资源包、题目附件或代码仓库的公开部分。
- 接口建议只在平台与外部服务之间可达；若必须走公网，务必 HTTPS（本仓库生产环境走 `https://ctf.pctf.top`）。
- 队伍 Token 是队伍身份的凭据：泄露即可代领 flag（但无法提交得分，因为提交仍需该队伍登录）。不要把它当成「一次性领取码」外发。
- 对外展示时应使用「领取码」之类的间接标识（如 MC 插件：选手在游戏内找到领取码，插件再拿 Token 换 flag），避免 Token 出现在题目内容、聊天记录或服务器日志里。

---

## 10. 参考实现：MC 插件

`tools/mc-flag-plugin`（`top.pctf.mcflag.FlagPlugin`）是本接口的参考调用方：

| 命令 | 作用 |
|---|---|
| `/register <密码> <确认密码> <队伍Token>` | 注册即校验 Token（向接口发一次探测请求），成功后账本记录 Token |
| `/login <密码>` | 每次进服登录 |
| `/bind <新Token>` | 换队 |
| `/claim <领取码>` | 用领取码换 flag（领取码 → 题目 ID 的映射在 `config.yml` 的 `codes`） |
| `/flag` | 查看已领取的 flag |

插件侧的两个坑（已踩过，写入文档避免复发）：

1. **校验用的是 `codes` 的第一个条目**。如果它指向一个不存在的题目/错误的比赛，注册会整体失败并提示「Token 无效」——即使 Token 本身没问题。
2. **不能把「非 200」一律解释成 Token 无效**。曾经因为平台 `teamToken` 长度上限（64）小于真实 token 长度（90），接口返回 400，插件提示「Token 无效」，排查方向完全被带偏。建议调用方至少把 400 与 404 分开记日志。

---

## 11. 变更记录

| 版本/提交 | 变更 |
|---|---|
| `f7370936` | `teamToken` 长度上限 64 → 200（真实 token 约 90 字符，原上限导致所有真实请求 400） |
| `f7370936` | `GetChallenge` 在实例缺失时自动补建，修复「报名后启用题目」导致前端一直加载 |
| — | 新增题型 `DynamicExternal`，替代原布尔开关 `ExternalDynamicFlag` |
