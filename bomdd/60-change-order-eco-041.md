# Change Order — ECO-041(fixed・golden 待ち): タグ追加の検索ボックスが未配線(CAD 定義済み機能の実装欠落+E-BOM 宣言漏れ)

> ECO-040 起票時のスコープ外所見(R3・51-cheat-log 2026-07-05)の分離起票。maintainer 裁定により起票。

## 1. 症状/要求(2026-07-05・ECO-040 診断中に検出)

- 画像タブ・タグ編集モード・右ペイン「タグ追加」タブの検索ボックスは**表示だけで機能しない**:
  `Text=""` 固定・VM に絞り込みロジックが存在しない。入力してもタグ候補は絞り込まれない。
- 作業タブのタグ編集「タグ追加」パネルには**検索ボックス自体が存在しない**(mock には有る — §3)。

## 2. 工程診断 — 二層欠陥(BOM 宣言漏れ+実装欠落)。CAD は健全

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **健全** | 検索の意味論が mock で完全確定(§3): タグ名部分一致(trim・大文字小文字無視)・種別グループ構造は維持・**空になったグループはグループごと非表示**。作業タブ mock にも同一構造の検索がある。`docs/screens/image_tab.md` L304「タグ追加欄には検索があります」・work_tab.md L14「画像タブと同一部品・同一意味論」 |
| BOM | **宣言漏れ** | ui-trace-map(凍結済み抽出台帳)は検索(TMP-UI-INP-0020/TMP-UI-ACT-0060)を handling:bom で E-UI-TAGASSIGN-029 へ帰属済み(ui-trace-map.json:442-454)なのに、**E-UI-TAGASSIGN-029 の name/invariants に検索の宣言が無い**(30-ebom)= 抽出台帳→E-BOM の転記漏れ |
| 実装 | **欠落** | 画像タブ= UI 殻のみ未配線(§3)。作業タブ= UI ごと無し(§3) |

- 未確定事項(FL-*/VE-*)との関係: 該当なし(既知の宣言済み延期ではない — 51-cheat-log/FL/VE に記録なしを ECO-040 起票時に確認済み)。
- R2(上流から直す): **E-BOM 宣言の補完が先、実装は後**(/eco-fix 内で同順に実施)。

## 3. 切り分け済みの事実(確定)

CAD(mock `資料/画像タブ/ViewPrism2 画像タブ.dc.html`・権威):

- L811: `const q = (s.addQuery || '').trim().toLowerCase();`
- L819-820: 種別グループ内のタグを `!q || name.toLowerCase().includes(q)` で絞り込み(部分一致)。
- L871(addGroups 末尾): `.filter(g => g.tags.length > 0)` = **空グループはグループごと消える**。
- L914-915: `addQuery` 状態+`onAddSearch`(入力即時反映・確定ボタン無し)。
- 作業タブ mock(`資料/作業タブ/ViewPrism2 作業タブ.html`)にも `addQuery`(6 出現)+
  同一構造の検索ボックス(`placeholder="タグを検索…"`)が**ある**。

実装:

- 画像タブ: ImageTabView.axaml:180(`Text=""` 固定・バインドなし)。
  ImageTabViewModel.BuildAddGroups(968-)に絞り込みが無い(全タグを常に列挙)。
  VM に AddQuery 相当のプロパティ無し。混入= `e10767b`(2026-06-17 M1+M2)から。
- 作業タブ: WorkTabView.axaml の「タグ追加」パネル(180-182)に検索ボックス自体が無い。
  混入= `81500a8`(2026-06-29 ECO-020/021 β-2 再利用配線)— ECO-021 β-2 の E-BOM 追記
  (30-ebom E-UI-WORKSPACE-043)も「現在のタグ/タグ追加=シンプル/テキスト/数値…」と列挙し
  検索に触れていない= 画像タブ側の宣言漏れがそのまま転写された(同根)。
- 潜伏期間: 約 18 日(画像タブ)。マスキング= 機能不全でも視覚上は完全な検索ボックスに
  見える(ECO-040 の整列欠陥はこの未配線が逆にマスキングしていた — 相互マスキング)。
- 回帰資産: GfPillTextBoxCaretAlignTests(ECO-040)が同ボックスの整列を headless 実測済み
  = 配線後も視覚回帰は自動検出される。

## 4. 是正方針(案 — 着手時確定)

1. **E-BOM 宣言補完(上流先行)**: E-UI-TAGASSIGN-029 invariants へ「タグ追加の検索=
   タグ名部分一致(trim・case-insensitive)・グループ構造維持・空グループ非表示・入力即時」を
   ECO-041 注記で追記。E-UI-WORKSPACE-043(β-2 再利用)にも読み替えが及ぶことを明記。
2. **実装(画像タブ)**: ImageTabViewModel に AddQuery(観測可能プロパティ)を追加し
   BuildAddGroups で mock 意味論どおり絞り込み(判定は描画から独立・unit 検査可能に)。
   XAML は `Text="{Binding AddQuery}"` 配線のみ。
3. **実装(作業タブ)**: WorkTabViewModel に同型の絞り込み+WorkTabView「タグ追加」へ
   検索ボックスを追加(画像タブと同一構造・ECO-040 の整列規約= VerticalContentAlignment
   +Padding 4,0 を適用)。
4. プローブ(R5): 絞り込み・空グループ除去・trim/大文字小文字の決定論テストを是正前に
   追加し不合格を確認(画像/作業 両 VM)。
5. 再発防止(クローズ時確定): 抽出台帳(trace-map handling:bom)→E-BOM 宣言の転記漏れは
   FMEA/監査観点の候補(ECO-022 retro の FMEA-037=mock→UI-IR 未取込と同族の転記断絶)。

## 5. 影響 BOM

- impacted: **E-UI-TAGASSIGN-029(宣言補完+実装)**・E-UI-WORKSPACE-043(β-2 読み替え)・
  M-UI-IMAGETAB-035(ImageTabViewModel)・M-UI-013(ImageTabView.axaml)・
  M-UI-WORKSPACE-029(WorkTabViewModel/WorkTabView.axaml)。
- DB/Core 意味論: 不変(VM 内の表示絞り込みのみ・タグ付与経路に変更なし)。オラクル影響なし(R6)。
- spec §2.6: タグ付与パネルの記述に検索が含まれるか確認し、無ければ M4 で 1 文追記(クローズ時判定)。

## 6. 残ゲート

1. ~~是正実施(E-BOM 宣言補完 → プローブ先行 → 実装)~~ → 完了(§7)
2. ~~機械受入: build 0 / Tests / Oracle / validate_bom 0 error~~ → 完了(§7)
3. golden(maintainer 実機): タグ追加検索の絞り込み(部分一致・空グループ消滅・クリアで全復帰)
   ×画像タブ/作業タブ
4. クローズ時: CP 観点明記+register 更新+M4 要否判定(§5 spec)

## 7. 実施記録(2026-07-05 — 機械受入完了・golden 待ち)

- **E-BOM 宣言補完(R2 上流先行)**: E-UI-TAGASSIGN-029 へ since ECO-041 invariant
  (検索意味論の全文+転記漏れの経緯)・E-UI-WORKSPACE-043 β-2 行へ再利用範囲の補完注記。
- **実測裏取り(プローブ先行)**: 受入テスト 2 件(CpTagUi013AddSearchTests —
  画像タブ/作業タブ・部分一致 trim/大小無視・空グループ消滅・クリア全復帰)を先に追加し、
  **是正前に不合格(CS1061= AddQuery が両 VM に不在)を確認** — 機能欠落の実測。
- **是正(mock 意味論どおり・両タブ同型)**:
  - ImageTabViewModel: `AddQuery` プロパティ(setter で BuildContextPanels 部分再構築=
    Items 不変)+ BuildAddGroups に `q=Trim().ToLowerInvariant()` の部分一致フィルタ。
    空グループ除去は既存(rows.Count>0 ガード)がそのまま効く。
  - WorkTabViewModel: 同型(`AddQuery` → RebuildAddPanel 部分再構築+同フィルタ)。
  - ImageTabView.axaml: `Text="{Binding AddQuery}"` 配線のみ(Avalonia の Text バインドは
    キーストローク即時= mock の onInput 相当)。
  - WorkTabView.axaml: 「タグ追加」へ検索ボックスを新設(画像タブと同一構造・
    ECO-040 整列規約= VerticalContentAlignment+Padding 4,0 適用)。
  - AddQuery はモード切替でリセットしない(mock toggleEdit は selected/expandTag/panelTab
    のみリセット= mock 準拠で保持)。
- 機械受入: build 0 error/0 warning・**Tests 534/534**(プローブ 2 件合格転化・ECO-040
  headless 整列 3 件も緑)・Oracle 100+2skip・validate_bom 0/0。オラクル改訂なし(R6)。
- Core/DB 意味論: 不変(VM 表示絞り込みのみ・タグ付与経路に変更なし)。
