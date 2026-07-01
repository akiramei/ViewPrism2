# ECO-023: REQ-043(画像詳細パネル/ノート)の撤回 — リバース誤りの是正

- **status**: 記録完了(spec/BOM 是正・doc レベル)・**maintainer 裁定済(2026-07-01)**。コード撤去は後続の原典撤去で実施。検証: validate_bom 0 error / Tests・Oracle は不変(コード非変更)
- **type**: 要件撤回(リバース欠陥の是正・遡及 BOM 訂正。ECO-003 同型)
- **baseline**: ECO-022 クローズ後(commit `6c5d0af` 系列・main)
- **bom_rev**: v4.0(eco:ECO-023)
- **reverse_input(裏取り)**: 原典 view-prism `src/components/Images/ImageDetail/ImageDetailModal.tsx` / `useImageData.ts:157-161` / `src/components/Images/types.ts:16`(`notes?: string`)

## 1. 問題(リバース欠陥)

REQ-043 は「画像**詳細パネル**(常時/選択で表示・メタデータ+ノート編集+タグ一覧)」と規定していたが、**原典 view-prism にそのようなパネルは存在しない**(maintainer 2026-07-01 確認)。

原典の実体(ソース確認):

| 事実 | 根拠 |
|---|---|
| 詳細/ノートは **`ImageDetailModal`(モーダル)** | `ImageDetail/ImageDetailModal.tsx` |
| **閲覧モードで画像を単一クリック**すると開く(選択モードでない時=`!selectable` の既定処理) | `useImageData.ts:157-161` |
| `notes` は**実フィールド**・編集可・`images:update` で永続化 | `types.ts:16` / `ImageDetailModal.tsx:88-95` |
| **選択に紐づくパネルではない**(選択トグルは `selectable` 時のみ別処理) | 同上 |

**リバース誤り**: 「クリックで開くモーダル」を「常時/選択の詳細パネル」と取り違えた。トレーサビリティ(`20-spec.md` §10 表)は正しく `ImageDetailModal.tsx` を指しており、**誤りは spec 散文『右パネル 通常時=詳細パネル』の形**にある。V1 初期に本要件どおり詳細パネルを製造し golden CP-UI-G3 を承認していた(`50-as-built` 2026-06-12)が、これも誤った要件に基づく。

新モック(CAD・ViewPrismUI)は詳細を**意図的に廃止**(右ペイン=文脈モード時のみ)。よって本要件は原典にも新設計にも根拠を持たない。

## 2. 裁定(maintainer 2026-07-01)

**REQ-043 を撤回する(案 1)。** 詳細/ノートは新設計に持ち込まない(後継部品なし)。

- 検討した代替: 案 2=原典どおりモーダル復元 / 案 3=ノートをビューアーへ。いずれも新機能追加になり、モックが詳細を廃止している以上いま作る必要はない → **撤回**を選択。
- 将来ノート機能が欲しくなれば、ビューアー(単一画像フォーカスの生きた surface)へ**新機能として**足す(別 ECO)。現実装の CAD 原器化はしない([[mock-ui-ir-is-cad]] 原則)。

## 3. 生きている部分(保持)

REQ-043 が束ねていた要素のうち、原典に実在し他所で使うものは保持:

- **サイズ/解像度の整形表示**: list/grid のサイズ表示は ECO-004(CP-DISPLAY-PARITY-022)が担当。**OC-7 サイズ整形器**は共有部品として継続(0→`0 B` / 1024→`1.0 KB`)。
- **`images.notes` フィールド**: DB スキーマに残置(dormant)。撤去しない(将来の再導入・データ保全のため無害に保持)。

## 4. 影響範囲と是正(遡及 BOM 訂正)

| 対象 | 是正 |
|---|---|
| `10-requirements.yaml` REQ-043 | `status: deprecated` + `deprecated_by: ECO-023` + `deprecation_reason`(リバース誤りの全経緯)。statement 先頭に `[撤回・ECO-023]`。エントリは provenance として残す |
| `20-spec.md` §2.6(右パネル・詳細ブロック) | 「通常時=詳細パネル(REQ-043)」を撤回注記へ。詳細ブロックを取り消し線+撤回理由。OC-7 の帰属を「共有・list/grid(ECO-004)」へ。§10 トレーサビリティ表 REQ-043 行を撤回注記 |
| `30-ebom.yaml` E-UI-DETAIL-023 | **削除(純撤回・後継なし)**。consumers 参照(E-DESIGN-028 graph_edges)から除去。prose 2 箇所(ECO-015/014 注記)を「ECO-023 で撤回」へ。active items 38→37 |
| `33-control-plan.yaml` CP-UI-G3 | `status: retired` + `retired_by: ECO-023` |
| `50-as-built.yaml` golden_approvals CP-UI-G3(2026-06-12) | `superseded_by: ECO-023` 注記(歴史記録は残す) |
| `60-change-register.yaml` | ECO-023 エントリ追加。既存 scope_out「詳細/ノート(REQ-043・別ECO)」は歴史記録として残置(本 ECO で解消済を register 側で示す) |
| ViewPrismUI `docs/screens/image_tab.md` / `review_points.md` | 前セッションの案 A 設計ブリーフ(詳細/ノート タブ・IMG-012)を撤回注記へ差し替え |

**コード非変更**: 原典 `DetailPanelViewModel.cs` / `MainWindow.axaml` の Detail.* は legacy surface の一部で、**原典撤去(次工程)で削除**する。本 ECO は要件/BOM の是正に限る。

## 5. 効果 — 原典撤去の最後のブロッカー解消

REQ-043 は「新 surface に詳細/ノートの行き先が要る」という理由で**原典撤去の最後のブロッカー**だった(register ECO-013/017/018/019/020/021 の scope_out)。撤回により**新設すべきものが消え、原典撤去がビルド無しで進められる**。

## 6. 次工程

**原典撤去 ECO**(別 ECO): `MainWindow.axaml` の画像タブ Grid(詳細パネル Detail.* 含む)・harness トグル・legacy VM(`DetailPanelViewModel`/画像タブ legacy メンバ)を撤去 → 撤去前にタグタブ共有メンバの依存検証 → **M4**(spec/M-BOM/Control Plan 同期)。

## 7. 検証

- `validate_bom.py`: **0 error**(E-UI-DETAIL-023 削除後の dangling/consumer 参照整合を確認)。
- Tests/Oracle: **不変**(本 ECO はコード非変更)。
