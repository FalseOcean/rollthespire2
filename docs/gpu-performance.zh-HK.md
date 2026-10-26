# GPU 讀回與排程改進

[简体中文](gpu-performance.zh-CN.md) · [English](gpu-performance.en-US.md)

## 問題與改動

Neow 每批可能只有少量候選，但大型來源緩衝的讀回與逐批提交仍有固定成本。本改動直接延續原 Mod，減少資料搬運與提交次數。

- ABI1 由 `ulong` span 直接打包為 GPU `uint` 位元組，保留順序及溢位檢查。
- ≤1024 候選使用 4 KiB scratch；合併 bank 一次讀取 32-byte 標頭與候選前綴。
- 1025–65536 候選使用 256 KiB 中型 bank，更大輸出保留完整退路。
- K4 合批共用 Submit/Sync；單人 N→R epoch 合併本組 Neow 與前組 Relic。
- 修正待處理尾組的 unsupported fallback，以及 CPU 分流冒認 GPU parity 的邊界。

上傳先完成，再開清單；同步完成後才讀回或釋放資源。原候選順序、篩選語義、CPU Exact、安全游標與取消提交邊界保留。沒有移除必要的 Sync，也沒有加入獨立程式。

## 預設配置

small／combined／medium readback、Neow K4 合批及 epoch 開啟。`Rt2TinyInputCpuOffload`、`Rt2BatchMergeRelic`、`Rt2NeowPipeline` 關閉；root batch 保留 `1 << 24`。雙 RenderingDevice 實驗不是雙 GPU。

## 重現

需要 .NET 9 SDK、NuGet 的 Godot.NET.Sdk 4.5.1，以及自己的 Beta 0.111.0 遊戲參照；不再分發遊戲 DLL。

```powershell
.\Build-Final.ps1 -GameReferenceDir '<遊戲參照目錄>'
dotnet run --project tools/NeowReadbackChecks -c Release -- readback-checks.json
dotnet run --project tools/BenchmarkRunLimitChecks -c Release
dotnet run --project tools/OrdinalPackingBenchmark -c Release -- --full-capacity --output=packing.json
```

編譯入口固定禁用自動部署，輸出至 `artifacts/Final/`。排程及 CPU Exact 的執行方式見各工具 README；使用自己的合法匯出資料。

## 驗證與限制

已記錄的離線檢查包括：Release 編譯、431 個讀回／digest 斷言（86 情境）、32 個範圍檢查、421 個打包斷言、預設配置的 12 個排程邊界檢查，以及 production RNG／CPU Exact 對照。

這些測試不執行 GPU。最新整合 DLL 尚未重新完成實機配對；不能據此宣稱新的整體提速。D3D12 候選完整性、native 資源生命週期、暫停／取消／續掃、故障恢復及首次有效結果時間仍須實機驗證。

Sync 是主機等待及驅動耗時，epoch 共享等待主要記入 Neow；部分讀回計數代表邏輯窗口。評估以端到端完成驗證的根數／秒及正確性為準，不以 GPU 利用率作保證。

原 MPL-2.0 `LICENSE` 及原作者聲明保留；修改檔案及工具源碼一併提供。
