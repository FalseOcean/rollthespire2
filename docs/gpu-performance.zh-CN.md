# GPU 读回与调度改进

[繁體中文](gpu-performance.zh-HK.md) · [English](gpu-performance.en-US.md)

## 问题与改动

Neow 每批可能只有少量候选，但大型源缓冲的读回和逐批提交仍有固定成本。本改动直接延续原 Mod，减少数据搬运与提交次数。

- ABI1 从 `ulong` span 直接打包为 GPU `uint` 字节，保留顺序和溢出检查。
- ≤1024 候选使用 4 KiB scratch；合并 bank 一次读取 32-byte 标头与候选前缀。
- 1025–65536 候选使用 256 KiB 中型 bank，更大输出保留完整回退路径。
- K4 合批共享 Submit/Sync；单人 N→R epoch 合并本组 Neow 与前组 Relic。
- 修复待处理尾组的 unsupported fallback，以及 CPU 分流误标 GPU parity 的边界。

上传先完成，再开启命令列表；同步完成后才读回或释放资源。保留原候选顺序、筛选语义、CPU Exact、安全游标和取消提交边界。没有移除必要的 Sync，也没有加入独立程序。

## 默认配置

启用 small／combined／medium readback、Neow K4 合批和 epoch。关闭 `Rt2TinyInputCpuOffload`、`Rt2BatchMergeRelic`、`Rt2NeowPipeline`；root batch 保留 `1 << 24`。双 RenderingDevice 实验并不是双 GPU。

## 复现

需要 .NET 9 SDK、NuGet 的 Godot.NET.Sdk 4.5.1，以及自己的 Beta 0.111.0 游戏引用；不再分发游戏 DLL。

```powershell
.\Build-Final.ps1 -GameReferenceDir '<游戏引用目录>'
dotnet run --project tools/NeowReadbackChecks -c Release -- readback-checks.json
dotnet run --project tools/BenchmarkRunLimitChecks -c Release
dotnet run --project tools/OrdinalPackingBenchmark -c Release -- --full-capacity --output=packing.json
```

编译入口固定禁用自动部署，输出至 `artifacts/Final/`。调度与 CPU Exact 的执行方式见各工具 README；使用自己的合法导出数据。

## 验证与限制

已记录的离线检查包括：Release 编译、431 个读回／digest 断言（86 个场景）、32 个范围检查、421 个打包断言、默认配置的 12 个调度边界检查，以及 production RNG／CPU Exact 对照。

这些测试不执行 GPU。最新整合 DLL 尚未重新完成实机配对；不能据此宣称新的整体提速。D3D12 候选完整性、native 资源生命周期、暂停／取消／续扫、故障恢复和首次有效结果时间仍须实机验证。

Sync 包含主机等待和驱动耗时，epoch 共享等待主要记入 Neow；部分读回计数代表逻辑窗口。评估应以端到端完成验证的根数／秒及正确性为准，不以 GPU 利用率作保证。

原 MPL-2.0 `LICENSE` 与原作者声明保留；修改文件及工具源码一并提供。
