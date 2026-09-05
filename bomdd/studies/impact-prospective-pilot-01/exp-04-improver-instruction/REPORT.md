# EXP-20260905-04 — 改善者は対象を名指しされずに、ECO の構造変更を BOM へ適切に反映できるか(旧指示 v1 / 新指示 v2・同一入力)

> 評価対象は **ECO-139/140 の構造変更を BOM へ適切に反映できるか**であり、ECO-142 への的中ではない。
> 正本= `REFERENCE.sealed.yaml`(独立作成の参照表・封印)・`candidates/*`(4 候補の diff・CHANGELOG・盲検採点)・`audits/*`。
> 探索的(各指示 2 run・N=4)。差の有無は成功条件ではない。

## 1. 設計

- 入力: 同一の未変更パッケージ(baseline `79123df` の code + BOM A + ECO-139/140 の order と diff・manifest `1c56700ad607…`・履歴なし)。
  4 候補すべて同一 manifest から開始。**旧 run の再利用**: imp-old-1 は pilot-01 の改善者 run(2026-09-05 02:30Z・単独実行)を
  そのまま候補にしたもので、本 EXP で新規に実行したのは imp-old-2 / imp-new-1 / imp-new-2 の 3 つ(同時刻に 3 並列+参照作成者で 4 並列)。
  したがって old-1 の所要時間・往復は単独実行の値であり、他 3 run とは実行条件が異なる(所要の比較には使わない)。
- 指示: **v1**(pilot-01 と同文)/ **v2** = v1 + 手順 3b「diff の hunk 単位で実装写像(追加・移動・削除された各メソッド/クラス/ファイルを担う
  unit・契約・artifact)を棚卸しし、反映済み/追記/未編集を分類。データアクセス層・集計経路も UI と同じ扱い」。差分は 3b の追加のみ。
  対象(件数クエリ・M-DB-007 等)は名指ししない。
- 参照表: 別の独立セッション(Codex Sol high・read-only)が同じ入力から「反映されるべき対応」32 項目(design 6 / implementation-mapping 14 /
  artifact-retirement 6 / cp-fixture 6・うち **未反映 7**)と must_not_change 11 件を作成。sha256 `38ed83e9…` @ 11:07:31Z に封印(採点前)。
- 採点: 候補ごとに独立セッション(盲検ラベル X1〜X4・対応表は封印)。回収= 参照 32 項目の covered/partial/missing/broken、
  誤追加= 候補 hunk ごとの grounded/ungrounded/contradicting/design-overwrite。集計は totals でなく item 列から再計算。
- 設備: 改善者・参照作成者・採点者はすべて Codex gpt-5.6-sol high の独立セッション。製造者(claude)は封印・開封・集計のみ。
  全セッションのコマンドはパッケージ内(監査: suspicious 0。「diff --git」文字列への一致は除外)。

## 2. 結果

| 候補 | 指示 | covered / partial / missing / broken(32) | grounded / ungrounded / contradicting / design-overwrite(hunk) | 往復 | input tokens(累計) |
|---|---|---|---|---|---|
| imp-old-1 (X1) | v1 | 26 / 4 / 1 / **1** | 19 / 1 / 1 / 0(21) | 24 | 4.8M |
| imp-old-2 (X3) | v1 | 27 / 4 / 1 / 0 | 18 / 0 / **3** / 0(21) | 27 | 9.9M |
| imp-new-1 (X2) | v2 | 26 / 5 / 1 / 0 | 23 / 1 / 2 / 0(26) | 56 | 16.6M |
| imp-new-2 (X4) | v2 | 27 / 5 / 0 / 0 | 18 / 0 / 0 / 0(18) | 56 | 15.8M |

未反映 7 項目の回収(参照 already_reflected=false):

| 項目 | 種別 | old-1 | old-2 | new-1 | new-2 |
|---|---|---|---|---|---|
| R-010 M-UI-REPAIR-027 の superseded | artifact-retirement | partial | covered | partial | partial |
| R-012 M-RELINK-025 の API 写像 | implementation-mapping | partial | partial | partial | partial |
| R-013 CountAutoRepairableAsync の撤去 | artifact-retirement | covered | partial | partial | covered |
| **R-017 M-DB-007 の batch API 契約** | implementation-mapping | **missing** | **missing** | partial | partial |
| R-025 CP-INTEGRITY-036 fixture | cp-fixture | covered | covered | covered | covered |
| R-026 acceptance_refs(3 unit) | cp-fixture | partial | missing | partial | partial |
| R-032 旧 Window/VM/visual test の撤去 | artifact-retirement | partial | partial | partial | partial |

- **回収**: 32 項目の covered は v1・v2 とも 26〜27 で差がない。未反映 7 項目のうち **データアクセス層の写像(R-017・M-DB-007 の
  ApplyRelinkBatchAsync / ApplyIntegrityReviewBatchAsync 契約)は v1 が 2/2 で missing、v2 が 2/2 で partial**。v2 の 3b(データアクセス層も
  棚卸し)が狙った層に届いた唯一の項目だが、partial(件数不一致 rollback 条件の欠落)に留まる。他の 6 項目は指示による差がない。
- **誤追加**: v1 は old-1 が **R-009 を broken**(M-UI-PENDING-054 の ebom_refs を空にして旧写像を失わせた)、old-2 が contradicting 3
  (must_not_change の歴史 CP エントリを編集)。v2 は new-1 が contradicting 2(34-routing の歴史ルートを編集)+ ungrounded 1
  (artifact に **実在しない識別子 `RelinkBatchPair`** を記載)、new-2 は 0。
- **コスト**: v2 は往復 2 倍(56 対 24〜27)、累計 input tokens 1.6〜3.5 倍、所要 約 40 分(v1 は 12 分)。編集量は同程度(hunk 18〜26)。

### 2b. 分けて評価(未反映 7 項目の回収 / 既存情報の保存 / 追加費用)

| 候補 | 指示 | 未反映 7 項目の回収(covered=1・partial=0.5) | 既存情報の保存(broken / 歴史エントリ編集 contradicting / ungrounded) | 追加費用(往復 / 累計 input tokens / 所要) |
|---|---|---|---|---|
| old-1(再利用) | v1 | 4.0 / 7(covered 2・partial 4・missing 1) | broken **1**・contradicting 1・ungrounded 1 | 24 / 4.8M / 12 分(単独) |
| old-2 | v1 | 4.0 / 7(covered 2・partial 4・missing 1) | broken 0・contradicting 3・ungrounded 0 | 27 / 9.9M / (終了時刻の記録なし) |
| new-1 | v2 | 3.5 / 7(covered 1・partial 5・missing 1) | broken 0・contradicting 2・ungrounded 1(実在しない識別子) | 56 / 16.6M / 42 分(3 並列) |
| new-2 | v2 | 4.5 / 7(covered 2・partial 5・missing 0) | broken 0・contradicting 0・ungrounded 0 | 56 / 15.8M / 39 分(3 並列) |

- 未反映 7 項目の回収は v1 4.0 / 4.0、v2 3.5 / 4.5 で**指示差はない**。v2 が届いた R-017 は partial 止まりで、v1 が covered だった
  R-010(old-2)を v2 は partial にしている。
- 既存情報の保存は v1 に broken 1(old-1)があり、v2 には無い。ただし contradicting の 3 件(old-2)は参照表の must_not_change 方針
  (歴史 CP への superseded 注記を「改変」とみなす)に依存する判定で、方針が変われば消える。
- 追加費用は v2 が往復 2 倍・累計 tokens 1.6〜3.5 倍。所要は old-1 が単独実行、他が 3〜4 並列のため比較しない。

## 3. 言えること / 言えないこと

**言えること**: ①同一入力・同一設備で、旧/新指示とも参照 32 項目の 8 割強を回収し、指示差は総回収率にも未反映 7 項目の回収にも出ない。
②新指示はデータアクセス層の実装写像(R-017)に部分的に届き、旧指示は 2/2 で落とした — 「名指しせずに層を指定する」指示は当該層への
到達を変えるが、完全な回収には至らず、他項目の回収を上げもしない。③誤追加は両指示で発生し種類が異なる(旧: 履歴写像の破壊 1・歴史 CP の
編集 3 / 新: 歴史 routing の編集 2・実在しない識別子 1)。④追加費用は新指示が往復 2 倍・tokens 1.6〜3.5 倍。
**したがって v2 の基本採用は本結果からは導けない**(回収の利得が R-017 の partial に限られ、費用は 2 倍超)。履歴エントリの編集を一律に禁止する
根拠も無い(contradicting の判定が参照表の方針に依存し、superseded 注記が是正か改変かは未裁定)。

**言えないこと**: 指示差の再現性(各 2 run)/ 参照表の妥当性(独立作成だが 1 名・must_not_change 11 件の判断は「歴史エントリへの
superseded 注記は是正か改変か」で作成者依存 — old-2 の contradicting 3 はこの判断に依る)/ 採点者のばらつき(候補ごとに別セッション・
同一候補の再採点なし)/ 到達モデル・到達 effort。

## 4. 訂正(pilot-01 への還流)

参照表 R-016 により、BOM A の M-DB-007 契約には ECO-140 の integrity read model(CountIntegrityReviewEventsAsync)が**既に反映済み**
だったことが判明した。pilot-01 REPORT の「A に写像なし」は製造者の読み違い(E-BOM 辺と path_note だけを見た)で、pilot-01 REPORT §1/§4 を
訂正済み。改善工程が生成しなかったのは E 品目レベルの依存辺であり、M レベルの契約には写像があった。

## 5. 処置(レビュー 2026-09-05・maintainer)

- **保留**: v2 の基本採用 / 履歴エントリ編集の一律禁止。どちらも本結果(各 2 run・参照表 1 名)からは導けない。
- 記録に留める観測: 参照表の must_not_change 方針は採点の分散源(歴史エントリの扱いを先に固定する案)/ 実在しない識別子の混入は
  `code/` への機械照合で捕まえられる(案)。
- **次の主題は「既に BOM にある情報を変更判断へ利用できるか」**(前向き評価へ戻る)。pilot-01 では予測者が BOM 内の過去事例記録
  (ECO-098)と M-DB-007 契約から実際の修正対象へ到達した。BOM を改善する工程より先に、現状の BOM が次の実変更の判断(影響 unit・確認すべき
  検査・非影響の根拠)をどこまで支えるかを、実際の起票時に測る。
