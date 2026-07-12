# ECO-076(implemented): 取り込みウィザード stepper の B-3・B-4 可視化 — CAD mock 改版への追随

- 起票: 2026-07-13
- 種別: 既存機能拡張(CAD mock 改版への追随。上流の設計変更は ViewPrismUI 側で決定・正典化済み=
  `4303337` decide(eco-073)。VP2 側の ECO-073 は applied でクローズ済みのため**再開せず新規採番**
  — 逆行遷移禁止)
- 状態: implemented(2026-07-13 是正+機械受入+R7 セルフゴールデン完了・golden 待ち)
- 関連: ECO-073(B層 V1・applied。stepper 初回実装=GF-073-04)/ ECO-072(GF-072-01=captures
  同梱の恒久対策)/ 工程改善 R7(セルフゴールデン・VP2 `a96a782`/VPUI `e6a3140`)

## 1. 要求(CAD 改版・確定済み)

maintainer による mock 改版が ViewPrismUI(CAD)へ反映された(`4303337`)。取り込みウィザードの
stepper(`ファイル → 検証 → プレビュー → 完了`)の可視面が **B-2 のみ → B-2・B-3・B-4 の全可視面**
へ拡大された。VP2 実装は旧 CAD(「B-2 に可視」)に忠実なまま=現 CAD と乖離しており、
**乖離時は常に CAD が正**(CLAUDE.md)につき追随が必要。

新契約(共通言語 L7・`docs/03_dialog_language.md` が正):

- 到達済み/現在: **青塗り丸+白数字+青ラベル**
- 未到達: **灰丸+灰ラベル**
- 最終「完了」到達時(B-4): **緑塗り丸+白チェック+緑ラベル**(数字ではなくチェックマーク)
- 接続線: **到達区間のみ青**・未到達区間は灰
- 書き出し B-1 は単段のため stepper なし(面の発明禁止=VC-4)

## 2. 工程診断

| 工程 | 判定 | 証拠 |
|---|---|---|
| CAD(ViewPrismUI) | **健全(改版済み・これが正)** | `4303337`: 一次資料差し替え(SHA-256 新 `5fdf44645e31…` / 旧 `5104c8cee489…`・他 4 サーフェスは画素不変を画素比較で確認済み)+ `docs/screens/snapshot_export_import.md`(改版記録表・レイアウト表 B-3/B-4 に stepper 追加・**視覚契約チェックリスト VC-1〜VC-4 新設**)+ `docs/03_dialog_language.md`(L7 定義に完了状態と接続線到達色を追記・適用面マトリクス L7 列を B-3/B-4 へ ○ 拡大)+ captures 再生成(B-3.png/B-4.png/full.png) |
| BOM(VP2) | **旧契約が残存(要同期)** | `33-control-plan.yaml:765` CP-UI-G13 が「stepper=番号バッジで**B-2限定可視**+面別Window.Title」と旧契約を明記。`32-mbom.yaml:689` M-UI-PACKAGE-043 wizard 契約は可視面を規定せず(沈黙・CP 側に委譲)。20-spec に stepper 記述なし |
| 実装 | **旧 CAD の忠実な転写(逸脱ではない)** | `CollectionImportWindow.axaml:186` — `IsVisible="{Binding OnFileStep}"` で B-3 以降非表示。同 184-185 のコメントが旧契約「CAD『B-2 に可視』=ファイル/検証面のみ表示」を明記(GF-073-04 由来)。バッジ 3/4 は常時灰の静的表現・完了チェック状態なし |
| 検査器(tests) | **旧契約を pin(要改訂)** | `tests/ViewPrism2.Tests/GfPackageVisualParityTests.cs:172` — テスト「B2のstepperはバッジ式で検証済みは2まで点灯し**B3以降は非表示**で専用タイトルになる」が旧契約を固定 |

**結論: 実装・BOM・テストのいずれにも欠陥はなく、旧 CAD に対しては全工程が整合していた。
上流(CAD)の設計変更への追随であり、修正対象は CAD 以外の全層(CP-UI-G13・実装・pin テスト)。**
gate①(裁定)は不要 — 設計判断は maintainer 自身が mock 改版として下し、CAD 正典化済み。

## 3. 切り分け済みの事実

確定:

- ViewPrismUI `4303337` の diff を実見: 一次資料 standalone.html 差し替え・改版記録表
  (2026-07-12 `5104c8cee489`=初回納品・stepper B-2 のみ → 2026-07-13 `5fdf44645e31`=B-3/B-4 追加)・
  VC-1〜VC-4 新設・L7 定義改訂・マトリクス L7 列 B-3/B-4 ○ 化・captures 3 点再生成を確認。
- VC-1〜VC-4(視覚契約チェックリスト・captures 並置突合の検査項目):
  - VC-1(B-2): タイトル直下・水平 1 行。1–2=青塗り丸+白数字+青ラベル、3–4=灰。接続線は 1–2 間のみ青。
  - VC-2(B-3): 同配置。1–3=青(現在=3)、4=灰。接続線は 1→3 青・3→4 灰。
  - VC-3(B-4): 1–3=青、4=**緑塗り丸+白チェック+緑ラベル「完了」**。接続線は全区間青。
  - VC-4(B-1): 書き出しは単段のため stepper を出さない。
- 現実装の stepper は単一 `StackPanel`(`CollectionImportWindow.axaml:186-207`)で、
  可視条件 `OnFileStep`・バッジ 1=常時 active・バッジ 2/接続線 1-2=`VerifyOk` 連動・
  バッジ 3/4 と接続線 2-3/3-4=静的(灰固定)。ステップ 3/4 の到達状態・完了チェック表現の
  VM プロパティは存在しない(B-2 限定可視ではその状態が不要だったため)。
- 旧契約 pin テスト(`GfPackageVisualParityTests.cs:172`)は B-3 遷移後の stepper 非可視と
  Window.Title の面別切替(L1)を一体で検証している。**L1(面別 Window.Title)は改版の対象外**
  — 改訂は stepper 可視性の期待値のみ。
- ECO-073 本文 §7 の CAD 正典化記録(`60-change-order-eco-073.md:640`)は
  旧 SHA `5104C8CE…2DC36CE7` を参照している。
  ECO-073 はクローズ済みのため本文は変更せず、**改版後の一次資料参照
  (SHA-256 `5fdf44645e31…`・ViewPrismUI `4303337`)は本 ECO を正とする**(本節がその記録)。
- i18n `package.stepFile/stepVerify/stepPreview/stepDone` は既存(ja/en)。ラベル文言の変更なし。

疑い(未検証):

- B-4(結果レポート)は取り込み**成功後のみ**到達する面か=「完了」チェック(VC-3)の表示条件が
  面到達と等価かは、CollectionImportViewModel の遷移フローで fix 時に実測確認する
  (失敗時は B-3 のフッターにメッセージ表示で面遷移しない設計=M-UI-PACKAGE-043 記載・のはず)。

## 4. 是正方針(案・着手時確定)

CAD 改版が確定済みのため単案。/eco-fix にて:

1. **視覚 probe 先行生成(R7・GF 後追い禁止)**: VC-1〜VC-4 を検査項目として headless 視覚 probe を
   **是正前に**作成し、VC-2/VC-3(B-3/B-4 の stepper 可視+状態表現)が不合格になることを実測
   (プローブ先行の赤)。旧契約 pin テスト(:172)は新契約の期待値へ改訂(L1=面別 Window.Title の
   検証は維持)。既存固定オラクル行は変更しない(R6・Oracle 対象外の UI 変更)。
2. **実装**: stepper を B-2〜B-4 の全面で可視化(`OnFileStep` 限定の解除)+ステップ到達状態の
   VM 公開(現在面→バッジ 1-4/接続線 3 本の到達状態写像)+B-4 到達時のバッジ 4=
   緑塗り丸+白チェック+緑ラベル表現。B-1(CollectionExportWindow)は変更しない(VC-4)。
3. **セルフゴールデン(R7)**: 出荷前に CAD captures(`snapshot_export_import/B-3.png・B-4.png`+
   B-2 既存)と実機面を**並置**し、マトリクス L7 列の ○ 全面(B-2・B-3・B-4)の転写漏れ 0 を確認
   してから golden 基準を提示。
4. **BOM 同期**: CP-UI-G13 の「B-2限定可視」記述を新契約(B-2〜B-4 可視+L7 状態表現+VC-1〜4
   並置)へ改訂・M-UI-PACKAGE-043 wizard 契約へ可視面を明記・改版参照(新 SHA)は本 ECO §3 が記録。

## 5. 影響 BOM

- CAD: 変更なし(ViewPrismUI `4303337` で正典化済み — 本 ECO の入力)
- CP: `33-control-plan.yaml` CP-UI-G13(stepper 可視面+状態表現の改訂)
- M-BOM: `32-mbom.yaml` M-UI-PACKAGE-043(wizard 契約へ stepper 可視面 B-2〜B-4 を明記)
- 実装: `src/ViewPrism2.App/Views/CollectionImportWindow.axaml`(+コードビハインド/
  `CollectionImportViewModel` のステップ状態公開)
- tests: `GfPackageVisualParityTests.cs`(旧契約 pin の改訂+VC-1〜VC-4 probe 新設)
- spec/E-BOM/REQ/K-BOM/DB/i18n: 変更なし予測(stepper は仕様 §2.14 の挙動契約に非登場・
  ラベル語彙は既存 `package.step*`)

## 6. 残ゲート

- gate①(裁定): **不要** — 設計変更は maintainer の mock 改版として CAD 正典化済み(§2)。
- gate②(golden): CP-UI-G13(改訂後)— B-2/B-3/B-4 の stepper 状態表現を CAD captures と
  並置で maintainer 実機承認。**基準は §7.5(2026-07-13 /eco-fix 完了時に確定)**。

## 7. 実施記録(2026-07-13 /eco-fix — 是正+機械受入完了・golden 待ち)

### 7.1 プローブ先行(R5+R7・GF 後追い禁止)

- CAD 視覚契約チェックリスト VC-1〜VC-4(`snapshot_export_import.md` visualContract)から
  headless 視覚 probe を**是正前に**生成(`GfPackageVisualParityTests` へ 3 本追加+旧契約 pin
  1 本を新契約へ改訂):
  - 改訂: 「B2のstepperはバッジ式で…B3以降は非表示で専用タイトルになる」→
    「B2のstepperはバッジ式で検証済みは2まで点灯し接続線は最初の区間のみ青になる」(VC-1。
    L1=面別 Window.Title の検証は VC-2/VC-3 側へ移設して維持)
  - 新設: VC-2(B3でもstepperが表示され3まで点灯し最後の接続線だけ灰…)/VC-3(B4は完了が
    緑チェックになり接続線が全区間青…)/VC-4(書き出しB1は単段のためstepperを出さない)
- **是正前の赤を実測**: 7 本中 VC-2/VC-3 の 2 本が不合格
  (「VC-2: B-3 で stepper が非表示(旧契約のまま)」「VC-3: B-4 で stepper が非表示」)=
  §2 診断どおり。VC-1/VC-4 は緑=既存承認状態のピン(旧ロットに欠陥なしの裏取り)。
- 完了バッジの緑 `#0F9D76`・接続線の到達契約(B-3=3→4 のみ灰・B-4=全区間青)は CAD capture
  B-3.png/B-4.png の**画素実測**で確定(一次資料 standalone.html は実行時展開バンドルで CSS 直読不可)。

### 7.2 是正(単案・§4)

- `CollectionImportViewModel`: 到達状態 3 プロパティを追加 — `Step2Reached`(=VerifyOk∨Step≥2)/
  `Step3Reached`(=Step≥2)/`Step4Done`(=Step=3)。`Step`/`VerifyError`/`Header` の変更通知へ配線
  (派生プロパティ通知漏れ=ECO-038 教訓の予防)。
- `CollectionImportWindow.axaml`: stepper の `IsVisible="{Binding OnFileStep}"` を撤去(全面表示)・
  バッジ 2/3+接続線 3 本の `Classes.active` を到達状態バインドへ・バッジ 4 は Panel 化して
  数字 4(未到達)⇔白チェック Path `stepCheck`(完了)を切替・`stepBadge.done`(緑 #0F9D76)/
  `stepLabel.done` スタイル追加。バッジ内 TextBlock セレクタは Panel 挿入に合わせ `>`(直子)から
  子孫へ変更(視覚不変)。B-1(CollectionExportWindow)は無変更(VC-4)。
- diff 規模: src 2 ファイル+tests 1 ファイル+台帳 3 ファイル(CP/M-BOM/register)。DB・Core・
  i18n・Oracle 変更なし。

### 7.3 機械受入(4 点・全緑)

- `dotnet build`: 0 warning / 0 error
- `dotnet test tests/ViewPrism2.Tests`(exe 直接実行): **649/649**(probe 7 本緑転を含む)
- `dotnet test tests/ViewPrism2.Oracle`(exe 直接実行): 109 pass / 2 known skip(R6 不変)
- `python bomdd/validate_bom.py`: 0 error / 0 warning

### 7.4 セルフゴールデン(R7・L7 マトリクス列の全面並置)

- scratchpad ハーネス(Avalonia.Headless UseHeadlessDrawing=false+Skia・CaptureRenderedFrame)で
  B-2(検証済み)/B-3(mock 数値の競合・5状態)/B-4(mock 数値の結果)を実レンダリングし、
  CAD captures(B-2.png/B-3.png/B-4.png)と並置。差分の全列挙と分類:
  - **stepper(L7・本 ECO の対象)= 3 面とも転写一致・転写漏れ 0**: B-2=1-2 青/3-4 灰/接続線
    1-2 間のみ青。B-3=1-3 青(現在=3)/4 灰/接続線 1→3 青・3→4 灰。B-4=1-3 青/4=緑塗り丸+
    白チェック+緑ラベル/接続線全区間青。
  - 裁定済み許容差分(引用): 擬似タイトルバー・ダイアログ幅(CP-UI-G13 tolerance)/B-3 ルート
    「変更」非搭載(32-mbom 沈黙次元「取り込み先ルートの変更 UI」)/「場所を指定」非搭載
    (同「未解決画像の場所を指定 即時解決」=deferred)。
  - スコープ外所見 1 件(R3・51-cheat-log へ記録): B-4 mock の「レポートを保存」+「一覧を書き出す」
    が実装非搭載で裁定記録なし(ECO-073 golden 承認済みの既存差分。本 ECO の diff に混ぜない)。

### 7.5 gate② golden 合格基準(maintainer 実機・CP-UI-G13 改訂分)

1. 画像タブ ⋯ メニュー「コレクションを取り込む」→ B-2 でパッケージ選択・検証 OK:
   stepper が `1 ファイル ― 2 検証 ― 3 プレビュー ― 4 完了` の水平 1 行で、**1-2=青塗り丸+
   白数字+青ラベル・3-4=灰丸+灰ラベル・接続線は 1-2 間のみ青**(CAD capture B-2.png と並置)。
2. 「次へ: プレビュー」→ B-3: **stepper が表示されたまま**(旧実装は消えていた)、
   **1-3=青(現在=3)・4=灰・接続線は 1→3 青・3→4 灰**。タイトルバーは
   「取り込みプレビュー — <名前>」のまま(B-3.png と並置)。
3. 取り込み実行 → B-4: **バッジ 4 が緑塗り丸+白チェック+緑ラベル「完了」へ切替**(数字 4 は
   消える)・**接続線が全区間青**。タイトルバーは「取り込み結果」のまま(B-4.png と並置)。
4. 裏面: B-1(コレクションを書き出す)に stepper が**出ていない**こと(単段・面の発明禁止)。
5. 回帰: B-2 の検証前(ファイル未選択/互換 NG)で stepper が 1 のみ青であること・
   ja/en 切替でステップラベルが追随すること。
