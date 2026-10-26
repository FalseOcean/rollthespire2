# 排程邊界離線檢查

此工具載入指定的 production DLL，直接測試共用 fallback 與分流規則，不初始化 Godot native、不建立 RenderingDevice，也不啟動遊戲。

從儲存庫根目錄編譯及執行：

```powershell
.\Build-Final.ps1 -GameReferenceDir '<遊戲參照目錄>'
dotnet run --project tools/FamilySchedulingChecks -c Release -- `
  --assembly artifacts/Final/RolltheSpire2.dll `
  --reference-dir '<遊戲參照目錄>' `
  --output scheduling-checks.json
```

根目錄的 `Build-Final.ps1` 參照同層 `RolltheSpire2.csproj`，固定傳入 `-p:Rt2ModsDir=`，不會部署到遊戲。參照目錄須包含自己的遊戲 `sts2.dll` 與 `0Harmony.dll`。

預設 tiny CPU 關閉，以上命令不需要私人 context／workload。此配置通過 **12 項檢查**：

- 正常 epoch 結果原樣返回，不執行 fallback。
- 同步或非同步的 unsupported 拒絕恰好退回一次，保留完整有序輸出。
- 一般錯誤、取消及 fallback 自身失敗原樣上拋，不重複重試。
- production 分流規則的 compact 0／1／32／33 個候選與 dense 輸入，確認關旗後不會意外轉 CPU。

## 可選 CPU 分流檢查

若另行編譯 `-p:Rt2TinyInputCpuOffload=true`，執行時還須提供 `--context '<匯出 context.json>' --workload '<匯出 workload.json>'`。該查詢須能建立 CombatReward 和 World 的 GPU plan。

此配置額外測試真實 C／W family 的 32 候選 CPU 輸出與既有 reference 一致、GPU executor 未建立、GPU parity 仍未驗證，以及 CPU／GPU 計數獨立；總計 21 項檢查。這是可選功能的測試配置，並非預設交付設定。

此工具不執行 epoch GPU dispatch，也不驗證 native fence、裝置故障清理或遊戲內取消延遲。這些仍須以相同條件、範圍與裝置做實機對照。遊戲資料與私人匯出檔不隨工具提供。

原 MPL-2.0 授權及聲明保留。
