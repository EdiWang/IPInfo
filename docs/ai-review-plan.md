# IPInfo AI Review Plan

## 2026-06-16 Codebase Review

### 分析范围

- `src/Program.cs`
- `src/Services/QqwryDb.cs`
- `src/Services/QqwryDbProvider.cs`
- `src/Services/QqwryDbWatcher.cs`
- `src/Services/IpLookupService.cs`
- `src/Models/IpLocationResult.cs`
- `src/IPInfo.csproj`
- `src/appsettings.json`
- `src/Dockerfile`
- `compose.yaml`
- `deploy-vm.sh`
- `azure-file-share-updater/Dockerfile`
- `azure-file-share-updater/update.sh`
- `.github/workflows/api-docker-image.yml`
- `.github/workflows/updater-docker-image.yml`
- `README.md`

未运行 `dotnet build`、测试、格式化、依赖安装、Docker build 或任何可能产生副作用的命令。

### 总体结论

- 整体风险等级：中。
- 当前实现非常轻量，Minimal API、服务拆分和 Docker/Updater 的职责大体清晰，适合项目现阶段规模。
- 最值得优先处理的是数据库文件初始化/解析的稳定性、缺失测试、客户端 IP 解析与限流身份不一致，以及运维可观测性不足。
- 暂不建议做大规模分层重构、引入数据库/缓存/认证框架，或为了小收益改写 QQWry 解析器。

### 问题列表

| ID | 优先级 | 类型 | 位置 | 问题描述 | 影响 | 证据 | 建议方向 |
|---|---|---|---|---|---|---|---|
| IPINFO-01 | P1 | 稳定性 | `src/Services/QqwryDbProvider.cs` 构造函数；`TryReload` | 初始加载数据库时没有复用热重载里的最小文件大小保护。 | 如果容器启动时 `qqwry.dat` 是空文件、半写入文件或异常小文件，可能在启动阶段或首次查询时抛异常，导致服务不可用。 | 构造函数在 `File.Exists(path)` 后直接 `new QqwryDb(path)`；`TryReload` 才有 `MinValidFileSizeBytes` 检查。 | 抽出统一的文件验证/加载方法，启动和热重载共用；初始文件不合格时保持 unavailable 并记录一次告警。 |
| IPINFO-02 | P1 | 稳定性/安全 | `src/Services/QqwryDb.cs` | QQWry 二进制解析缺少边界检查和递归保护。 | 损坏或恶意数据库文件可能触发 `IndexOutOfRangeException`、异常 500，极端情况下递归跳转可能造成栈溢出。 | `_data[pos]`、`ReadUInt24LE(pos + 1)`、`ReadUInt32LE(recordOffset)` 等读取前没有验证范围；`ReadLocationStrings` 遇到 `0x01` 会递归调用自身。 | 为 24/32 位读取、字符串读取、索引范围、重定向深度添加集中校验；解析失败时返回空位置或使加载失败，并保持旧数据库。 |
| IPINFO-03 | P1 | 测试/可维护性 | 仓库整体 | 没有版本化测试项目。 | QQWry 解析、X-Forwarded-For、限流、数据库不可用等行为缺少回归保护，后续改稳定性时风险变高。 | `git ls-files` 未发现测试项目或测试文件；`AGENTS.md` 也说明当前无测试项目。 | 新增小型测试项目，优先覆盖纯服务和 Minimal API 集成路径；使用小型人工构造 fixture，不提交真实 `qqwry.dat`。 |
| IPINFO-04 | P1 | 安全/限流 | `src/Program.cs` rate limiter policy 与 `ResolveClientIpV4` | 每 IP 限流使用的客户端 IP 与业务解析/日志使用的客户端 IP 逻辑不统一。 | 反向代理、多段 `X-Forwarded-For` 或 IPv4-mapped IPv6 场景下，限流分区可能与实际业务识别的调用方不一致，影响限流准确性。 | 限流 policy 使用 `context.Connection.RemoteIpAddress?.ToString()`；业务 helper 会读取 `X-Forwarded-For` 左侧第一个 IPv4，并处理 IPv4-mapped IPv6。 | 抽出共享的 client IP resolver，限流、日志、自查 IP 查询统一使用同一逻辑；补充反向代理场景测试。 |
| IPINFO-05 | P2 | 稳定性/配置 | `src/Services/QqwryDbWatcher.cs` | `IpDb:ReloadIntervalSeconds` 没有范围校验。 | 若配置为 `0` 或负数，`PeriodicTimer` 初始化可能失败，后台服务无法正常运行。 | `_interval = TimeSpan.FromSeconds(configuration.GetValue("IpDb:ReloadIntervalSeconds", 60))` 后直接 `new PeriodicTimer(_interval)`。 | 增加最小值校验和默认回退，异常配置记录告警。 |
| IPINFO-06 | P2 | 运维/可观测性 | `src/Program.cs` 数据库可用性中间件 | 数据库不可用时每个请求都会记录 Error。 | 文件缺失或挂载异常期间可能刷爆日志，掩盖真正问题，并增加运维成本。 | 中间件每次 `!db.IsAvailable` 都执行 `logger.LogError(...)`；Provider 已在启动/移除时记录状态日志。 | 将每请求日志降级或移除，改为状态变化日志；保留 503 Problem Details。 |
| IPINFO-07 | P2 | 运维/API 设计 | `src/Program.cs` `/db-info` 与全局 DB gate | `/db-info` 被全局数据库可用性 gate 拦截，数据库缺失时无法通过该接口观察文件状态。 | 运维排障时，最需要看数据库状态的时候反而只能得到通用 503。 | DB availability middleware 在 endpoint 映射前，对所有请求生效；`/db-info` endpoint 在之后映射。 | 待确认：如果 `/db-info` 是运维接口，可允许它绕过 DB gate 并返回 exists/size/lastWrite/error 状态。 |
| IPINFO-08 | P2 | 安全/隐私 | `src/Program.cs` lookup 日志；`/db-info` | 日志记录 IP、查询结果、User-Agent；`/db-info` 返回服务器文件路径。 | IP 和 UA 可能属于个人数据或可识别信息；路径暴露会泄漏部署细节。 | lookup 日志包含 `{ClientIp}`、`{QueryIp}`、`{Country}`、`{Area}`、`{Isp}`、`{UserAgent}`；`/db-info` 返回 `path = info.Path`。 | 待确认日志合规要求。可先限制 UA 长度、降低日志级别或做可配置开关；`/db-info` 可隐藏完整路径或仅在受控环境开放。 |
| IPINFO-09 | P2 | 部署/回滚 | `.github/workflows/*.yml`；`compose.yaml` | 镜像只发布和部署 `latest` 标签。 | 线上回滚和问题定位困难，无法直接从部署配置看出正在运行的版本。 | workflows 的 `tags` 仅为 `ediwang.azurecr.io/ipinfo:latest` 和 `ipinfo-updater:latest`；compose 也使用 `latest`。 | 增加 commit SHA 或版本号不可变标签，同时保留 `latest`；部署时支持指定镜像标签。 |
| IPINFO-10 | P2 | 运维 | `compose.yaml`；`deploy-vm.sh` | 缺少健康检查和部署后探活。 | 镜像拉取和容器启动成功不等于 API 可用，数据库缺失、端口异常时部署脚本仍会显示完成。 | compose services 没有 `healthcheck`；`deploy-vm.sh` 在 `docker compose up -d` 后只执行 `docker compose ps`。 | 添加轻量健康端点或使用现有 endpoint 探活；部署脚本可选等待健康状态。 |
| IPINFO-11 | P2 | 供应链 | `azure-file-share-updater/update.sh` | updater 从 GitHub latest 下载数据库，只做大小和 hash 记录，不校验来源完整性。 | 如果上游发布源或传输链路异常，可能写入非预期数据库。当前 curl 使用 HTTPS，风险不是立即致命，但完整性保证较弱。 | `QQWRY_URL` 默认 latest download；下载后仅检查 `SIZE >= 102400` 并记录 SHA。 | 待确认可接受的供应链保障级别。可支持可选 expected SHA、固定 release、或记录 release metadata。 |
| IPINFO-12 | P3 | 代码结构 | `src/Program.cs` | `Program.cs` 同时承载配置、限流、DB gate、IP 解析 helper 和 endpoint handler。 | 目前 193 行还能维护，但继续增长会降低可读性并增加改动冲突。 | endpoint handler、helper、middleware、服务注册均在同一文件。 | 在行为稳定和测试补齐后，按现有 Minimal API 风格抽出 endpoint mapping/client IP resolver/DB middleware 扩展。 |
| IPINFO-13 | P3 | API 一致性 | `src/Program.cs` | Problem Details 响应风格不完全统一。 | 客户端解析错误响应时可能遇到轻微差异；不属于当前最高风险。 | 429/503 手写匿名对象；400 使用 `Results.Problem`；已注册 `AddProblemDetails()`。 | 统一使用 Problem Details-compatible helper 或 TypedResults，保持 JSON contract。 |
| IPINFO-14 | P3 | 文档 | `README.md`；`azure-file-share-updater/README.md` | README 对本地运行、DBPath、updater、部署和验证说明较简略。 | 新环境接手时容易踩到数据库路径、Docker 端口、更新器调度和验证步骤。 | README 只给出基础 `dotnet run` 和 Docker 示例；updater README 仅有一个 Docker run 示例。 | 在代码行为稳定后补充运行、配置、健康检查、回滚和隐私说明。 |

### 分批次改进计划

#### Task 1：统一数据库文件加载保护

- **优先级**：P1
- **关联问题**：IPINFO-01
- **目标**：启动加载和热重载使用同一套文件存在性、大小和加载失败处理逻辑。
- **改动范围**：`QqwryDbProvider` 内部加载流程；必要时增加小型私有 helper。
- **不包含的内容**：不改 QQWry 解析算法；不改 API contract。
- **预期结果**：异常小文件或加载失败不会导致服务启动崩溃；已有可用数据库不会被坏文件替换。
- **验证方式**：新增/运行单元测试覆盖缺失文件、小文件、加载异常、正常文件；手动用小文件启动确认返回 503。
- **上线风险**：低
- **回滚方案**：还原 Provider 加载流程。
- **是否需要我确认**：否
- **需要确认的问题**：无

#### Task 2：补充 QQWry 解析边界保护

- **优先级**：P1
- **关联问题**：IPINFO-02
- **目标**：让损坏数据库文件以可控方式失败，而不是越界读取或无限递归。
- **改动范围**：`QqwryDb` 的读取 helper、重定向解析、索引范围校验。
- **不包含的内容**：不重写数据库格式解析；不引入新依赖。
- **预期结果**：非法 offset、截断文件、循环重定向能被识别并返回失败/空结果；正常查询不变。
- **验证方式**：构造最小二进制 fixture，覆盖正常记录、截断记录、越界 offset、循环 redirect。
- **上线风险**：中
- **回滚方案**：还原 `QqwryDb` 改动；保留 Task 1 的加载保护。
- **是否需要我确认**：否
- **需要确认的问题**：无

#### Task 3：建立最小测试项目

- **优先级**：P1
- **关联问题**：IPINFO-03
- **目标**：为后续稳定性和重构建立回归网。
- **改动范围**：新增测试项目、测试 fixture、必要的测试可见性调整。
- **不包含的内容**：不提交真实 `qqwry.dat`；不覆盖所有边界一次性做完。
- **预期结果**：至少覆盖 IP 解析、数据库不可用、Provider 加载保护、基础 endpoint 行为。
- **验证方式**：`dotnet test`。
- **上线风险**：低
- **回滚方案**：移除测试项目和 solution 引用。
- **是否需要我确认**：是
- **需要确认的问题**：是否接受新增测试项目和测试框架；是否偏好 xUnit、NUnit 或 MSTest。

#### Task 4：统一客户端 IP 解析与限流分区

- **优先级**：P1
- **关联问题**：IPINFO-04
- **目标**：限流、日志和自查 IP 查询使用一致的 IPv4 解析规则。
- **改动范围**：抽出 client IP resolver；更新 rate limiter policy 和 endpoint handler。
- **不包含的内容**：不改变“左侧第一个 X-Forwarded-For 为原始客户端”的既有语义。
- **预期结果**：反向代理场景中业务识别和限流分区一致。
- **验证方式**：集成测试覆盖无 XFF、单个 XFF、多个 XFF、IPv4-mapped IPv6、非法 XFF。
- **上线风险**：中
- **回滚方案**：恢复原 rate limiter policy 和 helper。
- **是否需要我确认**：否
- **需要确认的问题**：无

#### Task 5：配置校验和 DB unavailable 日志降噪

- **优先级**：P2
- **关联问题**：IPINFO-05、IPINFO-06
- **目标**：避免错误 reload interval 破坏后台服务，并减少数据库缺失期间的重复 Error 日志。
- **改动范围**：`QqwryDbWatcher` interval 校验；DB gate 日志策略。
- **不包含的内容**：不调整数据库路径配置结构。
- **预期结果**：非法 interval 自动回退或启动时明确失败；数据库缺失期间日志不会每请求刷屏。
- **验证方式**：配置 `ReloadIntervalSeconds=0/-1` 的测试或手动验证；数据库缺失时连续请求观察日志。
- **上线风险**：低
- **回滚方案**：恢复原 watcher 和中间件日志。
- **是否需要我确认**：否
- **需要确认的问题**：无

#### Task 6：明确 `/db-info` 的定位和隐私边界

- **优先级**：P2
- **关联问题**：IPINFO-07、IPINFO-08
- **目标**：决定 `/db-info` 是公开接口还是运维接口，并据此调整 DB gate、返回字段和访问边界。
- **改动范围**：`/db-info` endpoint、DB availability middleware，可能包含 README。
- **不包含的内容**：不引入完整认证体系，除非业务确认需要。
- **预期结果**：数据库缺失时仍可排障；不会无意暴露不必要的服务器路径信息。
- **验证方式**：手动或集成测试覆盖数据库存在/缺失时的 `/db-info` 响应。
- **上线风险**：中
- **回滚方案**：恢复原 `/db-info` 响应和 gate 行为。
- **是否需要我确认**：是
- **需要确认的问题**：`/db-info` 是否必须公开？是否允许隐藏完整路径？数据库缺失时是否应返回 200 状态信息还是 503？

#### Task 7：补充健康检查和部署后验证

- **优先级**：P2
- **关联问题**：IPINFO-10
- **目标**：让容器和部署脚本能判断 API 是否真的可用。
- **改动范围**：健康端点或 health checks、`compose.yaml` healthcheck、`deploy-vm.sh` 可选等待逻辑。
- **不包含的内容**：不引入外部监控系统。
- **预期结果**：部署完成后可以自动发现端口、数据库或启动异常。
- **验证方式**：Docker compose 本地/VM 环境启动后观察 health 状态；模拟缺失数据库。
- **上线风险**：中
- **回滚方案**：移除 healthcheck 和脚本等待逻辑。
- **是否需要我确认**：是
- **需要确认的问题**：健康检查是否要求数据库可用？是否需要区分 liveness/readiness？

#### Task 8：改进镜像标签与回滚能力

- **优先级**：P2
- **关联问题**：IPINFO-09
- **目标**：保留 `latest` 便利性，同时提供不可变版本标签用于部署和回滚。
- **改动范围**：两个 GitHub Actions workflow、`compose.yaml` 或部署脚本的镜像 tag 参数。
- **不包含的内容**：不改变 ACR、仓库名称或发布分支策略。
- **预期结果**：每次构建都有 SHA/版本标签；线上可指定回滚到某个已知镜像。
- **验证方式**：检查 workflow 输出 tags；在测试环境指定 tag 部署。
- **上线风险**：低
- **回滚方案**：恢复只推送 `latest` 的 workflow。
- **是否需要我确认**：是
- **需要确认的问题**：希望使用 commit SHA、语义版本，还是两者都用？

#### Task 9：增强 updater 完整性策略

- **优先级**：P2
- **关联问题**：IPINFO-11
- **目标**：在不破坏自动更新的前提下，提高下载数据库的完整性和可追溯性。
- **改动范围**：`azure-file-share-updater/update.sh`，可能补充 README。
- **不包含的内容**：不更换数据源，除非另行确认。
- **预期结果**：可选 expected SHA 或固定 release；metadata 更容易追踪来源。
- **验证方式**：用正确/错误 SHA 手动运行 updater；检查 `.sha256`、`.version`、`.updated_at`。
- **上线风险**：中
- **回滚方案**：恢复原 updater 脚本。
- **是否需要我确认**：是
- **需要确认的问题**：是否能接受固定版本/手动更新 SHA，还是必须始终跟随 latest？

#### Task 10：小步整理 `Program.cs`

- **优先级**：P3
- **关联问题**：IPINFO-12、IPINFO-13
- **目标**：在已有测试保护下改善可读性，不改变行为。
- **改动范围**：抽出 endpoint mapping、client IP resolver、DB gate 或 Problem Details helper。
- **不包含的内容**：不迁移到 Controllers；不引入复杂分层。
- **预期结果**：`Program.cs` 更聚焦 host setup；endpoint 和中间件职责更容易定位。
- **验证方式**：现有测试全部通过；手动 curl 核对 `/`、`/ip/{ipV4}`、`/db-info`。
- **上线风险**：低
- **回滚方案**：还原结构性抽取。
- **是否需要我确认**：否
- **需要确认的问题**：无

#### Task 11：补充运行与运维文档

- **优先级**：P3
- **关联问题**：IPINFO-14
- **目标**：让本地运行、Docker、数据库更新、健康检查、回滚和隐私说明更清楚。
- **改动范围**：`README.md`、`azure-file-share-updater/README.md`。
- **不包含的内容**：不改变代码行为。
- **预期结果**：新环境能按文档完成运行、验证和基本排障。
- **验证方式**：按 README 命令在干净环境演练。
- **上线风险**：低
- **回滚方案**：还原文档改动。
- **是否需要我确认**：否
- **需要确认的问题**：无

### 建议执行顺序

1. Task 3：先建立最小测试项目，为稳定性修复提供保护。
2. Task 1：统一数据库加载保护，降低启动和热重载风险。
3. Task 2：补齐 QQWry 解析边界保护，处理损坏文件风险。
4. Task 4：统一客户端 IP 解析与限流分区，减少代理场景下的安全偏差。
5. Task 5：配置校验和日志降噪，改善异常状态下的运维体验。
6. Task 6：确认并调整 `/db-info` 的公开边界。
7. Task 7：补充健康检查和部署后验证。
8. Task 8：增加不可变镜像标签，改善回滚能力。
9. Task 9：按确认结果增强 updater 完整性策略。
10. Task 10：最后做低风险结构整理。
11. Task 11：补充文档。

### 暂不建议处理的事项

- 暂不建议迁移到 Controllers：当前 API 面很小，Minimal API 符合项目规模。
- 暂不建议引入缓存层：数据库已在内存中读取，当前未看到真实性能瓶颈证据。
- 暂不建议引入完整认证系统：项目目前是公开 IP 查询 API，除非 `/db-info` 或运维接口需要访问控制。
- 暂不建议大改 QQWry 解析算法：优先加边界保护和测试，避免破坏现有查询结果。
- 暂不建议盲目升级基础镜像或 GitHub Actions：除非出现明确安全公告、兼容性问题或发布策略需求。

### 需要确认的问题

1. 是否接受新增测试项目？如果接受，偏好 xUnit、NUnit 还是 MSTest？
2. `/db-info` 是否必须公开访问？是否允许隐藏完整数据库路径？
3. 数据库缺失时，`/db-info` 应返回 200 状态信息还是继续返回 503？
4. 健康检查是否要求 `qqwry.dat` 可用？是否需要区分 liveness 和 readiness？
5. 镜像标签希望使用 commit SHA、项目版本号，还是两者都使用？
6. Updater 是否必须跟随 GitHub latest？是否可以改为可选固定 release 或 expected SHA 校验？
7. 访问日志中的 IP、地理位置和 User-Agent 是否有合规要求，例如保留时间、脱敏、开关或采样？

### 已确认决策

- 测试项目：允许新增 xUnit v3 测试项目，也允许使用 Moq。
- `/db-info`：必须公开访问，允许隐藏完整路径。
- 数据库缺失时的 `/db-info`：返回 503。
- 健康检查：要求 DB 可用，并区分 liveness 和 readiness。
- 镜像标签：commit SHA 和项目版本号都使用。
- Updater：必须跟随 GitHub latest。
- 访问日志：IP、地理位置和 User-Agent 暂时不脱敏，不做保留时间和开关。

### 执行记录

- 2026-06-16：开始执行 Task 1。已统一 `QqwryDbProvider` 启动加载和热重载的文件状态检查、小文件保护和加载失败处理；尚未运行 build/test。
- 2026-06-16：完成 Task 2。已为 `QqwryDb` 增加读取边界检查、索引范围校验、未终止字符串处理和 location redirect 深度限制；新增 xUnit v3 测试项目 `tests/IPInfo.Tests`，使用人工构造的小型 QQWry fixture 覆盖正常解析、短 header、越界 index、越界 record offset、循环 redirect、未终止字符串。验证已通过：`dotnet test tests\IPInfo.Tests\IPInfo.Tests.csproj --verbosity normal`、`dotnet build src\IPInfo.csproj`、`dotnet test src\IPInfo.slnx --verbosity minimal`。
- 2026-06-16：完成 Task 3。测试网扩展为 14 个 xUnit v3 测试，覆盖 `QqwryDb` 解析边界、`QqwryDbProvider` 缺失/小文件/有效文件/小文件热重载保护、Minimal API 正常查询、X-Forwarded-For 自查 IP、非法 IPv4、数据库缺失 503。为集成测试新增 `Microsoft.AspNetCore.Mvc.Testing` 和 `public partial class Program;` 测试入口；修正 503 分支响应 Content-Type 为 `application/problem+json`。验证已通过：`dotnet test src\IPInfo.slnx --verbosity minimal`、`dotnet build src\IPInfo.csproj`、`dotnet test tests\IPInfo.Tests\IPInfo.Tests.csproj --verbosity minimal`。
- 2026-06-16：完成 Task 4。新增 `ClientIpResolver`，将 endpoint handler、lookup 日志和 per-IP rate limiter 分区统一到同一套 IPv4 解析逻辑；保留 `X-Forwarded-For` 左侧第一个 IPv4 优先、非法 XFF fallback remote IP、IPv4-mapped IPv6 映射语义。测试网扩展为 19 个 xUnit v3 测试，新增 resolver 单测和 per-IP 限流按左侧 XFF 分区的集成测试。
- 2026-06-16：完成 Task 5 和 Task 6。新增 `IpDbReloadOptions` 校验 `IpDb:ReloadIntervalSeconds`，0/负数回退默认 60 秒并记录 warning；新增 `DbAvailabilityLogState`，数据库不可用期间只在进入 unavailable 状态时记录一次 Error，恢复可用后重置；`/db-info` 仍公开且数据库缺失时返回 503，可用时返回 `fileName`、`sizeMb`、`lastUpdatedUtc`，不再暴露完整 path。测试网扩展为 25 个 xUnit v3 测试，覆盖配置回退、日志状态、`/db-info` 可用/缺失行为。
- 2026-06-16：完成 Task 7。新增 `/health/live` 和 `/health/ready`，健康端点绕过 DB availability middleware；readiness 通过 `QqwryDbHealthCheck` 要求 DB 可用，compose healthcheck 和 `deploy-vm.sh` 部署后验证均使用 `/health/ready`；API Docker 镜像安装 `curl` 供容器内 healthcheck 使用。
- 2026-06-16：按用户要求跳过 Task 8 和 Task 9，暂不处理镜像不可变标签和 updater latest 下载完整性增强。
- 2026-06-16：完成 Task 10 和 Task 11。`Program.cs` 小步整理为高层启动/管线文件，新增 `IpInfoEndpoints`、`QqwryDbAvailabilityMiddlewareExtensions` 和 `ProblemDetailsResponse`，统一中间件层 Problem Details 写入；README 补充配置、健康检查、本地运行、Docker Compose、VM 部署、测试和运维说明；updater README 补充配置、原子写入和 metadata 文件说明。

### 后续执行注意事项

- 每一批改动都应保持小范围、可单独提交、可单独回滚。
- 不要提交真实 `qqwry.dat`、生成部署数据或 registry 凭据。
- 修改 QQWry 二进制解析前必须先补 fixture 和回归测试。
- 保持 IPv4-only 语义，除非单独确认 IPv6 支持需求。
- 保持 `IpLocationResult.Area` 为空、`Isp` 使用 QQWry 第二位置字符串的现有响应形态，除非确认破坏性变更。
- 保持 `X-Forwarded-For` 左侧第一个值作为原始客户端的现有语义，除非确认代理信任模型调整。
- 运行 `dotnet build`、`dotnet test`、Docker build 或脚本演练前，先征得用户确认。
