# ECO-016 — surface BOM 体系化: 旧 E-UI-GRID-022 の追跡付き再分割

## 1. 目的

`E-UI-GRID-022` は当初「グリッド/リスト表示」の surface 部品だったが、ECO-010〜015 の画像タブ設計・製造を経て、表示軸ナビゲーション、文脈モード、整理/メンテナンス入口まで吸収する hot spot になった。

本 ECO はコード/挙動変更ではなく、BOM 構造の是正である。旧 `E-UI-GRID-022` を UI-BOM 抽出済みの seam に沿って次の3部品へ分割し、以後の ECO 影響分析を局所化する。

| 新部品 | 旧 GRID から移す契約 | 主な UI-BOM seam |
|---|---|---|
| `E-UI-BROWSE-022` | グリッド/リスト、セル/行、ソート、レイアウト、既定選択 | `region.browse-list` / `TMP-UI-CMP-0023/0024/0027/0028` |
| `E-UI-AXIS-NAV-040` | FS/タグビュー軸、パンくず、チップナビ、全画像入口 | `region.axis-nav` / `TMP-UI-CMP-0021/0025/0026` |
| `E-UI-MODE-041` | タグ編集/整理/メンテ、ツールバー文脈、クリック意味論ディスパッチ | `region.mode-toolbar` / `TMP-UI-CMP-0022`, `ACT-0050/0061/0062/0063` |

## 2. 設計決定

### 2.1 E-BOM 分割

- `30-ebom.yaml` から active item としての `E-UI-GRID-022` を外し、`supersedes: E-UI-GRID-022` / `split_by: ECO-016` を持つ3部品へ分割した。
- `E-UI-BROWSE-022` と `E-UI-MODE-041` の接点を `interface_contracts` として明示した。`MODE` が画像クリックの意味論を決め、`BROWSE` が描画・母集合・既定選択計算を所有する。
- E-BOM の surface invariant から `ViewModel` という実装語を薄め、「描画から独立した決定論的ロジックとして unit 検査可能」という契約語に寄せた。MVVM などの配置語彙は M-BOM 側の責務とする。

### 2.2 UI-BOM / Trace Map

- `bomdd/ui/image-tab/ui-bom.json` に `dispositionVocabulary` を追加した。
- 旧 `region.browse` は aggregate とし、子 region `region.browse-list` / `region.axis-nav` / `region.mode-toolbar` を追加した。
- 各 UI-BOM item の `E-UI-GRID-022` 参照を BROWSE/AXIS/MODE へ再帰属した。
- `bomdd/ui/image-tab/ui-trace-map.json` も locator 単位で同じ再帰属を適用した。

### 2.3 Design System BOM

`35-design-system-bom.yaml` は画面部品と同列の E-BOM 候補ではなく、shared surface component sub-BOM candidate として扱う。

```
material: E-DESIGN-028 / K-DESIGN
  consumes
shared component: SC-CARD / SC-CHIP / SC-CTA / ...
  consumes
screen surface: E-UI-BROWSE / E-UI-AXIS-NAV / E-UI-MODE / ...
```

この変更により、Card/Chip/Button は独立した画面機能部品ではなく、surface が消費する共有原器として coverage gate で管理する。

### 2.4 M-BOM トレースラベル

- `32-mbom.yaml` の `ebom_refs` は active E-BOM item を指すトレースラベルなので、ECO-016 の再分割に合わせて即時に再帰属した。
- `M-UI-013` は旧 `E-UI-GRID-022` を `E-UI-BROWSE-022` / `E-UI-AXIS-NAV-040` / `E-UI-MODE-041` へ置換した。
- `M-UI-019` は GF-02 の行選択視覚是正が browse 表示契約に属するため、旧 `E-UI-GRID-022` を `E-UI-BROWSE-022` へ置換した。
- M-BOM の `interface_contract` prose は v1.2/画像タブ旧設計の記述を含むが、これは設計内容の同期であり、M4 の全面同期まで残す。今回変更するのはラベルだけである。

## 3. 過去 ECO の再帰属

過去の ECO 本文は履歴として書き換えない。構造化索引である `60-change-register.yaml` に `reattributed_by: ECO-016` を追加し、現行 BOM 上の影響先を次のように再帰属した。

| ECO | 旧帰属 | 現帰属 |
|---|---|---|
| ECO-002 | `E-UI-GRID-022` | `E-UI-BROWSE-022`(SHIFT 範囲選択・レスポンシブ列) |
| ECO-004 | `E-UI-GRID-022` | `E-UI-BROWSE-022`(DC-GRID-001 セルサイズ。後に ECO-010/I05 で差し戻し) |
| ECO-010 | `E-UI-GRID-022` | `E-UI-BROWSE-022`(グリッドセル), `E-UI-AXIS-NAV-040`(view 軸ナビ) |
| ECO-011 | `E-UI-GRID-022` | `E-UI-AXIS-NAV-040` |
| ECO-012 | `E-UI-GRID-022` | `E-UI-BROWSE-022`, `E-UI-AXIS-NAV-040`, `E-UI-MODE-041` |
| ECO-013 | `E-UI-GRID-022` | `E-UI-BROWSE-022`, `E-UI-MODE-041` |
| ECO-014 | `E-UI-GRID-022` | `E-UI-MODE-041`, `E-UI-BROWSE-022`(クリック意味論接点) |
| ECO-015 | `E-UI-GRID-022` | `E-UI-MODE-041` |

## 4. 非変更範囲

- Core 意味論は不変。
- UI 挙動・コード・スキーマは不変。
- 固定オラクル S-01〜S-31 は不変。
- ECO-015 の golden 待ちは本 ECO では解消しない。
- 詳細パネル/ノート編集の行き先未決は別 ECO のまま残す。

## 5. 受入

- JSON/YAML として構文が通ること。
- `30-ebom.yaml` の active surface 参照から `E-UI-GRID-022` が消え、座標としての `supersedes` のみに残ること。
- `32-mbom.yaml` の active `ebom_refs` が retired `E-UI-GRID-022` を指さないこと。
- `ui-bom.json` / `ui-trace-map.json` の現行参照が BROWSE/AXIS/MODE へ再帰属されていること。
- 表示/挙動変更が無いため golden は不要(`golden: n/a`)。
