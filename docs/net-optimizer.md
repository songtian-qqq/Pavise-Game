# 网络节点加速（按游戏自动选节点）

## 它做什么

对局开始时，Pavise 实测节点池里每个节点的真实延迟，挑出**这个游戏**当前延迟最低的节点，然后把该游戏进程的流量单独交给这个节点，其余程序仍走直连。

- 只在游戏进程运行期间接管；**切到后台、最小化、Alt+Tab 都不收回**，加速持续生效。
- 收回条件是「游戏进程不存在」，不是「窗口失焦」：会话判定的宽限、误判都不会提前断开，后台线程每 2 秒检查一次游戏进程，进程消失才真正拆除。
- 游戏进程结束（退局）或 Pavise 关闭时收回，内核退出、配置删除、虚拟网卡随之消失。
- 每个游戏单独记住上次用过的节点；新节点要比原节点快 18% 以上才会切换，避免对局中来回跳。

实现是「内置 mihomo 内核 + TUN + 进程规则」：TUN 把所有流量收进来，规则只把游戏 exe 发往选中的节点，`MATCH,DIRECT` 其余全部直连。不做注入、不挂全局代理、不改系统代理设置。

## 节点池从哪来

按以下顺序合并，重复节点自动去重，最多保留 96 个：

| 来源 | 位置 | 说明 |
| --- | --- | --- |
| 随包清单 | `Pavise.exe` 同目录的 `nodes.txt` | 分发时自带的默认节点，改这里等于改默认池 |
| 本地清单 | 数据目录的 `netnodes.txt` | `%AppData%\PaviseAuto\netnodes.txt`，用户自己添加 |
| 订阅 | 数据目录 `netnodes.subscriptions.txt`，或注册表项 `NetRouteSubscriptions` | 每行一个 URL，6 小时自动刷新一次 |

节点格式支持三种混写：

1. 分享链接一行一个：`ss://`、`vmess://`、`vless://`、`trojan://`、`hysteria2://`（`hy2://`）、`socks5://`、`http(s)://`。
2. Clash 配置里的 `proxies:` 块，直接把整段粘贴进来即可。
3. 订阅返回的 base64 整包内容，粘贴后会自动解码。

示例：

```
trojan://pass@hk1.example.com:443?sni=hk1.example.com#香港-01
ss://YWVzLTI1Ni1nY206cGFzcw==@sg1.example.com:8388#新加坡-02
vmess://eyJ2IjoiMiIsInBzIjoi...（标准 vmess base64）
```

## 内核要求

- 需要 `mihomo.exe`（Clash Meta 同源内核即可）和同目录的 `wintun.dll`。
- 查找顺序：注册表 `NetRouteCorePath` → 程序目录 → 数据目录 `netroute\core`。
- 都没有时自动从 mihomo 官方 release 下载 `windows-amd64` 包并解压（约 20 MB），失败会回退到 `mirror.ghproxy.com` 前缀重试。可用注册表 `NetRouteCoreUrls` 指定自己的下载地址（每行一个 zip URL），或用 `NetRouteAutoInstallCore=0` 关闭自动下载。
- mihomo 的官方包里不带 `wintun.dll`，Pavise 会在下载内核后另外取一份官方 wintun 构建（`wintun-0.14.1`，约 0.7 MB）并只保留 amd64 驱动。已手工放好内核但缺驱动时，同样会补下载；网络不可用则在状态卡里显示「缺少 wintun.dll」。
- 内核由 Pavise 以当前会话身份拉起，不注册服务，不驻留。
- 生成的配置不依赖 GeoIP/GeoSite 数据库：DNS 段落显式写了 `fallback-filter: {geoip: false}`，所以首次启动不会去 GitHub 拉 `geoip.metadb`，离线机房或杀软拦网也能起来（实测内核对缺少 MMDB 会直接 fatal 退出）。
- 下载内核走 `HttpWebRequest`，.NET 4.x 默认 TLS 1.0，代码里已统一把 `SecurityProtocol` 升到 TLS 1.2，否则 `api.github.com` 直接报「SSL/TLS 安全通道」。

## 开关

- 全局/按游戏开关：`GmNetRouteV1`，性能页「环境」分组里的「网络节点加速」，也可以在游戏库的深度调优弹窗里单独给某个游戏开或关。
- 总开关（默认开）：注册表 `NetRouteEnabled`（DWORD）。
- 默认值：所有游戏默认开启（`GmNetRouteV1` 的 fallback 为 `1`），没有可用节点时自动降级并记录原因，不影响对局。

## 运行数据

| 文件 | 内容 |
| --- | --- |
| `netroute\probe.yaml` | 探测用临时配置，探测结束即删除 |
| `netroute\route.yaml` | 接管用配置，退局即删除 |
| `netroute\health.txt` | 每个节点的延迟、抖动、失败次数（7 天清理） |
| `netroute\selected.txt` | 游戏 → 上次使用节点 |
| `netroute\subscription.txt` | 订阅原始内容缓存 |
| `netroute\core\` | 自动下载的内核与解压目录 |

## 故障排查

- 状态卡在「未生效」：先看性能页实验室卡片的第二行，会写明是节点池空、内核缺失、`wintun.dll` 缺失还是内核控制端口没起来。
- 接管后游戏仍走直连：确认游戏 exe 名字与库里的可执行文件一致（规则按 `PROCESS-NAME` 精确匹配；改名或走启动器换名会匹配不到）。
- 全部节点不通：免费节点失效很快，换订阅或更新 `netnodes.txt`；`health.txt` 里失败 2 次以上的节点会进 10 分钟冷却。
- 崩溃残留：下次启动 `NetRouteSession.HealFromCrash` 会按记录的 PID + 启动时间精确杀掉上次的内核，不会误杀别人的进程。

## 验证方法

```
build.cmd -b dev
```

编译产物为 `build\Pavise.exe`。功能自检：

1. 启动后打开性能页实验室卡片，状态应为「待命」并显示节点数量。
2. 点「立即探测」，观察 `health.txt` 出现延迟数据、状态显示「已探测 N 个节点」。
3. 开一个库里已登记的游戏，对局开始 3~10 秒内状态变为「已接管: 节点名 · xx ms」，`route.yaml` 出现在 `netroute` 目录。
4. 游戏切后台，状态保持「已接管」。
5. 退出游戏，状态回到「待命」，`route.yaml` 被删除，任务管理器里没有 mihomo 残留。
