prediction:
  request: REQUEST-R1
  change_units:
    - unit: M-UI-TRASH-032
      confidence: certain
      rationale: "bom/32-mbom.yaml M-UI-TRASH-032 acceptance_note は、ECO-098 の一覧ロードを GetDeletedByFolderAsync のみに限定し、全status取得への退行を禁止する。同じ normal 6件・missing 262,045件の事象は bom/30-ebom.yaml E-UI-REPAIR-039 invariant ECO-098 に明記される"
    - unit: M-DB-007
      confidence: certain
      rationale: "bom/32-mbom.yaml M-DB-007 interface_contract.repositories は、ECO-098 の是正用読取境界 GetDeletedByFolderAsync(collection+deleted predicate)を本unitの契約としている"
  investigate_units:
    - unit: M-UI-IMAGETAB-035
      confidence: likely
      rationale: "bom/32-mbom.yaml M-UI-IMAGETAB-035 はコレクション切替とcontent再ロードを所有し、M-UI-TRASH-032へ reloadImagesAsync を供給する。R1がゴミ箱操作時だけでなく単純切替でも発生するかを、この境界で切り分ける必要がある"
    - unit: M-UI-013
      confidence: uncertain
      rationale: "bom/32-mbom.yaml M-UI-013 startup と bom/30-ebom.yaml E-UI-SHELL-021 ECO-064/REQ-088 は、shellから画像タブのcatalog/content段階ロードを開始する。単純切替経路の遅延なら呼出し起点として調査対象だが、ECO-098の直接変更先とは記されていない"
  not_affected:
    - unit: M-SIMSEARCH-021
      rationale: "bom/32-mbom.yaml M-SIMSEARCH-021/FMEA-022 は類似検索の候補計算を扱う。R1の「類似」はコレクション名であり、類似検索実行の報告ではない"
    - unit: M-TRASH-026
      rationale: "bom/32-mbom.yaml M-TRASH-026 は復元・完全削除などの状態遷移核を所有するが、ECO-098の過剰materialize是正はM-UI-TRASH-032とM-DB-007の一覧読取境界に割り当てられている"
    - unit: M-SCAN-005
      rationale: "missing 262,045件は遅延を顕在化させるデータ規模だが、REQUEST-R1はスキャンやstatus遷移の変更を求めず、表示件数挙動も変更しないとしている"
    - unit: M-UI-SIMILARITY-023
      rationale: "bom/32-mbom.yaml M-UI-SIMILARITY-023 は retired(ECO-051)で、旧類似検索UIの到達不能残骸として撤去済み"
  ebom_items:
    - E-DB-010
    - E-UI-SHELL-021
    - E-UI-BROWSE-022
    - E-UI-REPAIR-039
  contracts_to_check:
    - CP-UI-G1
    - CP-STARTUP-028
    - CP-L1-SMOKE
    - CP-TRASH-020
    - CP-TRASH-001
    - CP-TRASH-021
    - CP-TRASH-022
  unresolved:
    - question: "遅延は単純なコレクション切替だけで発生するのか、それとも削除実行・ゴミ箱open/操作後reloadを伴うのか"
      needs: "R1操作経路の特定。BOMの同一数値プロファイルはECO-098のゴミ箱経路に限定して原因を記録している"
    - question: "R1はECO-098未適用版の報告か、2026-07-16承認後の再発か"
      needs: "対象製品版にECO-098契約が適用済みかを示す構成・ECO適用情報"
    - question: "単純切替経路で既存の選択collection限定API契約が破られているか"
      needs: "CP-STARTUP-028が規定するrepository method・argument・call countの観測結果"